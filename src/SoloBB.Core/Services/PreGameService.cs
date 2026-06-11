using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class PreGameService
{
    public const int BribeCost = 100_000;
    private const string StarPlayerInducementId = "star-player";
    private readonly IDiceRoller _dice;

    public PreGameService(IDiceRoller? dice = null)
    {
        _dice = dice ?? new RandomDiceRoller();
    }

    public PreGameSummary BuildSummary(Ruleset ruleset, RosterSet rosterSet, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        var homeRoster = FindRoster(rosterSet, homeTeam);
        var awayRoster = FindRoster(rosterSet, awayTeam);
        var homeTeamValue = CurrentTeamValue(ruleset, homeRoster, homeTeam);
        var awayTeamValue = CurrentTeamValue(ruleset, awayRoster, awayTeam);
        var (homePettyCash, awayPettyCash) = CalculatePettyCash(ruleset, homeTeamValue, awayTeamValue);

        return new PreGameSummary
        {
            Home = BuildTeamSummary(ruleset, rosterSet, homeRoster, homeTeam, homeTeamValue, homePettyCash),
            Away = BuildTeamSummary(ruleset, rosterSet, awayRoster, awayTeam, awayTeamValue, awayPettyCash),
            BribeCost = BribeCost,
            StarPlayersSupported = rosterSet.StarPlayers.Count > 0
        };
    }

    public MatchInducementPlan CreateDefaultPlan(Ruleset ruleset, RosterSet rosterSet, LeagueTeam homeTeam, LeagueTeam awayTeam)
    {
        return CreatePlan(ruleset, rosterSet, homeTeam, awayTeam, homeBribes: 0, awayBribes: 0);
    }

    public MatchInducementPlan CreatePlan(
        Ruleset ruleset,
        RosterSet rosterSet,
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
        var homeRoster = FindRoster(rosterSet, homeTeam);
        var awayRoster = FindRoster(rosterSet, awayTeam);
        var homeTeamValue = CurrentTeamValue(ruleset, homeRoster, homeTeam);
        var awayTeamValue = CurrentTeamValue(ruleset, awayRoster, awayTeam);
        var (homePettyCash, awayPettyCash) = CalculatePettyCash(
            ruleset,
            homeTeamValue,
            awayTeamValue,
            homeTreasurySpent,
            awayTreasurySpent);
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
        var plan = inducements ?? CreateDefaultPlan(ruleset, rosterSet, homeTeam, awayTeam);
        var expectedPlan = CreatePlan(
            ruleset,
            rosterSet,
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
        ValidateStarPlayerPlan(ruleset, rosterSet, homeRoster, homeTeam, plan.Home);
        ValidateStarPlayerPlan(ruleset, rosterSet, awayRoster, awayTeam, plan.Away);
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

    private static TeamPreGameSummary BuildTeamSummary(
        Ruleset ruleset,
        RosterSet rosterSet,
        TeamRoster roster,
        LeagueTeam team,
        int teamValue,
        int pettyCash)
    {
        var journeymenNeeded = JourneymenNeeded(ruleset, team);
        return new TeamPreGameSummary
        {
            TeamId = team.Id,
            TeamName = team.Name,
            TeamValue = teamValue,
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

    private static void ValidateStarPlayerPlan(Ruleset ruleset, RosterSet rosterSet, TeamRoster roster, LeagueTeam team, TeamInducementPlan plan)
    {
        var stars = plan.StarPlayerIds.Select(starId => FindStarPlayer(rosterSet, starId)).ToArray();
        var maximumStars = FindInducement(ruleset, StarPlayerInducementId).MaxCount;
        if (stars.Length > maximumStars)
        {
            throw new InvalidOperationException($"A team can select at most {maximumStars} Star Player{(maximumStars == 1 ? "" : "s")}.");
        }

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
            .GroupBy(inducement => $"{inducement.InducementId}|{inducement.OptionId}", StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Inducement option '{duplicate.Key}' was selected more than once.");
        }

        foreach (var group in plan.Inducements.GroupBy(inducement => inducement.InducementId, StringComparer.OrdinalIgnoreCase))
        {
            var definition = FindInducement(ruleset, group.Key);
            if (group.Sum(selected => selected.Count) > definition.MaxCount)
            {
                throw new InvalidOperationException($"{definition.Name} can be selected at most {definition.MaxCount} time(s).");
            }
        }

        foreach (var selected in plan.Inducements)
        {
            var definition = FindInducement(ruleset, selected.InducementId);
            if (selected.Count < 0)
            {
                throw new InvalidOperationException("Inducement values cannot be negative.");
            }

            if (!definition.MatchEffectImplemented)
            {
                throw new InvalidOperationException($"{definition.Name} is not implemented and cannot be purchased.");
            }

            if (!InducementAvailableToTeam(definition, roster, team))
            {
                throw new InvalidOperationException($"{definition.Name} is not available to {team.Name}.");
            }

            ValidateSelectedOption(definition, roster, selected);
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

    private LeagueTeam ApplyMatchOnlyInducements(Ruleset ruleset, RosterSet rosterSet, TeamRoster roster, LeagueTeam team, TeamInducementPlan plan)
    {
        var journeymanPosition = FindJourneymanPosition(roster);
        var journeymenNeeded = JourneymenNeeded(ruleset, team);
        var journeymen = Enumerable.Range(1, journeymenNeeded)
            .Select(index => CreateJourneyman(journeymanPosition, index))
            .ToArray();
        var starPlayers = plan.StarPlayerIds
            .Select(starId => CreateStarPlayer(FindStarPlayer(rosterSet, starId)))
            .ToArray();
        var riotousRookies = CreateRiotousRookies(roster, SelectedInducementCount(plan, "riotous-rookies"));
        var mercenaries = CreateMercenaries(roster, plan);
        var tacticsStaff = SelectedOptionEffectCount(ruleset, roster, plan, "infamous-coaching-staff", "staff-reroll");
        var recoveryStaff = SelectedOptionEffectCount(ruleset, roster, plan, "infamous-coaching-staff", "staff-recovery");

        return team with
        {
            Treasury = Math.Max(0, team.Treasury - plan.TreasurySpent),
            Rerolls = team.Rerolls + SelectedInducementCount(plan, "extra-team-training") + tacticsStaff,
            Cheerleaders = team.Cheerleaders + SelectedInducementCount(plan, "temp-agency-cheerleader"),
            AssistantCoaches = team.AssistantCoaches + SelectedInducementCount(plan, "part-time-assistant-coach") + tacticsStaff,
            Apothecaries = team.Apothecaries
                + SelectedInducementCount(plan, "wandering-apothecary")
                + SelectedInducementCount(plan, "mortuary-assistant")
                + SelectedInducementCount(plan, "plague-doctor")
                + recoveryStaff,
            Players = [.. team.Players, .. journeymen, .. riotousRookies, .. mercenaries, .. starPlayers]
        };
    }

    private static Player[] CreateMercenaries(TeamRoster roster, TeamInducementPlan plan)
    {
        var players = new List<Player>();
        foreach (var selected in plan.Inducements.Where(selected => string.Equals(selected.InducementId, "mercenary-player", StringComparison.OrdinalIgnoreCase)))
        {
            var position = roster.Positions.First(position => string.Equals(position.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase));
            for (var index = 1; index <= selected.Count; index++)
            {
                players.Add(new Player
                {
                    Id = Guid.NewGuid(),
                    Name = $"Mercenary {position.Name} {index}",
                    PositionId = position.Id,
                    Stats = position.Stats,
                    Skills = position.StartingSkills.Concat(["loner"]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Injuries = ["mercenary"]
                });
            }
        }

        return players.ToArray();
    }

    private Player[] CreateRiotousRookies(TeamRoster roster, int purchases)
    {
        if (purchases == 0)
        {
            return [];
        }

        var position = FindJourneymanPosition(roster);
        var count = Enumerable.Range(0, purchases).Sum(_ => RollD3() + RollD3() + 1);
        return Enumerable.Range(1, count)
            .Select(index => CreateJourneyman(position, index, "Riotous Rookie"))
            .ToArray();
    }

    private static Player CreateJourneyman(PositionTemplate position, int index, string prefix = "Journeyman")
    {
        var skills = position.StartingSkills
            .Concat(["loner"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new Player
        {
            Id = Guid.NewGuid(),
            Name = $"{prefix} {index} {position.Name}",
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
                MatchEffectImplemented = inducement.MatchEffectImplemented,
                PickerOptions = PickerOptions(inducement, roster)
            })
            .OrderBy(inducement => inducement.Cost)
            .ThenBy(inducement => inducement.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<SelectedInducement> NormalizeInducements(IReadOnlyList<SelectedInducement> inducements)
    {
        return inducements
            .Where(inducement => inducement.Count > 0)
            .Select(inducement => inducement with
            {
                InducementId = inducement.InducementId.Trim(),
                OptionId = inducement.OptionId.Trim()
            })
            .ToArray();
    }

    private static int SelectedCost(Ruleset ruleset, TeamRoster roster, TeamInducementPlan plan, IReadOnlyList<StarPlayerDefinition> selectedStars)
    {
        var bribeCost = InducementCost(FindInducement(ruleset, "bribe"), roster);
        var inducementCost = plan.Inducements.Sum(selected =>
        {
            var definition = FindInducement(ruleset, selected.InducementId);
            return SelectedInducementCost(definition, roster, selected) * selected.Count;
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

    public static int SelectedOptionEffectCount(Ruleset ruleset, TeamRoster roster, TeamInducementPlan plan, string inducementId, string effect)
    {
        var definition = FindInducement(ruleset, inducementId);
        return plan.Inducements
            .Where(selected => string.Equals(selected.InducementId, inducementId, StringComparison.OrdinalIgnoreCase))
            .Where(selected => string.Equals(SelectedOptionEffect(definition, roster, selected), effect, StringComparison.OrdinalIgnoreCase))
            .Sum(selected => selected.Count);
    }

    public static string SelectedOptionEffect(Ruleset ruleset, TeamRoster roster, TeamInducementPlan plan, string inducementId)
    {
        var definition = FindInducement(ruleset, inducementId);
        var selected = plan.Inducements.FirstOrDefault(current => string.Equals(current.InducementId, inducementId, StringComparison.OrdinalIgnoreCase));
        return selected is null ? "" : SelectedOptionEffect(definition, roster, selected);
    }

    private static string SelectedOptionEffect(InducementDefinition definition, TeamRoster roster, SelectedInducement selected)
    {
        if (string.Equals(definition.Kind, "mercenary", StringComparison.OrdinalIgnoreCase))
        {
            return roster.Positions.Any(position => string.Equals(position.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase))
                ? "mercenary-player"
                : "";
        }

        return definition.Options.FirstOrDefault(option => string.Equals(option.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase))?.Effect ?? "";
    }

    private static int SelectedInducementCost(InducementDefinition definition, TeamRoster roster, SelectedInducement selected)
    {
        if (string.Equals(definition.Kind, "mercenary", StringComparison.OrdinalIgnoreCase))
        {
            var position = roster.Positions.FirstOrDefault(position => string.Equals(position.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Mercenary position '{selected.OptionId}' is not available to this roster.");
            return position.Cost + 30_000;
        }

        if (!string.IsNullOrWhiteSpace(selected.OptionId))
        {
            return definition.Options.FirstOrDefault(option => string.Equals(option.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase))?.Cost
                ?? throw new InvalidOperationException($"{definition.Name} option '{selected.OptionId}' does not exist.");
        }

        return InducementCost(definition, roster);
    }

    private static void ValidateSelectedOption(InducementDefinition definition, TeamRoster roster, SelectedInducement selected)
    {
        var requiresOption = definition.Options.Count > 0 || string.Equals(definition.Kind, "mercenary", StringComparison.OrdinalIgnoreCase);
        if (requiresOption && string.IsNullOrWhiteSpace(selected.OptionId))
        {
            throw new InvalidOperationException($"{definition.Name} requires an option selection.");
        }

        if (!requiresOption && !string.IsNullOrWhiteSpace(selected.OptionId))
        {
            throw new InvalidOperationException($"{definition.Name} does not accept an option selection.");
        }

        if (string.Equals(definition.Kind, "mercenary", StringComparison.OrdinalIgnoreCase))
        {
            if (!roster.Positions.Any(position => string.Equals(position.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Mercenary position '{selected.OptionId}' is not available to this roster.");
            }
            return;
        }

        if (requiresOption)
        {
            var option = definition.Options.FirstOrDefault(option => string.Equals(option.Id, selected.OptionId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"{definition.Name} option '{selected.OptionId}' does not exist.");
            if (!string.IsNullOrWhiteSpace(option.RequiredSpecialRule) &&
                !roster.SpecialRules.Contains(option.RequiredSpecialRule, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"{option.Name} is not available to this roster.");
            }
        }
    }

    private static InducementOptionSummary[] PickerOptions(InducementDefinition definition, TeamRoster roster)
    {
        if (string.Equals(definition.Kind, "mercenary", StringComparison.OrdinalIgnoreCase))
        {
            return roster.Positions
                .OrderBy(position => position.Cost)
                .ThenBy(position => position.Name, StringComparer.OrdinalIgnoreCase)
                .Select(position => new InducementOptionSummary
                {
                    Id = position.Id,
                    Name = position.Name,
                    Cost = position.Cost + 30_000,
                    Effect = "mercenary-player",
                    Description = $"Temporary {position.Name} with Loner.",
                    PositionId = position.Id,
                    Stats = position.Stats,
                    Skills = position.StartingSkills
                })
                .ToArray();
        }

        return definition.Options
            .Where(option => string.IsNullOrWhiteSpace(option.RequiredSpecialRule) || roster.SpecialRules.Contains(option.RequiredSpecialRule, StringComparer.OrdinalIgnoreCase))
            .OrderBy(option => option.Cost)
            .ThenBy(option => option.Name, StringComparer.OrdinalIgnoreCase)
            .Select(option => new InducementOptionSummary
            {
                Id = option.Id,
                Name = option.Name,
                Cost = option.Cost,
                Effect = option.Effect,
                Description = option.Description
            })
            .ToArray();
    }

    private int RollD3()
    {
        return (_dice.RollD6() + 1) / 2;
    }

    private static int JourneymenNeeded(Ruleset ruleset, LeagueTeam team)
    {
        var availablePlayers = team.Players.Count(player => player.Status == PlayerStatus.Available);
        return Math.Max(0, ruleset.PlayersPerSide - availablePlayers);
    }

    public static int CurrentTeamValue(Ruleset ruleset, TeamRoster roster, LeagueTeam team)
    {
        var unavailableValue = team.Players
            .Where(player => player.Status != PlayerStatus.Available)
            .Sum(player => PlayerValue(ruleset, roster, player));
        var journeymanValue = JourneymenNeeded(ruleset, team) * FindJourneymanPosition(roster).Cost;
        return Math.Max(0, team.TeamValue - unavailableValue + journeymanValue);
    }

    private static int PlayerValue(Ruleset ruleset, TeamRoster roster, Player player)
    {
        var position = roster.Positions.FirstOrDefault(current => string.Equals(current.Id, player.PositionId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Roster '{roster.Id}' does not contain position '{player.PositionId}'.");
        var startingSkills = position.StartingSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var advancementValue = player.Skills
            .Where(skillId => !startingSkills.Contains(skillId))
            .Sum(skillId =>
            {
                var skill = ruleset.Skills.FirstOrDefault(current => string.Equals(current.Id, skillId, StringComparison.OrdinalIgnoreCase));
                if (skill is null)
                {
                    return 0;
                }

                return position.PrimarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase)
                    ? 20_000
                    : position.SecondarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase)
                        ? 40_000
                        : 0;
            });
        return position.Cost + advancementValue;
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
        int homeTeamValue,
        int awayTeamValue,
        int homeTreasurySpent = 0,
        int awayTreasurySpent = 0)
    {
        if (!ruleset.UseTeamValueInducements)
        {
            return (0, 0);
        }

        var difference = Math.Abs(homeTeamValue - awayTeamValue);
        if (difference == 0)
        {
            return (0, 0);
        }

        return homeTeamValue < awayTeamValue
            ? (difference + awayTreasurySpent, 0)
            : (0, difference + homeTreasurySpent);
    }
}
