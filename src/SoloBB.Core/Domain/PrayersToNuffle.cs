namespace SoloBB.Core.Domain;

// The BB2020 Prayers to Nuffle table (D16). The underdog rolls once for every full 50,000 gp of Current
// Team Value difference, re-rolling duplicate results.
public enum PrayerToNuffle
{
    TreacherousTrapdoor = 1,
    FriendsWithTheRef = 2,
    Stiletto = 3,
    IronMan = 4,
    KnuckleDusters = 5,
    BadHabits = 6,
    GreasyCleats = 7,
    BlessedStatueOfNuffle = 8,
    MolesUnderThePitch = 9,
    PerfectPassing = 10,
    FanInteraction = 11,
    NecessaryViolence = 12,
    FoulingFrenzy = 13,
    ThrowARock = 14,
    UnderScrutiny = 15,
    IntensiveTraining = 16
}

// A Prayer to Nuffle rolled by the underdog before the match. The engine bakes the player-affecting
// buffs/debuffs into the match-only teams; prayers whose effect is not yet modelled are still recorded
// here (with EffectApplied = false) so the UI can display them.
public sealed record ActivePrayer
{
    public required PrayerToNuffle Prayer { get; init; }
    // The underdog team that prayed (the beneficiary of buffs; the opponent is affected by debuffs).
    public required Guid TeamId { get; init; }
    // The specific player a prayer singled out, when applicable.
    public Guid? PlayerId { get; init; }
    // True when the engine mechanically applies the prayer's effect for this match.
    public bool EffectApplied { get; init; }
}
