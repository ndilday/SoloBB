namespace SoloBB.Core.Domain;

public sealed record MatchState
{
    public required Guid Id { get; init; }
    public required string RulesetId { get; init; }
    public required Guid HomeTeamId { get; init; }
    public required Guid AwayTeamId { get; init; }
    public Guid ActiveTeamId { get; init; }
    public int Half { get; init; } = 1;
    public int Drive { get; init; } = 1;
    public DriveState DriveState { get; init; } = DriveState.Setup;
    public int Turn { get; init; } = 1;
    public int HomeTurn { get; init; } = 1;
    public int AwayTurn { get; init; } = 1;
    public Guid? FirstHalfReceivingTeamId { get; init; }
    public MatchPhase Phase { get; init; } = MatchPhase.DefenseSetup;
    public int HomeScore { get; init; }
    public int AwayScore { get; init; }
    public int HomeRerollsRemaining { get; init; }
    public int AwayRerollsRemaining { get; init; }
    public int HomeTeamRerolls { get; init; }
    public int AwayTeamRerolls { get; init; }
    public bool HomeLeaderRerollAvailable { get; init; }
    public bool AwayLeaderRerollAvailable { get; init; }
    public int HomeCheerleaders { get; init; }
    public int AwayCheerleaders { get; init; }
    public int HomeAssistantCoaches { get; init; }
    public int AwayAssistantCoaches { get; init; }
    public int HomeBribesRemaining { get; init; }
    public int AwayBribesRemaining { get; init; }
    public int HomeTreasurySpent { get; init; }
    public int AwayTreasurySpent { get; init; }
    public int HomeApothecariesRemaining { get; init; }
    public int AwayApothecariesRemaining { get; init; }
    public int HomeBloodweiserKegs { get; init; }
    public int AwayBloodweiserKegs { get; init; }
    public int HomeWeatherMagesRemaining { get; init; }
    public int AwayWeatherMagesRemaining { get; init; }
    public int HomeSpecialPlaysRemaining { get; init; }
    public int AwaySpecialPlaysRemaining { get; init; }
    public bool HomeHasMasterChef { get; init; }
    public bool AwayHasMasterChef { get; init; }
    public string HomeWizardEffect { get; init; } = "";
    public string AwayWizardEffect { get; init; } = "";
    public int HomeWizardsRemaining { get; init; }
    public int AwayWizardsRemaining { get; init; }
    public int HomeBribeRollModifier { get; init; }
    public int AwayBribeRollModifier { get; init; }
    // BB2020: each team's Fan Factor for this game is its Dedicated Fans plus a D3, rolled once at kickoff.
    // FAME (FAns at the ME) is derived by comparing the two Fan Factors and is used by kickoff events.
    public int HomeFanFactor { get; init; }
    public int AwayFanFactor { get; init; }
    public int HomeFame { get; init; }
    public int AwayFame { get; init; }
    public WeatherCondition Weather { get; init; } = WeatherCondition.Nice;
    public BallState Ball { get; init; } = new();
    public IReadOnlyList<PlayerPlacement> Placements { get; init; } = [];
    public IReadOnlyList<ActivePrayer> Prayers { get; init; } = [];
    public IReadOnlyList<Guid> SecretWeaponPlayerIds { get; init; } = [];
    public IReadOnlyList<Guid> PickMeUpPlayerIds { get; init; } = [];
    public IReadOnlyList<PlayerTurnActivation> Activations { get; init; } = [];
    public IReadOnlyList<TeamRerollUse> TeamRerollUses { get; init; } = [];
    public IReadOnlyList<MatchPlayerAward> PlayerAwards { get; init; } = [];
    public PendingBlockChoice? PendingBlock { get; init; }
    public PendingBlockRerollChoice? PendingBlockReroll { get; init; }
    public PendingPushChoice? PendingPush { get; init; }
    public PendingInterceptionChoice? PendingInterception { get; init; }
    public PendingRerollChoice? PendingReroll { get; init; }
    public PendingApothecaryChoice? PendingApothecary { get; init; }
    public PendingStandFirmChoice? PendingStandFirm { get; init; }
    public PendingDivingTackleChoice? PendingDivingTackle { get; init; }
    public PendingFollowUpChoice? PendingFollowUp { get; init; }
    public PendingBallPlacementChoice? PendingBallPlacement { get; init; }
    public PendingBombThrowChoice? PendingBombThrow { get; init; }
    public PendingMultipleBlockContinuation? PendingMultipleBlock { get; init; }
    public PendingSendOffChoice? PendingSendOff { get; init; }
    public PendingKickoffEventChoice? PendingKickoffEvent { get; init; }
    public PendingOnTheBallChoice? PendingOnTheBall { get; init; }
    public PendingDumpOffChoice? PendingDumpOff { get; init; }
    public DeferredBlock? DeferredBlock { get; init; }
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

public enum DriveState
{
    Setup,
    InProgress,
    Ending,
    Complete
}

public enum WeatherCondition
{
    SwelteringHeat,
    VerySunny,
    Nice,
    PouringRain,
    Blizzard
}

public sealed record PlayerPlacement
{
    public required Guid PlayerId { get; init; }
    public required Guid TeamId { get; init; }
    public PitchSquare? Square { get; init; }
    public PlayerPitchState State { get; init; } = PlayerPitchState.Reserve;
    public bool TackleZonesLost { get; init; }
    public bool Rooted { get; init; }
    public bool IsLarge { get; init; }
    public int? StunnedRecoveryHalf { get; init; }
    public int? StunnedRecoveryTurn { get; init; }
    public CasualtyRoll? Casualty { get; init; }
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
    public int MovementSquaresUsed { get; init; }
    public PlayerTurnAction Action { get; init; } = PlayerTurnAction.Move;
    public bool DeclaredOnly { get; init; }
    public int BlocksMade { get; init; }
    public bool MayMoveAfterFoul { get; init; }

