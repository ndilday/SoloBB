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
    private Label _summaryLabel = null!;
    private OptionButton _reserveOption = null!;
    private GridContainer _pitchGrid = null!;
    private readonly Dictionary<PitchSquare, Button> _pitchButtons = [];
    private Ruleset _ruleset = null!;
    private MatchState _match = null!;
    private LeagueTeam _homeTeam = null!;
    private LeagueTeam _awayTeam = null!;
    private Func<MatchState, Task> _saveMatch = _ => Task.CompletedTask;

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

        AddTitle("Match Setup");
        _summaryLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_summaryLabel);

        _reserveOption = new OptionButton();
        AddChild(_reserveOption);

        _pitchGrid = new GridContainer { Columns = _ruleset.PitchWidth };
        AddChild(_pitchGrid);
        BuildPitchGrid();

        var backButton = new Button { Text = "Back" };
        backButton.Pressed += back;
        AddChild(backButton);

        PopulateReserveOptions();
        RefreshPitch();
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
                    CustomMinimumSize = new Vector2(24, 24),
                    TooltipText = $"{x + 1},{y + 1}"
                };
                button.Pressed += async () => await PlaceSelectedPlayerAsync(square);
                _pitchButtons[square] = button;
                _pitchGrid.AddChild(button);
            }
        }
    }

    private async Task PlaceSelectedPlayerAsync(PitchSquare square)
    {
        try
        {
            if (_reserveOption.Selected < 0)
            {
                return;
            }

            var playerId = Guid.Parse(_reserveOption.GetItemMetadata(_reserveOption.Selected).AsString());
            var service = new MatchService();
            _match = service.PlacePlayer(_match, _ruleset, playerId, square);
            await _saveMatch(_match);
            PopulateReserveOptions();
            RefreshPitch();
        }
        catch (Exception ex)
        {
            _summaryLabel.Text = $"Placement failed: {ex.Message}";
        }
    }

    private void PopulateReserveOptions()
    {
        _reserveOption.Clear();
        var reservePlacements = _match.Placements
            .Where(placement => placement.State == PlayerPitchState.Reserve && placement.TeamId == _match.ActiveTeamId)
            .OrderBy(placement => FindPlayer(placement.PlayerId)?.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var i = 0; i < reservePlacements.Length; i++)
        {
            var placement = reservePlacements[i];
            _reserveOption.AddItem(FindPlayer(placement.PlayerId)?.Name ?? "Unknown", i);
            _reserveOption.SetItemMetadata(i, Variant.From(placement.PlayerId.ToString()));
        }
    }

    private void RefreshPitch()
    {
        foreach (var button in _pitchButtons.Values)
        {
            button.Text = "";
            button.TooltipText = "";
        }

        foreach (var placement in _match.Placements.Where(placement => placement.Square is not null))
        {
            if (!_pitchButtons.TryGetValue(placement.Square!, out var button))
            {
                continue;
            }

            var isHome = placement.TeamId == _match.HomeTeamId;
            button.Text = isHome ? "H" : "A";
            button.TooltipText = FindPlayer(placement.PlayerId)?.Name ?? "Unknown";
        }

        var activeTeam = _match.ActiveTeamId == _homeTeam.Id ? _homeTeam : _awayTeam;
        _summaryLabel.Text = $"{activeTeam.Name} is kicking off and places players first. Phase: {_match.Phase}.";
    }

    private Player? FindPlayer(Guid playerId)
    {
        return _homeTeam.Players.Concat(_awayTeam.Players).FirstOrDefault(player => player.Id == playerId);
    }

    private void AddTitle(string text)
    {
        var title = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 32);
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
