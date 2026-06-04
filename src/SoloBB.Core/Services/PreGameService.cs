using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class PreGameService
{
    public const int BribeCost = 100_000;

    public PreGameSummary BuildSummary(Ruleset ruleset, RosterSet rosterSet, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        var homeRoster = FindRoster(rosterSet, homeTeam);
        var awayRoster = FindRoster(rosterSet, awayTeam);
        var (homePettyCash, awayPettyCash) = CalculatePettyCash(ruleset, homeTeam, awayTeam);

        return new PreGameSummary
        {
            Home = BuildTeamSummary(ruleset, homeRoster, homeTeam, homePettyCash),
            Away = BuildTeamSummary(ruleset, awayRoster, awayTeam, awayPettyCash),
            BribeCost = BribeCost,
            StarPlayersSupported = rosterSet.StarPlayers.Count > 0
        };
    }

    public MatchInducementPlan CreateDefaultPlan(Ruleset ruleset, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        var (homePettyCash, awayPettyCash) = CalculatePettyCash(ruleset, homeTeam, awayTeam);
        return new MatchInducementPlan
        {
            Home = new TeamInducementPlan { TeamId = homeTeam.Id, PettyCash = homePettyCash },
            Away = new TeamInducementPlan { TeamId = awayTeam.Id, PettyCash = awayPettyCash }
        };
    }

    public MatchInducementPlan CreatePlan(
        Ruleset ruleset,
        LeagueTeam homeTeam,
        LeagueTeam awayTeam,
        int homeBribes,
        int awayBribes,
        int homeTreasurySpent = 0,
        int awayTreasurySpent = 0)
    {
        var (homePettyCash, awayPettyCash) = CalculatePettyCash(ruleset, homeTeam, awayTeam);
        var home = CreateTeamPlan(homeTeam, homePettyCash, homeBribes, homeTreasurySpent);
        var away = CreateTeamPlan(awayTeam, awayPettyCash, awayBribes, awayTreasurySpent);

        return new MatchInducementPlan { Home = home, Away = away };
    }

    public PreparedPreGameMatch PrepareMatch(
        Ruleset ruleset,
        RosterSet rosterSet,
        LeagueTeam homeTeam,
        LeagueTeam awayTeam,
        MatchInducementPlan? inducements = null)
    {
        var summary = BuildSummary(ruleset, rosterSet, homeTeam, awayTeam);
        var plan = inducements ?? CreateDefaultPlan(ruleset, homeTeam, awayTeam);
        var expectedPlan = CreateDefaultPlan(ruleset, homeTeam, awayTeam);
        ValidateExpectedPettyCash(plan.Home, expectedPlan.Home);
        ValidateExpectedPettyCash(plan.Away, expectedPlan.Away);
        ValidatePlan(homeTeam, plan.Home);
        ValidatePlan(awayTeam, plan.Away);

        var homeRoster = FindRoster(rosterSet, homeTeam);
        var awayRoster = FindRoster(rosterSet, awayTeam);
        ValidateStarPlayerPlan(rosterSet, homeRoster, homeTeam, plan.Home);
        ValidateStarPlayerPlan(rosterSet, awayRoster, awayTeam, plan.Away);
        return new PreparedPreGameMatch
        {
            HomeTeam = ApplyMatchOnlyInducements(ruleset, rosterSet, homeRoster, homeTeam, plan.Home),
            AwayTeam = ApplyMatchOnlyInducements(ruleset, rosterSet, awayRoster, awayTeam, plan.Away),
            Inducements = plan,
            Summary = summary
        };
    }

    private static TeamPreGameSummary BuildTeamSummary(Ruleset ruleset, TeamRoster roster, LeagueTeam team, int pettyCash)
    {
        var journeymenNeeded = JourneymenNeeded(ruleset, team);
        return new TeamPreGameSummary
        {
            TeamId = team.Id,
            TeamName = team.Name,
            TeamValue = team.TeamValue,
            Treasury = team.Treasury,
            PettyCash = pettyCash,
            JourneymenNeeded = journeymenNeeded,
            MaximumBribesFromPettyCash = pettyCash / BribeCost
        };
    }

    private static TeamInducementPlan CreateTeamPlan(LeagueTeam team, int pettyCash, int bribes, int treasurySpent)
    {
        var plan = new TeamInducementPlan
        {
            TeamId = team.Id,
            PettyCash = pettyCash,
            Bribes = bribes,
            TreasurySpent = treasurySpent
        };
        ValidatePlan(team, plan);
        return plan;
    }

    private static void ValidatePlan(LeagueTeam team, TeamInducementPlan plan)
    {
        if (team.Id != plan.TeamId)
        {
            throw new InvalidOperationException("Inducement plan does not match the team.");
        }

        if (plan.PettyCash < 0 || plan.TreasurySpent < 0 || plan.Bribes < 0)
        {
            throw new InvalidOperationException("Inducement values cannot be negative.");
        }

        if (plan.TreasurySpent > team.Treasury)
        {
            throw new InvalidOperationException("A team cannot spend more treasury than it has.");
        }

        if (plan.Bribes * BribeCost > plan.PettyCash + plan.TreasurySpent)
        {
            throw new InvalidOperationException("Inducement budget does not cover the selected bribes.");
        }
    }

    private static void ValidateStarPlayerPlan(RosterSet rosterSet, TeamRoster roster, LeagueTeam team, TeamInducementPlan plan)
    {
        var stars = plan.StarPlayerIds.Select(starId => FindStarPlayer(rosterSet, starId)).ToArray();
        var duplicateStar = stars
            .GroupBy(star => star.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateStar is not null)
        {
            throw new InvalidOperationException($"Star player '{duplicateStar.Key}' was selected more than once.");
        }

        foreach (var star in stars)
        {
            if (!star.SpecialRules.Any(rule => roster.SpecialRules.Contains(rule, StringComparer.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"{star.Name} is not eligible for {team.Name}.");
            }
        }

        var starCost = stars.Sum(star => star.Cost);
        if ((plan.Bribes * BribeCost) + starCost > plan.PettyCash + plan.TreasurySpent)
        {
            throw new InvalidOperationException("Inducement budget does not cover the selected star players.");
        }
    }

    private static void ValidateExpectedPettyCash(TeamInducementPlan plan, TeamInducementPlan expected)
    {
        if (plan.TeamId != expected.TeamId || plan.PettyCash != expected.PettyCash)
        {
            throw new InvalidOperationException("Inducement plan petty cash does not match the team value comparison.");
        }
    }

    private static LeagueTeam ApplyMatchOnlyInducements(Ruleset ruleset, RosterSet rosterSet, TeamRoster roster, LeagueTeam team, TeamInducementPlan plan)
    {
        var journeymanPosition = FindJourneymanPosition(roster);
        var journeymenNeeded = JourneymenNeeded(ruleset, team);
        var journeymen = Enumerable.Range(1, journeymenNeeded)
            .Select(index => CreateJourneyman(journeymanPosition, index))
            .ToArray();
        var starPlayers = plan.StarPlayerIds
            .Select(starId => CreateStarPlayer(FindStarPlayer(rosterSet, starId)))
            .ToArray();

        return team with
        {
            Treasury = Math.Max(0, team.Treasury - plan.TreasurySpent),
            Players = [.. team.Players, .. journeymen, .. starPlayers]
        };
    }

    private static Player CreateJourneyman(PositionTemplate position, int index)
    {
        var skills = position.StartingSkills
            .Concat(["loner"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Player
        {
            Id = Guid.NewGuid(),
            Name = $"Journeyman {index} {position.Name}",
            PositionId = position.Id,
            Stats = position.Stats,
            Skills = skills,
            Injuries = ["journeyman"]
        };
    }

    private static Player CreateStarPlayer(StarPlayerDefinition star)
    {
        return new Player
        {
            Id = Guid.NewGuid(),
            Name = star.Name,
            PositionId = $"star:{star.Id}",
            Stats = star.Stats,
            Skills = star.Skills,
            Injuries = ["star-player"]
        };
    }

    private static StarPlayerDefinition FindStarPlayer(RosterSet rosterSet, string starPlayerId)
    {
        return rosterSet.StarPlayers.FirstOrDefault(star => string.Equals(star.Id, starPlayerId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Roster set '{rosterSet.Id}' does not contain star player '{starPlayerId}'.");
    }

    private static int JourneymenNeeded(Ruleset ruleset, LeagueTeam team)
    {
        var availablePlayers = team.Players.Count(player => player.Status == PlayerStatus.Available);
        return Math.Max(0, ruleset.PlayersPerSide - availablePlayers);
    }

    private static PositionTemplate FindJourneymanPosition(TeamRoster roster)
    {
        return roster.Positions
            .OrderByDescending(position => string.Equals(position.Id, "lineman", StringComparison.OrdinalIgnoreCase))
            .ThenBy(position => position.Cost)
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Roster '{roster.Id}' has no positions for journeymen.");
    }

    private static TeamRoster FindRoster(RosterSet rosterSet, LeagueTeam team)
    {
        return rosterSet.Rosters.FirstOrDefault(roster => string.Equals(roster.Id, team.RosterId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Roster set '{rosterSet.Id}' does not contain roster '{team.RosterId}'.");
    }

    private static (int HomePettyCash, int AwayPettyCash) CalculatePettyCash(Ruleset ruleset, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        if (!ruleset.UseTeamValueInducements)
        {
            return (0, 0);
        }

        var difference = Math.Abs(homeTeam.TeamValue - awayTeam.TeamValue);
        if (difference == 0)
        {
            return (0, 0);
        }

        return homeTeam.TeamValue < awayTeam.TeamValue
            ? (difference, 0)
            : (0, difference);
    }
}
