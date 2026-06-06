using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class PreGameService
{
    public const int BribeCost = 100_000;
    private const string StarPlayerInducementId = "star-player";

    public PreGameSummary BuildSummary(Ruleset ruleset, RosterSet rosterSet, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        var homeRoster = FindRoster(rosterSet, homeTeam);
        var awayRoster = FindRoster(rosterSet, awayTeam);
        var (homePettyCash, awayPettyCash) = CalculatePettyCash(ruleset, homeTeam, awayTeam);

        return new PreGameSummary
        {
            Home = BuildTeamSummary(ruleset, rosterSet, homeRoster, homeTeam, homePettyCash),
            Away = BuildTeamSummary(ruleset, rosterSet, awayRoster, awayTeam, awayPettyCash),
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
        int awayTreasurySpent = 0,
        IReadOnlyList<SelectedInducement>? homeInducements = null,
        IReadOnlyList<SelectedInducement>? awayInducements = null,
        IReadOnlyList<string>? homeStarPlayerIds = null,
        IReadOnlyList<string>? awayStarPlayerIds = null)
    {
        var (homePettyCash, awayPettyCash) = CalculatePettyCash(ruleset, homeTeam, awayTeam, homeTreasurySpent, awayTreasurySpent);
        var home = CreateTeamPlan(homeTeam, homePettyCash, homeBribes, homeTreasurySpent, homeInducements ?? [], homeStarPlayerIds ?? []);
        var away = CreateTeamPlan(awayTeam, awayPettyCash, awayBribes, awayTreasurySpent, awayInducements ?? [], awayStarPlayerIds ?? []);

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
        var expectedPlan = CreatePlan(
            ruleset,
            homeTeam,
            awayTeam,
            homeBribes: plan.Home.Bribes,
            awayBribes: plan.Away.Bribes,
            homeTreasurySpent: plan.Home.TreasurySpent,
            awayTreasurySpent: plan.Away.TreasurySpent,
            homeInducements: plan.Home.Inducements,
            awayInducements: plan.Away.Inducements,
            homeStarPlayerIds: plan.Home.StarPlayerIds,
            awayStarPlayerIds: plan.Away.StarPlayerIds);
        ValidateExpectedPettyCash(plan.Home, expectedPlan.Home);
        ValidateExpectedPettyCash(plan.Away, expectedPlan.Away);
        ValidatePlan(homeTeam, plan.Home);
        ValidatePlan(awayTeam, plan.Away);
        ValidateSharedStarPlayers(plan.Home, plan.Away);

        var homeRoster = FindRoster(rosterSet, homeTeam);
        var awayRoster = FindRoster(rosterSet, awayTeam);
        ValidateInducementPlan(ruleset, homeRoster, homeTeam, plan.Home);
        ValidateInducementPlan(ruleset, awayRoster, awayTeam, plan.Away);
        ValidateStarPlayerPlan(rosterSet, homeRoster, homeTeam, plan.Home);
        ValidateStarPlayerPlan(rosterSet, awayRoster, awayTeam, plan.Away);
        ValidateCompleteBudget(ruleset, rosterSet, homeRoster, plan.Home);
        ValidateCompleteBudget(ruleset, rosterSet, awayRoster, plan.Away);
        return new PreparedPreGameMatch
        {
            HomeTeam = ApplyMatchOnlyInducements(ruleset, rosterSet, homeRoster, homeTeam, plan.Home),
            AwayTeam = ApplyMatchOnlyInducements(ruleset, rosterSet, awayRoster, awayTeam, plan.Away),
            Inducements = plan,
            Summary = summary
        };
    }

    private static TeamPreGameSummary BuildTeamSummary(Ruleset ruleset, RosterSet rosterSet, TeamRoster roster, LeagueTeam team, int pettyCash)
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
            MaximumBribesFromPettyCash = pettyCash / BribeCost,
            SpecialRules = roster.SpecialRules,
            RosterRestrictions = roster.RosterRestrictions,
            AvailableInducements = AvailableInducements(ruleset, roster, team),
            EligibleStarPlayers = EligibleStarPlayers(rosterSet, roster)
        };
    }

    private static TeamInducementPlan CreateTeamPlan(
        LeagueTeam team,
        int pettyCash,
        int bribes,
        int treasurySpent,
        IReadOnlyList<SelectedInducement> inducements,
        IReadOnlyList<string> starPlayerIds)
    {
        var plan = new TeamInducementPlan
        {
            TeamId = team.Id,
            PettyCash = pettyCash,
            Bribes = bribes,
            TreasurySpent = treasurySpent,
            Inducements = NormalizeInducements(inducements),
            StarPlayerIds = starPlayerIds
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

    }

    private static void ValidateInducementPlan(Ruleset ruleset, TeamRoster roster, LeagueTeam team, TeamInducementPlan plan)
    {
        var bribeDefinition = FindInducement(ruleset, "bribe");
        var totalBribes = plan.Bribes + SelectedInducementCount(plan, "bribe");
        if (totalBribes > bribeDefinition.MaxCount)
        {
            throw new InvalidOperationException($"{bribeDefinition.Name} can be selected at most {bribeDefinition.MaxCount} time(s).");
        }

        var duplicate = plan.Inducements
            .GroupBy(inducement => inducement.InducementId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Inducement '{duplicate.Key}' was selected more than once.");
        }

        foreach (var selected in plan.Inducements)
        {
            var definition = FindInducement(ruleset, selected.InducementId);
            if (selected.Count < 0)
            {
                throw new InvalidOperationException("Inducement values cannot be negative.");
            }

            if (selected.Count > definition.MaxCount)
            {
                throw new InvalidOperationException($"{definition.Name} can be selected at most {definition.MaxCount} time(s).");
            }

            if (definition.Cost == 0 && !definition.MatchEffectImplemented)
            {
                throw new InvalidOperationException($"{definition.Name} requires a detailed selection before it can be purchased.");
            }

            if (!InducementAvailableToTeam(definition, roster, team))
            {
                throw new InvalidOperationException($"{definition.Name} is not available to {team.Name}.");
            }
        }

        if (SelectedCost(ruleset, roster, plan, []) > plan.PettyCash + plan.TreasurySpent)
        {
            throw new InvalidOperationException("Inducement budget does not cover the selected inducements.");
        }
    }

    private static void ValidateCompleteBudget(Ruleset ruleset, RosterSet rosterSet, TeamRoster roster, TeamInducementPlan plan)
    {
        var stars = plan.StarPlayerIds.Select(starId => FindStarPlayer(rosterSet, starId)).ToArray();
        if (SelectedCost(ruleset, roster, plan, stars) > plan.PettyCash + plan.TreasurySpent)
        {
            throw new InvalidOperationException("Inducement budget does not cover the selected inducements and star players.");
        }
    }

    private static void ValidateExpectedPettyCash(TeamInducementPlan plan, TeamInducementPlan expected)
    {
        if (plan.TeamId != expected.TeamId || plan.PettyCash != expected.PettyCash)
        {
            throw new InvalidOperationException("Inducement plan petty cash does not match the team value comparison.");
        }
    }

    private static void ValidateSharedStarPlayers(TeamInducementPlan home, TeamInducementPlan away)
    {
        var sharedStar = home.StarPlayerIds
            .FirstOrDefault(starId => away.StarPlayerIds.Contains(starId, StringComparer.OrdinalIgnoreCase));
        if (sharedStar is not null)
        {
            throw new InvalidOperationException($"Star player '{sharedStar}' cannot be selected by both teams.");
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
            Rerolls = team.Rerolls + SelectedInducementCount(plan, "extra-team-training"),
            Cheerleaders = team.Cheerleaders + SelectedInducementCount(plan, "temp-agency-cheerleader"),
            AssistantCoaches = team.AssistantCoaches + SelectedInducementCount(plan, "part-time-assistant-coach"),
            Apothecaries = team.Apothecaries
                + SelectedInducementCount(plan, "wandering-apothecary")
                + SelectedInducementCount(plan, "mortuary-assistant")
                + SelectedInducementCount(plan, "plague-doctor"),
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

    private static EligibleStarPlayerSummary[] EligibleStarPlayers(RosterSet rosterSet, TeamRoster roster)
    {
        return rosterSet.StarPlayers
            .Select(star => new
            {
                Star = star,
                MatchedRules = star.SpecialRules
                    .Where(rule => roster.SpecialRules.Contains(rule, StringComparer.OrdinalIgnoreCase))
                    .ToArray()
            })
            .Where(candidate => candidate.MatchedRules.Length > 0)
            .OrderBy(candidate => candidate.Star.Cost)
            .ThenBy(candidate => candidate.Star.Name, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => new EligibleStarPlayerSummary
            {
                Id = candidate.Star.Id,
                Name = candidate.Star.Name,
                Cost = candidate.Star.Cost,
                Stats = candidate.Star.Stats,
                Skills = candidate.Star.Skills,
                MatchedSpecialRules = candidate.MatchedRules
            })
            .ToArray();
    }

    private static AvailableInducementSummary[] AvailableInducements(Ruleset ruleset, TeamRoster roster, LeagueTeam team)
    {
        return ruleset.Inducements
            .Where(inducement => !string.Equals(inducement.Id, StarPlayerInducementId, StringComparison.OrdinalIgnoreCase))
            .Where(inducement => InducementAvailableToTeam(inducement, roster, team))
            .Select(inducement => new AvailableInducementSummary
            {
                Id = inducement.Id,
                Name = inducement.Name,
                Cost = InducementCost(inducement, roster),
                MaxCount = inducement.MaxCount,
                Kind = inducement.Kind,
                Description = inducement.Description,
                MatchEffectImplemented = inducement.MatchEffectImplemented
            })
            .OrderBy(inducement => inducement.Cost)
            .ThenBy(inducement => inducement.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SelectedInducement> NormalizeInducements(IReadOnlyList<SelectedInducement> inducements)
    {
        return inducements
            .Where(inducement => inducement.Count > 0)
            .Select(inducement => inducement with { InducementId = inducement.InducementId.Trim() })
            .ToArray();
    }

    private static int SelectedCost(Ruleset ruleset, TeamRoster roster, TeamInducementPlan plan, IReadOnlyList<StarPlayerDefinition> selectedStars)
    {
        var bribeCost = InducementCost(FindInducement(ruleset, "bribe"), roster);
        var inducementCost = plan.Inducements.Sum(selected =>
        {
            var definition = FindInducement(ruleset, selected.InducementId);
            return InducementCost(definition, roster) * selected.Count;
        });
        return (plan.Bribes * bribeCost) + inducementCost + selectedStars.Sum(star => star.Cost);
    }

    private static int InducementCost(InducementDefinition inducement, TeamRoster roster)
    {
        if (inducement.DiscountedCost is int discountedCost &&
            !string.IsNullOrWhiteSpace(inducement.DiscountSpecialRule) &&
            roster.SpecialRules.Contains(inducement.DiscountSpecialRule, StringComparer.OrdinalIgnoreCase))
        {
            return discountedCost;
        }

        return inducement.Cost;
    }

    private static bool InducementAvailableToTeam(InducementDefinition inducement, TeamRoster roster, LeagueTeam team)
    {
        if (!string.IsNullOrWhiteSpace(inducement.RequiredSpecialRule) &&
            !roster.SpecialRules.Contains(inducement.RequiredSpecialRule, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (inducement.RequiresApothecaryAccess && !TeamCanHireApothecary(roster, team))
        {
            return false;
        }

        return true;
    }

    private static bool TeamCanHireApothecary(TeamRoster roster, LeagueTeam team)
    {
        if (team.Apothecaries > 0)
        {
            return true;
        }

        return !roster.SpecialRules.Any(rule => rule is "sylvanian-spotlight" or "favoured-of-nurgle");
    }

    private static InducementDefinition FindInducement(Ruleset ruleset, string inducementId)
    {
        return ruleset.Inducements.FirstOrDefault(inducement => string.Equals(inducement.Id, inducementId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Ruleset '{ruleset.Id}' does not contain inducement '{inducementId}'.");
    }

    public static int SelectedInducementCount(TeamInducementPlan plan, string inducementId)
    {
        return plan.Inducements
            .Where(inducement => string.Equals(inducement.InducementId, inducementId, StringComparison.OrdinalIgnoreCase))
            .Sum(inducement => inducement.Count);
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

    private static (int HomePettyCash, int AwayPettyCash) CalculatePettyCash(
        Ruleset ruleset,
        LeagueTeam homeTeam,
        LeagueTeam awayTeam,
        int homeTreasurySpent = 0,
        int awayTreasurySpent = 0)
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
            ? (difference + awayTreasurySpent, 0)
            : (0, difference + homeTreasurySpent);
    }
}
