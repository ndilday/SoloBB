using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public static class SkillHookResolver
{
    public static bool PlayerHasHookedEffect(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage,
        SkillEffect effect)
    {
        return SkillCatalog
            .GetSkillsForHook(ruleset, player, eventKind, stage)
            .Any(skill => skill.Effects.Contains(effect));
    }

    public static bool PlayerHasHookedSkillId(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage,
        string skillId)
    {
        return SkillCatalog
            .GetSkillsForHook(ruleset, player, eventKind, stage)
            .Any(skill => string.Equals(skill.Id, skillId, StringComparison.OrdinalIgnoreCase));
    }

    public static IReadOnlyList<string> SkillIdsForHookedEffect(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage,
        SkillEffect effect)
    {
        return SkillCatalog
            .GetSkillsForHook(ruleset, player, eventKind, stage)
            .Where(skill => skill.Effects.Contains(effect))
            .Select(skill => skill.Id)
            .ToArray();
    }
}
