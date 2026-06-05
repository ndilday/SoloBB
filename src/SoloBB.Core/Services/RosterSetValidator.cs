using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class RosterSetValidator
{
    private static readonly ISet<string> KnownRosterRestrictions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "mixed-position-animosity"
    };

    public void Validate(RosterSet rosterSet, Ruleset ruleset)
    {
        RequireText(rosterSet.Id, "Roster set id is required.");
        RequireText(rosterSet.Name, "Roster set name is required.");

        if (!string.Equals(rosterSet.RulesetId, ruleset.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Roster set '{rosterSet.Id}' targets ruleset '{rosterSet.RulesetId}', not '{ruleset.Id}'.");
        }

        if (rosterSet.Rosters.Count == 0)
        {
            throw new InvalidDataException($"Roster set '{rosterSet.Id}' must define at least one roster.");
        }

        var knownSkills = ruleset.Skills.Select(skill => skill.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownSkillCategories = ruleset.Skills.Select(skill => skill.Category).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicateRoster = rosterSet.Rosters
            .GroupBy(roster => roster.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateRoster is not null)
        {
            throw new InvalidDataException($"Duplicate roster id '{duplicateRoster.Key}'.");
        }

        foreach (var roster in rosterSet.Rosters)
        {
            RequireText(roster.Id, "Roster id is required.");
            RequireText(roster.Name, $"Roster '{roster.Id}' name is required.");
            RequireUniqueText(roster.SpecialRules, $"Roster '{roster.Id}' special rule");
            RequireUniqueText(roster.RosterRestrictions, $"Roster '{roster.Id}' restriction");

            if (roster.RerollCost < 0)
            {
                throw new InvalidDataException($"Roster '{roster.Id}' has a negative reroll cost.");
            }

            if (roster.SpecialRules.Count == 0)
            {
                throw new InvalidDataException($"Roster '{roster.Id}' must define at least one special rule.");
            }

            if (roster.Positions.Count == 0)
            {
                throw new InvalidDataException($"Roster '{roster.Id}' must define at least one position.");
            }

            foreach (var restriction in roster.RosterRestrictions)
            {
                if (!KnownRosterRestrictions.Contains(restriction))
                {
                    throw new InvalidDataException($"Roster '{roster.Id}' references unknown restriction '{restriction}'.");
                }
            }

            foreach (var position in roster.Positions)
            {
                ValidatePosition(roster.Id, position, knownSkills, knownSkillCategories);
            }
        }

        var knownRosterSpecialRules = rosterSet.Rosters
            .SelectMany(roster => roster.SpecialRules)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var duplicateStar = rosterSet.StarPlayers
            .GroupBy(star => star.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateStar is not null)
        {
            throw new InvalidDataException($"Duplicate star player id '{duplicateStar.Key}'.");
        }

        foreach (var star in rosterSet.StarPlayers)
        {
            ValidateStarPlayer(star, knownSkills, knownRosterSpecialRules);
        }
    }

    private static void ValidateStarPlayer(StarPlayerDefinition star, ISet<string> knownSkills, ISet<string> knownRosterSpecialRules)
    {
        RequireText(star.Id, "Star player id is required.");
        RequireText(star.Name, $"Star player '{star.Id}' name is required.");
        RequireStats(star.Stats, $"Star player '{star.Id}'");
        RequireUniqueText(star.Skills, $"Star player '{star.Id}' skill");
        RequireUniqueText(star.SpecialRules, $"Star player '{star.Id}' special rule");

        if (star.Cost < 0)
        {
            throw new InvalidDataException($"Star player '{star.Id}' has a negative cost.");
        }

        if (star.SpecialRules.Count == 0)
        {
            throw new InvalidDataException($"Star player '{star.Id}' must define at least one eligibility special rule.");
        }

        foreach (var skill in star.Skills)
        {
            if (!knownSkills.Contains(skill))
            {
                throw new InvalidDataException($"Star player '{star.Id}' references unknown skill '{skill}'.");
            }
        }

        foreach (var specialRule in star.SpecialRules)
        {
            if (!knownRosterSpecialRules.Contains(specialRule))
            {
                throw new InvalidDataException($"Star player '{star.Id}' references unknown eligibility special rule '{specialRule}'.");
            }
        }
    }

    private static void ValidatePosition(string rosterId, PositionTemplate position, ISet<string> knownSkills, ISet<string> knownSkillCategories)
    {
        RequireText(position.Id, $"Roster '{rosterId}' has a position with no id.");
        RequireText(position.Name, $"Position '{position.Id}' name is required.");
        RequireStats(position.Stats, $"Position '{position.Id}'");
        RequireUniqueText(position.StartingSkills, $"Position '{position.Id}' starting skill");
        RequireUniqueText(position.PrimarySkillCategories, $"Position '{position.Id}' primary skill category");
        RequireUniqueText(position.SecondarySkillCategories, $"Position '{position.Id}' secondary skill category");

        if (position.Min < 0 || position.Max < position.Min)
        {
            throw new InvalidDataException($"Position '{position.Id}' has invalid min/max limits.");
        }

        if (position.Cost < 0)
        {
            throw new InvalidDataException($"Position '{position.Id}' has a negative cost.");
        }

        foreach (var skill in position.StartingSkills)
        {
            if (!knownSkills.Contains(skill))
            {
                throw new InvalidDataException($"Position '{position.Id}' references unknown skill '{skill}'.");
            }
        }

        foreach (var category in position.PrimarySkillCategories.Concat(position.SecondarySkillCategories))
        {
            if (!knownSkillCategories.Contains(category))
            {
                throw new InvalidDataException($"Position '{position.Id}' references unknown skill category '{category}'.");
            }
        }
    }

    private static void RequireStats(PlayerStats stats, string owner)
    {
        if (stats is null)
        {
            throw new InvalidDataException($"{owner} stats are required.");
        }

        if (stats.Movement <= 0 ||
            stats.Strength <= 0 ||
            stats.Agility <= 0 ||
            stats.Passing <= 0 ||
            stats.Armor <= 0)
        {
            throw new InvalidDataException($"{owner} has invalid non-positive stats.");
        }
    }

    private static void RequireUniqueText(IReadOnlyList<string> values, string label)
    {
        foreach (var value in values)
        {
            RequireText(value, $"{label} is required.");
        }

        var duplicate = values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidDataException($"Duplicate {label} '{duplicate.Key}'.");
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
