using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public static class SkillCatalog
{
    public static bool PlayerHasSkillId(Player player, string skillId)
    {
        return player.Skills.Any(skill => string.Equals(skill, skillId, StringComparison.OrdinalIgnoreCase));
    }

    public static bool PlayerHasEffect(Ruleset ruleset, Player player, SkillEffect effect)
    {
        return player.Skills.Any(skill => SkillHasEffect(ruleset, skill, effect));
    }

    public static bool SkillHasEffect(Ruleset ruleset, string skillId, SkillEffect effect)
    {
        return ruleset.Skills.Any(skill =>
            string.Equals(skill.Id, skillId, StringComparison.OrdinalIgnoreCase) &&
            skill.Effects.Contains(effect));
    }

    public static IReadOnlyList<SkillDefinition> GetSkillsForHook(
        Ruleset ruleset,
        Player player,
        GameEventKind eventKind,
        GameEventStage stage)
    {
        return ruleset.Skills
            .Where(skill =>
                player.Skills.Any(playerSkill => string.Equals(playerSkill, skill.Id, StringComparison.OrdinalIgnoreCase)) &&
                skill.Hooks.Any(hook => hook.Event == eventKind && hook.Stage == stage))
            .ToArray();
    }
}
