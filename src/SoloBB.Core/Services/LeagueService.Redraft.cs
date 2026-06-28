using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed partial class LeagueService
{
    // BB2020 end-of-season redraft constants.
    private const int RedraftBaseBudget = 1_000_000;
    private const int RedraftBudgetCap = 1_300_000;
    private const int RedraftFixtureGold = 20_000;
    private const int RedraftWinGold = 20_000;
    private const int RedraftDrawGold = 10_000;
    private const int RedraftAgentFee = 20_000;

    // BB2020 "Raise Funds": redraft budget = 1,000,000 + Treasury + 20,000 per fixture played + 20,000 per
    // fixture won + 10,000 per fixture drawn, capped at 1,300,000 (any excess is lost). The record is computed
    // from the most recent season's completed results.
    public RedraftBudget CalculateRedraftBudget(League league, Guid teamId)
    {
        var team = league.Teams.FirstOrDefault(current => current.Id == teamId)
            ?? throw new InvalidOperationException("Team is not part of this league.");

        var record = GetTeamRecord(league, teamId);
        var subtotal = RedraftBaseBudget
            + team.Treasury
            + (record.Played * RedraftFixtureGold)
            + (record.Wins * RedraftWinGold)
            + (record.Draws * RedraftDrawGold);

        return new RedraftBudget
        {
            TeamId = teamId,
            Base = RedraftBaseBudget,
            Treasury = team.Treasury,
            FixturesPlayed = record.Played,
            FixturesWon = record.Wins,
            FixturesDrawn = record.Draws,
            Subtotal = subtotal,
            Cap = RedraftBudgetCap,
            Total = Math.Min(subtotal, RedraftBudgetCap)
        };
    }

    // BB2020 "Re-draft Team": rebuild a team for the next season within its redraft budget. Retained players
    // are re-hired at their current value plus a 20,000 agent fee; new players are drafted at position cost.
    // Rerolls and staff are re-purchased at normal cost, while Dedicated Fans carry over unchanged. The
    // leftover budget becomes the new Treasury. Lasting injuries, characteristic reductions, unspent SPP, and
    // skills are preserved on retained players; transient match status is cleared back to Available.
    public League RedraftTeam(
        League league,
        Ruleset ruleset,
        TeamRoster roster,
        Guid teamId,
        IReadOnlyList<Guid> retainedPlayerIds,
        IReadOnlyList<PlayerDraftPick> newDraft,
        int rerolls,
        int cheerleaders,
        int assistantCoaches,
        int apothecaries)
    {
        var team = league.Teams.FirstOrDefault(current => current.Id == teamId)
            ?? throw new InvalidOperationException("Team is not part of this league.");
        if (!string.Equals(team.RosterId, roster.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Roster does not match the selected team.");
        }

        if (rerolls < 0 || rerolls > ruleset.RerollCap)
        {
            throw new InvalidOperationException($"Rerolls must be between 0 and {ruleset.RerollCap}.");
        }

        ValidateStaff(cheerleaders, assistantCoaches, apothecaries);

        var distinctRetained = retainedPlayerIds.Distinct().ToArray();
        if (distinctRetained.Length != retainedPlayerIds.Count)
        {
            throw new InvalidOperationException("A player cannot be retained more than once.");
        }

        var retainedPlayers = distinctRetained
            .Select(playerId => team.Players.FirstOrDefault(player => player.Id == playerId)
                ?? throw new InvalidOperationException("Retained player is not part of this team."))
            .ToArray();

        var unavailableToRetain = retainedPlayers.FirstOrDefault(player => player.Status is PlayerStatus.Dead or PlayerStatus.Retired);
        if (unavailableToRetain is not null)
        {
            throw new InvalidOperationException($"{unavailableToRetain.Name} cannot be retained ({unavailableToRetain.Status}).");
        }

        var newPlayers = newDraft.Select(pick => CreatePlayer(roster, pick)).ToArray();

        // Renumber retained-then-new in order so jersey numbers stay 1..N without collisions.
        var combined = ((Player[])[.. retainedPlayers, .. newPlayers])
            .Select((player, index) => (player with
            {
                Number = index + 1,
                Status = PlayerStatus.Available
            }))
            .ToArray();
        var retainedLookup = distinctRetained.ToHashSet();
        var previousPlayers = team.Players
            .Where(player => !retainedLookup.Contains(player.Id))
            .Select(player => player.Status is PlayerStatus.Dead or PlayerStatus.Retired
                ? player
                : player with { Status = PlayerStatus.Retired })
            .ToArray();

        ValidateDraft(roster, combined);
        if (combined.Length < ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"Redraft has {combined.Length} players; ruleset requires at least {ruleset.PlayersPerSide}.");
        }

        if (combined.Length > MaximumRosterPlayers)
        {
            throw new InvalidOperationException($"Redraft has {combined.Length} players; rosters can include no more than {MaximumRosterPlayers}.");
        }

        var budget = CalculateRedraftBudget(league, teamId).Total;
        var retainedCost = retainedPlayers.Sum(player => PreGameService.PlayerValue(ruleset, roster, player) + RedraftAgentFee);
        var newPlayerCost = newPlayers.Sum(player => FindPosition(roster, player.PositionId).Cost);
        var rerollCost = rerolls * roster.RerollCost;
        var staffCost = (cheerleaders * CheerleaderCost) + (assistantCoaches * AssistantCoachCost) + (apothecaries * ApothecaryCost);
        var totalCost = retainedCost + newPlayerCost + rerollCost + staffCost;

        if (totalCost > budget)
        {
            throw new InvalidOperationException($"Redraft cost {totalCost} exceeds the redraft budget {budget}.");
        }

        var dedicatedFansCost = Math.Max(0, team.DedicatedFans - 1) * DedicatedFanCost;
        var playerValue = combined.Sum(player => PreGameService.PlayerValue(ruleset, roster, player));
        var teamValue = playerValue + rerollCost + dedicatedFansCost + staffCost;

        var redraftedTeam = team with
        {
            Treasury = budget - totalCost,
            TeamValue = teamValue,
            Rerolls = rerolls,
            Cheerleaders = cheerleaders,
            AssistantCoaches = assistantCoaches,
            Apothecaries = apothecaries,
            Players = [.. combined, .. previousPlayers]
        };

        return league with
        {
            Teams = league.Teams.Select(current => current.Id == teamId ? redraftedTeam : current).ToArray()
        };
    }

    // Starts a fresh season once teams have been redrafted, appending a new double round-robin schedule.
    public League StartNewSeason(League league, string seasonName)
    {
        if (league.Teams.Count != league.TargetTeamCount)
        {
            throw new InvalidOperationException($"League has {league.Teams.Count} teams; expected {league.TargetTeamCount}.");
        }

        if (league.Teams.Count < 2 || league.Teams.Count % 2 != 0)
        {
            throw new InvalidOperationException("League scheduling requires an even number of teams.");
        }

        var season = new Season
        {
            Id = Guid.NewGuid(),
            Name = RequireText(seasonName, "Season name is required."),
            CurrentWeek = 1,
            Schedule = CreateDoubleRoundRobinSchedule(league.Teams)
        };

        return league with { Seasons = [.. league.Seasons, season] };
    }

    public TeamRecord GetTeamRecord(League league, Guid teamId)
    {
        var season = league.Seasons.LastOrDefault();
        if (season is null)
        {
            return new TeamRecord(0, 0, 0);
        }

        var wins = 0;
        var draws = 0;
        var losses = 0;
        foreach (var scheduled in season.Schedule)
        {
            if (scheduled.Result is not MatchResult result)
            {
                continue;
            }

            var isHome = scheduled.HomeTeamId == teamId;
            var isAway = scheduled.AwayTeamId == teamId;
            if (!isHome && !isAway)
            {
                continue;
            }

            var scoreFor = isHome ? result.HomeScore : result.AwayScore;
            var scoreAgainst = isHome ? result.AwayScore : result.HomeScore;
            if (scoreFor > scoreAgainst)
            {
                wins++;
            }
            else if (scoreFor == scoreAgainst)
            {
                draws++;
            }
            else
            {
                losses++;
            }
        }

        return new TeamRecord(wins, draws, losses);
    }
}

public sealed record RedraftBudget
{
    public required Guid TeamId { get; init; }
    public int Base { get; init; }
    public int Treasury { get; init; }
    public int FixturesPlayed { get; init; }
    public int FixturesWon { get; init; }
    public int FixturesDrawn { get; init; }
    public int Subtotal { get; init; }
    public int Cap { get; init; }
    public int Total { get; init; }
}
