using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class RulesetValidator
{
    private static readonly ISet<string> KnownBehaviorSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "animal-savagery",
        "always-hungry",
        "ball-and-chain",
        "bombardier",
        "bone-head",
        "bloodlust",
        "breathe-fire",
        "chainsaw",
        "decay",
        "hypnotic-gaze",
        "kick-team-mate",
        "loner",
        "no-hands",
        "pick-me-up",
        "plague-ridden",
        "projectile-vomit",
        "really-stupid",
        "regeneration",
        "right-stuff",
        "secret-weapon",
        "stab",
        "stunty",
        "swoop",
        "take-root",
        "titchy",
        "throw-team-mate",
        "unchannelled-fury"
    };

    public void Validate(Ruleset ruleset)
    {
        RequireText(ruleset.Id, "Ruleset id is required.");
        RequireText(ruleset.Name, "Ruleset name is required.");
        RequireText(ruleset.Version, "Ruleset version is required.");

        if (ruleset.PitchWidth <= 0 || ruleset.PitchHeight <= 0)
        {
            throw new InvalidDataException("Pitch dimensions must be positive.");
        }

        if (ruleset.PlayersPerSide <= 0)
        {
            throw new InvalidDataException("Players per side must be positive.");
        }

        var duplicateSkill = ruleset.Skills
            .GroupBy(skill => skill.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateSkill is not null)
        {
            throw new InvalidDataException($"Duplicate skill id '{duplicateSkill.Key}'.");
        }

        foreach (var skill in ruleset.Skills)
        {
            RequireText(skill.Id, "Skill id is required.");
            RequireText(skill.Name, $"Skill '{skill.Id}' name is required.");
            RequireText(skill.Category, $"Skill '{skill.Id}' category is required.");

            if (skill.Effects.Count == 0 && !KnownBehaviorSkillIds.Contains(skill.Id) && !skill.DataOnly)
            {
                throw new InvalidDataException($"Skill '{skill.Id}' has no known behavior coverage and must be marked dataOnly.");
            }
        }

        var duplicateInducement = ruleset.Inducements
            .GroupBy(inducement => inducement.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateInducement is not null)
        {
            throw new InvalidDataException($"Duplicate inducement id '{duplicateInducement.Key}'.");
        }

        foreach (var inducement in ruleset.Inducements)
        {
            RequireText(inducement.Id, "Inducement id is required.");
            RequireText(inducement.Name, $"Inducement '{inducement.Id}' name is required.");
            RequireText(inducement.Kind, $"Inducement '{inducement.Id}' kind is required.");
            if (inducement.Cost < 0)
            {
                throw new InvalidDataException($"Inducement '{inducement.Id}' has a negative cost.");
            }
        }
    }

    private static void RequireText(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(message);
        }
    }
}
