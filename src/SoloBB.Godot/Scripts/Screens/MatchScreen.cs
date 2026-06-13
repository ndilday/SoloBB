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
    private readonly Dictionary<PitchSquare, PitchTileView> _pitchTiles = [];
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
    private HBoxContainer _pushChoiceBox = null!;
    private HBoxContainer _divingTackleChoiceBox = null!;
    private HBoxContainer _dumpOffChoiceBox = null!;
    private HBoxContainer _onTheBallChoiceBox = null!;
    private HBoxContainer _sendOffChoiceBox = null!;
    private HBoxContainer _setupChoiceBox = null!;
    private Button _passModeButton = null!;
    private Button _handOffModeButton = null!;
    private Button _blitzModeButton = null!;
    private Button _throwTeamMateModeButton = null!;
    private Button _kickTeamMateModeButton = null!;
    private Button _weatherMageButton = null!;
    private Button _specialPlayButton = null!;
    private Button _wizardButton = null!;
    private Button _doneButton = null!;
    private Texture2D? _humanSpriteSheet;
    private Texture2D? _orcSpriteSheet;
    private Texture2D? _dwarfSpriteSheet;
    private Texture2D? _humanOgreSpriteSheet;
    private Texture2D? _orcUntrainedTrollSpriteSheet;
    private Texture2D? _dwarfDeathrollerSpriteSheet;
    private Texture2D? _shamblingUndeadSpriteSheet;
    private Texture2D? _highElfSpriteSheet;
    private Texture2D? _amazonSpriteSheet;
    private Texture2D? _darkElfSpriteSheet;
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
    private Guid? _onTheBallMoverId;
    private Guid? _currentActivationPlayerId;
    private PitchSquare? _previewDestination;
    private IReadOnlyList<PitchSquare> _previewPath = [];
    private Guid? _previewBlockDefenderId;
    private Guid? _previewBlitzDefenderId;
    private PitchSquare? _previewBlitzDestination;
    private Guid? _previewFoulVictimId;
    private Guid? _previewPassReceiverId;
    private PitchSquare? _previewPassTargetSquare;
    private IReadOnlyList<PitchSquare> _previewPassLinePath = [];
    private Guid? _previewHandOffReceiverId;
    private Guid? _previewLaunchedPlayerId;
    private PitchSquare? _previewLaunchTargetSquare;
    private PitchSquare? _animationBallSquare;
    private bool _isAnimating;
    private bool _passMode;
    private bool _handOffMode;
    private bool _blitzMode;
    private bool _throwTeamMateMode;
    private bool _kickTeamMateMode;
    private bool _wizardMode;
    private Guid? _wizardModeTeamId;
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
    private static readonly Color ActivatedPieceModulate = new(0.5f, 0.52f, 0.55f);

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
        _previewPassLinePath = [];
        _previewLaunchedPlayerId = null;
        _previewLaunchTargetSquare = null;
        _animationBallSquare = null;
        _passMode = false;
        _handOffMode = false;
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

        _pushChoiceBox = new HBoxContainer();
        _pushChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_pushChoiceBox);

        _divingTackleChoiceBox = new HBoxContainer();
        _divingTackleChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_divingTackleChoiceBox);

        _dumpOffChoiceBox = new HBoxContainer();
        _dumpOffChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_dumpOffChoiceBox);

        _onTheBallChoiceBox = new HBoxContainer();
        _onTheBallChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_onTheBallChoiceBox);

        _sendOffChoiceBox = new HBoxContainer();
        _sendOffChoiceBox.AddThemeConstantOverride("separation", 4);
        decisionActions.AddChild(_sendOffChoiceBox);

        _passModeButton = ActionButton("Pass");
        _passModeButton.Pressed += async () => await DeclarePassModeAsync();
        footer.AddChild(_passModeButton);

        _handOffModeButton = ActionButton("Hand-off");
        _handOffModeButton.Pressed += async () => await DeclareHandOffModeAsync();
        footer.AddChild(_handOffModeButton);

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
                DisableWizardMode();
                _passMode = false;
                _handOffMode = false;
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
                DisableWizardMode();
                _passMode = false;
                _handOffMode = false;
                _throwTeamMateMode = false;
            }

            RefreshPitch();
        };
        footer.AddChild(_kickTeamMateModeButton);

        _weatherMageButton = ActionButton("Weather");
        _weatherMageButton.Pressed += async () => await UseWeatherMageAsync();
        footer.AddChild(_weatherMageButton);

        _specialPlayButton = ActionButton("Special Play");
        _specialPlayButton.Pressed += async () => await UseSpecialPlayAsync();
        footer.AddChild(_specialPlayButton);

        _wizardButton = ActionButton("Wizard");
        _wizardButton.Pressed += () =>
        {
            ResetEndTurnConfirmation();
            _wizardMode = !_wizardMode;
            if (_wizardMode)
            {
                _wizardModeTeamId = _match.ActiveTeamId;
                _passMode = false;
                _handOffMode = false;
                _blitzMode = false;
                _throwTeamMateMode = false;
                _kickTeamMateMode = false;
                _selectedPlayerId = null;
            }
            else
            {
                _wizardModeTeamId = null;
            }
            RefreshPitch();
        };
        footer.AddChild(_wizardButton);

        // Placed in the footer (to the left of the done/Finish Setup button) rather than in the
        // decision-action row so that showing/hiding the "Return to Reserve" button does not change
        // the decision panel's height and reflow the body and footer below it.
        _setupChoiceBox = new HBoxContainer();
        _setupChoiceBox.AddThemeConstantOverride("separation", 4);
        footer.AddChild(_setupChoiceBox);

        _doneButton = ActionButton("Advance", primary: true);
        _doneButton.Pressed += async () => await CompleteCurrentStepAsync();
        footer.AddChild(_doneButton);

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

    private void DisableWizardMode()
    {
        _wizardMode = false;
        _wizardModeTeamId = null;
    }

    private sealed record BlockPreview(
        int AttackerStrength,
        int DefenderStrength,
        int Dice,
        IReadOnlyList<Guid> AttackerAssistPlayerIds,
        IReadOnlyList<Guid> DefenderAssistPlayerIds);

    private sealed record FoulAssistPreview(int AttackingAssists, int DefendingAssists);

    private sealed record PassPreview(
        string RangeName,
        int PassTarget,
        int? CatchTarget,
        IReadOnlyList<Guid> EligibleInterceptorPlayerIds);
}
