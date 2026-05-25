using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class LeagueService
{
    public League CreateLeague(string name, Ruleset ruleset, IEnumerable<RosterSet> rosterSets)
    {
        var rosterSetIds = rosterSets.Select(set => set.Id).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (rosterSetIds.Length == 0)
        {
            throw new InvalidOperationException("At least one roster set is required to create a league.");
        }

        return new League
        {
            Id = Guid.NewGuid(),
            Name = RequireText(name, "League name is required."),
            RulesetId = ruleset.Id,
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
        int fanFactor = 0)
    {
        if (!league.RosterSetIds.Any())
        {
            throw new InvalidOperationException("League has no roster sets configured.");
        }

        var players = draft.Select(pick => CreatePlayer(roster, pick)).ToArray();
        ValidateDraft(roster, players);

        if (players.Length != ruleset.PlayersPerSide)
        {
            throw new InvalidOperationException($"Draft has {players.Length} players; ruleset requires {ruleset.PlayersPerSide}.");
        }

        if (rerolls < 0 || rerolls > ruleset.RerollCap)
        {
            throw new InvalidOperationException($"Rerolls must be between 0 and {ruleset.RerollCap}.");
        }

        if (fanFactor < 0)
        {
            throw new InvalidOperationException("Fan factor cannot be negative.");
        }

        var playerCost = players.Sum(player => FindPosition(roster, player.PositionId).Cost);
        var rerollCost = rerolls * roster.RerollCost;
        var totalCost = playerCost + rerollCost;

        if (totalCost > ruleset.StartingTreasury)
        {
            throw new InvalidOperationException($"Team cost {totalCost} exceeds starting treasury {ruleset.StartingTreasury}.");
        }

        var treasury = ruleset.StartingTreasury - totalCost;

        var team = new LeagueTeam
        {
            Id = Guid.NewGuid(),
            Name = RequireText(teamName, "Team name is required."),
            CoachName = string.IsNullOrWhiteSpace(coachName) ? "Solo Coach" : coachName,
            RosterId = roster.Id,
            Treasury = treasury,
            Rerolls = rerolls,
            FanFactor = fanFactor,
            Players = players
        };

        return league with { Teams = [.. league.Teams, team] };
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
