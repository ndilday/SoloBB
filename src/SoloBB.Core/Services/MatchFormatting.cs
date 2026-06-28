using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public static class MatchFormatting
{
    public static string FormatCasualtyResult(CasualtyResult result)
    {
        return result switch
        {
            CasualtyResult.BadlyHurt => "Badly Hurt",
            CasualtyResult.SeriouslyHurt => "Seriously Hurt",
            CasualtyResult.SeriousInjury => "Serious Injury",
            CasualtyResult.LastingInjury => "Lasting Injury",
            CasualtyResult.Dead => "Dead",
            _ => result.ToString()
        };
    }

    public static string FormatRerollKind(PendingRerollKind kind)
    {
        return kind switch
        {
            PendingRerollKind.GoForIt => "go-for-it",
            PendingRerollKind.ThrowTeamMate => "throw team-mate",
            PendingRerollKind.KickTeamMate => "kick team-mate",
            PendingRerollKind.Landing => "landing",
            PendingRerollKind.BombThrow => "bomb throw",
            PendingRerollKind.BombCatch => "bomb catch",
            PendingRerollKind.HypnoticGaze => "Hypnotic Gaze",
            PendingRerollKind.JumpUp => "Jump Up",
            PendingRerollKind.Dauntless => "Dauntless",
            _ => kind.ToString().ToLowerInvariant()
        };
    }

    public static string FormatKickoffEventKind(KickoffEventKind kind)
    {
        return kind switch
        {
            KickoffEventKind.SolidDefence => "Solid Defence",
            KickoffEventKind.HighKick => "High Kick",
            KickoffEventKind.QuickSnap => "Quick Snap",
            KickoffEventKind.Blitz => "Blitz",
            _ => kind.ToString()
        };
    }

    public static string FormatPitchState(PlayerPitchState state)
    {
        return state.ToString().ToLowerInvariant();
    }

    public static string FormatInjuryOutcome(PlayerPitchState state)
    {
        return state switch
        {
            PlayerPitchState.Casualty or PlayerPitchState.Dead => "Casualty!",
            _ => FormatPitchState(state)
        };
    }

    public static string FormatPrayer(PrayerToNuffle prayer)
    {
        return prayer switch
        {
            PrayerToNuffle.TreacherousTrapdoor => "Treacherous Trapdoor",
            PrayerToNuffle.FriendsWithTheRef => "Friends with the Ref",
            PrayerToNuffle.Stiletto => "Stiletto",
            PrayerToNuffle.IronMan => "Iron Man",
            PrayerToNuffle.KnuckleDusters => "Knuckle Dusters",
            PrayerToNuffle.BadHabits => "Bad Habits",
            PrayerToNuffle.GreasyCleats => "Greasy Cleats",
            PrayerToNuffle.BlessedStatueOfNuffle => "Blessed Statue of Nuffle",
            PrayerToNuffle.MolesUnderThePitch => "Moles under the Pitch",
            PrayerToNuffle.PerfectPassing => "Perfect Passing",
            PrayerToNuffle.FanInteraction => "Fan Interaction",
            PrayerToNuffle.NecessaryViolence => "Necessary Violence",
            PrayerToNuffle.FoulingFrenzy => "Fouling Frenzy",
            PrayerToNuffle.ThrowARock => "Throw a Rock",
            PrayerToNuffle.UnderScrutiny => "Under Scrutiny",
            PrayerToNuffle.IntensiveTraining => "Intensive Training",
            _ => prayer.ToString()
        };
    }
}
