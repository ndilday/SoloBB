using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class RulesetValidator
{
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
    }

    private static void RequireText(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(message);
        }
    }
}
