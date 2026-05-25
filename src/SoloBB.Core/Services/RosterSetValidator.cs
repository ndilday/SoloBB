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
