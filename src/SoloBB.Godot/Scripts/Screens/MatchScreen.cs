using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public partial class MatchScreen : VBoxContainer
{
    private readonly Dictionary<PitchSquare, Button> _pitchButtons = [];
    private readonly Dictionary<Guid, Button> _rosterButtons = [];

    private Label _homeHudLabel = null!;
    private Label _turnHudLabel = null!;
    private Label _awayHudLabel = null!;
    private Label _summaryLabel = null!;
    private Label _lastEventLabel = null!;
    private Label _selectedLabel = null!;
    private VBoxContainer _rosterList = null!;
    private GridContainer _pitchGrid = null!;
    private HBoxContainer _blockDiceBox = null!;
    private HBoxContainer _interceptionChoiceBox = null!;
    private HBoxContainer _rerollChoiceBox = null!;
    private Button _doneButton = null!;
    private Ruleset _ruleset = null!;
    private MatchState _match = null!;
    private LeagueTeam _homeTeam = null!;
    private LeagueTeam _awayTeam = null!;
    private Guid? _selectedPlayerId;
    private Guid? _currentActivationPlayerId;
    private PitchSquare? _previewDestination;
    private IReadOnlyList<PitchSquare> _previewPath = [];
    private Guid? _previewBlockDefenderId;
    private Guid? _previewBlitzDefenderId;
    private PitchSquare? _previewBlitzDestination;
    private Guid? _previewFoulVictimId;
    private Guid? _previewPassReceiverId;
    private Func<MatchState, Task> _saveMatch = _ => Task.CompletedTask;

    private static readonly Color ScreenBackground = new("17211b");
    private static readonly Color PanelBackground = new("223128");
    private static readonly Color PitchGrass = new("3f7f46");
    private static readonly Color LegalPitchGrass = new("5d9960");
    private static readonly Color PreviewPathColor = new("c9b458");
    private static readonly Color DodgePathColor = new("d48b3d");
    private static readonly Color GoForItPathColor = new("b84a4a");
    private static readonly Color PickupPathColor = new("6ca6d9");
    private static readonly Color BlitzPathColor = new("d16f4c");
    private static readonly Color PushSquareColor = new("d6c15f");
    private static readonly Color BlockTargetColor = new("a33f3f");
    private static readonly Color AttackingAssistColor = new("4f9d5d");
    private static readonly Color DefendingAssistColor = new("c98b3f");
    private static readonly Color PassTargetColor = new("4d79c7");
    private static readonly Color InterceptorColor = new("8b5fbf");
    private static readonly Color EndZoneHome = new("274f7d");
    private static readonly Color EndZoneAway = new("7d3b34");
    private static readonly Color LineColor = new("f4f1df");
    private static readonly Color SelectedColor = new("f2c14e");
    private static readonly Color ReadyPlayerColor = new("2b3a31");
    private static readonly Color CurrentPlayerColor = new("4b4425");
    private static readonly Color ActivatedPlayerColor = new("303236");
    private static readonly Color UnavailablePlayerColor = new("252a27");

    public void Setup(
        Ruleset ruleset,
        MatchState match,
        LeagueTeam homeTeam,
        LeagueTeam awayTeam,
        Func<MatchState, Task> saveMatch,
        Action back)
    {
        Clear();

        _ruleset = ruleset;
        _match = match;
        _homeTeam = homeTeam;
        _awayTeam = awayTeam;
        _saveMatch = saveMatch;
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        _previewBlockDefenderId = null;
        _previewBlitzDefenderId = null;
        _previewBlitzDestination = null;
        _previewFoulVictimId = null;
        _previewPassReceiverId = null;

        AddThemeConstantOverride("separation", 6);
        AddThemeStyleboxOverride("panel", FlatStyle(ScreenBackground));

        AddTitle("Match Setup");
        AddChild(BuildMatchHud());

        _summaryLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_summaryLabel);

        _lastEventLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _lastEventLabel.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_lastEventLabel);

        var body = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 8);
        AddChild(body);

        body.AddChild(BuildRosterPanel());
        body.AddChild(BuildPitchPanel());

        var footer = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(footer);
        _blockDiceBox = new HBoxContainer();
        _blockDiceBox.AddThemeConstantOverride("separation", 4);
        footer.AddChild(_blockDiceBox);

        _interceptionChoiceBox = new HBoxContainer();
        _interceptionChoiceBox.AddThemeConstantOverride("separation", 4);
        footer.AddChild(_interceptionChoiceBox);

        _rerollChoiceBox = new HBoxContainer();
        _rerollChoiceBox.AddThemeConstantOverride("separation", 4);
        footer.AddChild(_rerollChoiceBox);

        _doneButton = new Button { Text = "Advance" };
        _doneButton.Pressed += async () => await CompleteCurrentStepAsync();
        footer.AddChild(_doneButton);

        var backButton = new Button { Text = "Back" };
        backButton.Pressed += back;
        footer.AddChild(backButton);

        RefreshRoster();
        RefreshPitch();
    }

    private Control BuildMatchHud()
    {
        var hud = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        _homeHudLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _homeHudLabel.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_homeHudLabel);

        _turnHudLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _turnHudLabel.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_turnHudLabel);

        _awayHudLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _awayHudLabel.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_awayHudLabel);

        return hud;
    }

    private Control BuildRosterPanel()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(220, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(PanelBackground, border: new Color("405044")));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 5);
        panel.AddChild(stack);

        stack.AddChild(new Label
        {
            Text = "Active Roster",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _selectedLabel = new Label
        {
            Text = "No player selected.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _selectedLabel.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(_selectedLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        stack.AddChild(scroll);

        _rosterList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rosterList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rosterList);

        return panel;
    }

    private Control BuildPitchPanel()
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("18261c"), border: new Color("41523f")));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 4);
        panel.AddChild(stack);

        _pitchGrid = new GridContainer
        {
            Columns = _ruleset.PitchWidth,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter
        };
        stack.AddChild(_pitchGrid);
        BuildPitchGrid();

        return panel;
    }

    private void BuildPitchGrid()
    {
        _pitchButtons.Clear();
        for (var y = 0; y < _ruleset.PitchHeight; y++)
        {
            for (var x = 0; x < _ruleset.PitchWidth; x++)
            {
                var square = new PitchSquare(x, y);
                var button = new Button
                {
                    Text = "",
                    CustomMinimumSize = new Vector2(21, 21),
                    TooltipText = $"{x + 1},{y + 1}",
                    FocusMode = FocusModeEnum.None
                };
                button.AddThemeFontSizeOverride("font_size", 10);
                button.Pressed += async () => await HandlePitchSquareAsync(square);
                _pitchButtons[square] = button;
                _pitchGrid.AddChild(button);
            }
        }
    }

    private void RefreshRoster()
    {
        foreach (var child in _rosterList.GetChildren())
        {
            child.QueueFree();
        }

        _rosterButtons.Clear();
        var activeTeam = ActiveTeam();
        foreach (var player in activeTeam.Players.OrderBy(player => player.Name, StringComparer.OrdinalIgnoreCase))
        {
            var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == player.Id);
            var state = placement?.Square is null ? "Reserve" : $"{placement.Square.X + 1},{placement.Square.Y + 1}";
            var marker = PlayerMarker(player.Id);
            var activationState = ActivationDisplayState(player.Id, placement);
            var button = new Button
            {
                Text = $"{marker}  {player.Name}  {state}  {activationState}",
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Disabled = !CanSelectPlayer(player.Id)
            };
            button.AddThemeFontSizeOverride("font_size", 11);
            button.TooltipText = FormatStats(player.Stats);
            var playerId = player.Id;
            button.Pressed += () => SelectPlayer(playerId);
            _rosterButtons[player.Id] = button;
            _rosterList.AddChild(button);
        }

        RefreshSelectionDisplay();
    }

    private async Task HandlePitchSquareAsync(PitchSquare square)
    {
        try
        {
            if (_match.PendingPush is not null)
            {
                await ChoosePushSquareAsync(square);
                return;
            }

            if (_match.Phase is MatchPhase.Kickoff)
            {
                await ResolveKickoffTargetAsync(square);
                return;
            }

            var occupied = _match.Placements.FirstOrDefault(placement => placement.Square == square);
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

                if (_selectedPlayerId is Guid passerId && IsLegalPassTarget(passerId, occupied.PlayerId))
                {
                    await HandlePassTargetAsync(passerId, occupied.PlayerId);
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

            var service = new MatchService();
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

        _previewDestination = square;
        _previewPath = BuildMovementPath(PlayerSquare(playerId)!, square);
        RefreshPitch();
    }

    private async Task ConfirmMoveAsync(Guid playerId, PitchSquare destination)
    {
        var beforeMatch = _match;
        var activeTeamBeforeMove = _match.ActiveTeamId;
        var path = _previewPath.ToArray();
        var movingTeam = ActiveTeam();
        var service = new MatchService();
        _match = service.MovePlayer(_match, _ruleset, movingTeam, playerId, destination);
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
        var activeTeamBeforeBlock = _match.ActiveTeamId;
        var service = new MatchService();
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
        var activeTeamBeforeFoul = _match.ActiveTeamId;
        var service = new MatchService();
        _match = service.FoulPlayer(_match, _ruleset, ActiveTeam(), foulerId, OpponentTeam(), victimId);
        _previewFoulVictimId = null;

        if (_match.ActiveTeamId == activeTeamBeforeFoul && IsPlayerTurnPhase())
        {
            _selectedPlayerId = foulerId;
            _currentActivationPlayerId = foulerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmBlitzAsync(Guid attackerId, Guid defenderId, PitchSquare destination)
    {
        var beforeMatch = _match;
        var activeTeamBeforeBlitz = _match.ActiveTeamId;
        var path = _previewPath.ToArray();
        var service = new MatchService();
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
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ChooseBlockDieAsync(int roll)
    {
        if (_match.PendingBlock is not PendingBlockChoice pending)
        {
            return;
        }

        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var attackerTeam = TeamById(pending.AttackerTeamId);
        var defenderTeam = TeamById(pending.DefenderTeamId);
        var service = new MatchService();
        _match = service.ChooseBlockDie(_match, _ruleset, attackerTeam, defenderTeam, roll);
        _previewBlockDefenderId = null;

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = pending.AttackerPlayerId;
            _currentActivationPlayerId = pending.AttackerPlayerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ChoosePushSquareAsync(PitchSquare square)
    {
        if (_match.PendingPush is not PendingPushChoice pending)
        {
            return;
        }

        if (!pending.LegalSquares.Contains(square))
        {
            _summaryLabel.Text = "Choose one of the highlighted push squares.";
            return;
        }

        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = new MatchService();
        _match = service.ChoosePushSquare(_match, _ruleset, TeamById(pending.AttackerTeamId), TeamById(pending.DefenderTeamId), square);

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = pending.AttackerPlayerId;
            _currentActivationPlayerId = pending.AttackerPlayerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task HandlePassTargetAsync(Guid passerId, Guid receiverId)
    {
        if (_previewPassReceiverId == receiverId)
        {
            await ConfirmPassAsync(passerId, receiverId);
            return;
        }

        _previewPassReceiverId = receiverId;
        _previewBlockDefenderId = null;
        _previewFoulVictimId = null;
        _previewDestination = null;
        _previewPath = [];
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmPassAsync(Guid passerId, Guid receiverId)
    {
        var activeTeamBeforePass = _match.ActiveTeamId;
        var service = new MatchService();
        _match = service.PassBall(_match, _ruleset, ActiveTeam(), passerId, receiverId, OpponentTeam());
        _previewPassReceiverId = null;

        if (_match.ActiveTeamId == activeTeamBeforePass && IsPlayerTurnPhase())
        {
            _selectedPlayerId = passerId;
            _currentActivationPlayerId = passerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ChooseInterceptorAsync(Guid interceptorId)
    {
        if (_match.PendingInterception is not PendingInterceptionChoice pending)
        {
            return;
        }

        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = new MatchService();
        _match = service.ChooseInterceptor(_match, _ruleset, TeamById(pending.PassingTeamId), TeamById(pending.DefendingTeamId), interceptorId);
        _previewPassReceiverId = null;

        if (_match.ActiveTeamId == activeTeamBeforeChoice && IsPlayerTurnPhase())
        {
            _selectedPlayerId = pending.PasserPlayerId;
            _currentActivationPlayerId = pending.PasserPlayerId;
        }
        else
        {
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
        }

        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ResolveRerollAsync(bool useTeamReroll, string? skillId = null)
    {
        if (_match.PendingReroll is not PendingRerollChoice pending)
        {
            return;
        }

        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = new MatchService();
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

        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task AnimateMovementAsync(MatchState beforeMatch, MatchState afterMatch, Guid playerId, IReadOnlyList<PitchSquare> path)
    {
        if (path.Count == 0)
        {
            return;
        }

        var finalSquare = afterMatch.Placements.FirstOrDefault(placement => placement.PlayerId == playerId)?.Square;
        foreach (var square in path)
        {
            _match = beforeMatch with
            {
                Placements = beforeMatch.Placements
                    .Select(placement => placement.PlayerId == playerId
                        ? placement with { Square = square }
                        : placement)
                    .ToArray()
            };
            RefreshPitch();
            await Task.Delay(70);
            if (finalSquare == square)
            {
                break;
            }
        }

        _match = afterMatch;
    }

    private async Task ResolveKickoffTargetAsync(PitchSquare square)
    {
        var receivingTeam = ActiveTeam();
        var service = new MatchService();
        _match = service.ResolveKickoff(_match, _ruleset, receivingTeam, square);
        _selectedPlayerId = null;
        _currentActivationPlayerId = null;
        ClearPreview();
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task CompleteCurrentStepAsync()
    {
        try
        {
            if (!CanAdvanceCurrentStep())
            {
                _summaryLabel.Text = AdvanceBlockedMessage();
                return;
            }

            var service = new MatchService();
            _match = _match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn
                ? service.AdvanceTurn(_match, _ruleset)
                : service.AdvancePhase(_match);
            _selectedPlayerId = null;
            _currentActivationPlayerId = null;
            ClearPreview();
            await _saveMatch(_match);
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Turn control failed: {ex.Message}";
        }
    }

    private void SelectPlayer(Guid playerId)
    {
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (placement is null)
        {
            return;
        }

        if (!CanSelectPlayer(playerId))
        {
            _summaryLabel.Text = HasCurrentTurnActivation(playerId)
                ? $"{FindPlayer(playerId)?.Name ?? "That player"} has already activated this turn."
                : "That player cannot be selected right now.";
            return;
        }

        _selectedPlayerId = playerId;
        if (IsPlayerTurnPhase())
        {
            _currentActivationPlayerId = playerId;
        }

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

    private void RefreshPitch()
    {
        RefreshMatchHud();
        RefreshBlockDiceChoice();
        RefreshInterceptionChoice();
        RefreshRerollChoice();
        foreach (var (square, button) in _pitchButtons)
        {
            var canPlace = IsLegalPlacementTarget(square);
            var canTargetKickoff = IsLegalKickoffTarget(square);
            var canMove = _selectedPlayerId is Guid movingPlayerId && IsLegalMovementTarget(movingPlayerId, square);
            var canPush = IsLegalPushSquare(square);
            var isPreview = _previewPath.Contains(square);
            var pathMarker = canPush ? ">" : MovementPathMarker(square);
            button.Text = "";
            button.Disabled = !canPlace && !canTargetKickoff && !canMove && !canPush;
            button.TooltipText = canPush
                ? "Push here"
                : canPlace || canTargetKickoff || canMove
                ? MovementTooltip(square, pathMarker)
                : "";
            ApplySquareStyle(button, square, isSelected: false, canUse: canPlace || canTargetKickoff || canMove || canPush, pathMarker);
            if (isPreview || canPush)
            {
                button.Text = pathMarker ?? (_previewDestination == square ? "X" : ".");
            }
        }

        foreach (var placement in _match.Placements.Where(placement => placement.Square is not null))
        {
            if (!_pitchButtons.TryGetValue(placement.Square!, out var button))
            {
                continue;
            }

            var isSelected = placement.PlayerId == _selectedPlayerId;
            button.Text = PlayerMarker(placement.PlayerId);
            button.TooltipText = PlayerPitchTooltip(placement);
            var canBlockTarget = _selectedPlayerId is Guid attackerId && IsLegalBlockTarget(attackerId, placement.PlayerId);
            var canBlitzTarget = _selectedPlayerId is Guid blitzerId && IsLegalBlitzTarget(blitzerId, placement.PlayerId);
            var canPassTarget = _selectedPlayerId is Guid passerId && IsLegalPassTarget(passerId, placement.PlayerId);
            var canFoulTarget = _selectedPlayerId is Guid foulerId && IsLegalFoulTarget(foulerId, placement.PlayerId);
            var canPushTarget = IsLegalPushSquare(placement.Square!);
            button.Disabled = !CanSelectPlayer(placement.PlayerId) && !canBlockTarget && !canBlitzTarget && !canPassTarget && !canFoulTarget && !canPushTarget;
            ApplySquareStyle(
                button,
                placement.Square!,
                isSelected,
                canUse: canBlockTarget || canBlitzTarget || canPassTarget || canFoulTarget || canPushTarget,
                pathMarker: canPushTarget ? ">" : null,
                blockRole: BlockPreviewRole(placement.PlayerId),
                passRole: PassPreviewRole(placement.PlayerId));
        }

        if (_match.Ball.Square is PitchSquare ballSquare && _pitchButtons.TryGetValue(ballSquare, out var ballButton))
        {
            ballButton.Text = "o";
            ballButton.TooltipText = "Ball";
        }

        if (_match.Ball.CarrierPlayerId is Guid carrierId &&
            _match.Placements.FirstOrDefault(placement => placement.PlayerId == carrierId)?.Square is PitchSquare carrierSquare &&
            _pitchButtons.TryGetValue(carrierSquare, out var carrierButton))
        {
            carrierButton.Text = $"{PlayerMarker(carrierId)} o";
            carrierButton.TooltipText = $"{FindPlayer(carrierId)?.Name ?? "Ball carrier"} with ball";
        }

        var activeTeam = ActiveTeam();
        var selected = _selectedPlayerId is Guid playerId ? FindPlayer(playerId)?.Name : "none";
        _doneButton.Disabled = !CanAdvanceCurrentStep();
        _doneButton.Text = AdvanceButtonText();
        _doneButton.TooltipText = _doneButton.Disabled ? AdvanceBlockedMessage() : "Advance the current phase or turn.";

        _summaryLabel.Text = _match.Phase switch
        {
            _ when _match.PendingReroll is PendingRerollChoice pending => RerollSummary(pending),
            _ when _match.PendingBlock is PendingBlockChoice pending => $"Choose a block die for {FindPlayer(pending.AttackerPlayerId)?.Name ?? "attacker"}'s block.",
            _ when _match.PendingPush is PendingPushChoice pending => $"Choose a push square for {FindPlayer(pending.DefenderPlayerId)?.Name ?? "defender"}.",
            _ when _match.PendingInterception is PendingInterceptionChoice pending => $"Choose an interceptor for the {pending.PassRangeName} pass.",
            MatchPhase.DefenseSetup => $"{activeTeam.Name} is kicking off and places players first. Selected: {selected}.",
            MatchPhase.OffenseSetup => $"{activeTeam.Name} is receiving the kick and places players. Selected: {selected}.",
            MatchPhase.Kickoff => $"{KickingTeam().Name} is kicking. Select a target square in {activeTeam.Name}'s half.",
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewBlockDefenderId is Guid defenderId => BlockPreviewSummary(defenderId),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewBlitzDefenderId is Guid blitzDefenderId => BlitzPreviewSummary(blitzDefenderId),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewFoulVictimId is Guid victimId => FoulPreviewSummary(victimId),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewPassReceiverId is Guid receiverId => PassPreviewSummary(receiverId),
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn when _previewDestination is not null => $"{activeTeam.Name} active. Click {_previewDestination.X + 1},{_previewDestination.Y + 1} again to confirm movement. Current activation: {selected}.",
            MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn => $"{activeTeam.Name} active. Ready players can still activate; spent players are disabled. Current activation: {selected}.",
            _ => $"{activeTeam.Name} active. Phase: {_match.Phase}. Selected: {selected}."
        };
        _lastEventLabel.Text = _match.Log.LastOrDefault()?.Message ?? "No match events yet.";
    }

    private void RefreshMatchHud()
    {
        _homeHudLabel.Text = FormatTeamHud(_homeTeam, _match.HomeScore, _match.HomeRerollsRemaining);
        _awayHudLabel.Text = FormatTeamHud(_awayTeam, _match.AwayScore, _match.AwayRerollsRemaining);
        _turnHudLabel.Text = $"Half {_match.Half}  {PhaseLabel(_match.Phase)}  Turn {_match.Turn}/{_ruleset.TurnsPerHalf}";
        _turnHudLabel.TooltipText = $"{ActiveTeam().Name} active. Home turn {_match.HomeTurn}, away turn {_match.AwayTurn}.";
    }

    private static string FormatTeamHud(LeagueTeam team, int score, int rerollsRemaining)
    {
        return $"{team.Name}  Score {score}  RR {rerollsRemaining}";
    }

    private bool CanAdvanceCurrentStep()
    {
        if (_match.PendingReroll is not null ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingInterception is not null)
        {
            return false;
        }

        return _match.Phase is MatchPhase.DefenseSetup or
            MatchPhase.OffenseSetup or
            MatchPhase.OffensivePlayerTurn or
            MatchPhase.DefensiveTurn;
    }

    private string AdvanceButtonText()
    {
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

        if (_match.PendingBlock is not null)
        {
            return "Choose a block die first.";
        }

        if (_match.PendingPush is not null)
        {
            return "Choose a push square first.";
        }

        if (_match.PendingInterception is not null)
        {
            return "Choose an interceptor first.";
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
        var pathColor = pathMarker switch
        {
            not null when pathMarker.EndsWith("+", StringComparison.Ordinal) && MovementPathNeedsDodge(square) => DodgePathColor,
            not null when pathMarker.EndsWith("+", StringComparison.Ordinal) && MovementPathNeedsPickup(square) => PickupPathColor,
            not null when pathMarker.EndsWith("+", StringComparison.Ordinal) => GoForItPathColor,
            ">" => PushSquareColor,
            "." or "X" => _previewBlitzDestination is not null ? BlitzPathColor : PreviewPathColor,
            _ => (Color?)null
        };
        var blockColor = blockRole switch
        {
            "target" => BlockTargetColor,
            "attackAssist" => AttackingAssistColor,
            "defenseAssist" => DefendingAssistColor,
            _ => (Color?)null
        };
        var passColor = passRole switch
        {
            "receiver" => PassTargetColor,
            "interceptor" => InterceptorColor,
            _ => (Color?)null
        };
        var baseColor = passColor ?? blockColor ?? pathColor ?? (canUse ? SquareColor(square).Lerp(LegalPitchGrass, 0.35f) : SquareColor(square));
        var style = FlatStyle(baseColor, border: SquareBorderColor(square), borderWidth: 1);
        var hasPitchLine = square.X == (_ruleset.PitchWidth / 2) - 1 ||
            square.X == _ruleset.PitchWidth / 2 ||
            square.Y == 3 ||
            square.Y == _ruleset.PitchHeight - 4;
        if (hasPitchLine)
        {
            style.BorderColor = LineColor;
            style.SetBorderWidthAll(0);
        }

        if (square.X == (_ruleset.PitchWidth / 2) - 1)
        {
            style.SetBorderWidth(Side.Right, 4);
        }
        else if (square.X == _ruleset.PitchWidth / 2)
        {
            style.SetBorderWidth(Side.Left, 4);
        }

        if (square.Y == 3)
        {
            style.SetBorderWidth(Side.Bottom, 4);
        }
        else if (square.Y == _ruleset.PitchHeight - 4)
        {
            style.SetBorderWidth(Side.Top, 4);
        }

        if (isSelected)
        {
            style.BorderColor = SelectedColor;
            style.SetBorderWidthAll(3);
        }
        else if (blockRole is not null)
        {
            style.BorderColor = blockRole == "target" ? new Color("f0d0c8") : new Color("f3e2a8");
            style.SetBorderWidthAll(blockRole == "target" ? 3 : 2);
        }
        else if (passRole is not null)
        {
            style.BorderColor = passRole == "receiver" ? new Color("d5e6ff") : new Color("ead7ff");
            style.SetBorderWidthAll(passRole == "receiver" ? 3 : 2);
        }

        button.AddThemeStyleboxOverride("normal", style);
        button.AddThemeStyleboxOverride("disabled", style);
        button.AddThemeStyleboxOverride("hover", canUse
            ? FlatStyle(baseColor.Lightened(0.12f), border: SelectedColor, borderWidth: 2)
            : style);
        button.AddThemeStyleboxOverride("pressed", canUse
            ? FlatStyle(baseColor.Darkened(0.12f), border: SelectedColor, borderWidth: 2)
            : style);
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

        return IsActiveTeamSide(square);
    }

    private bool CanSelectPlayer(Guid playerId)
    {
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (placement is null)
        {
            return false;
        }

        if (placement.State is PlayerPitchState.Casualty or PlayerPitchState.SentOff)
        {
            return false;
        }

        if (placement.TeamId != _match.ActiveTeamId)
        {
            return false;
        }

        if (_match.Phase is MatchPhase.DefenseSetup or MatchPhase.OffenseSetup)
        {
            return true;
        }

        if (!IsPlayerTurnPhase())
        {
            return false;
        }

        if (_match.PendingReroll is PendingRerollChoice pendingReroll && pendingReroll.PlayerId != playerId)
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

        return !HasCurrentTurnActivation(playerId) || _currentActivationPlayerId == playerId;
    }

    private string ActivationDisplayState(Guid playerId, PlayerPlacement? placement)
    {
        if (placement?.State is PlayerPitchState.Casualty or PlayerPitchState.SentOff)
        {
            return "Unavailable";
        }

        if (!IsPlayerTurnPhase())
        {
            return placement?.Square is null ? "Reserve" : "Available";
        }

        if (placement?.Square is null ||
            placement.State is not (PlayerPitchState.Standing or PlayerPitchState.Prone))
        {
            return "Unavailable";
        }

        if (_currentActivationPlayerId == playerId)
        {
            return "Current";
        }

        return HasCurrentTurnActivation(playerId) ? "Activated" : "Ready";
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
        var playerName = FindPlayer(placement.PlayerId)?.Name ?? "Unknown";
        return IsPlayerTurnPhase()
            ? $"{playerName} - {ActivationDisplayState(placement.PlayerId, placement)}"
            : playerName;
    }

    private bool IsPlayerTurnPhase()
    {
        return _match.Phase is MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn;
    }

    private bool HasCurrentTurnActivation(Guid playerId)
    {
        return _match.Activations.Any(activation =>
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
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingInterception is not null ||
            _match.PendingReroll is not null ||
            HasCurrentTurnActivation(attackerId) ||
            HasUsedBlitz(_match.ActiveTeamId))
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
            defenderPlacement.State != PlayerPitchState.Standing ||
            (attackerPlacement.State == PlayerPitchState.Standing && IsAdjacent(attackerSquare, defenderSquare)))
        {
            return false;
        }

        return FindBlitzDestination(attackerId, defenderId) is not null;
    }

    private bool IsLegalFoulTarget(Guid foulerId, Guid victimId)
    {
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingReroll is not null ||
            HasCurrentTurnActivation(foulerId) ||
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
        return $"Block preview: {preview.Dice} die{(preview.Dice == 1 ? "" : "s")}, ST {preview.AttackerStrength}-{preview.DefenderStrength} ({strengthLeader}). Click {defenderName} again to roll.";
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

        return $"Blitz preview: move to {destination.X + 1},{destination.Y + 1}, then {preview.Dice} die{(preview.Dice == 1 ? "" : "s")} block, ST {preview.AttackerStrength}-{preview.DefenderStrength}. Click {defenderName} again to blitz.";
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
                Text = roll.ToString(),
                CustomMinimumSize = new Vector2(34, 28)
            };
            button.TooltipText = BlockDieTooltip(roll);
            button.Pressed += async () => await ChooseBlockDieAsync(roll);
            _blockDiceBox.AddChild(button);
        }
    }

    private static string BlockDieTooltip(int roll)
    {
        return roll switch
        {
            <= 1 => "Attacker down",
            <= 3 => "Push back",
            _ => "Defender down"
        };
    }

    private bool IsLegalPassTarget(Guid passerId, Guid receiverId)
    {
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingInterception is not null ||
            passerId == receiverId ||
            HasCurrentTurnActivation(passerId) ||
            HasUsedPass(_match.ActiveTeamId) ||
            _match.Ball.CarrierPlayerId != passerId)
        {
            return false;
        }

        var passerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == passerId);
        var receiverPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == receiverId);
        if (passerPlacement?.TeamId != _match.ActiveTeamId ||
            receiverPlacement?.TeamId != _match.ActiveTeamId ||
            passerPlacement.Square is null ||
            receiverPlacement.Square is null ||
            passerPlacement.State != PlayerPitchState.Standing ||
            receiverPlacement.State != PlayerPitchState.Standing)
        {
            return false;
        }

        return ResolvePassRange(passerPlacement.Square, receiverPlacement.Square) is not null;
    }

    private PassPreview? ResolvePassPreview(Guid receiverId)
    {
        if (_selectedPlayerId is not Guid passerId)
        {
            return null;
        }

        var passer = FindPlayer(passerId);
        var receiver = FindPlayer(receiverId);
        var passerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == passerId);
        var receiverPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == receiverId);
        if (passer is null || receiver is null || passerPlacement?.Square is null || receiverPlacement?.Square is null)
        {
            return null;
        }

        var passRange = ResolvePassRange(passerPlacement.Square, receiverPlacement.Square);
        if (passRange is null)
        {
            return null;
        }

        var interceptors = FindEligibleInterceptors(OpponentTeam().Id, passerPlacement.Square, receiverPlacement.Square);
        return new PassPreview(
            passRange.Value.Name,
            PassingTarget(passer, passRange.Value.TargetModifier),
            CatchTarget(receiver),
            interceptors.Select(placement => placement.PlayerId).ToArray());
    }

    private string? PassPreviewRole(Guid playerId)
    {
        if (_match.PendingInterception is PendingInterceptionChoice pending && pending.EligiblePlayerIds.Contains(playerId))
        {
            return "interceptor";
        }

        if (_previewPassReceiverId is not Guid receiverId)
        {
            return null;
        }

        if (playerId == receiverId)
        {
            return "receiver";
        }

        var preview = ResolvePassPreview(receiverId);
        return preview?.EligibleInterceptorPlayerIds.Contains(playerId) == true ? "interceptor" : null;
    }

    private string PassPreviewSummary(Guid receiverId)
    {
        var receiverName = FindPlayer(receiverId)?.Name ?? "receiver";
        var preview = ResolvePassPreview(receiverId);
        if (preview is null)
        {
            return $"Click {receiverName} again to confirm the pass.";
        }

        var interceptionText = preview.EligibleInterceptorPlayerIds.Count == 0
            ? "no eligible interceptors"
            : $"{preview.EligibleInterceptorPlayerIds.Count} eligible interceptor{(preview.EligibleInterceptorPlayerIds.Count == 1 ? "" : "s")}";
        return $"Pass preview: {preview.RangeName} pass {preview.PassTarget}+, catch {preview.CatchTarget}+, {interceptionText}. Click {receiverName} again to throw.";
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
            button.TooltipText = player is null
                ? "Choose interceptor"
                : $"{player.Name} - intercept {InterceptionTarget(player)}+";
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

        if (_match.PendingReroll is not PendingRerollChoice pending)
        {
            _rerollChoiceBox.Visible = false;
            return;
        }

        _blockDiceBox.Visible = false;
        _interceptionChoiceBox.Visible = false;
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

    private bool HasUsedPass(Guid teamId)
    {
        return _match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == _match.Half &&
            activation.Turn == _match.Turn &&
            activation.Action == PlayerTurnAction.Pass);
    }

    private bool HasUsedBlitz(Guid teamId)
    {
        return _match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == _match.Half &&
            activation.Turn == _match.Turn &&
            activation.Action == PlayerTurnAction.Blitz);
    }

    private bool HasUsedFoul(Guid teamId)
    {
        return _match.Activations.Any(activation =>
            activation.TeamId == teamId &&
            activation.Half == _match.Half &&
            activation.Turn == _match.Turn &&
            activation.Action == PlayerTurnAction.Foul);
    }

    private int TeamRerollsRemaining(Guid teamId)
    {
        return teamId == _match.HomeTeamId ? _match.HomeRerollsRemaining : _match.AwayRerollsRemaining;
    }

    private bool IsLegalMovementTarget(Guid playerId, PitchSquare square)
    {
        if (_match.Phase is not (MatchPhase.OffensivePlayerTurn or MatchPhase.DefensiveTurn))
        {
            return false;
        }

        if (_match.PendingPush is not null)
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

        if (HasCurrentTurnActivation(playerId))
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
        var movementAllowance = placement.State == PlayerPitchState.Prone
            ? Math.Max(0, player.Stats.Movement - 3)
            : player.Stats.Movement;
        return (path.Count > 0 || placement.State == PlayerPitchState.Prone) &&
            path.Count <= movementAllowance + 3 &&
            path.All(pathSquare => !_match.Placements.Any(current => current.PlayerId != playerId && current.Square == pathSquare));
    }

    private bool IsLegalPushSquare(PitchSquare square)
    {
        return _match.PendingPush?.LegalSquares.Contains(square) == true;
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

        var target = MovementStepTarget(playerId, stepIndex);
        if (target is not null)
        {
            return $"{target}+";
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
            notes.Add("Go-for-it 2+ (fails on natural 1)");
        }

        if (MovementStepNeedsPickup(stepIndex))
        {
            var player = FindPlayer(playerId);
            if (player is not null)
            {
                var tackleZones = CountOpposingTackleZones(_match, _match.ActiveTeamId, playerId, square);
                notes.Add($"Pickup {PickupTarget(player, tackleZones)}+ (AG base {player.Stats.Agility}+, +1 pickup, -{tackleZones} tackle zone{(tackleZones == 1 ? "" : "s")})");
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
            targets.Add(2);
        }

        if (MovementStepNeedsPickup(stepIndex))
        {
            var tackleZones = CountOpposingTackleZones(_match, _match.ActiveTeamId, playerId, _previewPath[stepIndex]);
            targets.Add(PickupTarget(player, tackleZones));
        }

        return targets.Count == 0 ? null : targets.Max();
    }

    private bool MovementPathNeedsDodge(PitchSquare square)
    {
        if (_selectedPlayerId is not Guid playerId)
        {
            return false;
        }

        var stepIndex = MovementStepIndex(square);
        return stepIndex >= 0 && MovementStepNeedsDodge(playerId, stepIndex);
    }

    private bool MovementPathNeedsPickup(PitchSquare square)
    {
        var stepIndex = MovementStepIndex(square);
        return stepIndex >= 0 && MovementStepNeedsPickup(stepIndex);
    }

    private bool MovementStepNeedsGoForIt(Guid playerId, int stepIndex)
    {
        var player = FindPlayer(playerId);
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (player is null || placement is null)
        {
            return false;
        }

        var movementAllowance = placement.State == PlayerPitchState.Prone
            ? Math.Max(0, player.Stats.Movement - 3)
            : player.Stats.Movement;
        return stepIndex >= movementAllowance;
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
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare opponentSquare &&
            Math.Max(Math.Abs(opponentSquare.X - square.X), Math.Abs(opponentSquare.Y - square.Y)) == 1);
    }

    private static bool IsMarkedByOpponent(MatchState match, Guid teamId, Guid playerId, PitchSquare square, Guid ignoredOpponentId)
    {
        return match.Placements.Any(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            placement.PlayerId != ignoredOpponentId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare opponentSquare &&
            Math.Max(Math.Abs(opponentSquare.X - square.X), Math.Abs(opponentSquare.Y - square.Y)) == 1);
    }

    private static int CountOpposingTackleZones(MatchState match, Guid teamId, Guid playerId, PitchSquare square)
    {
        return match.Placements.Count(placement =>
            placement.TeamId != teamId &&
            placement.PlayerId != playerId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare opponentSquare &&
            Math.Max(Math.Abs(opponentSquare.X - square.X), Math.Abs(opponentSquare.Y - square.Y)) == 1);
    }

    private static int DodgeTarget(Player player, int opposingTackleZones)
    {
        return Math.Clamp(player.Stats.Agility - 1 + opposingTackleZones, 2, 6);
    }

    private static int PickupTarget(Player player, int opposingTackleZones)
    {
        return Math.Clamp(player.Stats.Agility - 1 + opposingTackleZones, 2, 6);
    }

    private static int CatchTarget(Player player)
    {
        return Math.Clamp(player.Stats.Agility, 2, 6);
    }

    private static int InterceptionTarget(Player player)
    {
        return Math.Clamp(player.Stats.Agility + 2, 2, 6);
    }

    private static int PassingTarget(Player player, int rangeModifier)
    {
        return Math.Max(2, player.Stats.Passing + rangeModifier);
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
        var lineX = receiverSquare.X - passerSquare.X;
        var lineY = receiverSquare.Y - passerSquare.Y;
        var squareX = square.X - passerSquare.X;
        var squareY = square.Y - passerSquare.Y;
        var lengthSquared = (lineX * lineX) + (lineY * lineY);

        if (lengthSquared == 0)
        {
            return false;
        }

        var projection = ((squareX * lineX) + (squareY * lineY)) / (double)lengthSquared;
        if (projection <= 0 || projection >= 1)
        {
            return false;
        }

        var closestX = passerSquare.X + (projection * lineX);
        var closestY = passerSquare.Y + (projection * lineY);
        var distanceX = square.X - closestX;
        var distanceY = square.Y - closestY;
        var distance = Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY));

        return distance <= 0.5;
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
    }

    private bool IsActiveTeamSide(PitchSquare square)
    {
        return _match.ActiveTeamId == _match.HomeTeamId
            ? square.X < _ruleset.PitchWidth / 2
            : square.X >= _ruleset.PitchWidth / 2;
    }

    private bool IsWideZone(PitchSquare square)
    {
        return square.Y < 4 || square.Y >= _ruleset.PitchHeight - 4;
    }

    private int CountActiveTeamWideZonePlayers(PitchSquare square)
    {
        return _match.Placements.Count(placement =>
            placement.PlayerId != _selectedPlayerId &&
            placement.TeamId == _match.ActiveTeamId &&
            placement.State == PlayerPitchState.Standing &&
            placement.Square is PitchSquare placedSquare &&
            IsSameWideZone(square, placedSquare));
    }

    private bool IsSameWideZone(PitchSquare first, PitchSquare second)
    {
        return (first.Y < 4 && second.Y < 4) ||
            (first.Y >= _ruleset.PitchHeight - 4 && second.Y >= _ruleset.PitchHeight - 4);
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

    private Player? FindPlayer(Guid playerId)
    {
        return _homeTeam.Players.Concat(_awayTeam.Players).FirstOrDefault(player => player.Id == playerId);
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

    private static StyleBoxFlat FlatStyle(Color background, Color? border = null, int borderWidth = 1)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border ?? background.Darkened(0.25f)
        };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(3);
        return style;
    }

    private static string FormatStats(PlayerStats stats)
    {
        return $"MA {stats.Movement} ST {stats.Strength} AG {stats.Agility}+ PA {stats.Passing}+ AV {stats.Armor}+";
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
        return square.X >= 0 && square.X < _ruleset.PitchWidth && square.Y >= 0 && square.Y < _ruleset.PitchHeight;
    }

    private static IEnumerable<PitchSquare> AdjacentSquares(PitchSquare square)
    {
        for (var y = square.Y - 1; y <= square.Y + 1; y++)
        {
            for (var x = square.X - 1; x <= square.X + 1; x++)
            {
                if (x == square.X && y == square.Y)
                {
                    continue;
                }

                yield return new PitchSquare(x, y);
            }
        }
    }

    private static string FormatRerollKind(PendingRerollKind kind)
    {
        return kind switch
        {
            PendingRerollKind.GoForIt => "go-for-it",
            _ => kind.ToString().ToLowerInvariant()
        };
    }

    private static PassRangePreview? ResolvePassRange(PitchSquare passerSquare, PitchSquare receiverSquare)
    {
        var distance = Math.Max(
            Math.Abs(passerSquare.X - receiverSquare.X),
            Math.Abs(passerSquare.Y - receiverSquare.Y));

        return distance switch
        {
            <= 3 => new PassRangePreview("quick", 0),
            <= 6 => new PassRangePreview("short", 1),
            <= 9 => new PassRangePreview("long", 2),
            <= 13 => new PassRangePreview("long bomb", 3),
            _ => null
        };
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

    private sealed record BlockPreview(
        int AttackerStrength,
        int DefenderStrength,
        int Dice,
        IReadOnlyList<Guid> AttackerAssistPlayerIds,
        IReadOnlyList<Guid> DefenderAssistPlayerIds);

    private sealed record FoulAssistPreview(int AttackingAssists, int DefendingAssists);

    private readonly record struct PassRangePreview(string Name, int TargetModifier);

    private sealed record PassPreview(
        string RangeName,
        int PassTarget,
        int CatchTarget,
        IReadOnlyList<Guid> EligibleInterceptorPlayerIds);
}
