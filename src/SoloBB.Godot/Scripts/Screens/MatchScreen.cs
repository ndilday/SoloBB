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
    private readonly Dictionary<PitchSquare, Button> _pitchButtons = [];
    private readonly Dictionary<PitchSquare, TextureRect> _pitchTileLayers = [];
    private readonly Dictionary<PitchSquare, TextureRect> _pitchHighlightLayers = [];
    private readonly Dictionary<PitchSquare, TextureRect> _pitchMarkingLayers = [];
    private readonly Dictionary<PitchSquare, TextureRect> _pitchPieceLayers = [];
    private readonly Dictionary<PitchSquare, TextureRect> _pitchOverlayLayers = [];
    private readonly Dictionary<Guid, Button> _rosterButtons = [];

    private Label _homeHudLabel = null!;
    private Label _turnHudLabel = null!;
    private Label _awayHudLabel = null!;
    private Label _decisionTitleLabel = null!;
    private Label _summaryLabel = null!;
    private Label _decisionDetailLabel = null!;
    private Label _lastEventLabel = null!;
    private Label _selectedLabel = null!;
    private VBoxContainer _eventLogList = null!;
    private VBoxContainer _rosterList = null!;
    private Control _pitchViewport = null!;
    private GridContainer _pitchGrid = null!;
    private HBoxContainer _blockDiceBox = null!;
    private HBoxContainer _interceptionChoiceBox = null!;
    private HBoxContainer _rerollChoiceBox = null!;
    private HBoxContainer _apothecaryChoiceBox = null!;
    private HBoxContainer _standFirmChoiceBox = null!;
    private HBoxContainer _divingTackleChoiceBox = null!;
    private HBoxContainer _sendOffChoiceBox = null!;
    private HBoxContainer _setupChoiceBox = null!;
    private Button _passModeButton = null!;
    private Button _blitzModeButton = null!;
    private Button _throwTeamMateModeButton = null!;
    private Button _kickTeamMateModeButton = null!;
    private Button _doneButton = null!;
    private Texture2D? _humanSpriteSheet;
    private Texture2D? _orcSpriteSheet;
    private Texture2D? _pitchObjectSheet;
    private Texture2D? _blockDiceSheet;
    private Texture2D? _pitchTileSheet;
    private Texture2D? _pitchFieldSheet;
    private Texture2D? _pitchMarkingSheet;
    private readonly Dictionary<string, Texture2D?> _atlasCache = [];
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
    private PitchSquare? _previewPassTargetSquare;
    private Guid? _previewLaunchedPlayerId;
    private PitchSquare? _previewLaunchTargetSquare;
    private PitchSquare? _animationBallSquare;
    private bool _isAnimating;
    private bool _passMode;
    private bool _blitzMode;
    private bool _throwTeamMateMode;
    private bool _kickTeamMateMode;
    private bool _isPitchDragging;
    private bool _pitchZoomInitialized;
    private bool _endTurnConfirmationArmed;
    private float _pitchZoom = 1.0f;
    private Vector2 _pitchPan = Vector2.Zero;
    private Func<MatchState, Task> _saveMatch = _ => Task.CompletedTask;

    private const float BasePitchSquareSize = 32.0f;
    private const float PitchSquareOverlap = 1.0f;
    private const float MinPitchZoom = 0.75f;
    private const float MaxPitchZoom = 2.5f;
    private const float PitchZoomStep = 1.12f;
    private const float KeyboardPanStep = 42.0f;

    private static readonly Color ScreenBackground = new("161817");
    private static readonly Color PanelBackground = new("1f3a35");
    private static readonly Color PitchGrass = new("4e8a50");
    private static readonly Color LegalPitchGrass = new("6ca264");
    private static readonly Color PreviewPathColor = new("d8a93a");
    private static readonly Color DodgePathColor = new("d48b3d");
    private static readonly Color GoForItPathColor = new("d98532");
    private static readonly Color PickupPathColor = new("6ca6d9");
    private static readonly Color BlitzPathColor = new("d16f4c");
    private static readonly Color PushSquareColor = new("d6c15f");
    private static readonly Color BlockTargetColor = new("a33f3f");
    private static readonly Color AttackingAssistColor = new("4f9d5d");
    private static readonly Color DefendingAssistColor = new("c98b3f");
    private static readonly Color PassTargetColor = new("4d79c7");
    private static readonly Color InterceptorColor = new("8b5fbf");
    private static readonly Color EndZoneHome = new("235b83");
    private static readonly Color EndZoneAway = new("7c333a");
    private static readonly Color LineColor = new("e9e1c8");
    private static readonly Color SelectedColor = new("d8a93a");
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
        _previewPassTargetSquare = null;
        _previewLaunchedPlayerId = null;
        _previewLaunchTargetSquare = null;
        _animationBallSquare = null;
        _passMode = false;
        _blitzMode = false;
        _throwTeamMateMode = false;
        _kickTeamMateMode = false;
        _isPitchDragging = false;
        _pitchZoomInitialized = false;
        _endTurnConfirmationArmed = false;
        _pitchZoom = 1.0f;
        _pitchPan = Vector2.Zero;
        LoadSpriteAssets();

        AddThemeConstantOverride("separation", 6);
        AddThemeStyleboxOverride("panel", FlatStyle(ScreenBackground));

        AddChild(BuildMatchHud());

        var decisionPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        decisionPanel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("24372f"), border: new Color("5b5840"), borderWidth: 2));
        AddChild(decisionPanel);

        var decisionStack = new VBoxContainer();
        decisionStack.AddThemeConstantOverride("separation", 5);
        decisionPanel.AddChild(decisionStack);

        _decisionTitleLabel = new Label
        {
            Text = "Current Decision",
            ThemeTypeVariation = "HeaderSmall"
        };
        _decisionTitleLabel.AddThemeFontSizeOverride("font_size", 15);
        decisionStack.AddChild(_decisionTitleLabel);

        _summaryLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _summaryLabel.AddThemeFontSizeOverride("font_size", 13);
        decisionStack.AddChild(_summaryLabel);

        _decisionDetailLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _decisionDetailLabel.AddThemeFontSizeOverride("font_size", 11);
        _decisionDetailLabel.AddThemeColorOverride("font_color", new Color("c9d1bd"));
        decisionStack.AddChild(_decisionDetailLabel);

        var decisionActions = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        decisionActions.AddThemeConstantOverride("separation", 4);
        decisionStack.AddChild(decisionActions);

        var body = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 8);
        AddChild(body);

        body.AddChild(BuildRosterPanel());
        body.AddChild(BuildPitchPanel());
        body.AddChild(BuildEventLogPanel());

        var footer = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        footer.Alignment = BoxContainer.AlignmentMode.Center;
        footer.AddThemeConstantOverride("separation", 6);
        AddChild(footer);
        _blockDiceBox = new HBoxContainer();
        _blockDiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_blockDiceBox);

        _interceptionChoiceBox = new HBoxContainer();
        _interceptionChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_interceptionChoiceBox);

        _rerollChoiceBox = new HBoxContainer();
        _rerollChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_rerollChoiceBox);

        _apothecaryChoiceBox = new HBoxContainer();
        _apothecaryChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_apothecaryChoiceBox);

        _standFirmChoiceBox = new HBoxContainer();
        _standFirmChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_standFirmChoiceBox);

        _divingTackleChoiceBox = new HBoxContainer();
        _divingTackleChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_divingTackleChoiceBox);

        _sendOffChoiceBox = new HBoxContainer();
        _sendOffChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_sendOffChoiceBox);

        _setupChoiceBox = new HBoxContainer();
        _setupChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_setupChoiceBox);

        _passModeButton = ActionButton("Pass");
        _passModeButton.Pressed += () =>
        {
            ResetEndTurnConfirmation();
            var enabled = !_passMode;
            _throwTeamMateMode = false;
            _kickTeamMateMode = false;
            ClearPreview();
            _passMode = enabled;
            RefreshPitch();
        };
        footer.AddChild(_passModeButton);

        _blitzModeButton = ActionButton("Blitz");
        _blitzModeButton.Pressed += async () => await DeclareBlitzModeAsync();
        footer.AddChild(_blitzModeButton);

        _throwTeamMateModeButton = ActionButton("TTM");
        _throwTeamMateModeButton.Pressed += () =>
        {
            ResetEndTurnConfirmation();
            var enabled = !_throwTeamMateMode;
            ClearPreview();
            _throwTeamMateMode = enabled;
            if (enabled)
            {
                _kickTeamMateMode = false;
            }

            RefreshPitch();
        };
        footer.AddChild(_throwTeamMateModeButton);

        _kickTeamMateModeButton = ActionButton("KTM");
        _kickTeamMateModeButton.Pressed += () =>
        {
            ResetEndTurnConfirmation();
            var enabled = !_kickTeamMateMode;
            ClearPreview();
            _kickTeamMateMode = enabled;
            if (enabled)
            {
                _throwTeamMateMode = false;
            }

            RefreshPitch();
        };
        footer.AddChild(_kickTeamMateModeButton);

        _doneButton = ActionButton("Advance", primary: true);
        _doneButton.Pressed += async () => await CompleteCurrentStepAsync();
        footer.AddChild(_doneButton);

        var backButton = ActionButton("Back");
        backButton.Pressed += back;
        footer.AddChild(backButton);

        RefreshRoster();
        RefreshPitch();
        CallDeferred(nameof(InitializePitchZoom));
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } keyEvent && HandlePitchKey(keyEvent.Keycode))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_pitchViewport is null || !IsInstanceValid(_pitchViewport) || !_pitchViewport.GetGlobalRect().HasPoint(GetGlobalMousePosition()))
        {
            if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Middle, Pressed: false })
            {
                _isPitchDragging = false;
            }

            return;
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Middle)
            {
                _isPitchDragging = mouseButton.Pressed;
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mouseButton.Pressed && mouseButton.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                ZoomPitch(mouseButton.ButtonIndex == MouseButton.WheelUp ? PitchZoomStep : 1.0f / PitchZoomStep, mouseButton.GlobalPosition);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is InputEventMouseMotion mouseMotion && _isPitchDragging)
        {
            PanPitch(mouseMotion.Relative);
            GetViewport().SetInputAsHandled();
            return;
        }
    }

    private Control BuildMatchHud()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("111614"), border: new Color("415044"), borderWidth: 2));

        var hud = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        hud.AddThemeConstantOverride("separation", 8);
        panel.AddChild(hud);

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

        return panel;
    }

    private Control BuildRosterPanel()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(250, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(PanelBackground, border: new Color("506358"), borderWidth: 2));

        var stack = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
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
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("18261c"), border: new Color("5a6a4f"), borderWidth: 2));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 4);
        panel.AddChild(stack);

        _pitchViewport = new Control
        {
            ClipContents = true,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _pitchViewport.Resized += OnPitchViewportResized;
        stack.AddChild(_pitchViewport);

        _pitchGrid = new GridContainer
        {
            Columns = _ruleset.PitchWidth,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            PivotOffset = Vector2.Zero,
            TextureFilter = TextureFilterEnum.Nearest
        };
        _pitchGrid.AddThemeConstantOverride("h_separation", -1);
        _pitchGrid.AddThemeConstantOverride("v_separation", -1);
        _pitchViewport.AddChild(_pitchGrid);
        BuildPitchGrid();
        InitializePitchZoom();

        return panel;
    }

    private bool HandlePitchKey(Key key)
    {
        if (key is Key.Plus or Key.Equal or Key.KpAdd)
        {
            ZoomPitch(PitchZoomStep, PitchViewportCenterGlobalPosition());
            return true;
        }

        if (key is Key.Minus or Key.KpSubtract)
        {
            ZoomPitch(1.0f / PitchZoomStep, PitchViewportCenterGlobalPosition());
            return true;
        }

        var panDelta = key switch
        {
            Key.W => new Vector2(0, KeyboardPanStep),
            Key.A => new Vector2(KeyboardPanStep, 0),
            Key.S => new Vector2(0, -KeyboardPanStep),
            Key.D => new Vector2(-KeyboardPanStep, 0),
            _ => Vector2.Zero
        };

        if (panDelta == Vector2.Zero)
        {
            return false;
        }

        PanPitch(panDelta);
        return true;
    }

    private void CenterPitch()
    {
        var baseSize = BasePitchSize();
        var viewportSize = _pitchViewport?.Size ?? Vector2.Zero;
        if (baseSize == Vector2.Zero || viewportSize == Vector2.Zero)
        {
            return;
        }

        _pitchPan = (viewportSize - (baseSize * _pitchZoom)) / 2.0f;
        ApplyPitchTransform();
    }

    private void InitializePitchZoom()
    {
        if (_pitchViewport is null || _pitchGrid is null || _pitchZoomInitialized)
        {
            return;
        }

        var viewportWidth = _pitchViewport.Size.X;
        var pitchWidth = BasePitchSize().X;
        if (viewportWidth <= 0 || pitchWidth <= 0)
        {
            CallDeferred(nameof(InitializePitchZoom));
            return;
        }

        _pitchZoom = Math.Clamp(viewportWidth / pitchWidth, MinPitchZoom, MaxPitchZoom);
        _pitchZoomInitialized = true;
        CenterPitch();
    }

    private void OnPitchViewportResized()
    {
        if (!_pitchZoomInitialized)
        {
            InitializePitchZoom();
            return;
        }

        ApplyPitchTransform();
    }

    private void PanPitch(Vector2 delta)
    {
        _pitchPan += delta;
        ApplyPitchTransform();
    }

    private void ZoomPitch(float factor, Vector2 globalAnchor)
    {
        var nextZoom = Math.Clamp(_pitchZoom * factor, MinPitchZoom, MaxPitchZoom);
        if (Math.Abs(nextZoom - _pitchZoom) < 0.001f)
        {
            return;
        }

        var localAnchor = globalAnchor - _pitchViewport.GlobalPosition;
        var pitchPoint = (localAnchor - _pitchPan) / _pitchZoom;
        _pitchZoom = nextZoom;
        _pitchPan = localAnchor - (pitchPoint * _pitchZoom);
        ApplyPitchTransform();
    }

    private void ApplyPitchTransform()
    {
        if (_pitchViewport is null || _pitchGrid is null)
        {
            return;
        }

        _pitchPan = ClampPitchPan(_pitchPan);
        _pitchGrid.Scale = new Vector2(_pitchZoom, _pitchZoom);
        _pitchGrid.Position = _pitchPan;
    }

    private Vector2 ClampPitchPan(Vector2 pan)
    {
        var viewportSize = _pitchViewport.Size;
        var contentSize = BasePitchSize() * _pitchZoom;

        return new Vector2(
            ClampPitchAxis(pan.X, viewportSize.X, contentSize.X),
            ClampPitchAxis(pan.Y, viewportSize.Y, contentSize.Y));
    }

    private static float ClampPitchAxis(float pan, float viewportLength, float contentLength)
    {
        if (contentLength <= viewportLength)
        {
            return (viewportLength - contentLength) / 2.0f;
        }

        return Math.Clamp(pan, viewportLength - contentLength, 0.0f);
    }

    private Vector2 BasePitchSize()
    {
        return new Vector2(
            (_ruleset.PitchWidth * BasePitchSquareSize) - ((_ruleset.PitchWidth - 1) * PitchSquareOverlap),
            (_ruleset.PitchHeight * BasePitchSquareSize) - ((_ruleset.PitchHeight - 1) * PitchSquareOverlap));
    }

    private Vector2 PitchViewportCenterGlobalPosition()
    {
        return _pitchViewport.GlobalPosition + (_pitchViewport.Size / 2.0f);
    }

    private Control BuildEventLogPanel()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(245, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("1c2420"), border: new Color("4f5846"), borderWidth: 2));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 5);
        panel.AddChild(stack);

        stack.AddChild(new Label
        {
            Text = "Event Log",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _lastEventLabel = new Label
        {
            Text = "No match events yet.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _lastEventLabel.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(_lastEventLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        stack.AddChild(scroll);

        _eventLogList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _eventLogList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_eventLogList);

        return panel;
    }

    private void BuildPitchGrid()
    {
        _pitchButtons.Clear();
        _pitchTileLayers.Clear();
        _pitchHighlightLayers.Clear();
        _pitchMarkingLayers.Clear();
        _pitchPieceLayers.Clear();
        _pitchOverlayLayers.Clear();
        for (var y = 0; y < _ruleset.PitchHeight; y++)
        {
            for (var x = 0; x < _ruleset.PitchWidth; x++)
            {
                var square = new PitchSquare(x, y);
                var button = new Button
                {
                    Text = "",
                    CustomMinimumSize = new Vector2(32, 32),
                    TooltipText = $"{x + 1},{y + 1}",
                    FocusMode = FocusModeEnum.None,
                    ClipContents = true,
                    TextureFilter = TextureFilterEnum.Nearest
                };
                ClearPitchButtonChrome(button);
                button.AddThemeFontSizeOverride("font_size", 10);
                button.Pressed += async () => await HandlePitchSquareAsync(square);

                var tileLayer = BuildPitchLayer(TextureRect.StretchModeEnum.Scale);
                var highlightLayer = BuildPitchLayer(TextureRect.StretchModeEnum.Scale);
                var markingLayer = BuildPitchLayer(TextureRect.StretchModeEnum.Scale);
                var pieceLayer = BuildPitchLayer(TextureRect.StretchModeEnum.KeepAspectCentered);
                var overlayLayer = BuildPitchLayer(TextureRect.StretchModeEnum.KeepAspectCentered);
                button.AddChild(tileLayer);
                button.AddChild(highlightLayer);
                button.AddChild(markingLayer);
                button.AddChild(pieceLayer);
                button.AddChild(overlayLayer);

                _pitchButtons[square] = button;
                _pitchTileLayers[square] = tileLayer;
                _pitchHighlightLayers[square] = highlightLayer;
                _pitchMarkingLayers[square] = markingLayer;
                _pitchPieceLayers[square] = pieceLayer;
                _pitchOverlayLayers[square] = overlayLayer;
                _pitchGrid.AddChild(button);
            }
        }
    }

    private static TextureRect BuildPitchLayer(TextureRect.StretchModeEnum stretchMode)
    {
        var layer = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = stretchMode,
            TextureFilter = TextureFilterEnum.Nearest
        };
        layer.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.OffsetLeft = 0;
        layer.OffsetTop = 0;
        layer.OffsetRight = stretchMode == TextureRect.StretchModeEnum.Scale ? 1 : 0;
        layer.OffsetBottom = stretchMode == TextureRect.StretchModeEnum.Scale ? 1 : 0;
        return layer;
    }

    private static void ClearPitchButtonChrome(Button button)
    {
        var empty = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", empty);
        button.AddThemeStyleboxOverride("disabled", empty);
        button.AddThemeStyleboxOverride("hover", empty);
        button.AddThemeStyleboxOverride("pressed", empty);
        button.AddThemeStyleboxOverride("focus", empty);
    }

    private void LoadSpriteAssets()
    {
        _atlasCache.Clear();
        _humanSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/human_team_32.png");
        _orcSpriteSheet = GD.Load<Texture2D>("res://assets/sprites/orc_team_32.png");
        _pitchObjectSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_objects_32.png");
        _blockDiceSheet = GD.Load<Texture2D>("res://assets/sprites/block_dice_32.png");
        _pitchTileSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_tiles_32.png");
        _pitchFieldSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_field_base_32.png");
        _pitchMarkingSheet = GD.Load<Texture2D>("res://assets/sprites/pitch_field_markings_32.png");
    }

    private Button ActionButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(primary ? 116 : 76, 30)
        };
        var background = primary ? new Color("4c4324") : new Color("253a32");
        var border = primary ? SelectedColor : new Color("5d6755");
        button.AddThemeStyleboxOverride("normal", FlatStyle(background, border, borderWidth: primary ? 2 : 1));
        button.AddThemeStyleboxOverride("hover", FlatStyle(background.Lightened(0.12f), SelectedColor, borderWidth: 2));
        button.AddThemeStyleboxOverride("pressed", FlatStyle(background.Darkened(0.12f), SelectedColor, borderWidth: 2));
        button.AddThemeStyleboxOverride("disabled", FlatStyle(new Color("242a26"), new Color("343a35")));
        return button;
    }

    private Texture2D? AtlasCell(Texture2D? sheet, string key, int column, int row)
    {
        if (sheet is null)
        {
            return null;
        }

        if (_atlasCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var atlas = new AtlasTexture
        {
            Atlas = sheet,
            Region = new Rect2(column * 32, row * 32, 32, 32)
        };
        _atlasCache[key] = atlas;
        return atlas;
    }

    private Texture2D? PlayerSprite(LeagueTeam team, Player player, PlayerPlacement? placement)
    {
        var prone = placement?.State is PlayerPitchState.Prone or PlayerPitchState.Stunned;
        var row = prone ? 1 : 0;
        if (string.Equals(team.RosterId, "orc", StringComparison.OrdinalIgnoreCase))
        {
            var column = player.PositionId switch
            {
                "thrower" => 0,
                "blitzer" => 1,
                "big-un" => 2,
                _ => 3
            };
            return AtlasCell(_orcSpriteSheet, $"orc:{column}:{row}", column, row);
        }

        var humanColumn = player.PositionId switch
        {
            "catcher" => 0,
            "thrower" => 1,
            "blitzer" => 2,
            _ => 3
        };
        return AtlasCell(_humanSpriteSheet, $"human:{humanColumn}:{row}", humanColumn, row);
    }

    private Texture2D? BallSprite(int frame)
    {
        var column = Math.Clamp(frame, 0, 5);
        return AtlasCell(_pitchObjectSheet, $"object:ball:{column}", column, 0);
    }

    private Texture2D? StunnedSprite(int frame)
    {
        var column = Math.Clamp(frame, 0, 5);
        return AtlasCell(_pitchObjectSheet, $"object:stunned:{column}", column, 1);
    }

    private Texture2D? BlockDieSprite(int roll)
    {
        var column = Math.Clamp(roll, 1, 6) - 1;
        return AtlasCell(_blockDiceSheet, $"block-die:{column}", column, 0);
    }

    private Texture2D? PitchTileTexture(PitchSquare square, bool canUse, string? pathMarker)
    {
        return AtlasCell(_pitchFieldSheet, $"field:{square.X}:{square.Y}", square.X, square.Y);
    }

    private Texture2D? PitchMarkingTexture(PitchSquare square)
    {
        return AtlasCell(_pitchMarkingSheet, $"marking:{square.X}:{square.Y}", square.X, square.Y);
    }

    private Texture2D? PitchHighlightTexture(bool canUse, string? pathMarker)
    {
        if (pathMarker is not null)
        {
            if (pathMarker.StartsWith('!'))
            {
                return AtlasCell(_pitchTileSheet, "overlay:rush", 2, 3);
            }

            return pathMarker switch
            {
                ">" => AtlasCell(_pitchTileSheet, "overlay:risk", 2, 3),
                "B" or "P" or "L" => AtlasCell(_pitchTileSheet, "overlay:target", 5, 3),
                "o" => AtlasCell(_pitchTileSheet, "overlay:ball-target", 4, 3),
                "." or "X" => AtlasCell(_pitchTileSheet, "overlay:path", 3, 3),
                _ => AtlasCell(_pitchTileSheet, "overlay:selected", 0, 3)
            };
        }

        return canUse ? AtlasCell(_pitchTileSheet, "overlay:legal", 1, 3) : null;
    }

    private void SetPitchTile(PitchSquare square, Texture2D? texture)
    {
        if (_pitchTileLayers.TryGetValue(square, out var layer))
        {
            layer.Texture = texture;
        }
    }

    private void SetPitchHighlight(PitchSquare square, Texture2D? texture, Color? modulate = null)
    {
        if (_pitchHighlightLayers.TryGetValue(square, out var layer))
        {
            layer.Texture = texture;
            layer.Modulate = modulate ?? Colors.White;
        }
    }

    private void SetPitchMarking(PitchSquare square, Texture2D? texture)
    {
        if (_pitchMarkingLayers.TryGetValue(square, out var layer))
        {
            layer.Texture = texture;
        }
    }

    private void SetPitchPiece(PitchSquare square, Texture2D? texture)
    {
        if (_pitchPieceLayers.TryGetValue(square, out var layer))
        {
            layer.Texture = texture;
        }
    }

    private void SetPitchOverlay(PitchSquare square, Texture2D? texture)
    {
        if (_pitchOverlayLayers.TryGetValue(square, out var layer))
        {
            layer.Texture = texture;
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
            button.Pressed += () => SelectPlayer(playerId);
            _rosterButtons[player.Id] = button;
            _rosterList.AddChild(button);
        }

        RefreshSelectionDisplay();
    }

    private async Task HandlePitchSquareAsync(PitchSquare square)
    {
        ResetEndTurnConfirmation();
        try
        {
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

            var occupied = _match.Placements.FirstOrDefault(placement => placement.Square == square);
            if ((_throwTeamMateMode || _kickTeamMateMode) && _selectedPlayerId is Guid launchActorId)
            {
                await HandleLaunchTargetAsync(launchActorId, square, occupied?.PlayerId);
                return;
            }

            if (_passMode &&
                _selectedPlayerId is Guid selectedPasserId &&
                IsLegalPassTargetSquare(selectedPasserId, square))
            {
                await HandlePassTargetAsync(selectedPasserId, square, occupied?.TeamId == _match.ActiveTeamId ? occupied.PlayerId : null);
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

                if (_selectedPlayerId is Guid passerId && IsLegalPassTarget(passerId, occupied.PlayerId))
                {
                    await HandlePassTargetAsync(passerId, square, occupied.PlayerId);
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
                if (_passMode && IsLegalPassTargetSquare(playerId, square))
                {
                    await HandlePassTargetAsync(playerId, square, receiverId: null);
                    return;
                }

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

        _previewDestination = square;
        _previewPath = BuildMovementPath(PlayerSquare(playerId)!, square);
        RefreshPitch();
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
        var isBlitzMove = CurrentTurnActivation(playerId)?.Action == PlayerTurnAction.Blitz;
        _match = isBlitzMove
            ? service.MovePlayerAsBlitz(_match, _ruleset, movingTeam, playerId, destination, OpponentTeam())
            : service.MovePlayer(_match, _ruleset, movingTeam, playerId, destination, OpponentTeam());
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
            _selectedPlayerId = pending.AttackerPlayerId;
            _currentActivationPlayerId = pending.AttackerPlayerId;
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
            _selectedPlayerId = pending.AttackerPlayerId;
            _currentActivationPlayerId = pending.AttackerPlayerId;
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
        var activeTeamBeforeChoice = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.ResolvePendingFollowUp(_match, TeamById(pending.AttackerTeamId), TeamById(pending.DefenderTeamId), useFollowUp);

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
        _previewBlockDefenderId = null;
        _previewFoulVictimId = null;
        _previewDestination = null;
        _previewPath = [];
        RefreshRoster();
        RefreshPitch();
    }

    private async Task ConfirmPassAsync(Guid passerId, PitchSquare targetSquare)
    {
        var beforeMatch = _match;
        var logStart = _match.Log.Count;
        var activeTeamBeforePass = _match.ActiveTeamId;
        var service = CreateMatchService();
        _match = service.PassBall(_match, _ruleset, ActiveTeam(), passerId, targetSquare, OpponentTeam());
        _previewPassReceiverId = null;
        _previewPassTargetSquare = null;

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

        _passMode = false;
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
            _selectedPlayerId = pending.PasserPlayerId;
            _currentActivationPlayerId = pending.PasserPlayerId;
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
            _selectedPlayerId = pending.AttackerPlayerId;
            _currentActivationPlayerId = pending.AttackerPlayerId;
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

    private async Task AnimateMovementAsync(MatchState beforeMatch, MatchState afterMatch, Guid playerId, IReadOnlyList<PitchSquare> path)
    {
        if (path.Count == 0)
        {
            return;
        }

        _isAnimating = true;
        try
        {
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
        }
        finally
        {
            _isAnimating = false;
            _match = afterMatch;
        }
    }

    private async Task AnimateBallAsync(MatchState beforeMatch, MatchState afterMatch, int logStart)
    {
        var path = BallAnimationPath(beforeMatch, afterMatch, logStart);
        if (path.Count == 0)
        {
            _animationBallSquare = null;
            return;
        }

        var savedMatch = _match;
        _match = afterMatch;
        foreach (var square in path)
        {
            _animationBallSquare = square;
            RefreshPitch();
            await Task.Delay(90);
        }

        _animationBallSquare = null;
        _match = savedMatch;
    }

    private IReadOnlyList<PitchSquare> BallAnimationPath(MatchState beforeMatch, MatchState afterMatch, int logStart)
    {
        var squares = new List<PitchSquare>();
        var start = BallDisplaySquare(beforeMatch);
        if (start is not null)
        {
            squares.Add(start);
        }

        foreach (var entry in afterMatch.Log.Skip(logStart))
        {
            if (!MentionsBall(entry.Message))
            {
                continue;
            }

            foreach (var square in ExtractPitchSquares(entry.Message))
            {
                if (IsOnPitch(square))
                {
                    squares.Add(square);
                }
            }
        }

        var end = BallDisplaySquare(afterMatch);
        if (end is not null)
        {
            squares.Add(end);
        }

        return squares
            .Where(square => IsOnPitch(square))
            .Aggregate(new List<PitchSquare>(), (path, square) =>
            {
                if (path.Count == 0 || path[^1] != square)
                {
                    path.Add(square);
                }

                return path;
            })
            .Skip(start is null ? 0 : 1)
            .ToArray();
    }

    private static bool MentionsBall(string message)
    {
        return message.Contains("ball", StringComparison.OrdinalIgnoreCase);
    }

    private PitchSquare? BallDisplaySquare(MatchState match)
    {
        if (match.Ball.Square is PitchSquare ballSquare)
        {
            return ballSquare;
        }

        return match.Ball.CarrierPlayerId is Guid carrierId
            ? match.Placements.FirstOrDefault(placement => placement.PlayerId == carrierId)?.Square
            : null;
    }

    private static IEnumerable<PitchSquare> ExtractPitchSquares(string message)
    {
        foreach (Match match in Regex.Matches(message, @"-?\d+,-?\d+"))
        {
            var parts = match.Value.Split(',');
            if (int.TryParse(parts[0], out var x) && int.TryParse(parts[1], out var y))
            {
                yield return new PitchSquare(x, y);
            }
        }
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

        _passMode = false;
        _blitzMode = CurrentTurnActivation(playerId)?.Action == PlayerTurnAction.Blitz;
        ClearPreview();
        _blitzMode = CurrentTurnActivation(playerId)?.Action == PlayerTurnAction.Blitz;
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
        RefreshApothecaryChoice();
        RefreshSendOffChoice();
        RefreshStandFirmChoice();
        RefreshDivingTackleChoice();
        RefreshSetupChoice();
        foreach (var (square, button) in _pitchButtons)
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
            button.Text = "";
            button.Icon = null;
            SetPitchTile(square, PitchTileTexture(square, canPlace || canTargetKickoff || canMove || canPassSquare || canPush || canFollowUp || canPlaceBall || canThrowBomb || canLaunchTarget, pathMarker));
            var isGfi = pathMarker?.StartsWith('!') == true ||
                (canMove && _selectedPlayerId is Guid gfiPlayerId && IsGoForItMovementTarget(gfiPlayerId, square));
            SetPitchHighlight(square, PitchHighlightTexture(canPlace || canTargetKickoff || canMove || canPassSquare || canPush || canFollowUp || canPlaceBall || canThrowBomb || canLaunchTarget, pathMarker), isGfi ? GoForItPathColor : null);
            SetPitchMarking(square, PitchMarkingTexture(square));
            SetPitchPiece(square, null);
            SetPitchOverlay(square, null);
            button.Disabled = !canPlace && !canTargetKickoff && !canMove && !canPassSquare && !canPush && !canFollowUp && !canPlaceBall && !canThrowBomb && !canLaunchTarget;
            button.TooltipText = canLaunchTarget
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
            ApplySquareStyle(button, square, isSelected: false, canUse: canPlace || canTargetKickoff || canMove || canPassSquare || canPush || canFollowUp || canPlaceBall || canThrowBomb || canLaunchTarget, pathMarker);
            if (isPreview || canPush || canFollowUp || canThrowBomb || canLaunchTarget || _previewPassTargetSquare == square)
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
            var player = FindPlayer(placement.PlayerId);
            var team = TeamById(placement.TeamId);
            SetPitchPiece(placement.Square!, player is null ? null : PlayerSprite(team, player, placement));
            SetPitchOverlay(placement.Square!, placement.State == PlayerPitchState.Stunned ? StunnedSprite(0) : null);
            if (isSelected)
            {
                SetPitchHighlight(placement.Square!, AtlasCell(_pitchTileSheet, "overlay:selected", 0, 3));
            }
            button.Text = "";
            button.TooltipText = PlayerPitchTooltip(placement);
            var canBlockTarget = _selectedPlayerId is Guid attackerId && IsLegalBlockTarget(attackerId, placement.PlayerId);
            var canBlitzTarget = _selectedPlayerId is Guid blitzerId && IsLegalBlitzTarget(blitzerId, placement.PlayerId);
            var canKickoffBlitzTarget = _selectedPlayerId is Guid kickoffBlitzerId && IsLegalKickoffBlitzTarget(kickoffBlitzerId, placement.PlayerId);
            var canPassTarget = _selectedPlayerId is Guid passerId && IsLegalPassTarget(passerId, placement.PlayerId);
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
            button.Disabled = !CanSelectPlayer(placement.PlayerId) && !canBlockTarget && !canBlitzTarget && !canKickoffBlitzTarget && !canPassTarget && !canFoulTarget && !canPushTarget && !canFollowUpTarget && !canThrowBombTarget && !canLaunchPlayer && !canLaunchTargetSquare;
            ApplySquareStyle(
                button,
                placement.Square!,
                isSelected,
                canUse: canBlockTarget || canBlitzTarget || canKickoffBlitzTarget || canPassTarget || canFoulTarget || canPushTarget || canFollowUpTarget || canThrowBombTarget || canLaunchPlayer || canLaunchTargetSquare,
                pathMarker: canLaunchPlayer ? "L" : canLaunchTargetSquare ? "L" : canFollowUpTarget ? "F" : canPushTarget ? ">" : null,
                blockRole: BlockPreviewRole(placement.PlayerId),
                passRole: PassPreviewRole(placement.PlayerId));
        }

        if (_animationBallSquare is PitchSquare animationBallSquare && _pitchButtons.TryGetValue(animationBallSquare, out var animationBallButton))
        {
            SetPitchOverlay(animationBallSquare, BallSprite(0));
            animationBallButton.Text = "";
            animationBallButton.TooltipText = "Ball";
        }
        else if (_match.Ball.Square is PitchSquare ballSquare && _pitchButtons.TryGetValue(ballSquare, out var ballButton))
        {
            SetPitchOverlay(ballSquare, BallSprite(0));
            ballButton.Text = "";
            ballButton.TooltipText = "Ball";
        }

        if (_animationBallSquare is null &&
            _match.Ball.CarrierPlayerId is Guid carrierId &&
            _match.Placements.FirstOrDefault(placement => placement.PlayerId == carrierId)?.Square is PitchSquare carrierSquare &&
            _pitchButtons.TryGetValue(carrierSquare, out var carrierButton))
        {
            SetPitchOverlay(carrierSquare, BallSprite(4));
            carrierButton.Text = "";
            carrierButton.TooltipText = $"{FindPlayer(carrierId)?.Name ?? "Ball carrier"} with ball";
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
        _passModeButton.TooltipText = canPass ? "Toggle pass targeting." : "Select an unactivated ball carrier to pass.";
        SetModeButtonStyle(_passModeButton, _passMode, canPass);
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

    private async Task DeclareBlitzModeAsync()
    {
        ResetEndTurnConfirmation();
        if (_selectedPlayerId is not Guid playerId || !CanEnterBlitzMode(playerId))
        {
            return;
        }

        try
        {
            var service = CreateMatchService();
            _match = service.DeclarePlayerAction(_match, ActiveTeam(), playerId, PlayerTurnAction.Blitz);
            _currentActivationPlayerId = playerId;
            _passMode = false;
            _throwTeamMateMode = false;
            _kickTeamMateMode = false;
            ClearPreview();
            _selectedPlayerId = playerId;
            _currentActivationPlayerId = playerId;
            _blitzMode = true;
            await _saveMatch(_match);
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Blitz declaration failed: {ex.Message}";
        }
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

        if (_match.PendingKickoffEvent is not null)
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

        if (_match.PendingBallPlacement is not null)
        {
            return false;
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

        return !HasCurrentTurnActivation(playerId) || _currentActivationPlayerId == playerId;
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

        if (_currentActivationPlayerId == playerId)
        {
            return "Current";
        }

        return HasCurrentTurnActivation(playerId) ? "Activated" : "Ready";
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

    private static string RosterTooltip(Player player, PlayerPlacement? placement)
    {
        var stats = FormatStats(player.Stats);
        return placement?.Casualty is null
            ? stats
            : $"{stats}\nCasualty: {FormatCasualtyResult(placement.Casualty.Result)} ({placement.Casualty.Roll})";
    }

    private static string FormatCasualtyResult(CasualtyResult result)
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
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingBombThrow is not null ||
            _match.PendingInterception is not null ||
            _match.PendingReroll is not null ||
            (HasUsedBlitz(_match.ActiveTeamId) && activation?.Action != PlayerTurnAction.Blitz))
        {
            return false;
        }

        if (activation is not null && activation.Action != PlayerTurnAction.Blitz)
        {
            return false;
        }

        if (activation is null && !_blitzMode)
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
            return activation?.Action == PlayerTurnAction.Blitz || _blitzMode;
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
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingReroll is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingBombThrow is not null ||
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
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingBombThrow is not null ||
            _match.PendingInterception is not null ||
            HasCurrentTurnActivation(passerId) ||
            HasUsedPass(_match.ActiveTeamId) ||
            _match.Ball.CarrierPlayerId != passerId)
        {
            return false;
        }

        var passerPlacement = _match.Placements.FirstOrDefault(placement => placement.PlayerId == passerId);
        return passerPlacement?.TeamId == _match.ActiveTeamId &&
            passerPlacement.Square is not null &&
            passerPlacement.State == PlayerPitchState.Standing;
    }

    private bool CanEnterBlitzMode(Guid playerId)
    {
        if (!IsPlayerTurnPhase() ||
            _match.PendingBlock is not null ||
            _match.PendingPush is not null ||
            _match.PendingFollowUp is not null ||
            _match.PendingSendOff is not null ||
            _match.PendingStandFirm is not null ||
            _match.PendingDivingTackle is not null ||
            _match.PendingBombThrow is not null ||
            _match.PendingInterception is not null ||
            _match.PendingReroll is not null ||
            HasCurrentTurnActivation(playerId) ||
            HasUsedBlitz(_match.ActiveTeamId))
        {
            return false;
        }

        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        return placement?.TeamId == _match.ActiveTeamId &&
            placement.Square is not null &&
            placement.State is PlayerPitchState.Standing or PlayerPitchState.Prone;
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
        if (!CanEnterPassMode(passerId) || !IsOnPitch(targetSquare))
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
            passRange.Value.Name,
            PassingTarget(passer, passRange.Value.TargetModifier, _match.Weather, passerTackleZones),
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
            return $"Click {targetName} again to confirm the pass.";
        }

        var interceptionText = preview.EligibleInterceptorPlayerIds.Count == 0
            ? "no eligible interceptors"
            : $"{preview.EligibleInterceptorPlayerIds.Count} eligible interceptor{(preview.EligibleInterceptorPlayerIds.Count == 1 ? "" : "s")}";
        var catchText = preview.CatchTarget is int catchTarget ? $", catch {catchTarget}+" : ", no catch target";
        return $"Pass preview: {preview.RangeName} pass {preview.PassTarget}+{catchText}, {interceptionText}. Click {targetName} again to throw.";
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

    private int TeamBribesRemaining(Guid teamId)
    {
        return teamId == _match.HomeTeamId ? _match.HomeBribesRemaining : _match.AwayBribesRemaining;
    }

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
            (_currentActivationPlayerId != playerId || activation.Action is not (PlayerTurnAction.Move or PlayerTurnAction.Blitz)))
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

    private static int PickupTarget(Player player, int opposingTackleZones, WeatherCondition weather)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        return Math.Clamp(player.Stats.Agility - 1 + opposingTackleZones + weatherModifier, 2, 6);
    }

    private static int CatchTarget(Player player, WeatherCondition weather, int opposingTackleZones = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        return Math.Clamp(player.Stats.Agility + weatherModifier + opposingTackleZones, 2, 6);
    }

    private static int InterceptionTarget(Player player, WeatherCondition weather, int opposingTackleZones = 0)
    {
        var weatherModifier = weather == WeatherCondition.PouringRain ? 1 : 0;
        return Math.Clamp(player.Stats.Agility + 2 + weatherModifier + opposingTackleZones, 2, 6);
    }

    private static int PassingTarget(Player player, int rangeModifier, WeatherCondition weather, int opposingTackleZones = 0)
    {
        var weatherModifier = weather is WeatherCondition.VerySunny or WeatherCondition.Blizzard ? 1 : 0;
        return Math.Clamp(player.Stats.Passing + rangeModifier + weatherModifier + opposingTackleZones, 2, 6);
    }

    private static int GoForItTarget(WeatherCondition weather)
    {
        return weather == WeatherCondition.Blizzard ? 3 : 2;
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
        _previewPassTargetSquare = null;
        _previewLaunchedPlayerId = null;
        _previewLaunchTargetSquare = null;
        _passMode = false;
        _throwTeamMateMode = false;
        _kickTeamMateMode = false;
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
        return square.Y < 4 || square.Y >= _ruleset.PitchHeight - 4;
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

    private static string FormatKickoffEventKind(KickoffEventKind kind)
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
        int? CatchTarget,
        IReadOnlyList<Guid> EligibleInterceptorPlayerIds);
}
