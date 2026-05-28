using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class LeagueService
{
    private const int MaximumRosterPlayers = 16;
    private const int FanFactorCost = 10_000;
    private const int CheerleaderCost = 10_000;
    private const int AssistantCoachCost = 10_000;
    private const int ApothecaryCost = 50_000;

    public League CreateLeague(string name, Ruleset ruleset, IEnumerable<RosterSet> rosterSets, int targetTeamCount = 2)
    {
        var rosterSetIds = rosterSets.Select(set => set.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (rosterSetIds.Length == 0)
        {
            throw new InvalidOperationException("At least one roster set is required to create a league.");
        }

        if (targetTeamCount < 2)
        {
            throw new InvalidOperationException("A league must have at least two teams.");
        }

        if (targetTeamCount % 2 != 0)
        {
            throw new InvalidOperationException("League scheduling currently requires an even number of teams.");
        }

        return new League
        {
            Id = Guid.NewGuid(),
            Name = RequireText(name, "League name is required."),
            RulesetId = ruleset.Id,
            TargetTeamCount = targetTeamCount,
            RosterSetIds = rosterSetIds,
            Settings = new LeagueSettings()
        };
    }

    public League AddTeam(
        League league,
        Ruleset ruleset,
        string teamName,
        string coachName,
        TeamRoster roster,
        IEnumerable<PlayerDraftPick> draft,
        int rerolls = 0,
        int fanFactor = 1,
        int cheerleaders = 0,
        int assistantCoaches = 0,
        int apothecaries = 0)
    {
        if (!league.RosterSetIds.Any())
        {
            throw new InvalidOperationException("League has no roster sets configured.");
        }

        var players = draft.Select(pick => CreatePlayer(roster, pick)).ToArray();
        ValidateDraft(roster, players);

        if (players.Length < ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; ruleset requires at least {ruleset.PlayersPerSide}.");
        }

        if (players.Length > MaximumRosterPlayers)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; rosters can include no more than {MaximumRosterPlayers}.");
        }

        if (rerolls < 0 || rerolls > ruleset.RerollCap)
        {
            throw new InvalidOperationException($"Rerolls must be between 0 and {ruleset.RerollCap}.");
        }

        if (fanFactor < 1)
        {
            throw new InvalidOperationException("Fan factor must be at least 1.");
        }

        ValidateStaff(cheerleaders, assistantCoaches, apothecaries);

        var team = BuildTeam(Guid.NewGuid(), ruleset, teamName, coachName, roster, players, rerolls, fanFactor, cheerleaders, assistantCoaches, apothecaries);

        return league with { Teams = [.. league.Teams, team] };
    }

    public League CreateSeason(League league, string seasonName = "Season 1")
    {
        if (league.Teams.Count != league.TargetTeamCount)
        {
            throw new InvalidOperationException($"League has {league.Teams.Count} teams; expected {league.TargetTeamCount}.");
        }

        if (league.Teams.Count < 2 || league.Teams.Count % 2 != 0)
        {
            throw new InvalidOperationException("League scheduling requires an even number of teams.");
        }

        if (league.Seasons.Any())
        {
            return league;
        }

        var season = new Season
        {
            Id = Guid.NewGuid(),
            Name = seasonName,
            CurrentWeek = 1,
            Schedule = CreateDoubleRoundRobinSchedule(league.Teams)
        };

        return league with { Seasons = [season] };
    }

    public League UpdateTeam(
        League league,
        Ruleset ruleset,
        Guid teamId,
        string teamName,
        string coachName,
        TeamRoster roster,
        IEnumerable<PlayerDraftPick> draft,
        int rerolls = 0,
        int fanFactor = 1,
        int cheerleaders = 0,
        int assistantCoaches = 0,
        int apothecaries = 0)
    {
        if (!league.Teams.Any(team => team.Id == teamId))
        {
            throw new InvalidOperationException("Team is not part of this league.");
        }

        var players = draft.Select(pick => CreatePlayer(roster, pick)).ToArray();
        ValidateDraft(roster, players);

        if (players.Length < ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; ruleset requires at least {ruleset.PlayersPerSide}.");
        }

        if (players.Length > MaximumRosterPlayers)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; rosters can include no more than {MaximumRosterPlayers}.");
        }

        if (rerolls < 0 || rerolls > ruleset.RerollCap)
        {
            throw new InvalidOperationException($"Rerolls must be between 0 and {ruleset.RerollCap}.");
        }

        if (fanFactor < 1)
        {
            throw new InvalidOperationException("Fan factor must be at least 1.");
        }

        ValidateStaff(cheerleaders, assistantCoaches, apothecaries);

        var updatedTeam = BuildTeam(teamId, ruleset, teamName, coachName, roster, players, rerolls, fanFactor, cheerleaders, assistantCoaches, apothecaries);

        return league with
        {
            Teams = league.Teams
                .Select(team => team.Id == teamId ? updatedTeam : team)
                .ToArray()
        };
    }

    private static LeagueTeam BuildTeam(
        Guid teamId,
        Ruleset ruleset,
        string teamName,
        string coachName,
        TeamRoster roster,
        IReadOnlyList<Player> players,
        int rerolls,
        int fanFactor,
        int cheerleaders,
        int assistantCoaches,
        int apothecaries)
    {
        var playerCost = players.Sum(player => FindPosition(roster, player.PositionId).Cost);
        var rerollCost = rerolls * roster.RerollCost;
        var fanFactorCost = Math.Max(0, fanFactor - 1) * FanFactorCost;
        var staffCost = (cheerleaders * CheerleaderCost) + (assistantCoaches * AssistantCoachCost) + (apothecaries * ApothecaryCost);
        var totalCost = playerCost + rerollCost + fanFactorCost + staffCost;

        if (totalCost > ruleset.StartingTreasury)
        {
            throw new InvalidOperationException($"Team cost {totalCost} exceeds starting treasury {ruleset.StartingTreasury}.");
        }

        return new LeagueTeam
        {
            Id = teamId,
            Name = RequireText(teamName, "Team name is required."),
            CoachName = string.IsNullOrWhiteSpace(coachName) ? "Solo Coach" : coachName,
            RosterId = roster.Id,
            Treasury = ruleset.StartingTreasury - totalCost,
            TeamValue = totalCost,
            Rerolls = rerolls,
            FanFactor = fanFactor,
            Cheerleaders = cheerleaders,
            AssistantCoaches = assistantCoaches,
            Apothecaries = apothecaries,
            Players = players
        };
    }

    private static void ValidateStaff(int cheerleaders, int assistantCoaches, int apothecaries)
    {
        if (cheerleaders < 0)
        {
            throw new InvalidOperationException("Cheerleaders cannot be negative.");
        }

        if (assistantCoaches < 0)
        {
            throw new InvalidOperationException("Assistant coaches cannot be negative.");
        }

        if (apothecaries is < 0 or > 1)
        {
            throw new InvalidOperationException("Apothecaries must be between 0 and 1.");
        }
    }

    private static ScheduledMatch[] CreateDoubleRoundRobinSchedule(IReadOnlyList<LeagueTeam> teams)
    {
        var firstHalf = CreateSingleRoundRobinPairings(teams);
        var secondHalfOrder = RotateRounds(firstHalf, Math.Max(1, firstHalf.Length / 2));
        var matches = new List<ScheduledMatch>();

        for (var roundIndex = 0; roundIndex < firstHalf.Length; roundIndex++)
        {
            foreach (var (homeTeamId, awayTeamId) in firstHalf[roundIndex])
            {
                matches.Add(new ScheduledMatch
                {
                    Id = Guid.NewGuid(),
                    Week = roundIndex + 1,
                    HomeTeamId = homeTeamId,
                    AwayTeamId = awayTeamId
                });
            }
        }

        for (var roundIndex = 0; roundIndex < secondHalfOrder.Length; roundIndex++)
        {
            foreach (var (homeTeamId, awayTeamId) in secondHalfOrder[roundIndex])
            {
                matches.Add(new ScheduledMatch
                {
                    Id = Guid.NewGuid(),
                    Week = firstHalf.Length + roundIndex + 1,
                    HomeTeamId = awayTeamId,
                    AwayTeamId = homeTeamId
                });
            }
        }

        return matches.ToArray();
    }

    private static (Guid HomeTeamId, Guid AwayTeamId)[][] CreateSingleRoundRobinPairings(IReadOnlyList<LeagueTeam> teams)
    {
        var rotatingTeams = teams.Select(team => team.Id).ToList();
        var rounds = new List<(Guid HomeTeamId, Guid AwayTeamId)[]>();
        var teamCount = rotatingTeams.Count;

        for (var round = 0; round < teamCount - 1; round++)
        {
            var pairings = new List<(Guid HomeTeamId, Guid AwayTeamId)>();
            for (var index = 0; index < teamCount / 2; index++)
            {
                var first = rotatingTeams[index];
                var second = rotatingTeams[teamCount - 1 - index];
                pairings.Add((round + index) % 2 == 0 ? (first, second) : (second, first));
            }

            rounds.Add(pairings.ToArray());

            var moved = rotatingTeams[^1];
            rotatingTeams.RemoveAt(teamCount - 1);
            rotatingTeams.Insert(1, moved);
        }

        return rounds.ToArray();
    }

    private static T[] RotateRounds<T>(IReadOnlyList<T> rounds, int offset)
    {
        return rounds
            .Skip(offset)
            .Concat(rounds.Take(offset))
            .ToArray();
    }

    private static Player CreatePlayer(TeamRoster roster, PlayerDraftPick pick)
    {
        var position = FindPosition(roster, pick.PositionId);
        return new Player
        {
            Id = Guid.NewGuid(),
            Name = RequireText(pick.Name, "Player name is required."),
            PositionId = position.Id,
            Stats = position.Stats,
            Skills = position.StartingSkills
        };
    }

    private static PositionTemplate FindPosition(TeamRoster roster, string positionId)
    {
        return roster.Positions.FirstOrDefault(position => string.Equals(position.Id, positionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Roster '{roster.Id}' does not contain position '{positionId}'.");
    }

    private static void ValidateDraft(TeamRoster roster, IReadOnlyList<Player> players)
    {
        var counts = players
            .GroupBy(player => player.PositionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var position in roster.Positions)
        {
            counts.TryGetValue(position.Id, out var count);
            if (count < position.Min || count > position.Max)
            {
                throw new InvalidOperationException(
                    $"Draft has {count} '{position.Name}' players; roster requires {position.Min}-{position.Max}.");
            }
        }
    }

    private static string RequireText(string value, string message)
    {
        return string.IsNullOrWhiteSpace(value) ? throw new ArgumentException(message) : value.Trim();
    }
}

public sealed record PlayerDraftPick(string Name, string PositionId);