    /// <summary>
    /// True once the player's activation is fully finished and they cannot take any further
    /// action this turn (e.g. after following up a block, a both-down result, a pass, or a hand-off).
    /// </summary>
    public bool Completed { get; init; }
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
    Pass,
    Foul,
    Special
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
    public bool PreventFollowUp { get; init; }
}

public sealed record PendingBlockRerollChoice
{
    public required Guid AttackerTeamId { get; init; }
    public required Guid DefenderTeamId { get; init; }
    public required Guid AttackerPlayerId { get; init; }
    public required Guid DefenderPlayerId { get; init; }
    public required IReadOnlyList<int> Rolls { get; init; }
    public required int ChosenRoll { get; init; }
    public required int AttackerStrength { get; init; }
    public required int DefenderStrength { get; init; }
    public required int Dice { get; init; }
    public bool TeamRerollAvailable { get; init; }
    public bool PreventFollowUp { get; init; }
    public required MatchState MatchBeforeRoll { get; init; }
}

public sealed record PendingPushChoice
{
    public required Guid AttackerTeamId { get; init; }
    public required Guid DefenderTeamId { get; init; }
    public required Guid AttackerPlayerId { get; init; }
    public required Guid DefenderPlayerId { get; init; }
    public required PitchSquare DefenderSquare { get; init; }
    public required IReadOnlyList<PitchSquare> LegalSquares { get; init; }
    public bool KnockDefenderDown { get; init; }
    public required string ResultMessage { get; init; }
    public bool PreventFollowUp { get; init; }
    public PendingPushContinuation? Continuation { get; init; }
}

public sealed record PendingPushContinuation
{
    public required Guid PlayerId { get; init; }
    public required PitchSquare Source { get; init; }
    public required PitchSquare Destination { get; init; }
    public bool KnockDown { get; init; }
    public required string ResultMessage { get; init; }
}

public sealed record PendingStandFirmChoice
{
    public required Guid AttackerTeamId { get; init; }
    public required Guid DefenderTeamId { get; init; }
    public required Guid AttackerPlayerId { get; init; }
    public required Guid DefenderPlayerId { get; init; }
    public required PitchSquare DefenderSquare { get; init; }
    public required IReadOnlyList<PitchSquare> LegalSquares { get; init; }
    public bool KnockDefenderDown { get; init; }
    public required string ResultMessage { get; init; }
    public bool PreventFollowUp { get; init; }
}

public sealed record PendingFollowUpChoice
{
    public required Guid AttackerTeamId { get; init; }
    public required Guid DefenderTeamId { get; init; }
    public required Guid AttackerPlayerId { get; init; }
    public required Guid DefenderPlayerId { get; init; }
    public required PitchSquare FollowUpSquare { get; init; }
}

public sealed record PendingBallPlacementChoice
{
    public required Guid TeamId { get; init; }
    public required Guid PlayerId { get; init; }
    public required IReadOnlyList<PitchSquare> LegalSquares { get; init; }
    public required string Reason { get; init; }
}

public sealed record PendingBombThrowChoice
{
    public required Guid ThrowingTeamId { get; init; }
    public required Guid OpposingTeamId { get; init; }
    public required Guid ThrowerPlayerId { get; init; }
    public required PitchSquare BombSquare { get; init; }
}

public sealed record PendingSendOffChoice
{
    public required Guid TeamId { get; init; }
    public required Guid PlayerId { get; init; }
    public required string Reason { get; init; }
    public bool BribeAvailable { get; init; }
    public PendingDriveEndContinuation? DriveEnd { get; init; }
}

public sealed record PendingDriveEndContinuation
{
    public required Guid NextDefenseTeamId { get; init; }
    public bool StartSecondHalf { get; init; }
    public bool CompleteMatch { get; init; }
    public IReadOnlyList<Guid> ResolvedPlayerIds { get; init; } = [];
}

public sealed record PendingMultipleBlockContinuation
{
    public required Guid AttackerTeamId { get; init; }
    public required Guid DefenderTeamId { get; init; }
    public required Guid AttackerPlayerId { get; init; }
    public required Guid DefenderPlayerId { get; init; }
}

public sealed record PendingInterceptionChoice
{
    public required Guid PassingTeamId { get; init; }
    public required Guid DefendingTeamId { get; init; }
    public required Guid PasserPlayerId { get; init; }
    public Guid? ReceiverPlayerId { get; init; }
    public required PitchSquare TargetSquare { get; init; }
    public required IReadOnlyList<Guid> EligiblePlayerIds { get; init; }
    public required int PassRoll { get; init; }
    public required int PassTarget { get; init; }
    public required string PassRangeName { get; init; }
    public bool UseCloudBurster { get; init; }
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

public sealed record PendingKickoffEventChoice
{
    public required KickoffEventKind Kind { get; init; }
    public required Guid TeamId { get; init; }
    public required Guid ReceivingTeamId { get; init; }
    public required PitchSquare LandingSquare { get; init; }
    public required IReadOnlyList<Guid> EligiblePlayerIds { get; init; }
    public required IReadOnlyList<Guid> MovedPlayerIds { get; init; }
    public int MovesRemaining { get; init; }
    public bool RequiresPlayerChoice { get; init; } = true;
}

public sealed record PendingApothecaryChoice
{
    public required Guid TeamId { get; init; }
    public required Guid PlayerId { get; init; }
    public required CasualtyRoll OriginalCasualty { get; init; }
}

public enum KickoffEventKind
{
    SolidDefence,
    HighKick,
    QuickSnap,
    Blitz
}

public enum PendingRerollKind
{
    Dodge,
    GoForIt,
    Pickup,
    Pass,
    Catch,
    ThrowTeamMate,
    KickTeamMate,
    Landing,
    BombThrow,
    BombCatch,
    HypnoticGaze,
    JumpUp,
    Dauntless
}

public sealed record PendingRerollContext
{
    public required MatchState MatchBeforeRoll { get; init; }
    public required PlayerTurnAction Action { get; init; }
    public required PitchSquare Destination { get; init; }
    public required IReadOnlyList<PitchSquare> Path { get; init; }
    public required int StepIndex { get; init; }
    public required int MovementAllowance { get; init; }
    public int GoForItNumber { get; init; }
    public Guid? BlitzDefenderPlayerId { get; init; }
    public bool BreakTackleUsed { get; init; }
    public bool ArmBarApplies { get; init; }
    public bool UseCloudBurster { get; init; }
    public bool IsDumpOff { get; init; }
    public bool IsHailMary { get; init; }

