using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

// A complete initial-team purchase plan produced by the AI GM: the draft picks plus the team
// assets to buy with the starting treasury. Feed it to LeagueService.AddTeam to create the team.
public sealed record GmTeamPlan(
    string TeamName,
    string CoachName,
    TeamRoster Roster,
    IReadOnlyList<PlayerDraftPick> Draft,
    int Rerolls,
    int Apothecaries,
    GmPersonality Personality);

// The AI GM: builds starting rosters and manages AI-controlled teams between matches (spending
// SPP, hiring replacements, and buying team assets). All league mutations are made through
// LeagueService so the same validation as the human path applies.
public sealed class GmAiService
{
    private const int ApothecaryCost = 50_000;
    private const int TargetSquadSize = 13;

    // The marginal value the GM assigns to each successive team reroll when comparing candidate
    // builds. Front-loaded so every team wants two or three, but only cheap-reroll teams buy more.
    private static readonly int[] RerollMarginalValues = [85_000, 70_000, 55_000, 40_000, 25_000, 15_000, 10_000, 5_000];
    private const int ApothecaryScoreValue = 45_000;
    private const int BenchPlayerScoreValue = 20_000;

    private static readonly string[] TeamNamePrefixes =
    [
        "Iron", "Grim", "Black", "Crimson", "Rotten", "Thunder", "Doom", "Wild",
        "Royal", "Savage", "Broken", "Rusty", "Bone", "Storm", "Golden", "Blood"
    ];

    private static readonly string[] TeamNameSuffixes =
    [
        "Maulers", "Reavers", "Chargers", "Wolves", "Ravens", "Smashers", "Giants", "Comets",
        "Butchers", "Knights", "Marauders", "Wanderers", "Hammers", "Serpents", "Vandals", "Griffons"
    ];

    private static readonly string[] CoachFirstNames =
    [
        "Borak", "Elandra", "Grimnir", "Hazel", "Karg", "Lysette", "Mordrek", "Nuffson",
        "Olga", "Piotr", "Quargle", "Rasputin", "Sigmar", "Thessaly", "Ulric", "Vexia"
    ];

    private static readonly string[] CoachLastNames =
    [
        "Ironfoot", "the Sly", "Bonecruncher", "of Altdorf", "Threefingers", "the Younger", "Grimjaw", "Halfpint",
        "von Carstein", "the Bold", "Mudbelly", "Swiftwind", "the Grey", "Skullsplitter", "Fairweather", "the Unlucky"
    ];

    // Skill preferences by rough player role, best first. The purchase logic filters these down to
    // skills that exist in the ruleset, are legal Primary picks for the player, and are not owned.
    private static readonly string[] BallHandlerSkillPreferences =
    [
        "block", "sure-hands", "accurate", "dodge", "leader", "sidestep", "nerves-of-steel", "safe-pair-of-hands",
        "sure-feet", "pro", "fend", "tackle"
    ];

    private static readonly string[] RunnerSkillPreferences =
    [
        "block", "dodge", "catch", "sidestep", "wrestle", "sure-feet", "sprint", "fend",
        "diving-catch", "sure-hands", "pro", "tackle"
    ];

    private static readonly string[] BruiserSkillPreferences =
    [
        "block", "mighty-blow", "guard", "stand-firm", "break-tackle", "tackle", "brawler", "thick-skull",
        "pro", "juggernaut", "arm-bar", "frenzy"
    ];

    private static readonly string[] LinemanSkillPreferences =
    [
        "block", "wrestle", "tackle", "dodge", "fend", "kick", "guard", "mighty-blow",
        "sidestep", "sure-feet", "pro", "dirty-player"
    ];

