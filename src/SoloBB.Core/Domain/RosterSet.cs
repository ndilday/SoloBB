namespace SoloBB.Core.Domain;

public sealed record RosterSet
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string RulesetId { get; init; }
    public IReadOnlyList<TeamRoster> Rosters { get; init; } = [];
    public IReadOnlyList<StarPlayerDefinition> StarPlayers { get; init; } = [];
}

public sealed record TeamRoster
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int RerollCost { get; init; }
    public IReadOnlyList<string> SpecialRules { get; init; } = [];
    public IReadOnlyList<string> RosterRestrictions { get; init; } = [];
    public IReadOnlyList<PositionTemplate> Positions { get; init; } = [];
}

public sealed record PositionTemplate
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public int Cost { get; init; }
    public required PlayerStats Stats { get; init; }
    public IReadOnlyList<string> StartingSkills { get; init; } = [];
    public IReadOnlyList<string> PrimarySkillCategories { get; init; } = [];
    public IReadOnlyList<string> SecondarySkillCategories { get; init; } = [];
}

public sealed record StarPlayerDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public int Cost { get; init; }
    public required PlayerStats Stats { get; init; }
    public IReadOnlyList<string> Skills { get; init; } = [];
    public IReadOnlyList<string> SpecialRules { get; init; } = [];
}

public sealed record PlayerStats
{
    public int Movement { get; init; }
    public int Strength { get; init; }
    public int Agility { get; init; }
    public int Passing { get; init; }
    public int Armor { get; init; }
    public bool IsLarge { get; init; }
}
