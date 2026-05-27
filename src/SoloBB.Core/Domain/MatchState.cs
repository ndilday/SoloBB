namespace SoloBB.Core.Domain;

public sealed record MatchState
{
    public required Guid Id { get; init; }
    public required string RulesetId { get; init; }
    public required Guid HomeTeamId { get; init; }
    public required Guid AwayTeamId { get; init; }
    public Guid ActiveTeamId { get; init; }
    public int Half { get; init; } = 1;
    public int Turn { get; init; } = 1;
    public int HomeTurn { get; init; } = 1;
    public int AwayTurn { get; init; } = 1;
    public Guid? FirstHalfReceivingTeamId { get; init; }
    public MatchPhase Phase { get; init; } = MatchPhase.DefenseSetup;
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public int HomeRerollsRemaining { get; init; }
    public int AwayRerollsRemaining { get; init; }
    public BallState Ball { get; init; } = new();
    public IReadOnlyList<PlayerPlacement> Placements { get; init; } = [];
    public IReadOnlyList<PlayerTurnActivation> Activations { get; init; } = [];
    public IReadOnlyList<TeamRerollUse> TeamRerollUses { get; init; } = [];
    public PendingBlockChoice? PendingBlock { get; init; }
    public PendingInterceptionChoice? PendingInterception { get; init; }
    public PendingRerollChoice? PendingReroll { get; init; }
    public IReadOnlyList<MatchLogEntry> Log { get; init; } = [];
}

public enum MatchPhase
{
    DefenseSetup,
    OffenseSetup,
    Kickoff,
    OffensivePlayerTurn,
    DefensiveTurn,
    EndOfHalf,
    Complete
}

public sealed record PlayerPlacement
{
    public required Guid PlayerId { get; init; }
    public required Guid TeamId { get; init; }
    public PitchSquare? Square { get; init; }
    public PlayerPitchState State { get; init; } = PlayerPitchState.Reserve;
}

public sealed record PitchSquare(int X, int Y);

public sealed record BallState
{
    public Guid? CarrierPlayerId { get; init; }
    public PitchSquare? Square { get; init; }
}

public sealed record PlayerTurnActivation
{
    public required Guid PlayerId { get; init; }
    public required Guid TeamId { get; init; }
    public int Half { get; init; }
    public int Turn { get; init; }
    public int GoForItsUsed { get; init; }
    public PlayerTurnAction Action { get; init; } = PlayerTurnAction.Move;
}

public sealed record TeamRerollUse
{
    public required Guid TeamId { get; init; }
    public int Half { get; init; }
    public int Turn { get; init; }
}

public enum PlayerTurnAction
{
    Move,
    Block,
    Blitz,
    HandOff,
    Pass
}

public sealed record PendingBlockChoice
{
    public required Guid AttackerTeamId { get; init; }
    public required Guid DefenderTeamId { get; init; }
    public required Guid AttackerPlayerId { get; init; }
    public required Guid DefenderPlayerId { get; init; }
    public required IReadOnlyList<int> Rolls { get; init; }
    public required int AttackerStrength { get; init; }
    public required int DefenderStrength { get; init; }
}

public sealed record PendingInterceptionChoice
{
    public required Guid PassingTeamId { get; init; }
    public required Guid DefendingTeamId { get; init; }
    public required Guid PasserPlayerId { get; init; }
    public required Guid ReceiverPlayerId { get; init; }
    public required IReadOnlyList<Guid> EligiblePlayerIds { get; init; }
    public required int PassRoll { get; init; }
    public required int PassTarget { get; init; }
    public required string PassRangeName { get; init; }
}

public sealed record PendingRerollChoice
{
    public required Guid TeamId { get; init; }
    public required Guid PlayerId { get; init; }
    public required PendingRerollKind Kind { get; init; }
    public required int Roll { get; init; }
    public required int Target { get; init; }
    public bool TeamRerollAvailable { get; init; }
    public IReadOnlyList<string> SkillRerollIds { get; init; } = [];
    public required PendingRerollContext Context { get; init; }
}

public enum PendingRerollKind
{
    Dodge,
    GoForIt,
    Pickup
}

public sealed record PendingRerollContext
{
    public required MatchState MatchBeforeRoll { get; init; }
    public required PlayerTurnAction Action { get; init; }
    public required PitchSquare Destination { get; init; }
    public required IReadOnlyList<PitchSquare> Path { get; init; }
    public required int StepIndex { get; init; }
    public int GoForItNumber { get; init; }
}

public enum PlayerPitchState
{
    Reserve,
    Standing,
    Prone,
    Stunned,
    KnockedOut,
    Casualty,
    SentOff
}

public sealed record MatchLogEntry
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public required string Message { get; init; }
}