    // Catch reroll context: the player who threw/handed the ball, plus the original pass result so the
    // catch can be re-resolved and logged after a reroll. Pass-range/roll/target are unused for hand-offs.
    public CatchResumeKind CatchResume { get; init; }
    public Guid? CatchPasserPlayerId { get; init; }
    public string? CatchPassRangeName { get; init; }
    public int CatchPassRoll { get; init; }
    public int CatchPassTarget { get; init; }

    // Bounce-catch reroll context: enough to re-enter the loose-ball bounce at the catch square and
    // continue it with a forced roll once the reroll choice resolves. The team ids are reused by the
    // bomb-catch reroll to carry the throwing/opposing teams.
    public Guid? BounceOriginalTeamId { get; init; }
    public Guid? BounceOpposingTeamId { get; init; }
    public bool BounceAllowDivingCatch { get; init; }
    public int BounceCount { get; init; }

    // Shared re-entry context for the remaining single-roll reroll prompts (Throw/Kick Team-Mate,
    // landing, bomb throw/catch, Hypnotic Gaze, Jump Up, Dauntless). Only the fields relevant to a
    // given reroll kind are populated.
    public Guid? SecondaryPlayerId { get; init; }
    public string? LaunchKind { get; init; }
    public int CollisionDepth { get; init; }
    public bool BombThrownBack { get; init; }
    public int DefenderStrengthBonus { get; init; }
    public bool PreventFollowUp { get; init; }
}

public enum CatchResumeKind
{
    PassLanding,
    HandOff,
    Bounce
}

public sealed record PendingDivingTackleChoice
{
    public required Guid DodgingTeamId { get; init; }
    public required Guid TacklerTeamId { get; init; }
    public required Guid DodgerPlayerId { get; init; }
    public required Guid TacklerPlayerId { get; init; }
    public required PitchSquare DodgerSquare { get; init; }
    public required PitchSquare Destination { get; init; }
    public required int Roll { get; init; }
    public required int TargetWithoutDivingTackle { get; init; }
    public required int TargetWithDivingTackle { get; init; }
    public required PendingRerollContext Context { get; init; }

