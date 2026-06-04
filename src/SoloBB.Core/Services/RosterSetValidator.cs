using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed class RosterSetValidator
{
    public void Validate(RosterSet rosterSet, Ruleset ruleset)
    {
        RequireText(rosterSet.Id, "Roster set id is required.");
        RequireText(rosterSet.Name, "Roster set name is required.");

        if (!string.Equals(rosterSet.RulesetId, ruleset.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"Roster set '{rosterSet.Id}' targets ruleset '{rosterSet.RulesetId}', not '{ruleset.Id}'.");
        }

        var knownSkills = ruleset.Skills.Select(skill => skill.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
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

            if (roster.RerollCost < 0)
            {
                throw new InvalidDataException($"Roster '{roster.Id}' has a negative reroll cost.");
            }

            foreach (var position in roster.Positions)
            {
                ValidatePosition(roster.Id, position, knownSkills);
            }
        }

        var duplicateStar = rosterSet.StarPlayers
            .GroupBy(star => star.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateStar is not null)
        {
            throw new InvalidDataException($"Duplicate star player id '{duplicateStar.Key}'.");
        }

        foreach (var star in rosterSet.StarPlayers)
        {
            ValidateStarPlayer(star, knownSkills);
        }
    }

    private static void ValidateStarPlayer(StarPlayerDefinition star, ISet<string> knownSkills)
    {
        RequireText(star.Id, "Star player id is required.");
        RequireText(star.Name, $"Star player '{star.Id}' name is required.");

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
    }

    private static void ValidatePosition(string rosterId, PositionTemplate position, ISet<string> knownSkills)
    {
        RequireText(position.Id, $"Roster '{rosterId}' has a position with no id.");
        RequireText(position.Name, $"Position '{position.Id}' name is required.");

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
    }

    private static void RequireText(string value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException(message);
        }
    }
}
