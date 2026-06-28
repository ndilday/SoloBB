using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public partial class MatchScreen : VBoxContainer
{
    private async Task HandlePitchSquareAsync(PitchSquare square, MouseButton button)
    {
        ResetEndTurnConfirmation();
        try
        {
            // Interrupt windows consume any board click as their action target, so they take priority
            // over the normal right-click targeting flow below.
            if (_match.PendingDumpOff is not null)
            {
                await ResolveDumpOffAsync(square);
                return;
            }

            if (_match.PendingOnTheBall is not null)
            {
                await ResolveOnTheBallSquareAsync(square);
                return;
            }

            // Right-click is reserved for aiming and confirming Pass / Hand-off targets so that
            // an ordinary left-click while a thrower is selected can never be mistaken for a throw.
            if (button == MouseButton.Right)
            {
                await HandleTargetingClickAsync(square);
                return;
            }

            if (_match.PendingPush is not null)
            {
                await ChoosePushSquareAsync(square);
                return;
            }

            if (_match.PendingFollowUp is not null)
            {
                await ResolvePendingFollowUpAsync(square == _match.PendingFollowUp.FollowUpSquare);
                return;
            }

            if (_match.PendingDivingTackle is not null)
            {
                return;
            }

            if (_match.PendingBombThrow is not null)
            {
                await ThrowPendingBombAsync(square);
                return;
            }

            if (_match.PendingBallPlacement is not null)
            {
                await ChooseBallPlacementAsync(square);
                return;
            }

            if (_match.PendingKickoffEvent is not null)
            {
                await HandlePendingKickoffEventSquareAsync(square);
                return;
            }

            if (_match.Phase is MatchPhase.Kickoff)
            {
                await ResolveKickoffTargetAsync(square);
                return;
            }

            if (_wizardMode)
            {
                await UseWizardAtAsync(square);
                return;
            }

            var occupied = _match.Placements.FirstOrDefault(placement => placement.Square == square);
            if ((_throwTeamMateMode || _kickTeamMateMode) && _selectedPlayerId is Guid launchActorId)
            {
                await HandleLaunchTargetAsync(launchActorId, square, occupied?.PlayerId);
                return;
            }

            if (occupied is not null)
            {
                if (_selectedPlayerId == occupied.PlayerId &&
                    IsPlayerTurnPhase() &&
                    IsLegalMovementTarget(occupied.PlayerId, square))
                {
                    await HandleMovementSquareAsync(square, occupied.PlayerId);
                    return;
                }

                if (_selectedPlayerId is Guid attackerId && IsLegalBlockTarget(attackerId, occupied.PlayerId))
                {
                    await HandleBlockTargetAsync(attackerId, occupied.PlayerId);
                    return;
                }

                if (_selectedPlayerId is Guid blitzerId && IsLegalBlitzTarget(blitzerId, occupied.PlayerId))
                {
                    await HandleBlitzTargetAsync(blitzerId, occupied.PlayerId);
                    return;
                }

                if (_selectedPlayerId is Guid foulerId && IsLegalFoulTarget(foulerId, occupied.PlayerId))
                {
                    await HandleFoulTargetAsync(foulerId, occupied.PlayerId);
                    return;
                }

                SelectPlayer(occupied.PlayerId);
                return;
            }

            if (_selectedPlayerId is not Guid playerId)
            {
                _summaryLabel.Text = "Select a player from the roster first.";
                return;
            }

            if (_match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn)
            {
                await HandleMovementSquareAsync(square, playerId);
                return;
            }

            if (!IsLegalPlacementTarget(square))
            {
                _summaryLabel.Text = "That square is not a legal setup location.";
                return;
            }

            var selectedPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == playerId)
                ?? throw new InvalidOperationException("Selected player is not part of this match.");

            if (selectedPlacement.TeamId != _match.ActiveTeamId)
            {
                _summaryLabel.Text = "Only the active setup team can place players.";
                return;
            }

            var service = CreateMatchService();
            _match = service.PlacePlayer(_match, _ruleset, playerId, square);
            await _saveMatch(_match);
            ClearPreview();
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            var action = _match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn
                ? "Movement"
                : "Placement";
            _summaryLabel.Text = $"{action} failed: {ex.Message}";
        }
    }

    private async Task HandleMovementSquareAsync(PitchSquare square, Guid playerId)
    {
        if (!IsLegalMovementTarget(playerId, square))
        {
            _summaryLabel.Text = "That square is not a legal movement destination.";
            ClearPreview();
            RefreshPitch();
            return;
        }

        if (_previewDestination == square)
        {
            await ConfirmMoveAsync(playerId, square);
            return;
        }

        // Starting a movement preview supersedes any in-progress Pass / Hand-off aim.
        _previewPassReceiverId = null;
        _previewPassTargetSquare = null;
        _previewHandOffReceiverId = null;
        _previewDestination = square;
        _previewPath = BuildMovementPath(PlayerSquare(playerId)!, square);
        RefreshPitch();
    }

    /// <summary>
    /// Handles a right-click on a pitch square as an "aim at this target" gesture. With lazy declaration the
    /// gesture itself commits the action implied by the target: an opponent resolves as a Blitz (after moving)
    /// or in-place Block, an adjacent standing team-mate as a Hand-off, and any other in-range square as a
    /// Pass. The first click aims (sets a preview) and a second right-click on the same target confirms it.
    /// Anything that is not a legal target is ignored except for a gentle nudge when the carrier could throw.
    /// </summary>
    private async Task HandleTargetingClickAsync(PitchSquare square)
    {
        if (_selectedPlayerId is not Guid actorId)
        {
            return;
        }

        var occupied = _match.Placements.FirstOrDefault(placement => placement.Square == square);
        if (occupied is not null)
        {
            // Right-clicking an opponent targets aggression, not a throw. Resolve it as a blitz (after moving)
            // or an in-place block first, so a carrier's pass range can never swallow a blitz on an adjacent
            // enemy whose square happens to be a legal pass target.
            if (occupied.TeamId != _match.ActiveTeamId)
            {
                if (IsLegalBlitzTarget(actorId, occupied.PlayerId))
                {
                    await HandleBlitzTargetAsync(actorId, occupied.PlayerId);
                    return;
                }

                if (IsLegalBlockTarget(actorId, occupied.PlayerId))
                {
                    await HandleBlockTargetAsync(actorId, occupied.PlayerId);
                    return;
                }
            }

            if (IsHandingOff(actorId) && IsLegalHandOffTarget(actorId, occupied.PlayerId))
            {
                await HandleHandOffTargetAsync(actorId, occupied.PlayerId);
                return;
            }

            if (IsLegalPassTarget(actorId, occupied.PlayerId))
            {
                await HandlePassTargetAsync(actorId, square, occupied.PlayerId);
                return;
            }
        }

        if ((occupied is null || occupied.TeamId == _match.ActiveTeamId) && IsLegalPassTargetSquare(actorId, square))
        {
            await HandlePassTargetAsync(actorId, square, occupied?.TeamId == _match.ActiveTeamId ? occupied.PlayerId : null);
            return;
        }

        if (_match.Ball.CarrierPlayerId == actorId && CanEnterPassMode(actorId))
        {
            _summaryLabel.Text = "Right-click a legal pass target (team-mate or square) to aim, then right-click again to confirm.";
        }
        else if (IsHandingOff(actorId))
        {
            _summaryLabel.Text = "Right-click an adjacent standing team-mate to aim the hand-off, then right-click again to confirm.";
        }
    }

    private async Task HandlePendingKickoffEventSquareAsync(PitchSquare square)
    {
        if (_selectedPlayerId is not Guid playerId)
        {
            _summaryLabel.Text = "Select an eligible kickoff event player first.";
            return;
        }

        var occupied = _match.Placements.FirstOrDefault(placement => placement.Square == square);
        if (occupied is not null && IsLegalKickoffBlitzTarget(playerId, occupied.PlayerId))
        {
            var beforeBlock = _match;
            var blockLogStart = _match.Log.Count;
            var blockService = CreateMatchService();
            _match = blockService.BlockDuringPendingKickoffBlitz(_match, _ruleset, TeamById(_match.PendingKickoffEvent!.TeamId), playerId, TeamById(_match.PendingKickoffEvent.ReceivingTeamId), occupied.PlayerId);
            _selectedPlayerId = null;
            ClearPreview();
            await AnimateBallAsync(beforeBlock, _match, blockLogStart);
            await _saveMatch(_match);
            RefreshRoster();
            RefreshPitch();
            return;
        }

        if (!IsLegalMovementTarget(playerId, square))
        {
            _summaryLabel.Text = "That square is not legal for this kickoff event.";
            ClearPreview();
            RefreshPitch();
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.MovePendingKickoffEventPlayer(_match, _ruleset, playerId, square);
        _selectedPlayerId = null;
        ClearPreview();
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmMoveAsync(Guid playerId, PitchSquare destination)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeMove = _match.ActiveTeamId;
        var path = _previewPath.ToArray();
        var movingTeam = ActiveTeam();
        var service = CreateMatchService();
        _match = CurrentTurnActivation(playerId)?.Action switch
        {
            PlayerTurnAction.Blitz => service.MovePlayerAsBlitz(_match, _ruleset, movingTeam, playerId, destination, OpponentTeam()),
            PlayerTurnAction.Pass => service.MovePlayerAsPass(_match, _ruleset, movingTeam, playerId, destination, OpponentTeam()),
            PlayerTurnAction.HandOff => service.MovePlayerAsHandOff(_match, _ruleset, movingTeam, playerId, destination, OpponentTeam()),
            _ => service.MovePlayer(_match, _ruleset, movingTeam, playerId, destination, OpponentTeam())
        };
        if (_match.ActiveTeamId == activeTeamBeforeMove && IsPlayerTurnPhase())
        {
            _selectedPlayerId = playerId;
            _currentActivationPlayerId = playerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        ClearPreview();
        await AnimateMovementAsync(beforeMatch, _match, playerId, path);
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task HandleBlockTargetAsync(Guid attackerId, Guid defenderId)
    {
        if (_previewBlockDefenderId == defenderId)
        {
            await ConfirmBlockAsync(attackerId, defenderId);
            return;
        }

        _previewBlockDefenderId = defenderId;
        _previewFoulVictimId = null;
        _previewDestination = null;
        _previewPath = [];
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmBlockAsync(Guid attackerId, Guid defenderId)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeBlock = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.BlockPlayer(_match, _ruleset, ActiveTeam(), attackerId, OpponentTeam(), defenderId);
        _previewBlockDefenderId = null;
        _previewFoulVictimId = null;

        if (_match.ActiveTeamId == activeTeamBeforeBlock && IsPlayerTurnPhase())
        {
            _selectedPlayerId = attackerId;
            _currentActivationPlayerId = attackerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task HandleBlitzTargetAsync(Guid attackerId, Guid defenderId)
    {
        var destination = FindBlitzDestination(attackerId, defenderId);
        if (destination is null)
        {
            _summaryLabel.Text = "No legal blitz path to that target.";
            return;
        }

        if (_previewBlitzDefenderId == defenderId && _previewBlitzDestination == destination)
        {
            await ConfirmBlitzAsync(attackerId, defenderId, destination);
            return;
        }

        _previewBlitzDefenderId = defenderId;
        _previewBlitzDestination = destination;
        _previewBlockDefenderId = null;
        _previewFoulVictimId = null;
        _previewPassReceiverId = null;
        _previewDestination = destination;
        _previewPath = BuildMovementPath(PlayerSquare(attackerId)!, destination);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task HandleFoulTargetAsync(Guid foulerId, Guid victimId)
    {
        if (_previewFoulVictimId == victimId)
        {
            await ConfirmFoulAsync(foulerId, victimId);
            return;
        }

        _previewFoulVictimId = victimId;
        _previewBlockDefenderId = null;
        _previewBlitzDefenderId = null;
        _previewBlitzDestination = null;
        _previewPassReceiverId = null;
        _previewDestination = null;
        _previewPath = [];
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmFoulAsync(Guid foulerId, Guid victimId)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeFoul = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.FoulPlayer(_match, _ruleset, ActiveTeam(), foulerId, OpponentTeam(), victimId);
        _previewFoulVictimId = null;

        if (_match.ActiveTeamId == activeTeamBeforeFoul &&
            IsPlayerTurnPhase() &&
            IsActivationOngoing(foulerId))
        {
            _selectedPlayerId = foulerId;
            _currentActivationPlayerId = foulerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmBlitzAsync(Guid attackerId, Guid defenderId, PitchSquare destination)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeBlitz = _match.ActiveTeamId;
        var path = _previewPath.ToArray();
        var service = CreateMatchService();
        _match = service.BlitzPlayer(_match, _ruleset, ActiveTeam(), attackerId, destination, OpponentTeam(), defenderId);

        if (_match.ActiveTeamId == activeTeamBeforeBlitz && IsPlayerTurnPhase())
        {
            _selectedPlayerId = attackerId;
            _currentActivationPlayerId = attackerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        ClearPreview();
        await AnimateMovementAsync(beforeMatch, _match, attackerId, path);
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ChooseBlockDieAsync(int roll)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingBlock is not PendingBlockChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var attackerTeam = TeamById(pending.AttackerTeamId);
        var defenderTeam = TeamById(pending.DefenderTeamId);
        var service = CreateMatchService();
        _match = service.ChooseBlockDie(_match, _ruleset, attackerTeam, defenderTeam, roll);
        _previewBlockDefenderId = null;

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
            _currentActivationPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task RerollPendingBlockAsync()
    {
        ResetEndTurnConfirmation();
        if (_match.PendingBlock is not PendingBlockChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var attackerTeam = TeamById(pending.AttackerTeamId);
        var defenderTeam = TeamById(pending.DefenderTeamId);
        try
        {
            var service = CreateMatchService();
            _match = service.RerollPendingBlock(_match, _ruleset, attackerTeam, defenderTeam);
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Block reroll failed: {ex.Message}";
            return;
        }

        _previewBlockDefenderId = null;
        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
            _currentActivationPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ChoosePushSquareAsync(PitchSquare square)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingPush is not PendingPushChoice pending)
        {
            return;
        }

        if (!pending.LegalSquares.Contains(square))
        {
            _summaryLabel.Text = "Choose one of the highlighted push squares.";
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.ChoosePushSquare(_match, _ruleset, TeamById(pending.AttackerTeamId), TeamById(pending.DefenderTeamId), square);

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
            _currentActivationPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolvePendingFollowUpAsync(bool useFollowUp)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingFollowUp is not PendingFollowUpChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.ResolvePendingFollowUp(_match, _ruleset, TeamById(pending.AttackerTeamId), TeamById(pending.DefenderTeamId), useFollowUp);

        ClearPreview();
        // A Block follow-up ends the activation (done for the turn); a Blitz follow-up leaves the
        // blitzer active so they may keep moving with any remaining allowance.
        var followUpOngoing = _match.ActiveTeamId == beforeMatch.ActiveTeamId &&
            IsPlayerTurnPhase() &&
            IsActivationOngoing(pending.AttackerPlayerId);
        _selectedPlayerId = followUpOngoing ? pending.AttackerPlayerId : null;
        _currentActivationPlayerId = followUpOngoing ? pending.AttackerPlayerId : null;

        RefreshPitch();
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task HandlePassTargetAsync(Guid passerId, PitchSquare targetSquare, Guid? receiverId)
    {
        if (_previewPassTargetSquare == targetSquare)
        {
            await ConfirmPassAsync(passerId, targetSquare);
            return;
        }

        _previewPassReceiverId = receiverId;
        _previewPassTargetSquare = targetSquare;
        _previewHandOffReceiverId = null;
        _previewBlockDefenderId = null;
        _previewFoulVictimId = null;
        _previewDestination = null;
        _previewPath = [];
        var passerPl = _match.Placements.FirstOrDefault(p => p.PlayerId == passerId);
        _previewPassLinePath = passerPl?.Square is PitchSquare passerSq
            ? BuildMovementPath(passerSq, targetSquare)
            : [];
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmPassAsync(Guid passerId, PitchSquare targetSquare)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.PassBall(_match, _ruleset, ActiveTeam(), passerId, targetSquare, OpponentTeam());
        _previewPassReceiverId = null;
        _previewPassTargetSquare = null;
        _previewPassLinePath = [];

        // The pass completes the player's action. Deselect so the spent passer cannot be
        // re-selected to move again under the already-resolved Pass action.
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task HandleHandOffTargetAsync(Guid carrierId, Guid receiverId)
    {
        if (_previewHandOffReceiverId == receiverId)
        {
            await ConfirmHandOffAsync(carrierId, receiverId);
            return;
        }

        _previewHandOffReceiverId = receiverId;
        _previewPassReceiverId = null;
        _previewPassTargetSquare = null;
        _previewBlockDefenderId = null;
        _previewFoulVictimId = null;
        _previewDestination = null;
        _previewPath = [];
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmHandOffAsync(Guid carrierId, Guid receiverId)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeHandOff = _match.ActiveTeamId;
        _previewHandOffReceiverId = null;
        try
        {
            var service = CreateMatchService();
            _match = service.HandOffBall(_match, _ruleset, ActiveTeam(), carrierId, receiverId, OpponentTeam());
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Hand-off failed: {ex.Message}";
            return;
        }

        // Auto-select the receiver for a hand-off-and-run, but only when they are still eligible to act.
        // A receiver who had already been activated this turn has their activation closed by the catch,
        // so they must not be re-selected to move again.
        if (_match.ActiveTeamId == activeTeamBeforeHandOff && IsPlayerTurnPhase() && _match.Ball.CarrierPlayerId == receiverId && CanSelectPlayer(receiverId))
        {
            _selectedPlayerId = receiverId;
            _currentActivationPlayerId = null;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task HandleLaunchTargetAsync(Guid actorId, PitchSquare square, Guid? occupiedPlayerId)
    {
        if (_previewLaunchedPlayerId is null)
        {
            if (occupiedPlayerId is not Guid launchedPlayerId || !IsLegalLaunchPlayer(actorId, launchedPlayerId))
            {
                _summaryLabel.Text = "Choose an adjacent standing Right Stuff team-mate to launch.";
                return;
            }

            _previewLaunchedPlayerId = launchedPlayerId;
            _previewLaunchTargetSquare = null;
            RefreshPitch();
            return;
        }

        var launchedId = _previewLaunchedPlayerId.Value;
        if (!IsLegalLaunchTargetSquare(actorId, launchedId, square))
        {
            _summaryLabel.Text = "Choose a legal launch target square.";
            return;
        }

        if (_previewLaunchTargetSquare == square)
        {
            await ConfirmLaunchAsync(actorId, launchedId, square);
            return;
        }

        _previewLaunchTargetSquare = square;
        RefreshPitch();
    }

    private async Task ConfirmLaunchAsync(Guid actorId, Guid launchedId, PitchSquare targetSquare)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeLaunch = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = _throwTeamMateMode
            ? service.ThrowTeamMate(_match, _ruleset, ActiveTeam(), actorId, launchedId, targetSquare, OpponentTeam())
            : service.KickTeamMate(_match, _ruleset, ActiveTeam(), actorId, launchedId, targetSquare, OpponentTeam());

        _throwTeamMateMode = false;
        _kickTeamMateMode = false;
        _previewLaunchedPlayerId = null;
        _previewLaunchTargetSquare = null;

        if (_match.ActiveTeamId == activeTeamBeforeLaunch && IsPlayerTurnPhase())
        {
            _selectedPlayerId = launchedId;
            _currentActivationPlayerId = null;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ChooseInterceptorAsync(Guid interceptorId)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingInterception is not PendingInterceptionChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.ChooseInterceptor(_match, _ruleset, TeamById(pending.PassingTeamId), TeamById(pending.DefendingTeamId), interceptorId);
        _previewPassReceiverId = null;

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = IsActivationOngoing(pending.PasserPlayerId) ? pending.PasserPlayerId : null;
            _currentActivationPlayerId = IsActivationOngoing(pending.PasserPlayerId) ? pending.PasserPlayerId : null;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ChooseBallPlacementAsync(PitchSquare square)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingBallPlacement is not PendingBallPlacementChoice pending)
        {
            return;
        }

        if (!pending.LegalSquares.Contains(square))
        {
            _summaryLabel.Text = "Choose one of the highlighted ball placement squares.";
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.ChooseBallPlacement(_match, TeamById(pending.TeamId), square);
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ThrowPendingBombAsync(PitchSquare square)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingBombThrow is not PendingBombThrowChoice pending)
        {
            return;
        }

        if (!IsLegalBombThrowSquare(square))
        {
            _summaryLabel.Text = "Choose a legal bomb throw target.";
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.ThrowPendingBomb(_match, _ruleset, TeamById(pending.ThrowingTeamId), TeamById(pending.OpposingTeamId), square);
        _selectedPlayerId = _match.PendingBombThrow?.ThrowerPlayerId;
        _currentActivationPlayerId = null;
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveRerollAsync(bool useTeamReroll, string? skillId = null)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingReroll is not PendingRerollChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = CreateMatchService();
        var rerollTeam = TeamById(pending.TeamId);
        var opposingTeam = pending.TeamId == _homeTeam.Id ? _awayTeam : _homeTeam;
        _match = service.ResolvePendingReroll(_match, _ruleset, rerollTeam, useTeamReroll, skillId, opposingTeam);

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = pending.PlayerId;
            _currentActivationPlayerId = pending.PlayerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveBlockRerollAsync(bool useTeamReroll)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingBlockReroll is not PendingBlockRerollChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.ResolvePendingBlockReroll(_match, _ruleset, TeamById(pending.AttackerTeamId), TeamById(pending.DefenderTeamId), useTeamReroll);

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
            _currentActivationPlayerId = IsActivationOngoing(pending.AttackerPlayerId) ? pending.AttackerPlayerId : null;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveApothecaryAsync(bool useApothecary)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingApothecary is not PendingApothecaryChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.ResolvePendingApothecary(_match, TeamById(pending.TeamId), useApothecary);
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveSendOffAsync(bool useBribe)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingSendOff is not PendingSendOffChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.ResolvePendingSendOff(_match, _ruleset, TeamById(pending.TeamId), useBribe);
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveStandFirmAsync(bool useStandFirm)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingStandFirm is not PendingStandFirmChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var service = CreateMatchService();
        _match = service.ResolvePendingStandFirm(_match, _ruleset, TeamById(pending.AttackerTeamId), TeamById(pending.DefenderTeamId), useStandFirm);
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveDivingTackleAsync(bool useDivingTackle)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingDivingTackle is not PendingDivingTackleChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.ResolvePendingDivingTackle(_match, _ruleset, TeamById(pending.DodgingTeamId), TeamById(pending.TacklerTeamId), useDivingTackle);

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = pending.DodgerPlayerId;
            _currentActivationPlayerId = pending.DodgerPlayerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveDumpOffAsync(PitchSquare? targetSquare)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingDumpOff is not PendingDumpOffChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        try
        {
            var service = CreateMatchService();
            _match = service.ResolvePendingDumpOff(_match, _ruleset, TeamById(pending.CarrierTeamId), TeamById(pending.BlockingTeamId), targetSquare);
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Dump-Off failed: {ex.Message}";
            return;
        }

        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveOnTheBallSquareAsync(PitchSquare square)
    {
        if (_match.PendingOnTheBall is null)
        {
            return;
        }

        if (_onTheBallMoverId is not Guid moverId)
        {
            _summaryLabel.Text = "Choose an On the Ball player to move first.";
            return;
        }

        await ResolveOnTheBallAsync(moverId, square);
    }

    private async Task ResolveOnTheBallAsync(Guid? playerId, PitchSquare? destination)
    {
        ResetEndTurnConfirmation();
        if (_match.PendingOnTheBall is not PendingOnTheBallChoice pending)
        {
            return;
        }

        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        try
        {
            var service = CreateMatchService();
            _match = service.ResolvePendingOnTheBall(_match, _ruleset, TeamById(pending.TeamId), TeamById(pending.OpposingTeamId), playerId, destination);
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"On the Ball failed: {ex.Message}";
            return;
        }

        _onTheBallMoverId = null;
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveKickoffTargetAsync(PitchSquare square)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var receivingTeam = ActiveTeam();
        var service = CreateMatchService();
        _match = service.ResolveKickoff(_match, _ruleset, receivingTeam, square, KickingTeam());
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        ClearPreview();
        await AnimateBallAsync(beforeMatch, _match, logStart);
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task CompleteCurrentStepAsync()
    {
        try
        {
            if (_match.Phase is MatchPhase.Complete)
            {
                // Results are saved as soon as the match completes; this just returns to the league.
                _back();
                return;
            }

            if (!CanAdvanceCurrentStep())
            {
                _summaryLabel.Text = AdvanceBlockedMessage();
                return;
            }

            if (RequiresEndTurnConfirmation())
            {
                if (!_endTurnConfirmationArmed)
                {
                    _endTurnConfirmationArmed = true;
                    RefreshPitch();
                    return;
                }

                _endTurnConfirmationArmed = false;
            }

            var service = CreateMatchService();
            var beforeMatch = _match;
            var logStart = _match.Log.Count;
            if (_match.PendingFollowUp is not null)
            {
                await ResolvePendingFollowUpAsync(useFollowUp: false);
                return;
            }

            if (_match.PendingKickoffEvent is PendingKickoffEventChoice pendingKickoff)
            {
                _match = service.CompletePendingKickoffEvent(_match, _ruleset, TeamById(pendingKickoff.ReceivingTeamId));
            }
            else
            {
                _match = _match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn
                ? service.AdvanceTurn(_match, _ruleset)
                : service.AdvancePhase(_match, _ruleset);
            }
            ResetEndTurnConfirmation();
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
            ClearPreview();
            await AnimateBallAsync(beforeMatch, _match, logStart);
            await _saveMatch(_match);
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Turn control failed: {ex.Message}";
        }
    }

    private async Task UseWeatherMageAsync()
    {
        await UseTurnStartInducementAsync((service, team) => service.UseWeatherMage(_match, team), "Weather Mage");
    }

    private async Task UseSpecialPlayAsync()
    {
        await UseTurnStartInducementAsync((service, team) => service.UseSpecialPlay(_match, team), "Special Play");
    }

    private async Task UseWizardAtAsync(PitchSquare square)
    {
        try
        {
            var service = CreateMatchService();
            _match = service.UseWizard(_match, _ruleset, ActiveTeam(), square);
            DisableWizardMode();
            ResetEndTurnConfirmation();
            await _saveMatch(_match);
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Wizard failed: {ex.Message}";
        }
    }

    private async Task UseTurnStartInducementAsync(Func<MatchService, LeagueTeam, MatchState> use, string name)
    {
        try
        {
            var service = CreateMatchService();
            _match = use(service, ActiveTeam());
            ResetEndTurnConfirmation();
            await _saveMatch(_match);
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"{name} failed: {ex.Message}";
        }
    }

    private async Task SelectOrTargetPlayerAsync(Guid playerId)
    {
        if (_match.PendingBallPlacement is not null &&
            _match.Placements.FirstOrDefault(p => p.PlayerId == playerId)?.Square is PitchSquare ballPlacementSquare &&
            IsLegalBallPlacementSquare(ballPlacementSquare))
        {
            await ChooseBallPlacementAsync(ballPlacementSquare);
            return;
        }

        // Roster clicks only select. Pass / Hand-off targeting is driven by right-clicking the target on
        // the pitch, so a roster click never inadvertently throws the ball.
        SelectPlayer(playerId);
    }

    private void SelectPlayer(Guid playerId)
    {
        ResetEndTurnConfirmation();
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (placement is null)
        {
            return;
        }

        if (!CanSelectPlayer(playerId))
        {
            _summaryLabel.Text = CannotSelectReason(playerId);
            return;
        }

        _selectedPlayerId = playerId;
        if (IsPlayerTurnPhase())
        {
            // Selecting a player who already has an activation resumes it and makes them current.
            // Selecting a fresh player while a different player's activation is still in progress is
            // only a *tentative* pick: keep the in-progress player current (and re-selectable) so a
            // stray click does not finalize them. They are promoted to current only once this newly
            // picked player actually commits to an action (the Confirm*/Declare* handlers do that).
            var keepInProgressCurrent =
                CurrentTurnActivation(playerId) is null &&
                _currentActivationPlayerId is Guid inProgress &&
                inProgress != playerId &&
                IsActivationOngoing(inProgress);
            if (!keepInProgressCurrent)
            {
                _currentActivationPlayerId = playerId;
            }
        }

        _throwTeamMateMode = false;
        _kickTeamMateMode = false;

        ClearPreview();
        RefreshRoster();
        RefreshSelectionDisplay();
        RefreshPitch();
    }

    private void RefreshSelectionDisplay()
    {
        var selectedPlayer = _selectedPlayerId is Guid selectedId ? FindPlayer(selectedId) : null;
        _selectedLabel.Text = selectedPlayer is null
            ? "No player selected."
            : $"Selected: {selectedPlayer.Name}";

        foreach (var (playerId, button) in _rosterButtons)
        {
            var isSelected = playerId == _selectedPlayerId;
            var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
            var baseColor = RosterButtonColor(playerId, placement);
            var borderColor = isSelected ? SelectedColor : new Color("3c4b40");
            button.AddThemeStyleboxOverride("normal", FlatStyle(baseColor, borderColor));
            button.AddThemeStyleboxOverride("disabled", FlatStyle(baseColor.Darkened(0.08f), new Color("303832")));
            button.AddThemeStyleboxOverride("hover", FlatStyle(baseColor.Lightened(0.12f), isSelected ? SelectedColor : new Color("536856")));
        }
    }

    private async Task ReturnSelectedSetupPlayerToReserveAsync()
    {
        if (_selectedPlayerId is not Guid playerId)
        {
            return;
        }

        try
        {
            var service = CreateMatchService();
            _match = service.ReturnSetupPlayerToReserve(_match, playerId);
            await _saveMatch(_match);
            ClearPreview();
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = ex.Message;
        }
    }
}
