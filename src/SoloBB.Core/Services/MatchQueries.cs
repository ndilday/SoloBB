using SoloBB.Core.Domain;

namespace SoloBB.Core.Services;

public static class MatchQueries
{
    public static int CountOpposingTackleZones(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Count(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            MatchGeometry.HasActiveTackleZone(placement) &&
            MatchGeometry.IsAdjacentToPlacement(placement, square));
    }

    public static int CountOpposingTackleZones(MatchState match, Ruleset ruleset, LeagueTeam? opposingTeam, Guid teamId, Guid playerId, PitchSquare square)
    {
        if (opposingTeam is null)
        {
            return CountOpposingTackleZones(match, teamId, playerId, square);
        }

        return match.Placements.Count(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            MatchGeometry.HasActiveTackleZone(placement) &&
            MatchGeometry.IsAdjacentToPlacement(placement, square) &&
            !SkillHookResolver.PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.ModifyTarget, SkillEffect.Titchy));
    }

    public static bool IsMarkedByOpponent(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            MatchGeometry.HasActiveTackleZone(placement) &&
            MatchGeometry.IsAdjacentToPlacement(placement, square));
    }

    public static bool IsMarkedByOpponent(MatchState match, Guid teamId, Guid playerId, PitchSquare square, Guid ignoredOpponentId)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            placement.PlayerId != ignoredOpponentId &&
            MatchGeometry.HasActiveTackleZone(placement) &&
            MatchGeometry.IsAdjacentToPlacement(placement, square));
    }

    public static bool IsMarkedByOpponent(MatchState match, Ruleset ruleset, LeagueTeam? opposingTeam, Guid teamId, Guid playerId, PitchSquare square)
    {
        if (opposingTeam is null)
        {
            return IsMarkedByOpponent(match, teamId, playerId, square);
        }

        return match.Placements.Any(placement =>
            placement.TeamId == opposingTeam.Id &&
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            MatchGeometry.HasActiveTackleZone(placement) &&
            MatchGeometry.IsAdjacentToPlacement(placement, square) &&
            !SkillHookResolver.PlayerHasHookedEffect(ruleset, FindTeamPlayer(opposingTeam, placement.PlayerId), GameEventKind.MoveStep, GameEventStage.ModifyTarget, SkillEffect.Titchy));
    }

    public static bool HasUsedBlitz(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.Blitz);
    }

    public static bool HasUsedHandOff(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.HandOff);
    }

    public static bool HasUsedPass(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.Pass);
    }

    public static bool HasUsedFoul(MatchState match, Guid teamId)
    {
        return match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == match.Half &&
            activation.Turn == match.Turn &&
            activation.Action == PlayerTurnAction.Foul);
    }

    public static int TeamRerollsRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeRerollsRemaining : match.AwayRerollsRemaining;
    }

    public static int TeamBribesRemaining(MatchState match, Guid teamId)
    {
        return teamId == match.HomeTeamId ? match.HomeBribesRemaining : match.AwayBribesRemaining;
    }

    public static int CountTeamPlayersInWideZone(MatchState match, Ruleset ruleset, Guid teamId, PitchSquare square, Guid ignoredPlayerId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == teamId &&
            placement.PlayerId != ignoredPlayerId &&
            MatchGeometry.OccupiesPitch(placement.State) &&
            placement.Square is PitchSquare placedSquare &&
            MatchGeometry.IsSameWideZone(ruleset, square, placedSquare));
    }

    public static int CountTeamPlayersOnPitch(MatchState match, Guid teamId)
    {
        return match.Placements.Count(placement =>
            placement.TeamId == teamId &&
            MatchGeometry.OccupiesPitch(placement.State));
    }

    public static Player FindTeamPlayer(LeagueTeam team, Guid playerId)
    {
        return team.Players.FirstOrDefault(player => player.Id == playerId)
            ?? throw new InvalidOperationException($"Team '{team.Name}' does not contain player '{playerId}'.");
    }
}