    // Style is derived from what a player IS (stats and skills), not from the roster it belongs
    // to, so a finesse GM naturally favors goblins on a Black Orc team and catchers on an elf
    // team: the personality picks within whatever palette the roster sheet offers.
    private static readonly HashSet<string> BashStyleSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "mighty-blow", "guard", "frenzy", "grab", "brawler", "juggernaut", "stand-firm",
        "claws", "horns", "tackle", "break-tackle", "arm-bar", "pile-driver", "thick-skull"
    };

    private static readonly HashSet<string> FinesseStyleSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "dodge", "catch", "pass", "sure-hands", "sidestep", "sprint", "sure-feet", "diving-catch",
        "on-the-ball", "leap", "jump-up", "dump-off", "nerves-of-steel", "accurate", "safe-pass",
        "running-pass", "safe-pair-of-hands", "stunty"
    };

    private static readonly HashSet<string> SafeSkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "block", "sure-hands", "sure-feet", "fend", "thick-skull", "safe-pair-of-hands", "sidestep"
    };

    private static readonly HashSet<string> RiskySkills = new(StringComparer.OrdinalIgnoreCase)
    {
        "frenzy", "dauntless", "juggernaut", "pile-driver", "dirty-player", "sprint", "leap", "multiple-block"
    };

    private readonly IDiceRoller _dice;
    private readonly LeagueService _leagueService;

    public GmAiService(IDiceRoller? dice = null, LeagueService? leagueService = null)
    {
        _dice = dice ?? new RandomDiceRoller();
        _leagueService = leagueService ?? new LeagueService(_dice);
    }

    public TeamRoster PickRandomRoster(RosterSet rosterSet)
    {
        if (rosterSet.Rosters.Count == 0)
        {
            throw new InvalidOperationException("Roster set contains no rosters to choose from.");
        }

        return rosterSet.Rosters[RollIndex(rosterSet.Rosters.Count)];
    }

    public string GenerateTeamName(IEnumerable<string>? existingNames = null)
    {
        var taken = (existingNames ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var name = $"{TeamNamePrefixes[RollIndex(TeamNamePrefixes.Length)]} {TeamNameSuffixes[RollIndex(TeamNameSuffixes.Length)]}";
            if (!taken.Contains(name))
            {
                return name;
            }
        }

        return $"{TeamNamePrefixes[RollIndex(TeamNamePrefixes.Length)]} {TeamNameSuffixes[RollIndex(TeamNameSuffixes.Length)]} {_dice.RollD16()}";
    }

    public string GenerateCoachName()
    {
        return $"{CoachFirstNames[RollIndex(CoachFirstNames.Length)]} {CoachLastNames[RollIndex(CoachLastNames.Length)]}";
    }

    public GmPersonality RollPersonality()
    {
        return new GmPersonality
        {
            BashFinesse = RollIndex(5) - 2,
            RiskAversion = RollIndex(5) - 2
        };
    }

    // Builds the best starting team the GM can find for the roster: candidate builds are generated
    // for each affordable reroll count (positional players bought greedily by cost, filled out with
    // linemen), then compared with a simple value heuristic.
    public GmTeamPlan PlanInitialTeam(Ruleset ruleset, TeamRoster roster, GmPersonality? personality = null, string? teamName = null, string? coachName = null)
    {
        var gm = personality ?? new GmPersonality();

        // Rerolls double in price after creation, so the GM never starts below two unless the
        // roster genuinely cannot afford a legal draft alongside them.
        var maxRerolls = Math.Min(ruleset.RerollCap, 6);
        var best = BestCandidate(ruleset, roster, gm, minRerolls: Math.Min(2, maxRerolls), maxRerolls)
            ?? BestCandidate(ruleset, roster, gm, minRerolls: 0, maxRerolls)
            ?? throw new InvalidOperationException($"The AI GM could not build a legal starting team for roster '{roster.Id}'.");

        return new GmTeamPlan(
            teamName ?? GenerateTeamName(),
            coachName ?? GenerateCoachName(),
            roster,
            best.Draft,
            best.Rerolls,
            best.Apothecaries,
            gm);
    }

    // Creates an AI-controlled team from a plan and adds it to the league.
    public League AddPlannedTeam(League league, Ruleset ruleset, GmTeamPlan plan)
    {
        var next = _leagueService.AddTeam(
            league,
            ruleset,
            plan.TeamName,
            plan.CoachName,
            plan.Roster,
            plan.Draft,
            rerolls: plan.Rerolls,
            apothecaries: plan.Apothecaries,
            isAiControlled: true);

        var added = next.Teams[^1] with { GmPersonality = plan.Personality };
        return next with { Teams = [.. next.Teams.Take(next.Teams.Count - 1), added] };
    }

    // Post-match management pass for the AI-controlled teams among teamIds: spends SPP on skill
    // advancements, rehires up to a healthy squad size, and buys an apothecary or reroll when the
    // treasury allows. Safe to call with human team ids; they are ignored.
    public League RunPostMatchDevelopment(League league, Ruleset ruleset, RosterSet rosterSet, IEnumerable<Guid> teamIds)
    {
        foreach (var teamId in teamIds.Distinct())
        {
            var team = league.Teams.FirstOrDefault(current => current.Id == teamId);
            var roster = team is null ? null : FindRoster(rosterSet, team.RosterId);
            if (team is null || roster is null || !team.IsAiControlled)
            {
                continue;
            }

            var gm = team.GmPersonality ?? new GmPersonality();
            league = SpendStarPlayerPoints(league, ruleset, roster, teamId, gm);
            league = HireReplacements(league, ruleset, roster, teamId, gm);
            league = PurchaseTeamAssets(league, ruleset, roster, teamId, gm);
        }

        return league;
    }

    private sealed record DraftCandidate(IReadOnlyList<PlayerDraftPick> Draft, int Rerolls, int Apothecaries, int Score);

    private static DraftCandidate? BestCandidate(Ruleset ruleset, TeamRoster roster, GmPersonality gm, int minRerolls, int maxRerolls)
    {
        DraftCandidate? best = null;
        for (var rerolls = minRerolls; rerolls <= maxRerolls; rerolls++)
        {
            var budget = ruleset.StartingTreasury - (rerolls * roster.RerollCost);
            if (budget < 0)
            {
                break;
            }

            var candidate = BuildDraftCandidate(ruleset, roster, gm, budget, rerolls);
            if (candidate is not null && (best is null || candidate.Score > best.Score))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static DraftCandidate? BuildDraftCandidate(Ruleset ruleset, TeamRoster roster, GmPersonality gm, int budget, int rerolls)
    {
        var linemanPosition = LinemanPosition(roster);
        var counts = roster.Positions.ToDictionary(position => position.Id, _ => 0, StringComparer.OrdinalIgnoreCase);
        var playerCount = 0;
        var positionalPremium = 0;

        // Any roster-mandated minimums come first so the draft always validates.
        foreach (var position in roster.Positions.Where(position => position.Min > 0))
        {
            counts[position.Id] = position.Min;
            playerCount += position.Min;
            budget -= position.Min * position.Cost;
        }

        if (budget < 0)
        {
            return null;
        }

        // Positional players are bought in order of how much this GM values them: the premium over
        // a lineman body, scaled by how well the position's style matches the GM's bash/finesse
        // lean. The reserve keeps enough back to fill the remaining required slots with linemen.
        // Secret-weapon players are left for coaches who enjoy bribes.
        var positionals = roster.Positions
            .Where(position => !string.Equals(position.Id, linemanPosition.Id, StringComparison.OrdinalIgnoreCase))
            .Where(position => !position.StartingSkills.Contains("secret-weapon", StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(position => AdjustedPremium(position, linemanPosition, gm))
            .ThenBy(position => position.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var position in positionals)
        {
            var personalityCap = PersonalityCap(position, gm);
            while (playerCount < ruleset.PlayersPerSide && counts[position.Id] < personalityCap)
            {
                var remainingAfter = budget - position.Cost;
                var linemenStillNeeded = ruleset.PlayersPerSide - (playerCount + 1);
                if (remainingAfter < linemenStillNeeded * linemanPosition.Cost)
                {
                    break;
                }

                counts[position.Id]++;
                playerCount++;
                positionalPremium += AdjustedPremium(position, linemanPosition, gm);
                budget = remainingAfter;
            }
        }

        // Fill the remaining required slots with linemen.
        var linemenToHire = ruleset.PlayersPerSide - playerCount;
        if (linemenToHire > 0)
        {
            if (counts[linemanPosition.Id] + linemenToHire > linemanPosition.Max || budget < linemenToHire * linemanPosition.Cost)
            {
                return null;
            }

            counts[linemanPosition.Id] += linemenToHire;
            playerCount += linemenToHire;
            budget -= linemenToHire * linemanPosition.Cost;
        }

        var apothecaries = 0;
        if (RosterUsesApothecary(roster, linemanPosition) && budget >= ApothecaryCost)
        {
            apothecaries = 1;
            budget -= ApothecaryCost;
        }

        // Spend meaningful leftovers on bench linemen rather than banking gold; cautious GMs carry
        // a deeper bench, reckless ones run lighter.
        var targetSquadSize = TargetSquadSize + Math.Sign(RiskAversion(gm));
        while (playerCount < targetSquadSize && counts[linemanPosition.Id] < linemanPosition.Max && budget >= linemanPosition.Cost)
        {
            counts[linemanPosition.Id]++;
            playerCount++;
            budget -= linemanPosition.Cost;
        }

        // A positional player is worth the (style-adjusted) premium paid over a lineman body (every
        // candidate fields roughly the same number of bodies, so face-value cost would just reward
        // overspending), weighted up because starting skills are cheaper at creation than as
        // advancements. Cautious GMs value the safety assets (rerolls, apothecary, bench) more.
        var rerollScale = 10 + RiskAversion(gm);
        var assetScale = 10 + (2 * RiskAversion(gm));
        var score = (positionalPremium * 3 / 2)
            + (RerollMarginalValues.Take(rerolls).Sum() * rerollScale / 10)
            + (apothecaries * ApothecaryScoreValue * assetScale / 10)
            + (Math.Max(0, playerCount - ruleset.PlayersPerSide) * BenchPlayerScoreValue * assetScale / 10);

        var draft = new List<PlayerDraftPick>();
        foreach (var position in roster.Positions)
        {
            for (var index = 1; index <= counts[position.Id]; index++)
            {
                draft.Add(new PlayerDraftPick($"{position.Name} {index}", position.Id));
            }
        }

        return new DraftCandidate(draft, rerolls, apothecaries, score);
    }

    // How many of a position this GM is willing to field. Favored and neutral archetypes fill to
    // the roster maximum; strongly disfavored ones get squeezed (a finesse GM fields one token
    // Big 'Un, and skips a strongly mismatched big guy altogether).
    private static int PersonalityCap(PositionTemplate position, GmPersonality gm)
    {
        var alignment = BashFinesse(gm) * PositionStyle(position);
        if (alignment > -0.8)
        {
            return position.Max;
        }

        if (position.Max == 1)
        {
            return alignment <= -1.5 ? 0 : 1;
        }

        var reduced = alignment <= -1.5 ? position.Max / 3 : position.Max * 2 / 3;
        return Math.Max(1, reduced);
    }

    // What this GM thinks a positional player is worth beyond a lineman body: the cost premium,
    // scaled up to +/-30% by how well the position's style matches the GM's bash/finesse lean.
    private static int AdjustedPremium(PositionTemplate position, PositionTemplate linemanPosition, GmPersonality gm)
    {
        var premium = position.Cost - linemanPosition.Cost;
        var multiplier = 1.0 + (0.15 * BashFinesse(gm) * PositionStyle(position));
        return (int)Math.Round(premium * multiplier);
    }

    // Where a position sits on the bash..finesse spectrum (-1..+1), derived from its stats and
    // starting skills rather than from the roster it belongs to. That keeps personality relative:
    // the most finesse option on a Black Orc sheet is still a goblin.
    private static double PositionStyle(PositionTemplate position)
    {
        var bash = 0.0;
        var finesse = 0.0;

        if (position.Stats.Strength >= 5)
        {
            bash += 3;
        }
        else if (position.Stats.Strength == 4)
        {
            bash += 2;
        }

        if (position.Stats.Armor >= 10)
        {
            bash += 1;
        }

        if (position.Stats.Movement >= 8)
        {
            finesse += 2;
        }
        else if (position.Stats.Movement == 7)
        {
            finesse += 1;
        }

        if (position.Stats.Agility <= 2)
        {
            finesse += 2;
        }

        if (position.Stats.Passing <= 3)
        {
            finesse += 1;
        }

        bash += position.StartingSkills.Count(skill => BashStyleSkills.Contains(skill));
        finesse += position.StartingSkills.Count(skill => FinesseStyleSkills.Contains(skill));

        return Math.Clamp((finesse - bash) / 4.0, -1.0, 1.0);
    }

    private static int BashFinesse(GmPersonality gm)
    {
        return Math.Clamp(gm.BashFinesse, -2, 2);
    }

    private static int RiskAversion(GmPersonality gm)
    {
        return Math.Clamp(gm.RiskAversion, -2, 2);
    }

    // The roster's "lineman" slot: the position that can field a whole team, cheapest first.
    private static PositionTemplate LinemanPosition(TeamRoster roster)
    {
        return roster.Positions
            .Where(position => !position.StartingSkills.Contains("secret-weapon", StringComparer.OrdinalIgnoreCase))
            .OrderByDescending(position => position.Max)
            .ThenBy(position => position.Cost)
            .First();
    }

    // Undead-style rosters patch players back together with Regeneration instead of paying for an
    // apothecary; use the lineman's starting skills as the tell.
    private static bool RosterUsesApothecary(TeamRoster roster, PositionTemplate linemanPosition)
    {
        return !linemanPosition.StartingSkills.Contains("regeneration", StringComparer.OrdinalIgnoreCase);
    }

    private League SpendStarPlayerPoints(League league, Ruleset ruleset, TeamRoster roster, Guid teamId, GmPersonality gm)
    {
        // Repeat until nobody can buy anything else, so a big SPP haul buys multiple advancements.
        var purchased = true;
        while (purchased)
        {
            purchased = false;
            var team = league.Teams.First(current => current.Id == teamId);
            foreach (var player in team.Players.Where(IsActiveRosterPlayer))
            {
                var skill = PreferredSkillPurchase(ruleset, roster, player, gm);
                if (skill is null)
                {
                    continue;
                }

                league = _leagueService.PurchaseSelectedSkillAdvancement(league, ruleset, roster, teamId, player.Id, skill.Id);
                purchased = true;
                break;
            }
        }

        return league;
    }

    private static SkillDefinition? PreferredSkillPurchase(Ruleset ruleset, TeamRoster roster, Player player, GmPersonality gm)
    {
        var position = roster.Positions.FirstOrDefault(current => string.Equals(current.Id, player.PositionId, StringComparison.OrdinalIgnoreCase));
        if (position is null)
        {
            return null;
        }

        if (!ruleset.AdvancementThresholds.TryGetValue("chosenPrimary", out var cost) || player.StarPlayerPoints < cost)
        {
            return null;
        }

        // BB2020 career cap: six advancements per player.
        var startingSkillIds = position.StartingSkills.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var advancementsTaken = player.Skills.Count(skill => !startingSkillIds.Contains(skill)) + player.CharacteristicImprovements.Count;
        if (advancementsTaken >= 6)
        {
            return null;
        }

        var eligible = ruleset.Skills
            .Where(skill => position.PrimarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase))
            .Where(skill => !skill.DataOnly && !skill.Compulsory)
            .Where(skill => !player.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (eligible.Length == 0)
        {
            return null;
        }

        // Rank the role's preference list, then let personality nudge picks toward or away from
        // bash/finesse/risky skills. Only skills on the role list are considered so the quality
        // floor holds regardless of personality.
        var preferences = SkillPreferencesFor(player);
        SkillDefinition? bestSkill = null;
        var bestScore = int.MinValue;
        for (var index = 0; index < preferences.Length; index++)
        {
            var candidate = eligible.FirstOrDefault(skill => string.Equals(skill.Id, preferences[index], StringComparison.OrdinalIgnoreCase));
            if (candidate is null || IsRedundantPick(player, candidate.Id))
            {
                continue;
            }

            var score = ((preferences.Length - index) * 2)
                + (BashFinesse(gm) * SkillStyle(candidate.Id) * 3)
                + (RiskAversion(gm) * SkillSafety(candidate.Id) * 2);
            if (score > bestScore)
            {
                bestScore = score;
                bestSkill = candidate;
            }
        }

        return bestSkill;
    }

    // +1 finesse, -1 bash, 0 neutral.
    private static int SkillStyle(string skillId)
    {
        return FinesseStyleSkills.Contains(skillId) ? 1 : BashStyleSkills.Contains(skillId) ? -1 : 0;
    }

    // +1 safe, -1 risky, 0 neutral.
    private static int SkillSafety(string skillId)
    {
        return SafeSkills.Contains(skillId) ? 1 : RiskySkills.Contains(skillId) ? -1 : 0;
    }

    // Block and Wrestle solve the same problem; no GM stacks them on one player.
    private static bool IsRedundantPick(Player player, string skillId)
    {
        return (string.Equals(skillId, "block", StringComparison.OrdinalIgnoreCase) && player.Skills.Contains("wrestle", StringComparer.OrdinalIgnoreCase))
            || (string.Equals(skillId, "wrestle", StringComparison.OrdinalIgnoreCase) && player.Skills.Contains("block", StringComparer.OrdinalIgnoreCase));
    }

    private static string[] SkillPreferencesFor(Player player)
    {
        if (player.Skills.Contains("pass", StringComparer.OrdinalIgnoreCase) ||
            player.Skills.Contains("sure-hands", StringComparer.OrdinalIgnoreCase))
        {
            return BallHandlerSkillPreferences;
        }

        if (player.Stats.Strength >= 4 || player.Skills.Contains("mighty-blow", StringComparer.OrdinalIgnoreCase))
        {
            return BruiserSkillPreferences;
        }

        if (player.Stats.Movement >= 7 || player.Skills.Contains("catch", StringComparer.OrdinalIgnoreCase))
        {
            return RunnerSkillPreferences;
        }

        return LinemanSkillPreferences;
    }

    private League HireReplacements(League league, Ruleset ruleset, TeamRoster roster, Guid teamId, GmPersonality gm)
    {
        while (true)
        {
            var team = league.Teams.First(current => current.Id == teamId);
            var activePlayers = team.Players.Where(IsActiveRosterPlayer).ToArray();
            if (activePlayers.Length >= 16)
            {
                return league;
            }

            var belowMatchday = activePlayers.Length < ruleset.PlayersPerSide;
            var hire = ChooseHire(roster, team, activePlayers, belowMatchday, gm);
            if (hire is null)
            {
                return league;
            }

            league = _leagueService.HirePlayer(league, ruleset, roster, teamId, hire.Id, playerName: "");
        }
    }

    private static PositionTemplate? ChooseHire(TeamRoster roster, LeagueTeam team, IReadOnlyList<Player> activePlayers, bool belowMatchday, GmPersonality gm)
    {
        var affordable = roster.Positions
            .Where(position => !position.StartingSkills.Contains("secret-weapon", StringComparer.OrdinalIgnoreCase))
            .Where(position => position.Cost <= team.Treasury)
            .Where(position => activePlayers.Count(player => string.Equals(player.PositionId, position.Id, StringComparison.OrdinalIgnoreCase)) < position.Max)
            .ToArray();
        if (affordable.Length == 0)
        {
            return null;
        }

        if (belowMatchday)
        {
            // Short of a full match-day squad: refill with the cheapest warm body available.
            return affordable.OrderBy(position => position.Cost).First();
        }

        // With a full squad, only extend the bench while gold is plentiful, favorite player first.
        // Cautious GMs keep a bigger rainy-day fund but also carry a deeper bench.
        var linemanPosition = LinemanPosition(roster);
        var savings = Math.Max(0, 50_000 + (RiskAversion(gm) * 25_000));
        var bestAffordable = affordable
            .Where(position => team.Treasury >= position.Cost + savings)
            .Where(position => activePlayers.Count(player => string.Equals(player.PositionId, position.Id, StringComparison.OrdinalIgnoreCase)) < PersonalityCap(position, gm))
            .OrderByDescending(position => AdjustedPremium(position, linemanPosition, gm))
            .FirstOrDefault();
        return activePlayers.Count < TargetSquadSize + Math.Sign(RiskAversion(gm)) ? bestAffordable : null;
    }

    private League PurchaseTeamAssets(League league, Ruleset ruleset, TeamRoster roster, Guid teamId, GmPersonality gm)
    {
        var team = league.Teams.First(current => current.Id == teamId);
        var linemanPosition = LinemanPosition(roster);

        var apothecaries = team.Apothecaries;
        var treasury = team.Treasury;
        if (apothecaries == 0 && RosterUsesApothecary(roster, linemanPosition) && treasury >= ApothecaryCost)
        {
            apothecaries = 1;
            treasury -= ApothecaryCost;
        }

        // Rerolls cost double after creation, so only top up to the target while gold is spare;
        // cautious GMs aim one reroll higher.
        var rerolls = team.Rerolls;
        var baseTarget = roster.RerollCost <= 50_000 ? 3 : 2;
        var targetRerolls = Math.Min(ruleset.RerollCap, baseTarget + (RiskAversion(gm) >= 1 ? 1 : 0));
        while (rerolls < targetRerolls && treasury >= roster.RerollCost * 2)
        {
            rerolls++;
            treasury -= roster.RerollCost * 2;
        }

        if (apothecaries == team.Apothecaries && rerolls == team.Rerolls)
        {
            return league;
        }

        return _leagueService.UpdateTeamManagement(
            league,
            ruleset,
            roster,
            teamId,
            team.Name,
            team.CoachName,
            rerolls,
            team.DedicatedFans,
            team.Cheerleaders,
            team.AssistantCoaches,
            apothecaries);
    }

    private static TeamRoster? FindRoster(RosterSet rosterSet, string rosterId)
    {
        return rosterSet.Rosters.FirstOrDefault(roster => string.Equals(roster.Id, rosterId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsActiveRosterPlayer(Player player)
    {
        return player.Status is not PlayerStatus.Dead and not PlayerStatus.Retired;
    }

    private int RollIndex(int count)
    {
        // Two chained D16s give 256 evenly-mixed outcomes; enough spread for any list used here.
        var roll = ((_dice.RollD16() - 1) * 16) + (_dice.RollD16() - 1);
        return roll % count;
    }
}
