using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed partial class PreGameService
{
    private const int PrayerTeamValueStep = 50_000;

    // BB2020: the underdog rolls on the Prayers to Nuffle table once for every full 50,000 gp of Current
    // Team Value difference (re-rolling duplicates). Player-affecting prayers are baked into the match-only
    // teams; prayers whose effect is not yet modelled are still recorded so they can be displayed/logged.
    //
    // Simplifications versus the tabletop rules: the roll count is based on the pre-inducement CTV
    // difference, "choose" selections are made deterministically (lowest jersey number), and prayers with a
    // "this drive"/"this half" duration currently last the whole match.
    private (LeagueTeam Home, LeagueTeam Away, IReadOnlyList<ActivePrayer> Prayers) ApplyPrayersToNuffle(
        Ruleset ruleset,
        TeamRoster homeRoster,
        TeamRoster awayRoster,
        LeagueTeam home,
        LeagueTeam away,
        int homeCtv,
        int awayCtv)
    {
        var rollCount = Math.Abs(homeCtv - awayCtv) / PrayerTeamValueStep;
        if (rollCount == 0 || homeCtv == awayCtv)
        {
            return (home, away, []);
        }

        var homeIsUnderdog = homeCtv < awayCtv;
        var underdog = homeIsUnderdog ? home : away;
        var opponent = homeIsUnderdog ? away : home;
        var underdogRoster = homeIsUnderdog ? homeRoster : awayRoster;
        var underdogTeamId = underdog.Id;

        var prayers = new List<ActivePrayer>();
        foreach (var prayer in RollDistinctPrayers(rollCount))
        {
            var result = ApplyPrayer(ruleset, underdogRoster, underdog, opponent, underdogTeamId, prayer);
            underdog = result.Underdog;
            opponent = result.Opponent;
            prayers.Add(result.Prayer);
        }

        return homeIsUnderdog
            ? (underdog, opponent, prayers)
            : (opponent, underdog, prayers);
    }

    private IReadOnlyList<PrayerToNuffle> RollDistinctPrayers(int rollCount)
    {
        var all = Enum.GetValues<PrayerToNuffle>();
        var capped = Math.Min(rollCount, all.Length);
        var chosen = new List<PrayerToNuffle>(capped);
        // Re-roll duplicates. The safety bound keeps this terminating even though a fair D16 fills the table.
        for (var attempts = 0; chosen.Count < capped && attempts < 1000; attempts++)
        {
            var rolled = (PrayerToNuffle)_dice.RollD16();
            if (!chosen.Contains(rolled))
            {
                chosen.Add(rolled);
            }
        }

        return chosen;
    }

    private (LeagueTeam Underdog, LeagueTeam Opponent, ActivePrayer Prayer) ApplyPrayer(
        Ruleset ruleset,
        TeamRoster underdogRoster,
        LeagueTeam underdog,
        LeagueTeam opponent,
        Guid underdogTeamId,
        PrayerToNuffle prayer)
    {
        switch (prayer)
        {
            case PrayerToNuffle.Stiletto:
            {
                var (team, playerId) = GrantPrayerSkill(underdog, "stab", random: true);
                return (team, opponent, Prayer(prayer, underdogTeamId, playerId, playerId is not null));
            }
            case PrayerToNuffle.KnuckleDusters:
            {
                var (team, playerId) = GrantPrayerSkill(underdog, "mighty-blow", random: false);
                return (team, opponent, Prayer(prayer, underdogTeamId, playerId, playerId is not null));
            }
            case PrayerToNuffle.BlessedStatueOfNuffle:
            {
                var (team, playerId) = GrantPrayerSkill(underdog, "pro", random: false);
                return (team, opponent, Prayer(prayer, underdogTeamId, playerId, playerId is not null));
            }
            case PrayerToNuffle.IntensiveTraining:
            {
                var (team, playerId) = GrantPrayerPrimarySkill(ruleset, underdogRoster, underdog);
                return (team, opponent, Prayer(prayer, underdogTeamId, playerId, playerId is not null));
            }
            case PrayerToNuffle.IronMan:
            {
                var (team, playerId) = AdjustPrayerStat(underdog, eligibleNonLoner: true, armorDelta: 1, movementDelta: 0);
                return (team, opponent, Prayer(prayer, underdogTeamId, playerId, playerId is not null));
            }
            case PrayerToNuffle.GreasyCleats:
            {
                var (team, playerId) = AdjustPrayerStat(opponent, eligibleNonLoner: false, armorDelta: 0, movementDelta: -1);
                return (underdog, team, Prayer(prayer, underdogTeamId, playerId, playerId is not null));
            }
            case PrayerToNuffle.BadHabits:
            {
                var team = GrantLonerToOpponents(opponent, RollD3());
                return (underdog, team, Prayer(prayer, underdogTeamId, playerId: null, effectApplied: true));
            }
            default:
                // Recorded for display/logging; the effect is not yet modelled by the engine.
                return (underdog, opponent, Prayer(prayer, underdogTeamId, playerId: null, effectApplied: false));
        }
    }

    private static ActivePrayer Prayer(PrayerToNuffle prayer, Guid teamId, Guid? playerId, bool effectApplied)
    {
        return new ActivePrayer { Prayer = prayer, TeamId = teamId, PlayerId = playerId, EffectApplied = effectApplied };
    }

    private (LeagueTeam Team, Guid? PlayerId) GrantPrayerSkill(LeagueTeam team, string skillId, bool random)
    {
        var eligible = PrayerEligiblePlayers(team, requireNonLoner: true);
        if (eligible.Count == 0)
        {
            return (team, null);
        }

        var chosen = random ? eligible[PrayerIndex(eligible.Count)] : eligible[0];
        return (ApplyPlayerSkill(team, chosen.Id, skillId), chosen.Id);
    }

    private (LeagueTeam Team, Guid? PlayerId) GrantPrayerPrimarySkill(Ruleset ruleset, TeamRoster roster, LeagueTeam team)
    {
        foreach (var player in PrayerEligiblePlayers(team, requireNonLoner: true))
        {
            var position = roster.Positions.FirstOrDefault(current => string.Equals(current.Id, player.PositionId, StringComparison.OrdinalIgnoreCase));
            if (position is null)
            {
                continue;
            }

            var skill = ruleset.Skills.FirstOrDefault(current =>
                position.PrimarySkillCategories.Contains(current.Category, StringComparer.OrdinalIgnoreCase) &&
                !player.Skills.Contains(current.Id, StringComparer.OrdinalIgnoreCase));
            if (skill is not null)
            {
                return (ApplyPlayerSkill(team, player.Id, skill.Id), player.Id);
            }
        }

        return (team, null);
    }

    private (LeagueTeam Team, Guid? PlayerId) AdjustPrayerStat(LeagueTeam team, bool eligibleNonLoner, int armorDelta, int movementDelta)
    {
        var eligible = PrayerEligiblePlayers(team, requireNonLoner: eligibleNonLoner);
        if (eligible.Count == 0)
        {
            return (team, null);
        }

        var chosen = eligible[0];
        var stats = chosen.Stats with
        {
            Armor = Math.Min(11, chosen.Stats.Armor + armorDelta),
            Movement = Math.Max(1, chosen.Stats.Movement + movementDelta)
        };
        var updated = team with
        {
            Players = team.Players.Select(player => player.Id == chosen.Id ? player with { Stats = stats } : player).ToArray()
        };
        return (updated, chosen.Id);
    }

    private LeagueTeam GrantLonerToOpponents(LeagueTeam team, int count)
    {
        var targets = PrayerEligiblePlayers(team, requireNonLoner: true)
            .OrderBy(_ => PrayerIndex(64))
            .Take(Math.Max(0, count))
            .Select(player => player.Id)
            .ToHashSet();
        if (targets.Count == 0)
        {
            return team;
        }

        return team with
        {
            Players = team.Players
                .Select(player => targets.Contains(player.Id) ? player with { Skills = AddSkill(player.Skills, "loner") } : player)
                .ToArray()
        };
    }

    private static LeagueTeam ApplyPlayerSkill(LeagueTeam team, Guid playerId, string skillId)
    {
        return team with
        {
            Players = team.Players
                .Select(player => player.Id == playerId ? player with { Skills = AddSkill(player.Skills, skillId) } : player)
                .ToArray()
        };
    }

    private static IReadOnlyList<string> AddSkill(IReadOnlyList<string> skills, string skillId)
    {
        return skills.Contains(skillId, StringComparer.OrdinalIgnoreCase)
            ? skills
            : [.. skills, skillId];
    }

    private static List<Player> PrayerEligiblePlayers(LeagueTeam team, bool requireNonLoner)
    {
        return team.Players
            .Where(player => player.Status == PlayerStatus.Available)
            .Where(player => !requireNonLoner || !player.Skills.Contains("loner", StringComparer.OrdinalIgnoreCase))
            .OrderBy(player => player.Number)
            .ToList();
    }

    private int PrayerIndex(int count)
    {
        return count <= 0 ? 0 : (_dice.RollD16() - 1) % count;
    }
}
