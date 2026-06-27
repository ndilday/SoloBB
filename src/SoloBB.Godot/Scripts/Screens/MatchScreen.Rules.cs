using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;
using static SoloBB.Core.Services.MatchFormatting;
using static SoloBB.Core.Services.MatchGeometry;
using static SoloBB.Core.Services.MatchQueries;
using static SoloBB.Core.Services.RollTargets;

namespace SoloBB.Godot.Scripts.Screens;

public partial class MatchScreen : VBoxContainer
{
    private bool IsLegalPlacementTarget(PitchSquare square)
    {
        if (_match.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup))
        {
            return false;
        }

        if (_match.Placements.Any(placement => placement.Square == square))
        {
            return false;
        }

        if (_selectedPlayerId is Guid selectedPlayerId)
        {
            var selectedPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == selectedPlayerId);
            if (selectedPlacement?.Square is null && CountActiveTeamPitchPlayers() >= _ruleset.PlayersPerSide)
            {
                return false;
            }
        }

        if (!IsActiveTeamSide(square))
        {
            return false;
        }

        return !IsWideZone(square) || CountActiveTeamWideZonePlayers(square) < 2;
    }

    private bool IsLegalKickoffTarget(PitchSquare square)
    {
        if (_match.Phase is not MatchPhase.Kickoff)
        {
            return false;
        }

        if (_match.PendingKickoffEvent is not null || _match.PendingOnTheBall is not null)
        {
            return false;
        }

        return IsActiveTeamSide(square);
    }

    private bool IsLegalWizardTarget(PitchSquare square)
    {
        if (!_wizardMode || _wizardModeTeamId != _match.ActiveTeamId || _match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn) || _match.Activations.Count > 0)
        {
            return false;
        }

        var effect = _match.ActiveTeamId == _match.HomeTeamId ? _match.HomeWizardEffect : _match.AwayWizardEffect;
        var remaining = _match.ActiveTeamId == _match.HomeTeamId ? _match.HomeWizardsRemaining : _match.AwayWizardsRemaining;
        if (remaining <= 0)
        {
            return false;
        }

        return effect == "wizard-fireball" ||
            effect == "wizard-lightning" && _match.Placements.Any(placement =>
                placement.TeamId != _match.ActiveTeamId &&
                placement.Square == square &&
                placement.State == PlayerPitchState.Standing);
    }

    private bool CanSelectPlayer(Guid playerId)
    {
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (placement is null)
        {
            return false;
        }

        if (placement.State is PlayerPitchState.Casualty or PlayerPitchState.Dead or PlayerPitchState.SentOff)
        {
            return false;
        }

        if (_match.PendingKickoffEvent is PendingKickoffEventChoice pendingKickoff)
        {
            return placement.TeamId == pendingKickoff.TeamId &&
                pendingKickoff.EligiblePlayerIds.Contains(playerId) &&
                !pendingKickoff.MovedPlayerIds.Contains(playerId) &&
                placement.Square is not null &&
                placement.State == PlayerPitchState.Standing;
        }

        if (placement.TeamId != _match.ActiveTeamId)
        {
            return false;
        }

        if (_match.Phase is MatchPhase.DefenseSetup or MatchPhase.OffenseSetup)
        {
            return placement.State is PlayerPitchState.Reserve or PlayerPitchState.Standing;
        }

        if (!IsPlayerTurnPhase())
        {
            return false;
        }

        if (_match.PendingReroll is PendingRerollChoice pendingReroll && pendingReroll.PlayerId != playerId)
        {
            return false;
        }

        if (_match.PendingApothecary is not null)
        {
            return false;
        }

        if (_match.PendingSendOff is not null)
        {
            return false;
        }

        if (_match.PendingStandFirm is not null)
        {
            return false;
        }

        if (_match.PendingDivingTackle is not null)
        {
            return false;
        }

        if (_match.PendingDumpOff is not null || _match.PendingOnTheBall is not null)
        {
            return false;
        }

        if (_match.PendingBallPlacement is PendingBallPlacementChoice ballPlacement)
        {
            return placement.Square is not null && ballPlacement.LegalSquares.Contains(placement.Square);
        }

        if (_match.PendingBombThrow is not null)
        {
            return false;
        }

        if (_match.PendingInterception is PendingInterceptionChoice pendingInterception &&
            pendingInterception.PasserPlayerId != playerId &&
            pendingInterception.ReceiverPlayerId != playerId &&
            !pendingInterception.EligiblePlayerIds.Contains(playerId))
        {
            return false;
        }

        if (_match.PendingBlock is PendingBlockChoice pendingBlock && pendingBlock.AttackerPlayerId != playerId)
        {
            return false;
        }

        if (_match.PendingPush is PendingPushChoice pendingPush &&
            pendingPush.AttackerPlayerId != playerId &&
            pendingPush.DefenderPlayerId != playerId)
        {
            return false;
        }

        if (placement.Square is null || placement.State is not (PlayerPitchState.Standing or PlayerPitchState.Prone))
        {
            return false;
        }

        var activation = CurrentTurnActivation(playerId);
        if (activation is { Completed: true })
        {
            return false;
        }

        // A still-uncommitted declaration (a Pass/Blitz/Hand-off declared but not yet acted on)
        // locks selection, because switching away would orphan it and it can still be cancelled
        // cleanly. Once the player has started moving the action is committed for good: from then on
        // a stray click is caught by the tentative selection in SelectPlayer (which keeps them
        // "current" and resumable) rather than by refusing the click.
        if (_currentActivationPlayerId is Guid activeId && activeId != playerId)
        {
            var activeActivation = CurrentTurnActivation(activeId);
            if (activeActivation is { Completed: false, DeclaredOnly: true })
            {
                return false;
            }
        }

        return activation is null || _currentActivationPlayerId == playerId;
    }

    private string CannotSelectReason(Guid playerId)
    {
        if (_currentActivationPlayerId is Guid activeId && activeId != playerId)
        {
            var activeActivation = CurrentTurnActivation(activeId);
            if (activeActivation is { Completed: false, DeclaredOnly: true })
            {
                return $"You must finish or cancel {FindPlayer(activeId)?.Name ?? "the active player"}'s action first.";
            }
        }

        if (HasCurrentTurnActivation(playerId))
        {
            return $"{FindPlayer(playerId)?.Name ?? "That player"} has already activated this turn.";
        }

        return "That player cannot be selected right now.";
    }

    private bool CanReturnSelectedSetupPlayerToReserve()
    {
        if (_selectedPlayerId is not Guid playerId ||
            _match.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup))
        {
            return false;
        }

        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        return placement is
        {
            State: PlayerPitchState.Standing,
            Square: not null
        } && placement.TeamId == _match.ActiveTeamId;
    }

    private string ActivationDisplayState(Guid playerId, PlayerPlacement? placement)
    {
        if (placement?.State is PlayerPitchState.Casualty or PlayerPitchState.Dead or PlayerPitchState.SentOff)
        {
            return "Unavailable";
        }

        if (!IsPlayerTurnPhase())
        {
            if (_match.PendingKickoffEvent is PendingKickoffEventChoice pendingKickoff)
            {
                if (pendingKickoff.MovedPlayerIds.Contains(playerId))
                {
                    return "Activated";
                }

                return pendingKickoff.EligiblePlayerIds.Contains(playerId) ? "Ready" : "Available";
            }

            return placement?.Square is null ? "Reserve" : "Available";
        }

        if (placement?.Square is null ||
            placement.State is not (PlayerPitchState.Standing or PlayerPitchState.Prone))
        {
            return "Unavailable";
        }

        var activation = CurrentTurnActivation(playerId);
        if (activation is { Completed: true })
        {
            return "Activated";
        }

        if (_currentActivationPlayerId == playerId)
        {
            return "Current";
        }

        return activation is not null ? "Activated" : "Ready";
    }

    private static string RosterStatusLabel(PlayerPlacement? placement)
    {
        if (placement is null)
        {
            return "Unknown";
        }

        return placement.State switch
        {
            PlayerPitchState.KnockedOut => "KOed",
            PlayerPitchState.Casualty => "Injured",
            PlayerPitchState.Dead => "Dead",
            PlayerPitchState.SentOff => "Sent Off",
            _ when placement.Square is PitchSquare square => $"{square.X + 1},{square.Y + 1}",
            PlayerPitchState.Reserve => "Reserve",
            PlayerPitchState.Prone => "Prone",
            PlayerPitchState.Stunned => "Stunned",
            _ => placement.State.ToString()
        };
    }

    private string RosterTooltip(Player player, PlayerPlacement? placement)
    {
        var stats = FormatStats(player.Stats);
        var skillNames = player.Skills.Count == 0
            ? ""
            : $"\n{string.Join(", ", player.Skills.Select(id => _ruleset.Skills.FirstOrDefault(s => s.Id == id)?.Name ?? id))}";
        var baseText = $"{stats}{skillNames}";
        return placement?.Casualty is null
            ? baseText
            : $"{baseText}\nCasualty: {FormatCasualtyResult(placement.Casualty.Result)} ({placement.Casualty.Roll})";
    }

    private Color RosterButtonColor(Guid playerId, PlayerPlacement? placement)
    {
        return ActivationDisplayState(playerId, placement) switch
        {
            "Current" => CurrentPlayerColor,
            "Activated" => ActivatedPlayerColor,
            "Unavailable" => UnavailablePlayerColor,
            _ => ReadyPlayerColor
        };
    }


    private string PlayerPitchTooltip(PlayerPlacement placement)
    {
        var player = FindPlayer(placement.PlayerId);
        var playerName = player?.Name ?? "Unknown";
        var stats = player is null ? "" : $"\n{FormatStats(player.Stats)}";
        var skillNames = player is null || player.Skills.Count == 0
            ? ""
            : $"\n{string.Join(", ", player.Skills.Select(id => _ruleset.Skills.FirstOrDefault(s => s.Id == id)?.Name ?? id))}";
        var baseText = $"{playerName}{stats}{skillNames}";
        return IsPlayerTurnPhase()
            ? $"{baseText}\n{ActivationDisplayState(placement.PlayerId, placement)}"
            : baseText;
    }

    private bool IsPlayerTurnPhase()
    {
        return _match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn;
    }

    private bool HasCurrentTurnActivation(Guid playerId)
    {
        return CurrentTurnActivation(playerId) is not null;
    }

    // True when the player has an activation this turn that has not yet finished, so they may
    // still be the "current" highlighted player. A completed activation (e.g. after a follow-up,
    // both-down, pass, or hand-off) means the player is done for the turn.
    private bool IsActivationOngoing(Guid playerId)
    {
        return CurrentTurnActivation(playerId) is { Completed: false };
    }

    private PlayerTurnActivation? CurrentTurnActivation(Guid playerId)
    {
        return _match.Activations.FirstOrDefault(activation =>
            activation.PlayerId == playerId &&
            activation.TeamId == _match.ActiveTeamId &&
            activation.Half == _match.Half &&
            activation.Turn == _match.Turn);
    }

    private bool IsLegalBlockTarget(Guid attackerId, Guid defenderId)
    {
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingDumpOff is not null ||
            _match.PendingOnTheBall is not null ||
            _match.PendingBombThrow is not null ||
            HasCurrentTurnActivation(attackerId))
        {
            return false;
        }

        var attackerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerId);
        var defenderPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderId);
        return attackerPlacement?.TeamId == _match.ActiveTeamId &&
            defenderPlacement is not null &&
            defenderPlacement.TeamId != _match.ActiveTeamId &&
            attackerPlacement.Square is PitchSquare attackerSquare &&
            defenderPlacement.Square is PitchSquare defenderSquare &&
            attackerPlacement.State == PlayerPitchState.Standing &&
            defenderPlacement.State == PlayerPitchState.Standing &&
            IsAdjacent(attackerSquare, defenderSquare);
    }

    private bool IsLegalBlitzTarget(Guid attackerId, Guid defenderId)
    {
        var activation = CurrentTurnActivation(attackerId);
        // A blitz is offered to an unactivated player, a player still on a provisional Move (lazy upgrade),
        // or one who has already committed to a Blitz this activation.
        var canBlitz = activation is null ||
            activation is { Action: PlayerTurnAction.Move, Completed: false } ||
            activation.Action == PlayerTurnAction.Blitz;
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingDumpOff is not null ||
            _match.PendingOnTheBall is not null ||
            _match.PendingBombThrow is not null ||
            _match.PendingInterception is not null ||
            _match.PendingReroll is not null ||
            !canBlitz ||
            (HasUsedBlitz(_match.ActiveTeamId) && activation?.Action != PlayerTurnAction.Blitz))
        {
            return false;
        }

        var attackerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerId);
        var defenderPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderId);
        if (attackerPlacement?.TeamId != _match.ActiveTeamId ||
            defenderPlacement is null ||
            defenderPlacement.TeamId == _match.ActiveTeamId ||
            attackerPlacement.Square is not PitchSquare attackerSquare ||
            defenderPlacement.Square is not PitchSquare defenderSquare ||
            attackerPlacement.State is not (PlayerPitchState.Standing or PlayerPitchState.Prone) ||
            defenderPlacement.State != PlayerPitchState.Standing)
        {
            return false;
        }

        if (attackerPlacement.State == PlayerPitchState.Standing && IsAdjacent(attackerSquare, defenderSquare))
        {
            return true;
        }

        return FindBlitzDestination(attackerId, defenderId) is not null;
    }

    private bool IsLegalKickoffBlitzTarget(Guid attackerId, Guid defenderId)
    {
        if (_match.PendingKickoffEvent is not PendingKickoffEventChoice pending ||
            pending.Kind != KickoffEventKind.Blitz ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            !pending.EligiblePlayerIds.Contains(attackerId) ||
            pending.MovedPlayerIds.Contains(attackerId))
        {
            return false;
        }

        var attackerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerId);
        var defenderPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderId);
        return attackerPlacement?.TeamId == pending.TeamId &&
            defenderPlacement is not null &&
            defenderPlacement.TeamId == pending.ReceivingTeamId &&
            attackerPlacement.Square is PitchSquare attackerSquare &&
            defenderPlacement.Square is PitchSquare defenderSquare &&
            attackerPlacement.State == PlayerPitchState.Standing &&
            defenderPlacement.State == PlayerPitchState.Standing &&
            IsAdjacent(attackerSquare, defenderSquare);
    }

    private bool IsLegalFoulTarget(Guid foulerId, Guid victimId)
    {
        // A foul may be the gesture that resolves a provisional Move (move adjacent, then foul); it is only
        // blocked once the player has committed to some other action this turn.
        var foulerActivation = CurrentTurnActivation(foulerId);
        var foulerUncommitted = foulerActivation is null || foulerActivation is { Action: PlayerTurnAction.Move, Completed: false };
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingReroll is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingDumpOff is not null ||
            _match.PendingOnTheBall is not null ||
            _match.PendingBombThrow is not null ||
            !foulerUncommitted ||
            HasUsedFoul(_match.ActiveTeamId))
        {
            return false;
        }

        var foulerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == foulerId);
        var victimPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == victimId);
        return foulerPlacement?.TeamId == _match.ActiveTeamId &&
            victimPlacement is not null &&
            victimPlacement.TeamId != _match.ActiveTeamId &&
            foulerPlacement.Square is PitchSquare foulerSquare &&
            victimPlacement.Square is PitchSquare victimSquare &&
            foulerPlacement.State == PlayerPitchState.Standing &&
            victimPlacement.State is PlayerPitchState.Prone or PlayerPitchState.Stunned &&
            IsAdjacent(foulerSquare, victimSquare);
    }

    private PitchSquare? FindBlitzDestination(Guid attackerId, Guid defenderId)
    {
        var attackerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerId);
        var defenderPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderId);
        var attacker = FindPlayer(attackerId);
        if (attackerPlacement?.Square is not PitchSquare attackerSquare ||
            defenderPlacement?.Square is not PitchSquare defenderSquare ||
            attacker is null)
        {
            return null;
        }

        if (attackerPlacement.State == PlayerPitchState.Standing && IsAdjacent(attackerSquare, defenderSquare))
        {
            return attackerSquare;
        }

        if (attackerPlacement.State == PlayerPitchState.Prone && IsAdjacent(attackerSquare, defenderSquare))
        {
            return attackerSquare;
        }

        var movementAllowance = attackerPlacement.State == PlayerPitchState.Prone
            ? Math.Max(0, attacker.Stats.Movement - 3)
            : attacker.Stats.Movement;
        return AdjacentSquares(defenderSquare)
            .Where(square => IsOnPitch(square))
            .Where(square => !_match.Placements.Any(placement => placement.PlayerId != attackerId && placement.Square == square))
            .Select(square => new { Square = square, Path = BuildMovementPath(attackerSquare, square) })
            .Where(candidate => candidate.Path.Count > 0 &&
                candidate.Path.Count <= movementAllowance + 3 &&
                candidate.Path.All(pathSquare => !_match.Placements.Any(placement => placement.PlayerId != attackerId && placement.Square == pathSquare)))
            .OrderBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => Math.Abs(candidate.Square.X - attackerSquare.X) + Math.Abs(candidate.Square.Y - attackerSquare.Y))
            .Select(candidate => (PitchSquare?)candidate.Square)
            .FirstOrDefault();
    }

    private BlockPreview? ResolveBlockPreview(Guid defenderId)
    {
        if (_selectedPlayerId is not Guid attackerId)
        {
            return null;
        }

        var attacker = FindPlayer(attackerId);
        var defender = FindPlayer(defenderId);
        var attackerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == attackerId);
        var defenderPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == defenderId);
        if (attacker is null || defender is null || attackerPlacement?.Square is null || defenderPlacement?.Square is null)
        {
            return null;
        }

        var attackerSquare = _previewBlitzDefenderId == defenderId && _previewBlitzDestination is PitchSquare blitzDestination
            ? blitzDestination
            : attackerPlacement.Square;
        var attackerAssistIds = FindAssistIds(_match.ActiveTeamId, defenderId, defenderPlacement.Square, attackerId);
        var defenderAssistIds = FindAssistIds(defenderPlacement.TeamId, attackerId, attackerSquare, defenderId);
        var attackerStrength = attacker.Stats.Strength + attackerAssistIds.Count;
        var defenderStrength = defender.Stats.Strength + defenderAssistIds.Count;
        var dice = ResolveBlockDice(attackerStrength, defenderStrength);
        return new BlockPreview(attackerStrength, defenderStrength, dice, attackerAssistIds, defenderAssistIds);
    }

    private IReadOnlyList<Guid> FindAssistIds(Guid assistingTeamId, Guid opposedPlayerId, PitchSquare targetSquare, Guid primaryPlayerId)
    {
        return _match.Placements
            .Where(placement =>
                placement.TeamId == assistingTeamId &&
                placement.PlayerId != primaryPlayerId &&
                placement.PlayerId != opposedPlayerId &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is PitchSquare square &&
                IsAdjacent(square, targetSquare) &&
                !IsMarkedByOpponent(_match, assistingTeamId, placement.PlayerId, square, opposedPlayerId))
            .Select(placement => placement.PlayerId)
            .ToArray();
    }

    private string? BlockPreviewRole(Guid playerId)
    {
        if (_previewFoulVictimId == playerId)
        {
            return "target";
        }

        var defenderId = _previewBlockDefenderId ?? _previewBlitzDefenderId;
        if (defenderId is not Guid previewDefenderId)
        {
            return null;
        }

        if (playerId == previewDefenderId)
        {
            return "target";
        }

        var preview = ResolveBlockPreview(previewDefenderId);
        if (preview is null)
        {
            return null;
        }

        if (preview.AttackerAssistPlayerIds.Contains(playerId))
        {
            return "attackAssist";
        }

        return preview.DefenderAssistPlayerIds.Contains(playerId) ? "defenseAssist" : null;
    }

    private string BlockPreviewSummary(Guid defenderId)
    {
        var defenderName = FindPlayer(defenderId)?.Name ?? "defender";
        var preview = ResolveBlockPreview(defenderId);
        if (preview is null)
        {
            return $"Click {defenderName} again to confirm the block.";
        }

        var strengthLeader = preview.AttackerStrength == preview.DefenderStrength
            ? "even strength"
            : preview.AttackerStrength > preview.DefenderStrength ? "attacker stronger" : "defender stronger";
        return $"Block preview: {preview.Dice} block dice, ST {preview.AttackerStrength}-{preview.DefenderStrength} ({strengthLeader}). Click {defenderName} again to roll.";
    }

    private string BlitzPreviewSummary(Guid defenderId)
    {
        var defenderName = FindPlayer(defenderId)?.Name ?? "defender";
        var destination = _previewBlitzDestination;
        var preview = ResolveBlockPreview(defenderId);
        if (destination is null || preview is null)
        {
            return $"Click {defenderName} again to confirm the blitz.";
        }

        return $"Blitz preview: move to {destination.X + 1},{destination.Y + 1}, then {preview.Dice} block dice, ST {preview.AttackerStrength}-{preview.DefenderStrength}. Click {defenderName} again to blitz.";
    }

    private string FoulPreviewSummary(Guid victimId)
    {
        var victimName = FindPlayer(victimId)?.Name ?? "victim";
        var assists = ResolveFoulAssistPreview(victimId);
        return $"Foul preview: armor modifier +{assists.AttackingAssists} -{assists.DefendingAssists}. Doubles may send off the fouler. Click {victimName} again to foul.";
    }

    private FoulAssistPreview ResolveFoulAssistPreview(Guid victimId)
    {
        if (_selectedPlayerId is not Guid foulerId)
        {
            return new FoulAssistPreview(0, 0);
        }

        var victimPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == victimId);
        if (victimPlacement?.Square is not PitchSquare victimSquare)
        {
            return new FoulAssistPreview(0, 0);
        }

        return new FoulAssistPreview(
            CountFoulAssists(_match.ActiveTeamId, victimId, victimSquare, foulerId),
            CountFoulAssists(victimPlacement.TeamId, victimId, victimSquare, foulerId));
    }

    private int CountFoulAssists(Guid assistingTeamId, Guid victimId, PitchSquare victimSquare, Guid foulerId)
    {
        return _match.Placements.Count(placement =>
            placement.TeamId == assistingTeamId &&
            placement.PlayerId != foulerId &&
            placement.PlayerId != victimId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare square &&
            IsAdjacent(square, victimSquare) &&
            !IsMarkedByOpponent(_match, assistingTeamId, placement.PlayerId, square, victimId));
    }

    private bool IsLegalPassTarget(Guid passerId, Guid receiverId)
    {
        if (passerId == receiverId)
        {
            return false;
        }

        var receiverPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == receiverId);
        if (receiverPlacement?.TeamId != _match.ActiveTeamId ||
            receiverPlacement.Square is null ||
            receiverPlacement.State != PlayerPitchState.Standing)
        {
            return false;
        }

        return IsLegalPassTargetSquare(passerId, receiverPlacement.Square);
    }

    private bool CanEnterPassMode(Guid passerId)
    {
        return CanDeclareBallAction(passerId, PlayerTurnAction.Pass, HasUsedPass(_match.ActiveTeamId));
    }

    private bool CanEnterHandOffMode(Guid carrierId)
    {
        return CanDeclareBallAction(carrierId, PlayerTurnAction.HandOff, HasUsedHandOff(_match.ActiveTeamId));
    }

    // True while the selected carrier may hand off: they have either already committed a hand-off this
    // activation, or are still eligible to (provisional Move, hand-off budget free). With lazy declaration
    // there is no mode toggle — the targeting affordance is offered whenever the hand-off is legal.
    private bool IsHandingOff(Guid carrierId)
    {
        return CurrentTurnActivation(carrierId)?.Action == PlayerTurnAction.HandOff ||
            CanEnterHandOffMode(carrierId);
    }

    // A pass or hand-off can be declared before the player holds the ball, so they may move to
    // collect it and still throw. The action is committed for the turn once declared.
    private bool CanDeclareBallAction(Guid playerId, PlayerTurnAction action, bool teamAlreadyUsedAction)
    {
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingDumpOff is not null ||
            _match.PendingOnTheBall is not null ||
            _match.PendingBombThrow is not null ||
            _match.PendingInterception is not null ||
            _match.PendingReroll is not null)
        {
            return false;
        }

        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (placement?.TeamId != _match.ActiveTeamId ||
            placement.Square is null ||
            placement.State != PlayerPitchState.Standing)
        {
            return false;
        }

        var activation = CurrentTurnActivation(playerId);
        if (activation is not null)
        {
            // Already committed to this ball action, or still on a provisional Move that the throw can upgrade.
            return activation.Action == action ||
                (activation is { Action: PlayerTurnAction.Move, Completed: false } && !teamAlreadyUsedAction);
        }

        return !teamAlreadyUsedAction;
    }

    private bool IsLegalHandOffTarget(Guid carrierId, Guid receiverId)
    {
        if (carrierId == receiverId || !CanEnterHandOffMode(carrierId) || _match.Ball.CarrierPlayerId != carrierId)
        {
            return false;
        }

        var carrierPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == carrierId);
        var receiverPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == receiverId);
        if (carrierPlacement?.Square is not PitchSquare carrierSquare ||
            receiverPlacement?.TeamId != _match.ActiveTeamId ||
            receiverPlacement.Square is not PitchSquare receiverSquare ||
            receiverPlacement.State != PlayerPitchState.Standing)
        {
            return false;
        }

        return IsAdjacent(carrierSquare, receiverSquare);
    }

    private bool CanEnterLaunchMode(Guid actorId, string skillId)
    {
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingDumpOff is not null ||
            _match.PendingOnTheBall is not null ||
            _match.PendingBombThrow is not null ||
            _match.PendingInterception is not null ||
            HasCurrentTurnActivation(actorId))
        {
            return false;
        }

        if (skillId == "throw-team-mate" && HasUsedPass(_match.ActiveTeamId))
        {
            return false;
        }

        var actor = FindPlayer(actorId);
        var actorPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == actorId);
        return actor is not null &&
            PlayerHasLaunchActionEffect(actor, skillId) &&
            actorPlacement?.TeamId == _match.ActiveTeamId &&
            actorPlacement.Square is not null &&
            actorPlacement.State == PlayerPitchState.Standing;
    }

    private bool IsLegalLaunchPlayer(Guid actorId, Guid launchedId)
    {
        var actorPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == actorId);
        var launchedPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == launchedId);
        var launched = FindPlayer(launchedId);
        return actorPlacement?.TeamId == _match.ActiveTeamId &&
            launchedPlacement?.TeamId == _match.ActiveTeamId &&
            actorPlacement.Square is PitchSquare actorSquare &&
            launchedPlacement.Square is PitchSquare launchedSquare &&
            launchedPlacement.State == PlayerPitchState.Standing &&
            launched is not null &&
            PlayerHasLaunchEligibilityEffect(launched, LaunchSkillIdForMode()) &&
            IsAdjacent(actorSquare, launchedSquare);
    }

    private bool IsLegalLaunchTargetSquare(Guid actorId, Guid launchedId, PitchSquare targetSquare)
    {
        if (!IsOnPitch(targetSquare) || !IsLegalLaunchPlayer(actorId, launchedId))
        {
            return false;
        }

        var actorPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == actorId);
        return actorPlacement?.Square is PitchSquare actorSquare &&
            ResolvePassRange(actorSquare, targetSquare) is not null;
    }

    private bool IsLegalPassTargetSquare(Guid passerId, PitchSquare targetSquare)
    {
        if (!CanEnterPassMode(passerId) || _match.Ball.CarrierPlayerId != passerId || !IsOnPitch(targetSquare))
        {
            return false;
        }

        var passerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == passerId);
        if (passerPlacement?.Square is null || passerPlacement.Square == targetSquare)
        {
            return false;
        }

        return ResolvePassRange(passerPlacement.Square, targetSquare) is not null;
    }

    private PassPreview? ResolvePassPreview(PitchSquare targetSquare)
    {
        if (_selectedPlayerId is not Guid passerId)
        {
            return null;
        }

        var passer = FindPlayer(passerId);
        var passerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == passerId);
        if (passer is null || passerPlacement?.Square is null)
        {
            return null;
        }

        var passRange = ResolvePassRange(passerPlacement.Square, targetSquare);
        if (passRange is null)
        {
            return null;
        }

        var receiverPlacement = _match.Placements.FirstOrDefault(placement =>
            placement.TeamId == _match.ActiveTeamId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square == targetSquare);
        var receiver = receiverPlacement is null ? null : FindPlayer(receiverPlacement.PlayerId);
        var passerTackleZones = CountOpposingTackleZones(_match, _match.ActiveTeamId, passerId, passerPlacement.Square);
        var catchTackleZones = receiver is null
            ? 0
            : CountOpposingTackleZones(_match, _match.ActiveTeamId, receiver.Id, targetSquare);
        var interceptors = FindEligibleInterceptors(OpponentTeam().Id, passerPlacement.Square, targetSquare);
        return new PassPreview(
            passRange.Name,
            PassingTarget(passer, passRange, _match.Weather, passerTackleZones),
            receiver is null ? null : CatchTarget(receiver, _match.Weather, catchTackleZones),
            interceptors.Select(placement => placement.PlayerId).ToArray());
    }

    private string? PassPreviewRole(Guid playerId)
    {
        if (_match.PendingInterception is PendingInterceptionChoice pending && pending.EligiblePlayerIds.Contains(playerId))
        {
            return "interceptor";
        }

        if (_previewPassTargetSquare is not PitchSquare targetSquare)
        {
            return null;
        }

        if (_previewPassReceiverId == playerId)
        {
            return "receiver";
        }

        var preview = ResolvePassPreview(targetSquare);
        return preview?.EligibleInterceptorPlayerIds.Contains(playerId) == true ? "interceptor" : null;
    }

    private string PassPreviewSummary(PitchSquare targetSquare)
    {
        var targetName = _previewPassReceiverId is Guid receiverId
            ? FindPlayer(receiverId)?.Name ?? "receiver"
            : $"{targetSquare.X + 1},{targetSquare.Y + 1}";
        var preview = ResolvePassPreview(targetSquare);
        if (preview is null)
        {
            return $"Right-click {targetName} again to confirm the pass.";
        }

        var interceptionText = preview.EligibleInterceptorPlayerIds.Count == 0
            ? "no eligible interceptors"
            : $"{preview.EligibleInterceptorPlayerIds.Count} eligible interceptor{(preview.EligibleInterceptorPlayerIds.Count == 1 ? "" : "s")}";
        var catchText = preview.CatchTarget is int catchTarget ? $", catch {catchTarget}+" : ", no catch target";
        return $"Pass preview: {preview.RangeName} pass {preview.PassTarget}+{catchText}, {interceptionText}. Right-click {targetName} again to throw.";
    }

    private string PassSquareTooltip(PitchSquare targetSquare)
    {
        var preview = ResolvePassPreview(targetSquare);
        return preview is null
            ? $"{targetSquare.X + 1},{targetSquare.Y + 1} - pass target"
            : $"{targetSquare.X + 1},{targetSquare.Y + 1} - {preview.RangeName} pass {preview.PassTarget}+";
    }

    private string LaunchPreviewSummary()
    {
        var actionName = _throwTeamMateMode ? "Throw Team-Mate" : "Kick Team-Mate";
        if (_previewLaunchedPlayerId is not Guid launchedId)
        {
            return $"{actionName}: choose an adjacent standing Right Stuff team-mate.";
        }

        var launchedName = FindPlayer(launchedId)?.Name ?? "team-mate";
        if (_previewLaunchTargetSquare is not PitchSquare targetSquare)
        {
            return $"{actionName}: choose a landing target for {launchedName}. Occupied squares can cause collision chains.";
        }

        return $"{actionName}: click {targetSquare.X + 1},{targetSquare.Y + 1} again to launch {launchedName}.";
    }

    private string LaunchSquareTooltip(PitchSquare targetSquare)
    {
        return _previewLaunchTargetSquare == targetSquare
            ? "Click again to confirm launch"
            : $"{targetSquare.X + 1},{targetSquare.Y + 1} - launch target";
    }

    private bool IsLegalMovementTarget(Guid playerId, PitchSquare square)
    {
        if (_isAnimating)
        {
            return false;
        }

        if (_match.PendingKickoffEvent is PendingKickoffEventChoice pendingKickoff)
        {
            var kickoffPlacement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
            if (kickoffPlacement?.TeamId != pendingKickoff.TeamId ||
                kickoffPlacement.Square is not PitchSquare source ||
                kickoffPlacement.State != PlayerPitchState.Standing ||
                !pendingKickoff.EligiblePlayerIds.Contains(playerId) ||
                pendingKickoff.MovedPlayerIds.Contains(playerId) ||
                !_match.Phase.Equals(MatchPhase.Kickoff) ||
                !IsOnPitch(square))
            {
                return false;
            }

            if (_match.Placements.Any(current => current.PlayerId != playerId && current.Square == square))
            {
                return false;
            }

            if (pendingKickoff.Kind == KickoffEventKind.SolidDefence)
            {
                return square != source &&
                    IsTeamSetupSide(pendingKickoff.TeamId, square) &&
                    (!IsWideZone(square) || CountTeamWideZonePlayers(pendingKickoff.TeamId, square, playerId) < 2);
            }

            return pendingKickoff.Kind == KickoffEventKind.HighKick
                ? square == pendingKickoff.LandingSquare
                : IsAdjacent(source, square);
        }

        if (_match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            return false;
        }

        if (_match.PendingPush is not null)
        {
            return false;
        }

        if (_match.PendingFollowUp is not null)
        {
            return false;
        }

        if (_match.PendingBombThrow is not null)
        {
            return false;
        }

        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (placement is null ||
            placement.TeamId != _match.ActiveTeamId ||
            placement.Square is null ||
            placement.State is not (PlayerPitchState.Standing or PlayerPitchState.Prone))
        {
            return false;
        }

        var activation = CurrentTurnActivation(playerId);
        if (activation is not null &&
            (_currentActivationPlayerId != playerId || activation.Action is not (PlayerTurnAction.Move or PlayerTurnAction.Blitz or PlayerTurnAction.Pass or PlayerTurnAction.HandOff)))
        {
            return false;
        }

        if (_match.Placements.Any(current => current.PlayerId != playerId && current.Square == square))
        {
            return false;
        }

        var player = FindPlayer(playerId);
        if (player is null)
        {
            return false;
        }

        var path = BuildMovementPath(placement.Square, square);
        var remainingMovement = RemainingTotalMovement(player, placement, activation);
        return (path.Count > 0 || placement.State == PlayerPitchState.Prone) &&
            path.Count <= remainingMovement &&
            path.All(pathSquare => !_match.Placements.Any(current => current.PlayerId != playerId && current.Square == pathSquare));
    }

    private bool IsLegalPushSquare(PitchSquare square)
    {
        return _match.PendingPush?.LegalSquares.Contains(square) == true;
    }

    private bool IsLegalFollowUpSquare(PitchSquare square)
    {
        return _match.PendingFollowUp?.FollowUpSquare == square;
    }

    private bool IsLegalBallPlacementSquare(PitchSquare square)
    {
        return _match.PendingBallPlacement?.LegalSquares.Contains(square) == true;
    }

    private bool IsLegalBombThrowSquare(PitchSquare square)
    {
        if (_match.PendingBombThrow is not PendingBombThrowChoice pending || !IsOnPitch(square))
        {
            return false;
        }

        return ResolvePassRange(pending.BombSquare, square) is not null;
    }

    private bool IsLegalDumpOffSquare(PitchSquare square)
    {
        if (_match.PendingDumpOff is not PendingDumpOffChoice pending || !IsOnPitch(square))
        {
            return false;
        }

        var carrierSquare = _match.Placements.FirstOrDefault(placement => placement.PlayerId == pending.CarrierPlayerId)?.Square;
        if (carrierSquare is null || carrierSquare == square)
        {
            return false;
        }

        // Dump-Off can only make a Quick Pass.
        return ResolvePassRange(carrierSquare, square)?.Name == "quick";
    }

    private bool IsLegalOnTheBallSquare(PitchSquare square)
    {
        if (_match.PendingOnTheBall is not PendingOnTheBallChoice pending || !IsOnPitch(square))
        {
            return false;
        }

        if (_onTheBallMoverId is not Guid moverId || !pending.EligiblePlayerIds.Contains(moverId))
        {
            return false;
        }

        var moverSquare = _match.Placements.FirstOrDefault(placement => placement.PlayerId == moverId)?.Square;
        if (moverSquare is null || moverSquare == square)
        {
            return false;
        }

        if (ChebyshevDistance(moverSquare, square) > 3)
        {
            return false;
        }

        if (_match.Placements.Any(placement => placement.PlayerId != moverId && PlacementOccupiesSquare(placement, square) && OccupiesPitch(placement.State)))
        {
            return false;
        }

        if (pending.Trigger == OnTheBallTrigger.PassDeclared)
        {
            return pending.PassTargetSquare is PitchSquare target &&
                ChebyshevDistance(square, target) < ChebyshevDistance(moverSquare, target);
        }

        // Kickoff window: the mover must stay in their own half (the side they currently occupy).
        var midline = _ruleset.PitchWidth / 2;
        return (moverSquare.X < midline) == (square.X < midline);
    }

    private string? MovementPathMarker(PitchSquare square)
    {
        if (_selectedPlayerId is not Guid playerId || !_previewPath.Contains(square))
        {
            return null;
        }

        var stepIndex = MovementStepIndex(square);
        if (stepIndex < 0)
        {
            return null;
        }

        var needsGoForIt = MovementStepNeedsGoForIt(playerId, stepIndex);
        var target = MovementStepTarget(playerId, stepIndex);
        if (target is not null)
        {
            return needsGoForIt ? $"!{target}+" : $"{target}+";
        }

        return _previewDestination == square ? "X" : ".";
    }

    private string MovementTooltip(PitchSquare square, string? pathMarker)
    {
        var coordinate = $"{square.X + 1},{square.Y + 1}";
        if (pathMarker is null || _selectedPlayerId is not Guid playerId)
        {
            return coordinate;
        }

        var stepIndex = MovementStepIndex(square);
        if (stepIndex < 0)
        {
            return coordinate;
        }

        var notes = new List<string>();
        if (MovementStepNeedsDodge(playerId, stepIndex))
        {
            var player = FindPlayer(playerId);
            if (player is not null)
            {
                var tackleZones = CountOpposingTackleZones(_match, _match.ActiveTeamId, playerId, square);
                notes.Add($"Dodge {DodgeTarget(player, tackleZones)}+ (AG base {player.Stats.Agility}+, +1 dodge, -{tackleZones} tackle zone{(tackleZones == 1 ? "" : "s")})");
            }
        }

        if (MovementStepNeedsGoForIt(playerId, stepIndex))
        {
            var target = GoForItTarget(_match.Weather);
            notes.Add($"Go-for-it {target}+ (fails on natural 1)");
        }

        if (MovementStepNeedsPickup(stepIndex))
        {
            var player = FindPlayer(playerId);
            if (player is not null)
            {
                var tackleZones = CountOpposingTackleZones(_match, _match.ActiveTeamId, playerId, square);
                var weatherText = _match.Weather == WeatherCondition.PouringRain ? ", -1 pouring rain" : "";
                notes.Add($"Pickup {PickupTarget(player, tackleZones, _match.Weather)}+ (AG base {player.Stats.Agility}+, +1 pickup, -{tackleZones} tackle zone{(tackleZones == 1 ? "" : "s")}{weatherText})");
            }
        }

        if (_previewDestination == square)
        {
            notes.Add("destination");
        }

        return notes.Count == 0
            ? coordinate
            : $"{coordinate} - {string.Join(", ", notes)}";
    }

    private int MovementStepIndex(PitchSquare square)
    {
        for (var index = 0; index < _previewPath.Count; index++)
        {
            if (_previewPath[index] == square)
            {
                return index;
            }
        }

        return -1;
    }

    private bool MovementStepNeedsDodge(Guid playerId, int stepIndex)
    {
        var start = PlayerSquare(playerId);
        if (start is null)
        {
            return false;
        }

        var previousSquare = stepIndex == 0 ? start : _previewPath[stepIndex - 1];
        return IsMarkedByOpponent(_match, _match.ActiveTeamId, playerId, previousSquare);
    }

    private int? MovementStepTarget(Guid playerId, int stepIndex)
    {
        var player = FindPlayer(playerId);
        if (player is null)
        {
            return null;
        }

        var targets = new List<int>();
        if (MovementStepNeedsDodge(playerId, stepIndex))
        {
            var tackleZones = CountOpposingTackleZones(_match, _match.ActiveTeamId, playerId, _previewPath[stepIndex]);
            targets.Add(DodgeTarget(player, tackleZones));
        }

        if (MovementStepNeedsGoForIt(playerId, stepIndex))
        {
            targets.Add(GoForItTarget(_match.Weather));
        }

        if (MovementStepNeedsPickup(stepIndex))
        {
            var tackleZones = CountOpposingTackleZones(_match, _match.ActiveTeamId, playerId, _previewPath[stepIndex]);
            targets.Add(PickupTarget(player, tackleZones, _match.Weather));
        }

        return targets.Count == 0 ? null : targets.Max();
    }

    private bool MovementStepNeedsGoForIt(Guid playerId, int stepIndex)
    {
        var player = FindPlayer(playerId);
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (player is null || placement is null)
        {
            return false;
        }

        var activation = CurrentTurnActivation(playerId);
        return stepIndex >= RemainingRegularMovement(player, placement, activation);
    }

    private int RemainingRegularMovement(Player player, PlayerPlacement placement, PlayerTurnActivation? activation)
    {
        return Math.Max(0, player.Stats.Movement - MovementSpentBeforePreview(player, placement, activation));
    }

    private int RemainingTotalMovement(Player player, PlayerPlacement placement, PlayerTurnActivation? activation)
    {
        return Math.Max(0, player.Stats.Movement + MaxRushes(player) - MovementSpentBeforePreview(player, placement, activation));
    }

    private int MovementSpentBeforePreview(Player player, PlayerPlacement placement, PlayerTurnActivation? activation)
    {
        return (activation?.MovementSquaresUsed ?? 0) + PendingStandUpCost(player, placement, activation);
    }

    private int PendingStandUpCost(Player player, PlayerPlacement placement, PlayerTurnActivation? activation)
    {
        if (placement.State != PlayerPitchState.Prone ||
            activation is not null ||
            SkillCatalog.PlayerHasEffect(_ruleset, player, SkillEffect.JumpUp))
        {
            return 0;
        }

        return Math.Min(3, player.Stats.Movement);
    }

    private int MaxRushes(Player player)
    {
        return SkillCatalog.PlayerHasEffect(_ruleset, player, SkillEffect.Sprint) ? 4 : 3;
    }

    private bool MovementStepNeedsPickup(int stepIndex)
    {
        return _match.Ball.CarrierPlayerId is null &&
            _match.Ball.Square is PitchSquare ballSquare &&
            stepIndex >= 0 &&
            stepIndex < _previewPath.Count &&
            _previewPath[stepIndex] == ballSquare;
    }

    private PitchSquare? PlayerSquare(Guid playerId)
    {
        return _match.Placements.FirstOrDefault(placement => placement.PlayerId == playerId)?.Square;
    }

    private bool IsGoForItMovementTarget(Guid playerId, PitchSquare square)
    {
        var player = FindPlayer(playerId);
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (player is null || placement?.Square is null)
        {
            return false;
        }

        var activation = CurrentTurnActivation(playerId);
        var path = BuildMovementPath(placement.Square, square);
        return path.Count > RemainingRegularMovement(player, placement, activation);
    }

    private bool MovementTargetRequiresDodge(Guid playerId, PitchSquare destination)
    {
        var player = FindPlayer(playerId);
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (player is null || placement?.Square is not PitchSquare start)
        {
            return false;
        }

        if (destination == start)
        {
            return false;
        }

        var activation = CurrentTurnActivation(playerId);
        var remainingMovement = RemainingTotalMovement(player, placement, activation);
        return !CanReachWithoutDodge(playerId, start, destination, remainingMovement);
    }

    private bool CanReachWithoutDodge(Guid playerId, PitchSquare start, PitchSquare destination, int maxSteps)
    {
        if (maxSteps <= 0)
        {
            return false;
        }

        var queue = new Queue<(PitchSquare Square, int Steps)>();
        var bestSteps = new Dictionary<PitchSquare, int> { [start] = 0 };
        queue.Enqueue((start, 0));

        while (queue.Count > 0)
        {
            var (current, steps) = queue.Dequeue();
            if (current == destination)
            {
                return true;
            }

            if (steps >= maxSteps || IsMarkedByOpponent(_match, _match.ActiveTeamId, playerId, current))
            {
                continue;
            }

            foreach (var next in AdjacentPitchSquares(current))
            {
                if (_match.Placements.Any(placement => placement.PlayerId != playerId && placement.Square == next))
                {
                    continue;
                }

                var nextSteps = steps + 1;
                if (bestSteps.TryGetValue(next, out var seenSteps) && seenSteps <= nextSteps)
                {
                    continue;
                }

                bestSteps[next] = nextSteps;
                queue.Enqueue((next, nextSteps));
            }
        }

        return false;
    }

    private IEnumerable<PitchSquare> AdjacentPitchSquares(PitchSquare square)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0)
                {
                    continue;
                }

                var next = new PitchSquare(square.X + dx, square.Y + dy);
                if (IsOnPitch(next))
                {
                    yield return next;
                }
            }
        }
    }

    private static IReadOnlyList<PitchSquare> BuildMovementPath(PitchSquare start, PitchSquare destination)
    {
        var path = new List<PitchSquare>();
        var currentX = start.X;
        var currentY = start.Y;

        while (currentX != destination.X || currentY != destination.Y)
        {
            currentX += Math.Sign(destination.X - currentX);
            currentY += Math.Sign(destination.Y - currentY);
            path.Add(new PitchSquare(currentX, currentY));
        }

        return path;
    }

    private static bool IsMarkedByOpponent(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            MatchGeometry.HasActiveTackleZone(placement) &&
            MatchGeometry.IsAdjacentToPlacement(placement, square));
    }

    private static bool IsMarkedByOpponent(MatchState match, Guid teamId, Guid playerId, PitchSquare square, Guid ignoredOpponentId)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            placement.PlayerId != ignoredOpponentId &&
            MatchGeometry.HasActiveTackleZone(placement) &&
            MatchGeometry.IsAdjacentToPlacement(placement, square));
    }

    private static int CountOpposingTackleZones(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return MatchQueries.CountOpposingTackleZones(match, teamId, playerId, square);
    }

    private int DodgeTarget(Player player, int opposingTackleZones)
    {
        return RollTargets.DodgeTarget(_ruleset, player, opposingTackleZones);
    }

    private int PickupTarget(Player player, int opposingTackleZones, WeatherCondition weather)
    {
        return RollTargets.PickupTarget(_ruleset, player, opposingTackleZones, weather);
    }

    private int CatchTarget(Player player, WeatherCondition weather, int opposingTackleZones = 0)
    {
        return RollTargets.CatchTarget(_ruleset, player, weather, opposingTackleZones);
    }

    private int InterceptionTarget(Player player, WeatherCondition weather, int opposingTackleZones = 0)
    {
        return RollTargets.InterceptionTarget(_ruleset, player, weather, opposingTackleZones);
    }

    private int PassingTarget(Player player, PassRange passRange, WeatherCondition weather, int opposingTackleZones = 0)
    {
        return RollTargets.PassingTarget(_ruleset, player, passRange, weather, opposingTackleZones);
    }

    private static int GoForItTarget(WeatherCondition weather)
    {
        return RollTargets.GoForItTarget(weather);
    }

    private IReadOnlyList<PlayerPlacement> FindEligibleInterceptors(Guid defendingTeamId, PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        return _match.Placements
            .Where(placement =>
                placement.TeamId == defendingTeamId &&
                placement.State == PlayerPitchState.Standing &&
                placement.Square is PitchSquare square &&
                square != passerSquare &&
                square != receiverSquare &&
                IsOnPassingLane(square, passerSquare, receiverSquare))
            .OrderBy(placement => placement.Square!.X)
            .ThenBy(placement => placement.Square!.Y)
            .ToArray();
    }

    private static bool IsOnPassingLane(PitchSquare square, PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        return MatchGeometry.IsOnPassingLane(square, passerSquare, receiverSquare);
    }

    private void ClearPreview()
    {
        _previewDestination = null;
        _previewPath = [];
        _previewBlockDefenderId = null;
        _previewBlitzDefenderId = null;
        _previewBlitzDestination = null;
        _previewFoulVictimId = null;
        _previewPassReceiverId = null;
        _previewPassTargetSquare = null;
        _previewPassLinePath = [];
        _previewHandOffReceiverId = null;
        _previewLaunchedPlayerId = null;
        _previewLaunchTargetSquare = null;
    }

    private bool IsActiveTeamSide(PitchSquare square)
    {
        return IsTeamSetupSide(_match.ActiveTeamId, square);
    }

    private bool IsTeamSetupSide(Guid teamId, PitchSquare square)
    {
        return teamId == _match.HomeTeamId
            ? square.X < _ruleset.PitchWidth / 2
            : square.X >= _ruleset.PitchWidth / 2;
    }

    private bool IsWideZone(PitchSquare square)
    {
        return MatchGeometry.IsWideZone(_ruleset, square);
    }

    private int CountActiveTeamWideZonePlayers(PitchSquare square)
    {
        return CountTeamWideZonePlayers(_match.ActiveTeamId, square, _selectedPlayerId);
    }

    private int CountTeamWideZonePlayers(Guid teamId, PitchSquare square, Guid? ignoredPlayerId)
    {
        return _match.Placements.Count(placement =>
            placement.PlayerId != ignoredPlayerId &&
            placement.TeamId == teamId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare placedSquare &&
            IsSameWideZone(square, placedSquare));
    }

    private int CountActiveTeamPitchPlayers()
    {
        return _match.Placements.Count(placement =>
            placement.TeamId == _match.ActiveTeamId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is not null);
    }

    private bool IsSameWideZone(PitchSquare first, PitchSquare second)
    {
        return MatchGeometry.IsSameWideZone(_ruleset, first, second);
    }

    private LeagueTeam ActiveTeam()
    {
        return _match.ActiveTeamId == _homeTeam.Id ? _homeTeam : _awayTeam;
    }

    private LeagueTeam KickingTeam()
    {
        return _match.ActiveTeamId == _homeTeam.Id ? _awayTeam : _homeTeam;
    }

    private LeagueTeam OpponentTeam()
    {
        return _match.ActiveTeamId == _homeTeam.Id ? _awayTeam : _homeTeam;
    }

    private LeagueTeam TeamById(Guid teamId)
    {
        if (teamId == _homeTeam.Id)
        {
            return _homeTeam;
        }

        if (teamId == _awayTeam.Id)
        {
            return _awayTeam;
        }

        throw new InvalidOperationException("Unknown match team.");
    }

    private MatchService CreateMatchService()
    {
        var service = new MatchService();
        service.RegisterTeams(_homeTeam, _awayTeam);
        return service;
    }

    private Player? FindPlayer(Guid playerId)
    {
        return _homeTeam.Players.Concat(_awayTeam.Players).FirstOrDefault(player => player.Id == playerId);
    }

    private bool PlayerHasLaunchActionEffect(Player player, string skillId)
    {
        var requiredEffect = skillId switch
        {
            "throw-team-mate" => SkillEffect.ThrowTeamMate,
            "kick-team-mate" => SkillEffect.KickTeamMate,
            _ => throw new InvalidOperationException($"Unknown launch skill '{skillId}'.")
        };

        return SkillHookResolver.PlayerHasHookedEffect(
            _ruleset,
            player,
            LaunchEventKind(skillId),
            GameEventStage.BeforeEvent,
            requiredEffect);
    }

    private bool PlayerHasLaunchEligibilityEffect(Player player, string skillId)
    {
        return SkillHookResolver.PlayerHasHookedEffect(
            _ruleset,
            player,
            LaunchEventKind(skillId),
            GameEventStage.BeforeEvent,
            SkillEffect.RightStuff);
    }

    private string LaunchSkillIdForMode()
    {
        return _kickTeamMateMode ? "kick-team-mate" : "throw-team-mate";
    }

    private static GameEventKind LaunchEventKind(string skillId)
    {
        return skillId switch
        {
            "throw-team-mate" => GameEventKind.ThrowTeamMate,
            "kick-team-mate" => GameEventKind.KickTeamMate,
            _ => throw new InvalidOperationException($"Unknown launch skill '{skillId}'.")
        };
    }


    private static int ResolveBlockDice(int attackerStrength, int defenderStrength)
    {
        var high = Math.Max(attackerStrength, defenderStrength);
        var low = Math.Max(1, Math.Min(attackerStrength, defenderStrength));
        return high >= low * 2 ? 3 : high > low ? 2 : 1;
    }

    private static bool IsAdjacent(PitchSquare first, PitchSquare second)
    {
        return Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y)) == 1;
    }

    private bool IsOnPitch(PitchSquare square)
    {
        return MatchGeometry.IsOnPitch(_ruleset, square);
    }

    private static IEnumerable<PitchSquare> AdjacentSquares(PitchSquare square)
    {
        return MatchGeometry.AdjacentSquares(square);
    }

    private static string FormatRerollKind(PendingRerollKind kind)
    {
        return MatchFormatting.FormatRerollKind(kind);
    }

    private static string FormatKickoffEventKind(KickoffEventKind kind)
    {
        return MatchFormatting.FormatKickoffEventKind(kind);
    }

    private static PassRange? ResolvePassRange(PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        return MatchGeometry.ResolvePassRange(passerSquare, receiverSquare);
    }
}
