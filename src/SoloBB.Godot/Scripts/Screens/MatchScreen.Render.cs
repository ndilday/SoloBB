using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;
using static SoloBB.Core.Services.MatchFormatting;
using static SoloBB.Core.Services.MatchQueries;

namespace SoloBB.Godot.Scripts.Screens;

public partial class MatchScreen : VBoxContainer
{
    private void RefreshRoster()
    {
        foreach (var child in _rosterList.GetChildren())
        {
            child.QueueFree();
        }

        _rosterButtons.Clear();
        var activeTeam = ActiveTeam();
        if (_match.PendingKickoffEvent is PendingKickoffEventChoice pendingKickoff)
        {
            activeTeam = TeamById(pendingKickoff.TeamId);
        }

        foreach (var player in activeTeam.Players.OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase))
        {
            var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == player.Id);
            var state = RosterStatusLabel(placement);
            var marker = PlayerMarker(player.Id);
            var activationState = ActivationDisplayState(player.Id, placement);
            var button = new Button
            {
                Text = $"{marker}  {player.Name}  {state}  {activationState}",
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Disabled = !CanSelectPlayer(player.Id),
                Icon = PlayerSprite(activeTeam, player, placement),
                ExpandIcon = false
            };
            button.AddThemeFontSizeOverride("font_size", 11);
            button.TooltipText = RosterTooltip(player, placement);
            var playerId = player.Id;
            button.Pressed += async () => await SelectOrTargetPlayerAsync(playerId);
            _rosterButtons[player.Id] = button;
            _rosterList.AddChild(button);
        }

        RefreshSelectionDisplay();
    }

    private void RefreshPitch()
    {
        RefreshMatchHud();
        RefreshBlockDiceChoice();
        RefreshInterceptionChoice();
        RefreshRerollChoice();
        RefreshApothecaryChoice();
        RefreshSendOffChoice();
        RefreshStandFirmChoice();
        RefreshDivingTackleChoice();
        RefreshSetupChoice();
        foreach (var (square, tile) in _pitchTiles)
        {
            var canPlace = IsLegalPlacementTarget(square);
            var canTargetKickoff = IsLegalKickoffTarget(square);
            var canMove = _selectedPlayerId is Guid movingPlayerId && IsLegalMovementTarget(movingPlayerId, square);
            var canPassSquare = _selectedPlayerId is Guid passingPlayerId && IsLegalPassTargetSquare(passingPlayerId, square);
            var canPush = IsLegalPushSquare(square);
            var canFollowUp = IsLegalFollowUpSquare(square);
            var canPlaceBall = IsLegalBallPlacementSquare(square);
            var canThrowBomb = IsLegalBombThrowSquare(square);
            var canLaunchTarget = _selectedPlayerId is Guid launchActorId &&
                _previewLaunchedPlayerId is Guid launchedId &&
                IsLegalLaunchTargetSquare(launchActorId, launchedId, square);
            var isPreview = _previewPath.Contains(square);
            var pathMarker = canLaunchTarget ? "L" : canThrowBomb ? "B" : canPlaceBall ? "o" : canFollowUp ? "F" : canPush ? ">" : _previewPassTargetSquare == square ? "P" : MovementPathMarker(square);
            tile.Text = "";
            tile.Icon = null;
            tile.SetTile(PitchTileTexture(square, canPlace || canTargetKickoff || canMove || canPassSquare || canPush || canFollowUp || canPlaceBall || canThrowBomb || canLaunchTarget, pathMarker));
            var isGfi = pathMarker?.StartsWith('!') == true ||
                (canMove && _selectedPlayerId is Guid gfiPlayerId && IsGoForItMovementTarget(gfiPlayerId, square));
            tile.SetHighlight(PitchHighlightTexture(canPlace || canTargetKickoff || canMove || canPassSquare || canPush || canFollowUp || canPlaceBall || canThrowBomb || canLaunchTarget, pathMarker), isGfi ? GoForItPathColor : null);
            tile.SetMarking(PitchMarkingTexture(square));
            tile.SetPiece(null);
            tile.SetOverlay(null);
            tile.Disabled = !canPlace && !canTargetKickoff && !canMove && !canPassSquare && !canPush && !canFollowUp && !canPlaceBall && !canThrowBomb && !canLaunchTarget;
            tile.TooltipText = canLaunchTarget
                ? LaunchSquareTooltip(square)
                : canThrowBomb
                ? "Throw bomb here"
                : canPlaceBall
                ? "Place ball here"
                : canFollowUp
                ? "Follow up here"
                : canPush
                ? "Push here"
                : canPassSquare
                ? PassSquareTooltip(square)
                : canPlace || canTargetKickoff || canMove
                ? MovementTooltip(square, pathMarker)
                : "";
            ApplySquareStyle(tile, square, isSelected: false, canUse: canPlace || canTargetKickoff || canMove || canPassSquare || canPush || canFollowUp || canPlaceBall || canThrowBomb || canLaunchTarget, pathMarker);
            if (isPreview || canPush || canFollowUp || canThrowBomb || canLaunchTarget || _previewPassTargetSquare == square)
            {
                tile.Text = pathMarker ?? (_previewDestination == square ? "X" : ".");
            }
        }

        foreach (var placement in _match.Placements.Where(placement => placement.Square is not null))
        {
            if (!_pitchTiles.TryGetValue(placement.Square!, out var tile))
            {
                continue;
            }

            var isSelected = placement.PlayerId == _selectedPlayerId;
            var player = FindPlayer(placement.PlayerId);
            var team = TeamById(placement.TeamId);
            var pieceModulate = ActivationDisplayState(placement.PlayerId, placement) == "Activated"
                ? ActivatedPieceModulate
                : Colors.White;
            tile.SetPiece(player is null ? null : PlayerSprite(team, player, placement), pieceModulate);
            tile.SetOverlay(placement.State == PlayerPitchState.Stunned ? StunnedSprite(0) : null);
            if (isSelected)
            {
                tile.SetHighlight(AtlasCell(_pitchTileSheet, "overlay:selected", 0, 3));
            }
            tile.Text = "";
            tile.TooltipText = PlayerPitchTooltip(placement);
            var canBlockTarget = _selectedPlayerId is Guid attackerId && IsLegalBlockTarget(attackerId, placement.PlayerId);
            var canBlitzTarget = _selectedPlayerId is Guid blitzerId && IsLegalBlitzTarget(blitzerId, placement.PlayerId);
            var canKickoffBlitzTarget = _selectedPlayerId is Guid kickoffBlitzerId && IsLegalKickoffBlitzTarget(kickoffBlitzerId, placement.PlayerId);
            var canPassTarget = _selectedPlayerId is Guid passerId && IsLegalPassTarget(passerId, placement.PlayerId);
            var canHandOffTarget = _selectedPlayerId is Guid handOffCarrierId && IsHandingOff(handOffCarrierId) && IsLegalHandOffTarget(handOffCarrierId, placement.PlayerId);
            var canFoulTarget = _selectedPlayerId is Guid foulerId && IsLegalFoulTarget(foulerId, placement.PlayerId);
            var canPushTarget = IsLegalPushSquare(placement.Square!);
            var canFollowUpTarget = IsLegalFollowUpSquare(placement.Square!);
            var canThrowBombTarget = IsLegalBombThrowSquare(placement.Square!);
            var canLaunchPlayer = _selectedPlayerId is Guid launchActorIdForPlayer &&
                _previewLaunchedPlayerId is null &&
                (_throwTeamMateMode || _kickTeamMateMode) &&
                IsLegalLaunchPlayer(launchActorIdForPlayer, placement.PlayerId);
            var canLaunchTargetSquare = _selectedPlayerId is Guid launchActorIdForSquare &&
                _previewLaunchedPlayerId is Guid launchedPlayerId &&
                IsLegalLaunchTargetSquare(launchActorIdForSquare, launchedPlayerId, placement.Square!);
            tile.Disabled = !CanSelectPlayer(placement.PlayerId) && !canBlockTarget && !canBlitzTarget && !canKickoffBlitzTarget && !canPassTarget && !canHandOffTarget && !canFoulTarget && !canPushTarget && !canFollowUpTarget && !canThrowBombTarget && !canLaunchPlayer && !canLaunchTargetSquare;
            ApplySquareStyle(
                tile,
                placement.Square!,
                isSelected,
                canUse: canBlockTarget || canBlitzTarget || canKickoffBlitzTarget || canPassTarget || canHandOffTarget || canFoulTarget || canPushTarget || canFollowUpTarget || canThrowBombTarget || canLaunchPlayer || canLaunchTargetSquare,
                pathMarker: canLaunchPlayer ? "L" : canLaunchTargetSquare ? "L" : canHandOffTarget ? "H" : canFollowUpTarget ? "F" : canPushTarget ? ">" : null,
                blockRole: BlockPreviewRole(placement.PlayerId),
                passRole: PassPreviewRole(placement.PlayerId));
        }

        if (_animationBallSquare is PitchSquare animationBallSquare && _pitchTiles.TryGetValue(animationBallSquare, out var animationBallTile))
        {
            animationBallTile.SetOverlay(BallSprite(0));
            animationBallTile.Text = "";
            animationBallTile.TooltipText = "Ball";
        }
        else if (_match.Ball.Square is PitchSquare ballSquare && _pitchTiles.TryGetValue(ballSquare, out var ballTile))
        {
            ballTile.SetOverlay(BallSprite(0));
            ballTile.Text = "";
            ballTile.TooltipText = "Ball";
        }

        if (_animationBallSquare is null &&
            _match.Ball.CarrierPlayerId is Guid carrierId &&
            _match.Placements.FirstOrDefault(placement => placement.PlayerId == carrierId)?.Square is PitchSquare carrierSquare &&
            _pitchTiles.TryGetValue(carrierSquare, out var carrierTile))
        {
            carrierTile.SetOverlay(BallSprite(4));
            carrierTile.Text = "";
            carrierTile.TooltipText = $"{FindPlayer(carrierId)?.Name ?? "Ball carrier"} with ball";
        }

        var activeTeam = ActiveTeam();
        var selected = _selectedPlayerId is Guid playerId ? FindPlayer(playerId)?.Name ?? "none" : "none";
        _doneButton.Disabled = !CanAdvanceCurrentStep();
        _doneButton.Text = AdvanceButtonText();
        _doneButton.TooltipText = _doneButton.Disabled
            ? AdvanceBlockedMessage()
            : _endTurnConfirmationArmed && RequiresEndTurnConfirmation()
                ? "Click again to end the current team turn."
                : "Advance the current phase or turn.";
        RefreshPassModeButton();
        RefreshHandOffModeButton();
        RefreshBlitzModeButton();
        RefreshLaunchModeButtons();

        _decisionTitleLabel.Text = DecisionTitle();
        _summaryLabel.Text = DecisionInstruction(activeTeam, selected);
        _decisionDetailLabel.Text = DecisionDetail(activeTeam, selected);
        RefreshEventLog();
    }

    private void RefreshMatchHud()
    {
        _homeHudLabel.Text = FormatTeamHud(_homeTeam, _match.HomeScore, _match.HomeRerollsRemaining, _match.HomeApothecariesRemaining);
        _awayHudLabel.Text = FormatTeamHud(_awayTeam, _match.AwayScore, _match.AwayRerollsRemaining, _match.AwayApothecariesRemaining);
        _turnHudLabel.Text = $"Half {_match.Half}  Drive {_match.Drive} ({DriveStateLabel(_match.DriveState)})  {PhaseLabel(_match.Phase)}  Turn {_match.Turn}/{_ruleset.TurnsPerHalf}\nWeather: {WeatherLabel(_match.Weather)}";
        _turnHudLabel.TooltipText = $"{ActiveTeam().Name} active. Home turn {_match.HomeTurn}, away turn {_match.AwayTurn}. {WeatherEffectSummary(_match.Weather)}";
    }

    private string DecisionTitle()
    {
        if (_match.PendingReroll is not null)
        {
            return "Reroll Choice";
        }

        if (_match.PendingBlockReroll is not null)
        {
            return "Block Reroll";
        }

        if (_match.PendingApothecary is not null)
        {
            return "Apothecary Choice";
        }

        if (_match.PendingSendOff is not null)
        {
            return "Send-Off Choice";
        }

        if (_match.PendingStandFirm is not null)
        {
            return "Stand Firm";
        }

        if (_match.PendingDivingTackle is not null)
        {
            return "Diving Tackle";
        }

        if (_match.PendingBallPlacement is not null)
        {
            return "Place Ball";
        }

        if (_match.PendingBombThrow is not null)
        {
            return "Bomb Throw";
        }

        if (_match.PendingBlock is not null)
        {
            return "Block Dice";
        }

        if (_match.PendingPush is not null)
        {
            return "Choose Push";
        }

        if (_match.PendingFollowUp is not null)
        {
            return "Follow-Up";
        }

        if (_match.PendingInterception is not null)
        {
            return "Interception";
        }

        if (_match.PendingKickoffEvent is PendingKickoffEventChoice kickoff)
        {
            return FormatKickoffEventKind(kickoff.Kind);
        }

        if (_previewBlockDefenderId is not null)
        {
            return "Block Preview";
        }

        if (_previewBlitzDefenderId is not null)
        {
            return "Blitz Preview";
        }

        if (_previewFoulVictimId is not null)
        {
            return "Foul Preview";
        }

        if (_previewPassTargetSquare is not null)
        {
            return "Pass Preview";
        }

        if (_throwTeamMateMode || _kickTeamMateMode)
        {
            return _kickTeamMateMode ? "Kick Team-Mate" : "Throw Team-Mate";
        }

        if (_previewDestination is not null)
        {
            return "Confirm Movement";
        }

        return PhaseLabel(_match.Phase);
    }

    private string DecisionInstruction(LeagueTeam activeTeam, string selected)
    {
        return _match.Phase switch
        {
            _ when _match.PendingReroll is PendingRerollChoice pending => RerollSummary(pending),
            _ when _match.PendingBlockReroll is PendingBlockRerollChoice pending => BlockRerollSummary(pending),
            _ when _match.PendingApothecary is PendingApothecaryChoice pending => ApothecarySummary(pending),
            _ when _match.PendingSendOff is PendingSendOffChoice pending => SendOffSummary(pending),
            _ when _match.PendingStandFirm is PendingStandFirmChoice pending => StandFirmSummary(pending),
            _ when _match.PendingDivingTackle is PendingDivingTackleChoice pending => DivingTackleSummary(pending),
            _ when _match.PendingBallPlacement is PendingBallPlacementChoice { Reason: "Touchback" } => "Choose a receiving player to carry the touchback.",
            _ when _match.PendingBallPlacement is PendingBallPlacementChoice pending => $"Choose where {FindPlayer(pending.PlayerId)?.Name ?? "player"} places the ball with {pending.Reason}.",
            _ when _match.PendingBombThrow is PendingBombThrowChoice pending => BombThrowSummary(pending),
            _ when _match.PendingBlock is PendingBlockChoice pending => $"Choose block dice for {FindPlayer(pending.AttackerPlayerId)?.Name ?? "attacker"}'s block.",
            _ when _match.PendingPush is PendingPushChoice pending => $"Choose where {FindPlayer(pending.DefenderPlayerId)?.Name ?? "defender"} is pushed.",
            _ when _match.PendingFollowUp is PendingFollowUpChoice pending => $"Click {pending.FollowUpSquare.X + 1},{pending.FollowUpSquare.Y + 1} to follow up, or skip the follow-up.",
            _ when _match.PendingInterception is PendingInterceptionChoice pending => $"Choose an interceptor for the {pending.PassRangeName} pass.",
            _ when _match.PendingKickoffEvent is PendingKickoffEventChoice pending => KickoffEventSummary(pending),
            MatchPhase.DefenseSetup => $"{activeTeam.Name}: place the kicking team in the legal setup area.",
            MatchPhase.OffenseSetup => $"{activeTeam.Name}: place the receiving team in the legal setup area.",
            MatchPhase.Kickoff => $"{KickingTeam().Name}: select a kick target square in {activeTeam.Name}'s half.",
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewBlockDefenderId is Guid defenderId => BlockPreviewSummary(defenderId),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewBlitzDefenderId is Guid blitzDefenderId => BlitzPreviewSummary(blitzDefenderId),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewFoulVictimId is Guid victimId => FoulPreviewSummary(victimId),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewPassTargetSquare is PitchSquare passTargetSquare => PassPreviewSummary(passTargetSquare),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _throwTeamMateMode || _kickTeamMateMode => LaunchPreviewSummary(),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewDestination is not null => $"Click {_previewDestination.X + 1},{_previewDestination.Y + 1} again to confirm movement.",
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn => $"{activeTeam.Name}: choose a ready player or continue the turn.",
            _ => $"{activeTeam.Name}: resolve {_match.Phase}."
        };
    }

    private string DecisionDetail(LeagueTeam activeTeam, string selected)
    {
        var details = new List<string>();
        if (_selectedPlayerId is Guid playerId && FindPlayer(playerId) is Player player)
        {
            details.Add($"Selected: {player.Name} ({player.PositionId})");
        }
        else
        {
            details.Add("Selected: none");
        }

        if (_match.Phase is MatchPhase.DefenseSetup or MatchPhase.OffenseSetup)
        {
            details.Add("Legal setup squares are highlighted");
        }
        else if (_match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn)
        {
            details.Add($"Active: {activeTeam.Name}");
        }

        if (_match.Log.LastOrDefault()?.Message is string lastEvent)
        {
            details.Add($"Last: {lastEvent}");
        }

        return string.Join("  |  ", details);
    }

    private void RefreshEventLog()
    {
        _lastEventLabel.Text = _match.Log.LastOrDefault()?.Message ?? "No match events yet.";

        foreach (var child in _eventLogList.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var entry in _match.Log.TakeLast(10).Reverse())
        {
            var label = new Label
            {
                Text = entry.Message,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            _eventLogList.AddChild(label);
        }
    }

    private static string FormatTeamHud(LeagueTeam team, int score, int rerollsRemaining, int apothecariesRemaining)
    {
        return $"{team.Name}  Score {score}  RR {rerollsRemaining}  Apo {apothecariesRemaining}";
    }

    private static string WeatherLabel(WeatherCondition weather)
    {
        return weather switch
        {
            WeatherCondition.SwelteringHeat => "Sweltering Heat",
            WeatherCondition.VerySunny => "Very Sunny",
            WeatherCondition.Nice => "Nice",
            WeatherCondition.PouringRain => "Pouring Rain",
            WeatherCondition.Blizzard => "Blizzard",
            _ => weather.ToString()
        };
    }

    private static string DriveStateLabel(DriveState state)
    {
        return state switch
        {
            DriveState.Setup => "setup",
            DriveState.InProgress => "in play",
            DriveState.Ending => "ending",
            DriveState.Complete => "complete",
            _ => state.ToString()
        };
    }

    private static string WeatherEffectSummary(WeatherCondition weather)
    {
        return weather switch
        {
            WeatherCondition.SwelteringHeat => "Weather effect: no active roll modifier.",
            WeatherCondition.VerySunny => "Weather effect: passing rolls are harder by 1.",
            WeatherCondition.Nice => "Weather effect: none.",
            WeatherCondition.PouringRain => "Weather effect: pickup, catch, and interception rolls are harder by 1.",
            WeatherCondition.Blizzard => "Weather effect: passing rolls are harder by 1 and go-for-its need 3+.",
            _ => "Weather effect: unknown."
        };
    }

    private void RefreshPassModeButton()
    {
        var canPass = _selectedPlayerId is Guid passerId && CanEnterPassMode(passerId);
        if (!canPass)
        {
            _passMode = false;
        }

        _passModeButton.Disabled = !canPass;
        _passModeButton.Text = _passMode ? "Pass: On" : "Pass";
        _passModeButton.TooltipText = canPass
            ? "Declare a pass (commits the action), then move to collect the ball if needed and toggle targeting to throw."
            : "Select an unactivated player to declare a pass.";
        SetModeButtonStyle(_passModeButton, _passMode, canPass);
    }

    private void RefreshHandOffModeButton()
    {
        var canHandOff = _selectedPlayerId is Guid carrierId && CanEnterHandOffMode(carrierId);
        if (!canHandOff)
        {
            _handOffMode = false;
        }

        _handOffModeButton.Disabled = !canHandOff;
        _handOffModeButton.Text = _handOffMode ? "Hand-off: On" : "Hand-off";
        _handOffModeButton.TooltipText = canHandOff
            ? "Declare a hand-off (commits the action), then move to collect the ball if needed and hand off to an adjacent team-mate."
            : "Select an unactivated player to declare a hand-off.";
        SetModeButtonStyle(_handOffModeButton, _handOffMode, canHandOff);
    }

    private void RefreshBlitzModeButton()
    {
        var declaredBlitz = _selectedPlayerId is Guid playerId &&
            CurrentTurnActivation(playerId)?.Action == PlayerTurnAction.Blitz;
        var canBlitz = _selectedPlayerId is Guid blitzerId && CanEnterBlitzMode(blitzerId);
        if (!canBlitz && !declaredBlitz)
        {
            _blitzMode = false;
        }

        if (declaredBlitz)
        {
            _blitzMode = true;
        }

        _blitzModeButton.Disabled = !canBlitz && !declaredBlitz;
        _blitzModeButton.Text = _blitzMode ? "Blitz: On" : "Blitz";
        _blitzModeButton.TooltipText = declaredBlitz
            ? "This player has declared a Blitz."
            : canBlitz ? "Declare this player's Blitz action." : "Select an unactivated player to declare a Blitz.";
        SetModeButtonStyle(_blitzModeButton, _blitzMode, canBlitz || declaredBlitz);
    }

    private void RefreshLaunchModeButtons()
    {
        var canThrow = _selectedPlayerId is Guid throwerId && CanEnterLaunchMode(throwerId, "throw-team-mate");
        var canKick = _selectedPlayerId is Guid kickerId && CanEnterLaunchMode(kickerId, "kick-team-mate");
        if (!canThrow)
        {
            _throwTeamMateMode = false;
        }

        if (!canKick)
        {
            _kickTeamMateMode = false;
        }

        _throwTeamMateModeButton.Disabled = !canThrow;
        _throwTeamMateModeButton.Text = _throwTeamMateMode ? "TTM: On" : "TTM";
        _throwTeamMateModeButton.TooltipText = canThrow ? "Throw an adjacent Right Stuff team-mate." : "Select an unactivated player with Throw Team-Mate.";
        SetModeButtonStyle(_throwTeamMateModeButton, _throwTeamMateMode, canThrow);

        _kickTeamMateModeButton.Disabled = !canKick;
        _kickTeamMateModeButton.Text = _kickTeamMateMode ? "KTM: On" : "KTM";
        _kickTeamMateModeButton.TooltipText = canKick ? "Kick an adjacent Right Stuff team-mate." : "Select an unactivated player with Kick Team-Mate.";
        SetModeButtonStyle(_kickTeamMateModeButton, _kickTeamMateMode, canKick);
    }

    private void SetModeButtonStyle(Button button, bool active, bool enabled)
    {
        if (!enabled)
        {
            button.AddThemeStyleboxOverride("normal", FlatStyle(new Color("242a26"), new Color("343a35")));
            button.AddThemeStyleboxOverride("hover", FlatStyle(new Color("242a26"), new Color("343a35")));
            return;
        }

        var background = active ? new Color("5a4a22") : new Color("253a32");
        var border = active ? SelectedColor : new Color("5d6755");
        button.AddThemeStyleboxOverride("normal", FlatStyle(background, border, borderWidth: active ? 2 : 1));
        button.AddThemeStyleboxOverride("hover", FlatStyle(background.Lightened(0.12f), SelectedColor, borderWidth: 2));
        button.AddThemeStyleboxOverride("pressed", FlatStyle(background.Darkened(0.12f), SelectedColor, borderWidth: 2));
    }

    private bool CanAdvanceCurrentStep()
    {
        if (_match.PendingReroll is not null ||
            _match.PendingBlockReroll is not null ||
            _match.PendingApothecary is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingBallPlacement is not null ||
            _match.PendingBombThrow is not null ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingInterception is not null)
        {
            return false;
        }

        if (_match.PendingFollowUp is not null)
        {
            return true;
        }

        if (_match.PendingKickoffEvent is not null)
        {
            return true;
        }

        return _match.Phase is MatchPhase.DefenseSetup or
            MatchPhase.OffenseSetup or
            MatchPhase.OffensivePlayerTurn or
            MatchPhase.DefensiveTurn;
    }

    private bool RequiresEndTurnConfirmation()
    {
        return _match.PendingFollowUp is null &&
            _match.PendingKickoffEvent is null &&
            _match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn;
    }

    private void ResetEndTurnConfirmation()
    {
        if (!_endTurnConfirmationArmed)
        {
            return;
        }

        _endTurnConfirmationArmed = false;
        if (_doneButton is not null && IsInstanceValid(_doneButton))
        {
            _doneButton.Text = AdvanceButtonText();
            _doneButton.TooltipText = _doneButton.Disabled ? AdvanceBlockedMessage() : "Advance the current phase or turn.";
        }
    }

    private string AdvanceButtonText()
    {
        if (_endTurnConfirmationArmed && RequiresEndTurnConfirmation())
        {
            return "Sure?";
        }

        if (_match.PendingKickoffEvent is not null)
        {
            return "Resolve Kickoff";
        }

        if (_match.PendingFollowUp is not null)
        {
            return "Skip Follow-Up";
        }

        return _match.Phase switch
        {
            MatchPhase.DefenseSetup => "Finish Defense Setup",
            MatchPhase.OffenseSetup => "Finish Offense Setup",
            MatchPhase.Kickoff => "Kickoff",
            MatchPhase.OffensivePlayerTurn => "End Offense Turn",
            MatchPhase.DefensiveTurn => "End Defense Turn",
            MatchPhase.Complete => "Match Complete",
            _ => "Advance"
        };
    }

    private string AdvanceBlockedMessage()
    {
        if (_match.PendingReroll is not null)
        {
            return "Resolve the pending reroll first.";
        }

        if (_match.PendingBlockReroll is not null)
        {
            return "Resolve the pending block reroll first.";
        }

        if (_match.PendingApothecary is not null)
        {
            return "Resolve the pending apothecary choice first.";
        }

        if (_match.PendingSendOff is not null)
        {
            return "Resolve the pending send-off choice first.";
        }

        if (_match.PendingStandFirm is not null)
        {
            return "Resolve the pending Stand Firm choice first.";
        }

        if (_match.PendingDivingTackle is not null)
        {
            return "Resolve the pending Diving Tackle choice first.";
        }

        if (_match.PendingBallPlacement is not null)
        {
            return "Resolve the pending ball placement first.";
        }

        if (_match.PendingBombThrow is not null)
        {
            return "Resolve the pending bomb throw first.";
        }

        if (_match.PendingBlock is not null)
        {
            return "Choose block dice first.";
        }

        if (_match.PendingPush is not null)
        {
            return "Choose a push square first.";
        }

        if (_match.PendingFollowUp is not null)
        {
            return "Choose whether to follow up first.";
        }

        if (_match.PendingInterception is not null)
        {
            return "Choose an interceptor first.";
        }

        if (_match.PendingKickoffEvent is not null)
        {
            return "Resolve the kickoff event first.";
        }

        return _match.Phase switch
        {
            MatchPhase.Kickoff => "Select a kick target square.",
            MatchPhase.Complete => "The match is complete.",
            _ => "This phase cannot be advanced from here."
        };
    }

    private static string PhaseLabel(MatchPhase phase)
    {
        return phase switch
        {
            MatchPhase.DefenseSetup => "Defense Setup",
            MatchPhase.OffenseSetup => "Offense Setup",
            MatchPhase.OffensivePlayerTurn => "Offense",
            MatchPhase.DefensiveTurn => "Defense",
            MatchPhase.EndOfHalf => "Half Time",
            _ => phase.ToString()
        };
    }

    private void ApplySquareStyle(Button button, PitchSquare square, bool isSelected, bool canUse, string? pathMarker = null, string? blockRole = null, string? passRole = null)
    {
        ClearPitchButtonChrome(button);
        var textColor = pathMarker?.StartsWith('!') == true
            ? GoForItPathColor
            : pathMarker is not null
                ? LineColor
                : new Color("e8eadc");
        button.AddThemeColorOverride("font_color", textColor);
        button.AddThemeColorOverride("font_hover_color", textColor.Lightened(0.12f));
        button.AddThemeColorOverride("font_pressed_color", textColor.Darkened(0.1f));
        button.AddThemeColorOverride("font_disabled_color", textColor.Darkened(0.16f));
    }

    private Color SquareColor(PitchSquare square)
    {
        if (square.X == 0)
        {
            return EndZoneHome;
        }

        if (square.X == _ruleset.PitchWidth - 1)
        {
            return EndZoneAway;
        }

        return PitchGrass;
    }

    private static Color SquareBorderColor(PitchSquare square)
    {
        return new Color("285c31");
    }

    private static string FormatCasualtyResult(CasualtyResult result)
    {
        return MatchFormatting.FormatCasualtyResult(result);
    }

    private void RefreshBlockDiceChoice()
    {
        foreach (var child in _blockDiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (_match.PendingBlock is not PendingBlockChoice pending)
        {
            _blockDiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = true;
        _blockDiceBox.AddChild(new Label { Text = "Block dice:" });
        foreach (var roll in pending.Rolls)
        {
            var button = new Button
            {
                Text = "",
                Icon = BlockDieSprite(roll),
                ExpandIcon = false,
                CustomMinimumSize = new Vector2(38, 34)
            };
            button.TooltipText = BlockDieTooltip(roll);
            button.Pressed += async () => await ChooseBlockDieAsync(roll);
            _blockDiceBox.AddChild(button);
        }

        if (TeamRerollsRemaining(pending.AttackerTeamId) > 0)
        {
            var rerollButton = new Button { Text = $"Reroll ({TeamRerollsRemaining(pending.AttackerTeamId)})" };
            rerollButton.TooltipText = "Use a team reroll to reroll all block dice.";
            rerollButton.Pressed += async () => await RerollPendingBlockAsync();
            _blockDiceBox.AddChild(rerollButton);
        }
    }

    private void RefreshSetupChoice()
    {
        foreach (var child in _setupChoiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (!CanReturnSelectedSetupPlayerToReserve())
        {
            _setupChoiceBox.Visible = false;
            return;
        }

        _setupChoiceBox.Visible = true;
        var button = new Button { Text = "Return to Reserve" };
        button.TooltipText = "Remove the selected setup player from the pitch so another reserve player can be placed.";
        button.Pressed += async () => await ReturnSelectedSetupPlayerToReserveAsync();
        _setupChoiceBox.AddChild(button);
    }

    private static string BlockDieTooltip(int roll)
    {
        return roll switch
        {
            <= 1 => "Attacker down",
            2 => "Both down",
            <= 4 => "Push",
            5 => "Defender stumbles - defender down unless Dodge applies",
            _ => "Defender down"
        };
    }

    private void RefreshInterceptionChoice()
    {
        foreach (var child in _interceptionChoiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (_match.PendingInterception is not PendingInterceptionChoice pending)
        {
            _interceptionChoiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = false;

        _interceptionChoiceBox.Visible = true;
        _interceptionChoiceBox.AddChild(new Label { Text = "Interceptor:" });
        foreach (var playerId in pending.EligiblePlayerIds)
        {
            var button = new Button
            {
                Text = PlayerMarker(playerId),
                CustomMinimumSize = new Vector2(42, 28)
            };
            var player = FindPlayer(playerId);
            var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
            var tackleZones = placement?.Square is PitchSquare square
                ? CountOpposingTackleZones(_match, pending.DefendingTeamId, playerId, square)
                : 0;
            button.TooltipText = player is null
                ? "Choose interceptor"
                : $"{player.Name} - intercept {InterceptionTarget(player, _match.Weather, tackleZones)}+";
            button.Pressed += async () => await ChooseInterceptorAsync(playerId);
            _interceptionChoiceBox.AddChild(button);
        }
    }

    private void RefreshRerollChoice()
    {
        foreach (var child in _rerollChoiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (_match.PendingBlockReroll is PendingBlockRerollChoice pendingBlockReroll)
        {
            _blockDiceBox.Visible = false;
            _interceptionChoiceBox.Visible = false;
            _rerollChoiceBox.Visible = true;
            _rerollChoiceBox.AddChild(new Label { Text = "Block reroll:" });

            if (pendingBlockReroll.TeamRerollAvailable)
            {
                var teamButton = new Button { Text = $"Team ({TeamRerollsRemaining(pendingBlockReroll.AttackerTeamId)})" };
                teamButton.TooltipText = "Use a team reroll on the block dice.";
                teamButton.Pressed += async () => await ResolveBlockRerollAsync(useTeamReroll: true);
                _rerollChoiceBox.AddChild(teamButton);
            }

            var acceptButton = new Button { Text = "Accept" };
            acceptButton.TooltipText = "Accept the attacker down result.";
            acceptButton.Pressed += async () => await ResolveBlockRerollAsync(useTeamReroll: false);
            _rerollChoiceBox.AddChild(acceptButton);
            return;
        }

        if (_match.PendingReroll is not PendingRerollChoice pending)
        {
            _rerollChoiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = false;
        _interceptionChoiceBox.Visible = false;
        _divingTackleChoiceBox.Visible = false;
        _sendOffChoiceBox.Visible = false;
        _rerollChoiceBox.Visible = true;
        _rerollChoiceBox.AddChild(new Label { Text = "Reroll:" });

        if (pending.TeamRerollAvailable)
        {
            var teamButton = new Button { Text = $"Team ({TeamRerollsRemaining(pending.TeamId)})" };
            teamButton.TooltipText = "Use a team reroll.";
            teamButton.Pressed += async () => await ResolveRerollAsync(useTeamReroll: true);
            _rerollChoiceBox.AddChild(teamButton);
        }

        foreach (var skillId in pending.SkillRerollIds)
        {
            var skillButton = new Button { Text = skillId };
            skillButton.TooltipText = $"Use {skillId}.";
            skillButton.Pressed += async () => await ResolveRerollAsync(useTeamReroll: false, skillId);
            _rerollChoiceBox.AddChild(skillButton);
        }

        var declineButton = new Button { Text = "Accept" };
        declineButton.TooltipText = "Accept the failed roll.";
        declineButton.Pressed += async () => await ResolveRerollAsync(useTeamReroll: false);
        _rerollChoiceBox.AddChild(declineButton);
    }

    private void RefreshApothecaryChoice()
    {
        foreach (var child in _apothecaryChoiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (_match.PendingApothecary is not PendingApothecaryChoice pending)
        {
            _apothecaryChoiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = false;
        _interceptionChoiceBox.Visible = false;
        _rerollChoiceBox.Visible = false;
        _divingTackleChoiceBox.Visible = false;
        _sendOffChoiceBox.Visible = false;
        _apothecaryChoiceBox.Visible = true;
        _apothecaryChoiceBox.AddChild(new Label { Text = "Apothecary:" });

        var useButton = new Button { Text = "Use" };
        useButton.TooltipText = "Spend an apothecary and roll a second casualty result.";
        useButton.Pressed += async () => await ResolveApothecaryAsync(useApothecary: true);
        _apothecaryChoiceBox.AddChild(useButton);

        var declineButton = new Button { Text = "Decline" };
        declineButton.TooltipText = "Keep the original casualty result.";
        declineButton.Pressed += async () => await ResolveApothecaryAsync(useApothecary: false);
        _apothecaryChoiceBox.AddChild(declineButton);
    }

    private void RefreshSendOffChoice()
    {
        foreach (var child in _sendOffChoiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (_match.PendingSendOff is not PendingSendOffChoice pending)
        {
            _sendOffChoiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = false;
        _interceptionChoiceBox.Visible = false;
        _rerollChoiceBox.Visible = false;
        _apothecaryChoiceBox.Visible = false;
        _standFirmChoiceBox.Visible = false;
        _divingTackleChoiceBox.Visible = false;
        _sendOffChoiceBox.Visible = true;
        _sendOffChoiceBox.AddChild(new Label { Text = "Send-off:" });

        if (pending.BribeAvailable)
        {
            var bribeButton = new Button { Text = "Use Bribe" };
            bribeButton.TooltipText = "Spend a bribe and roll to prevent the send-off.";
            bribeButton.Pressed += async () => await ResolveSendOffAsync(useBribe: true);
            _sendOffChoiceBox.AddChild(bribeButton);
        }

        var declineButton = new Button { Text = pending.BribeAvailable ? "Decline" : "Send Off" };
        declineButton.TooltipText = pending.BribeAvailable ? "Decline the bribe and send the player off." : "Resolve the send-off.";
        declineButton.Pressed += async () => await ResolveSendOffAsync(useBribe: false);
        _sendOffChoiceBox.AddChild(declineButton);
    }

    private void RefreshStandFirmChoice()
    {
        foreach (var child in _standFirmChoiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (_match.PendingStandFirm is not PendingStandFirmChoice)
        {
            _standFirmChoiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = false;
        _interceptionChoiceBox.Visible = false;
        _rerollChoiceBox.Visible = false;
        _apothecaryChoiceBox.Visible = false;
        _divingTackleChoiceBox.Visible = false;
        _sendOffChoiceBox.Visible = false;
        _standFirmChoiceBox.Visible = true;
        _standFirmChoiceBox.AddChild(new Label { Text = "Stand Firm:" });

        var useButton = new Button { Text = "Use" };
        useButton.TooltipText = "Refuse the push and stay in this square.";
        useButton.Pressed += async () => await ResolveStandFirmAsync(useStandFirm: true);
        _standFirmChoiceBox.AddChild(useButton);

        var declineButton = new Button { Text = "Decline" };
        declineButton.TooltipText = "Allow the push to continue.";
        declineButton.Pressed += async () => await ResolveStandFirmAsync(useStandFirm: false);
        _standFirmChoiceBox.AddChild(declineButton);
    }

    private void RefreshDivingTackleChoice()
    {
        foreach (var child in _divingTackleChoiceBox.GetChildren())
        {
            child.QueueFree();
        }

        if (_match.PendingDivingTackle is not PendingDivingTackleChoice)
        {
            _divingTackleChoiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = false;
        _interceptionChoiceBox.Visible = false;
        _rerollChoiceBox.Visible = false;
        _apothecaryChoiceBox.Visible = false;
        _standFirmChoiceBox.Visible = false;
        _sendOffChoiceBox.Visible = false;
        _divingTackleChoiceBox.Visible = true;
        _divingTackleChoiceBox.AddChild(new Label { Text = "Diving Tackle:" });

        var useButton = new Button { Text = "Use" };
        useButton.TooltipText = "Go prone to apply the dodge modifier.";
        useButton.Pressed += async () => await ResolveDivingTackleAsync(useDivingTackle: true);
        _divingTackleChoiceBox.AddChild(useButton);

        var declineButton = new Button { Text = "Decline" };
        declineButton.TooltipText = "Let the dodge succeed.";
        declineButton.Pressed += async () => await ResolveDivingTackleAsync(useDivingTackle: false);
        _divingTackleChoiceBox.AddChild(declineButton);
    }

    private string RerollSummary(PendingRerollChoice pending)
    {
        var playerName = FindPlayer(pending.PlayerId)?.Name ?? "player";
        var options = new List<string>();
        if (pending.TeamRerollAvailable)
        {
            options.Add($"team reroll ({TeamRerollsRemaining(pending.TeamId)} left)");
        }

        options.AddRange(pending.SkillRerollIds);
        var optionText = options.Count == 0 ? "no reroll available" : string.Join(", ", options);
        return $"{playerName} failed {FormatRerollKind(pending.Kind)}: rolled {pending.Roll} vs {pending.Target}+. Choose reroll or accept failure; {optionText}.";
    }

    private string BlockRerollSummary(PendingBlockRerollChoice pending)
    {
        var attackerName = FindPlayer(pending.AttackerPlayerId)?.Name ?? "attacker";
        var defenderName = FindPlayer(pending.DefenderPlayerId)?.Name ?? "defender";
        return $"{attackerName} rolled attacker down against {defenderName}. Use a team reroll or accept failure.";
    }

    private string ApothecarySummary(PendingApothecaryChoice pending)
    {
        var playerName = FindPlayer(pending.PlayerId)?.Name ?? "player";
        return $"{playerName} suffered {FormatCasualtyResult(pending.OriginalCasualty.Result)}. Use an apothecary?";
    }

    private string SendOffSummary(PendingSendOffChoice pending)
    {
        var playerName = FindPlayer(pending.PlayerId)?.Name ?? "player";
        var bribeText = pending.BribeAvailable
            ? $" Use a bribe? {TeamBribesRemaining(pending.TeamId)} available."
            : " No bribe is available.";
        var driveEndText = pending.DriveEnd is null
            ? ""
            : $" Drive end queue: {RemainingDriveEndSecretWeapons(pending)} more Secret Weapon send-off{(RemainingDriveEndSecretWeapons(pending) == 1 ? "" : "s")} after this, then knockout recovery and {DriveEndDestination(pending.DriveEnd)}.";
        return $"{playerName} faces send-off for {pending.Reason}.{bribeText}{driveEndText}";
    }

    private string StandFirmSummary(PendingStandFirmChoice pending)
    {
        var playerName = FindPlayer(pending.DefenderPlayerId)?.Name ?? "defender";
        return $"{playerName} can use Stand Firm. Use it to refuse the push?";
    }

    private string DivingTackleSummary(PendingDivingTackleChoice pending)
    {
        var tacklerName = FindPlayer(pending.TacklerPlayerId)?.Name ?? "tackler";
        var dodgerName = FindPlayer(pending.DodgerPlayerId)?.Name ?? "dodger";
        return $"{tacklerName} can use Diving Tackle against {dodgerName}: roll {pending.Roll} succeeds on {pending.TargetWithoutDivingTackle}+ but fails on {pending.TargetWithDivingTackle}+. Use it?";
    }

    private string BombThrowSummary(PendingBombThrowChoice pending)
    {
        var throwerName = FindPlayer(pending.ThrowerPlayerId)?.Name ?? "thrower";
        return $"{throwerName} caught the bomb at {pending.BombSquare.X + 1},{pending.BombSquare.Y + 1}. Choose a target square to throw it back.";
    }

    private string KickoffEventSummary(PendingKickoffEventChoice pending)
    {
        var team = TeamById(pending.TeamId);
        var selected = _selectedPlayerId is Guid selectedId ? FindPlayer(selectedId)?.Name : "none";
        var action = pending.Kind switch
        {
            KickoffEventKind.HighKick => $"choose one open player to move under the ball at {pending.LandingSquare.X + 1},{pending.LandingSquare.Y + 1}",
            KickoffEventKind.SolidDefence => $"reposition up to {pending.MovesRemaining} more open defensive player{(pending.MovesRemaining == 1 ? "" : "s")} within a legal setup",
            _ => $"move up to {pending.MovesRemaining} more open player{(pending.MovesRemaining == 1 ? "" : "s")} one square"
        };
        return $"{FormatKickoffEventKind(pending.Kind)}: {team.Name} may {action}. Selected: {selected}.";
    }

    private bool HasUsedPass(Guid teamId) => MatchQueries.HasUsedPass(_match, teamId);
    private bool HasUsedHandOff(Guid teamId) => MatchQueries.HasUsedHandOff(_match, teamId);
    private bool HasUsedBlitz(Guid teamId) => MatchQueries.HasUsedBlitz(_match, teamId);
    private bool HasUsedFoul(Guid teamId) => MatchQueries.HasUsedFoul(_match, teamId);
    private int TeamRerollsRemaining(Guid teamId) => MatchQueries.TeamRerollsRemaining(_match, teamId);
    private int TeamBribesRemaining(Guid teamId) => MatchQueries.TeamBribesRemaining(_match, teamId);

    private int RemainingDriveEndSecretWeapons(PendingSendOffChoice pending)
    {
        if (pending.DriveEnd is null)
        {
            return 0;
        }

        return _match.Placements.Count(placement =>
            placement.PlayerId != pending.PlayerId &&
            !pending.DriveEnd.ResolvedPlayerIds.Contains(placement.PlayerId) &&
            _match.SecretWeaponPlayerIds.Contains(placement.PlayerId) &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone or PlayerPitchState.Stunned or PlayerPitchState.Reserve &&
            placement.Square is not null);
    }

    private static string DriveEndDestination(PendingDriveEndContinuation continuation)
    {
        if (continuation.CompleteMatch)
        {
            return "full time";
        }

        return continuation.StartSecondHalf ? "second-half setup" : "the next drive setup";
    }

    private string PlayerMarker(Guid playerId)
    {
        var homeIndex = _homeTeam.Players.ToList().FindIndex(player => player.Id == playerId);
        if (homeIndex >= 0)
        {
            return $"H{homeIndex + 1}";
        }

        var awayIndex = _awayTeam.Players.ToList().FindIndex(player => player.Id == playerId);
        return awayIndex >= 0 ? $"A{awayIndex + 1}" : "?";
    }

    private static string FormatStats(PlayerStats stats)
    {
        return $"MA {stats.Movement} ST {stats.Strength} AG {stats.Agility}+ PA {stats.Passing}+ AV {stats.Armor}+";
    }


    private void AddTitle(string text)
    {
        var title = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 24);
        AddChild(title);
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
