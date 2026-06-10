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
}
