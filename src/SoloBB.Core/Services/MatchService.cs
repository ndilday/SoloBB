using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public sealed partial class MatchService
{
    private const int MaxGoForItsPerActivation = 3;
    private const int SprintGoForItsPerActivation = 4;
    private readonly IDiceRoller _dice;
    private Dictionary<Guid, string> _playerNames = [];

    public MatchService(IDiceRoller? dice = null)
    {
        _dice = dice ?? new RandomDiceRoller();
    }
}

public sealed record BlockStrength(int AttackerStrength, int DefenderStrength, int Dice);

public sealed record PassRange(string Name, int TargetModifier);

public sealed record DiceRoll2D6(int First, int Second)
{
    public int Total => First + Second;
    public bool IsDoubles => First == Second;
}

public sealed record BallLanding(PitchSquare Square, IReadOnlyList<MatchLogEntry> Log);

sealed record InjuryResolution(PlayerPitchState State, CasualtyRoll? Casualty = null);

sealed record CatchAttempt(int Roll, int? Reroll, bool Success);

sealed record PassAttempt(int Roll, int? Reroll, int FinalRoll, bool Success, bool Fumbled, bool SafePassPreventedFumble);

sealed record TentaclesResolution(MatchState Match, bool Held);

sealed record FoulAppearanceResolution(MatchState Match, bool BlockPrevented);

sealed record ApothecaryResolution(MatchState Match, InjuryResolution Injury, IReadOnlyList<MatchLogEntry> Log);

sealed record KickoffEventResult(MatchState Match, string Name, string Message, KickoffEventKind? PendingKind = null, bool ExtraScatter = false);

sealed record ActionStart(MatchState Match, bool Prevented);

sealed record SpecialActionContext(
    MatchState Match,
    Player Actor,
    PlayerPlacement ActorPlacement,
    Player Target,
    PlayerPlacement TargetPlacement,
    bool Prevented);

sealed record TeamMateLaunchContext(
    Player Actor,
    PlayerPlacement ActorPlacement,
    Player Launched,
    PlayerPlacement LaunchedPlacement);

public interface IDiceRoller
{
    int RollD6();
    int RollD8();
    int RollD16();
}

public sealed class RandomDiceRoller : IDiceRoller
{
    private readonly Random _random = new();

    public int RollD6()
    {
        return _random.Next(1, 7);
    }

    public int RollD8()
    {
        return _random.Next(1, 9);
    }

    public int RollD16()
    {
        return _random.Next(1, 17);
    }
}
