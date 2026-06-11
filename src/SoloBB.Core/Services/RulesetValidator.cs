using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class RulesetValidator
{
    private static readonly ISet<string> KnownSkillCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "agility",
        "general",
        "mutation",
        "passing",
        "strength",
        "trait"
    };

    private static readonly ISet<string> KnownInducementKinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "apothecary",
        "bribe",
        "cheerleader",
        "coach",
        "coachingStaff",
        "chef",
        "mage",
        "mercenary",
        "players",
        "recovery",
        "referee",
        "reroll",
        "specialPlay",
        "special",
        "starPlayer"
    };

    private static readonly ISet<string> RequiredAdvancementThresholds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "randomPrimary",
        "chosenPrimary",
        "randomSecondary",
        "chosenSecondary",
        "characteristic"
    };

    private static readonly ISet<string> KnownInducementOptionEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "staff-reroll",
        "staff-recovery",
        "wizard-fireball",
        "wizard-lightning",
        "referee-friendly",
        "referee-intimidating"
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

        if (ruleset.TurnsPerHalf <= 0)
        {
            throw new InvalidDataException("Turns per half must be positive.");
        }

        if (ruleset.StartingTreasury < 0)
        {
            throw new InvalidDataException("Starting treasury cannot be negative.");
        }

        if (ruleset.RerollCap < 0)
        {
            throw new InvalidDataException("Reroll cap cannot be negative.");
        }

        if (ruleset.Dice is null)
        {
            throw new InvalidDataException("Dice rules are required.");
        }

        if (ruleset.Dice.BlockDieFaces <= 0 || ruleset.Dice.AgilityDieFaces <= 0)
        {
            throw new InvalidDataException("Dice face counts must be positive.");
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
            RequireText(skill.Description, $"Skill '{skill.Id}' description is required.");

            if (!KnownSkillCategories.Contains(skill.Category))
            {
                throw new InvalidDataException($"Skill '{skill.Id}' references unknown category '{skill.Category}'.");
            }

            if (skill.Effects.Count == 0 && skill.Hooks.Count == 0 && !skill.DataOnly)
            {
                throw new InvalidDataException($"Skill '{skill.Id}' has no known behavior coverage and must be marked dataOnly.");
            }

            var duplicateHook = skill.Hooks
                .GroupBy(hook => new { hook.Event, hook.Stage })
                .FirstOrDefault(group => group.Count() > 1);

            if (duplicateHook is not null)
            {
                throw new InvalidDataException($"Skill '{skill.Id}' declares duplicate hook '{duplicateHook.Key.Event}.{duplicateHook.Key.Stage}'.");
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
            RequireText(inducement.Description, $"Inducement '{inducement.Id}' description is required.");
            if (inducement.Cost < 0)
            {
                throw new InvalidDataException($"Inducement '{inducement.Id}' has a negative cost.");
            }

            if (inducement.MaxCount < 0)
            {
                throw new InvalidDataException($"Inducement '{inducement.Id}' has a negative max count.");
            }

            if (inducement.DiscountedCost is < 0)
            {
                throw new InvalidDataException($"Inducement '{inducement.Id}' has a negative discounted cost.");
            }

            if (!KnownInducementKinds.Contains(inducement.Kind))
            {
                throw new InvalidDataException($"Inducement '{inducement.Id}' references unknown kind '{inducement.Kind}'.");
            }

            var duplicateOption = inducement.Options
                .GroupBy(option => option.Id, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateOption is not null)
            {
                throw new InvalidDataException($"Inducement '{inducement.Id}' has duplicate option '{duplicateOption.Key}'.");
            }

            foreach (var option in inducement.Options)
            {
                RequireText(option.Id, $"Inducement '{inducement.Id}' option id is required.");
                RequireText(option.Name, $"Inducement '{inducement.Id}' option '{option.Id}' name is required.");
                RequireText(option.Effect, $"Inducement '{inducement.Id}' option '{option.Id}' effect is required.");
                RequireText(option.Description, $"Inducement '{inducement.Id}' option '{option.Id}' description is required.");
                if (option.Cost < 0)
                {
                    throw new InvalidDataException($"Inducement '{inducement.Id}' option '{option.Id}' has a negative cost.");
                }

                if (!KnownInducementOptionEffects.Contains(option.Effect))
                {
                    throw new InvalidDataException($"Inducement '{inducement.Id}' option '{option.Id}' has unknown effect '{option.Effect}'.");
                }
            }
        }

        foreach (var threshold in RequiredAdvancementThresholds)
        {
            if (!ruleset.AdvancementThresholds.TryGetValue(threshold, out var cost) || cost <= 0)
            {
                throw new InvalidDataException($"Advancement threshold '{threshold}' must be defined with a positive cost.");
            }
        }

        var unknownThreshold = ruleset.AdvancementThresholds.Keys.FirstOrDefault(threshold => !RequiredAdvancementThresholds.Contains(threshold));
        if (unknownThreshold is not null)
        {
            throw new InvalidDataException($"Unknown advancement threshold '{unknownThreshold}'.");
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
