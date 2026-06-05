namespace SoloBB.Core.Domain;

public enum GameEventKind
{
    ActionStart,
    MoveStep,
    DodgeRoll,
    GoForItRoll,
    PickupRoll,
    PassRoll,
    CatchRoll,
    InterceptionRoll,
    BlockRoll,
    Push,
    ThrowTeamMate,
    KickTeamMate,
    BombThrow,
    SpecialAction,
    ArmorRoll,
    InjuryRoll,
    BallScatter,
    Kickoff,
    DriveEnd,
    PostMatch
}

public enum GameEventStage
{
    BeforeEvent,
    BeforeRoll,
    ModifyTarget,
    AfterRoll,
    BeforeResolve,
    AfterEvent
}

public sealed record SkillHookDefinition
{
    public required GameEventKind Event { get; init; }
    public required GameEventStage Stage { get; init; }
}
