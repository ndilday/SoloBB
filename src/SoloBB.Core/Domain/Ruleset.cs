namespace SoloBB.Core.Domain;

public sealed record Ruleset
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public int PitchWidth { get; init; } = 26;
    public int PitchHeight { get; init; } = 15;
    public int PlayersPerSide { get; init; } = 11;
    public int TurnsPerHalf { get; init; } = 8;
    public int StartingTreasury { get; init; } = 1_000_000;
    public int RerollCap { get; init; } = 8;
    public bool UseTeamValueInducements { get; init; } = true;
    public required DiceRules Dice { get; init; }
    public IReadOnlyList<SkillDefinition> Skills { get; init; } = [];
    public IReadOnlyDictionary<string, int> AdvancementThresholds { get; init; } = new Dictionary<string, int>();
}

public sealed record DiceRules
{
    public int BlockDieFaces { get; init; } = 6;
    public int AgilityDieFaces { get; init; } = 6;
    public bool NaturalOneAlwaysFails { get; init; } = true;
    public bool NaturalSixAlwaysSucceeds { get; init; } = true;
}

public sealed record SkillDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Category { get; init; } = "";
    public bool Compulsory { get; init; }
    public SkillTiming Timing { get; init; } = SkillTiming.Passive;
    public IReadOnlyList<SkillEffect> Effects { get; init; } = [];
    public string Description { get; init; } = "";
}

public enum SkillTiming
{
    Passive,
    BeforeRoll,
    AfterRoll,
    DuringAction,
    PostMatch
}

public enum SkillEffect
{
    DodgeReroll,
    CatchReroll,
    PassReroll,
    PickupReroll,
    GoForItReroll,
    BothDownProtection,
    CancelDodgeReroll,
    GuardAssist,
    MightyBlow,
    StandFirm,
    DirtyPlayer,
    ThickSkull,
    DivingCatch,
    DivingTackle,
    Defensive,
    JumpUp,
    Leap,
    SafePairOfHands,
    SideStep,
    SneakyGit,
    Sprint
}