    /// <summary>
    /// True when the interrupted action is a Leap (single-roll) rather than a stepwise Dodge. The leap
    /// resolves to a finalize-or-fall outcome instead of re-entering the movement step machinery.
    /// </summary>
    public bool IsLeap { get; init; }
}

/// <summary>
/// BB2020 On the Ball, which has two interrupt windows for a player with the skill:
/// <list type="bullet">
/// <item>after an opposing Pass action is declared but before any roll, the defender may move up to three
/// squares, each closer to the pass target (to set up an interception);</item>
/// <item>after the opposing team's Kick-off but before the roll on the kick-off event table, the receiver
/// may move up to three squares (staying in their own half).</item>
/// </list>
/// The interrupted pass or kickoff resumes once this resolves.
/// </summary>
public sealed record PendingOnTheBallChoice
{
    public required OnTheBallTrigger Trigger { get; init; }
    public required Guid TeamId { get; init; }
    public required Guid OpposingTeamId { get; init; }
    public required IReadOnlyList<Guid> EligiblePlayerIds { get; init; }

    // Pass window only: the pass target (moves must get closer to it) and the data to resume the pass.
    public PitchSquare? PassTargetSquare { get; init; }
    public PendingPassResume? PassResume { get; init; }

    // Kickoff window only: the data to resume the kick-off resolution.
    public PendingKickoffResume? KickoffResume { get; init; }
}

public enum OnTheBallTrigger
{
    PassDeclared,
    Kickoff
}

/// <summary>
/// Everything needed to re-enter <c>ResolveKickoff</c> after the On the Ball kickoff window resolves.
/// </summary>
public sealed record PendingKickoffResume
{
    public required PitchSquare TargetSquare { get; init; }
    public bool HadKickingTeam { get; init; }
}

/// <summary>
/// Everything needed to re-enter <c>PassBallCore</c> after an interrupt window (On the Ball) has resolved.
/// </summary>
public sealed record PendingPassResume
{
    public required Guid PasserPlayerId { get; init; }
    public required PitchSquare TargetSquare { get; init; }
    public bool UsePassSkillReroll { get; init; }
    public bool UseCloudBurster { get; init; }
    public bool IsDumpOff { get; init; }
    public bool IsHailMary { get; init; }
}

/// <summary>
/// BB2020 Dump-Off: when a standing ball carrier with this skill is the target of a Block (or the block at
/// the end of a Blitz), before any block dice are rolled they may make a Quick Pass. The block is held in
/// <see cref="Block"/> and resumes once the pass (and any interception/reroll it spawns) resolves.
/// </summary>
public sealed record PendingDumpOffChoice
{
    public required Guid CarrierTeamId { get; init; }
    public required Guid BlockingTeamId { get; init; }
    public required Guid CarrierPlayerId { get; init; }
    public required DeferredBlock Block { get; init; }
}

/// <summary>
/// A block whose resolution has been deferred behind an interrupt (Dump-Off). Carries enough context to
/// re-enter <c>ResolveBlock</c> once the interrupting pass has fully resolved.
/// </summary>
public sealed record DeferredBlock
{
    public required Guid AttackerTeamId { get; init; }
    public required Guid DefenderTeamId { get; init; }
    public required Guid AttackerPlayerId { get; init; }
    public required Guid DefenderPlayerId { get; init; }
    public int DefenderStrengthBonus { get; init; }
    public bool PreventFollowUp { get; init; }
}

public enum PlayerPitchState
{
    Reserve,
    Standing,
    Prone,
    Stunned,
    KnockedOut,
    Casualty,
    Dead,
    SentOff
}

public sealed record CasualtyRoll
{
    public required int Roll { get; init; }
    public required CasualtyResult Result { get; init; }
}

public enum CasualtyResult
{
    BadlyHurt,
    SeriouslyHurt,
    SeriousInjury,
    LastingInjury,
    Dead
}

public sealed record MatchLogEntry
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public required string Message { get; init; }
}

public sealed record MatchPlayerAward
{
    public required Guid TeamId { get; init; }
    public required Guid PlayerId { get; init; }
    public Guid? VictimPlayerId { get; init; }
    public Guid? VictimTeamId { get; init; }
    public required MatchPlayerAwardKind Kind { get; init; }
    public int StarPlayerPoints { get; init; }
    public string? TeamName { get; init; }
    public string? PlayerName { get; init; }
    public string? VictimTeamName { get; init; }
    public string? VictimPlayerName { get; init; }
    public CasualtyResult? CasualtyResult { get; init; }
}

public enum MatchPlayerAwardKind
{
    Touchdown,
    Casualty,
    Completion,
    Interception,
    Deflection,
    MostValuablePlayer
}
