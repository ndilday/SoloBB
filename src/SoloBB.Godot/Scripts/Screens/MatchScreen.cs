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

    private Label _summaryLabel = null!;
    private Label _lastEventLabel = null!;
    private Label _selectedLabel = null!;
    private VBoxContainer _rosterList = null!;
    private GridContainer _pitchGrid = null!;
    private Button _doneButton = null!;
    private Ruleset _ruleset = null!;
    private MatchState _match = null!;
    private LeagueTeam _homeTeam = null!;
    private LeagueTeam _awayTeam = null!;
    private Guid? _selectedPlayerId;
    private Func<MatchState, Task> _saveMatch = _ => Task.CompletedTask;

    private static readonly Color ScreenBackground = new("17211b");
    private static readonly Color PanelBackground = new("223128");
    private static readonly Color PitchGrass = new("3f7f46");
    private static readonly Color LegalPitchGrass = new("5d9960");
    private static readonly Color EndZoneHome = new("274f7d");
    private static readonly Color EndZoneAway = new("7d3b34");
    private static readonly Color LineColor = new("f4f1df");
    private static readonly Color SelectedColor = new("f2c14e");

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

        AddThemeConstantOverride("separation", 6);
        AddThemeStyleboxOverride("panel", FlatStyle(ScreenBackground));

        AddTitle("Match Setup");
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
        _doneButton = new Button { Text = "Done" };
        _doneButton.Pressed += async () => await CompleteCurrentStepAsync();
        footer.AddChild(_doneButton);

        var backButton = new Button { Text = "Back" };
        backButton.Pressed += back;
        footer.AddChild(backButton);

        RefreshRoster();
        RefreshPitch();
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
            var button = new Button
            {
                Text = $"{marker}  {player.Name}  {state}",
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Disabled = placement?.State is PlayerPitchState.Casualty or PlayerPitchState.SentOff
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
            if (_match.Phase is MatchPhase.Kickoff)
            {
                await ResolveKickoffTargetAsync(square);
                return;
            }

            var occupied = _match.Placements.FirstOrDefault(placement => placement.Square == square);
            if (occupied is not null)
            {
                SelectPlayer(occupied.PlayerId);
                return;
            }

            if (_selectedPlayerId is not Guid playerId)
            {
                _summaryLabel.Text = "Select a player from the roster first.";
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
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Placement failed: {ex.Message}";
        }
    }

    private async Task ResolveKickoffTargetAsync(PitchSquare square)
    {
        var receivingTeam = ActiveTeam();
        var service = new MatchService();
        _match = service.ResolveKickoff(_match, _ruleset, receivingTeam, square);
        _selectedPlayerId = null;
        await _saveMatch(_match);
        RefreshRoster();
        RefreshPitch();
    }

    private async Task CompleteCurrentStepAsync()
    {
        try
        {
            if (_match.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup))
            {
                return;
            }

            var service = new MatchService();
            _match = service.AdvancePhase(_match);
            _selectedPlayerId = null;
            await _saveMatch(_match);
            RefreshRoster();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Advance failed: {ex.Message}";
        }
    }

    private void SelectPlayer(Guid playerId)
    {
        var placement = _match.Placements.FirstOrDefault(current => current.PlayerId == playerId);
        if (placement is null)
        {
            return;
        }

        _selectedPlayerId = playerId;
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
            button.AddThemeStyleboxOverride("normal", FlatStyle(isSelected ? new Color("4b4425") : new Color("2b3a31"), isSelected ? SelectedColor : new Color("3c4b40")));
            button.AddThemeStyleboxOverride("hover", FlatStyle(isSelected ? new Color("5c522b") : new Color("34483b"), isSelected ? SelectedColor : new Color("536856")));
        }
    }

    private void RefreshPitch()
    {
        foreach (var (square, button) in _pitchButtons)
        {
            var canPlace = IsLegalPlacementTarget(square);
            var canTargetKickoff = IsLegalKickoffTarget(square);
            button.Text = "";
            button.Disabled = !canPlace && !canTargetKickoff;
            button.TooltipText = canPlace || canTargetKickoff ? $"{square.X + 1},{square.Y + 1}" : "";
            ApplySquareStyle(button, square, isSelected: false, canUse: canPlace || canTargetKickoff);
        }

        foreach (var placement in _match.Placements.Where(placement => placement.Square is not null))
        {
            if (!_pitchButtons.TryGetValue(placement.Square!, out var button))
            {
                continue;
            }

            var isSelected = placement.PlayerId == _selectedPlayerId;
            button.Text = PlayerMarker(placement.PlayerId);
            button.TooltipText = FindPlayer(placement.PlayerId)?.Name ?? "Unknown";
            button.Disabled = _match.Phase is MatchPhase.Kickoff || placement.TeamId != _match.ActiveTeamId;
            ApplySquareStyle(button, placement.Square!, isSelected, canUse: false);
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
        _doneButton.Disabled = _match.Phase is not (MatchPhase.DefenseSetup or MatchPhase.OffenseSetup);
        _doneButton.Text = _match.Phase switch
        {
            MatchPhase.DefenseSetup => "Defense Done",
            MatchPhase.OffenseSetup => "Offense Done",
            _ => "Done"
        };

        _summaryLabel.Text = _match.Phase switch
        {
            MatchPhase.DefenseSetup => $"{activeTeam.Name} is kicking off and places players first. Selected: {selected}.",
            MatchPhase.OffenseSetup => $"{activeTeam.Name} is receiving the kick and places players. Selected: {selected}.",
            MatchPhase.Kickoff => $"{KickingTeam().Name} is kicking. Select a target square in {activeTeam.Name}'s half.",
            _ => $"{activeTeam.Name} active. Phase: {_match.Phase}. Selected: {selected}."
        };
        _lastEventLabel.Text = _match.Log.LastOrDefault()?.Message ?? "No match events yet.";
    }

    private void ApplySquareStyle(Button button, PitchSquare square, bool isSelected, bool canUse)
    {
        var baseColor = canUse ? SquareColor(square).Lerp(LegalPitchGrass, 0.35f) : SquareColor(square);
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
