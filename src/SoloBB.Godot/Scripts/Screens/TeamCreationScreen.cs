using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public sealed record TeamDraftRequest(
    Guid? ExistingTeamId,
    string TeamName,
    string CoachName,
    TeamRoster Roster,
    IReadOnlyList<PlayerDraftPick> Draft,
    int Rerolls,
    int FanFactor,
    int Cheerleaders,
    int AssistantCoaches,
    int Apothecaries);

public partial class TeamCreationScreen : VBoxContainer
{
    private const int MaximumRosterPlayers = 16;
    private const int FanFactorCost = 10_000;
    private const int CheerleaderCost = 10_000;
    private const int AssistantCoachCost = 10_000;
    private const int ApothecaryCost = 50_000;
    private readonly Dictionary<string, SpinBox> _positionCounts = new(StringComparer.OrdinalIgnoreCase);

    private Ruleset _ruleset = null!;
    private RosterSet _rosterSet = null!;
    private TeamRoster? _selectedRoster;
    private LineEdit _teamNameEdit = null!;
    private LineEdit _coachNameEdit = null!;
    private OptionButton _rosterOption = null!;
    private Label _rerollCostLabel = null!;
    private SpinBox _rerollsSpin = null!;
    private SpinBox _fanFactorSpin = null!;
    private SpinBox _cheerleadersSpin = null!;
    private SpinBox _assistantCoachesSpin = null!;
    private SpinBox _apothecariesSpin = null!;
    private GridContainer _positionGrid = null!;
    private Label _summaryLabel = null!;
    private Button _saveButton = null!;
    private Label _statusLabel = null!;
    private LeagueTeam? _editingTeam;
    private Func<TeamDraftRequest, Task> _saveTeam = _ => Task.CompletedTask;

    public void Setup(
        Ruleset ruleset,
        RosterSet rosterSet,
        string defaultTeamName,
        Func<TeamDraftRequest, Task> saveTeam,
        Action back,
        LeagueTeam? editingTeam = null)
    {
        Clear();

        _ruleset = ruleset;
        _rosterSet = rosterSet;
        _editingTeam = editingTeam;
        _saveTeam = saveTeam;

        AddTitle(editingTeam is null ? "Create Team" : "Edit Team");

        var identityGrid = new GridContainer { Columns = 2 };
        AddChild(identityGrid);

        identityGrid.AddChild(new Label { Text = "Team Name" });
        _teamNameEdit = AddLineEdit(identityGrid, "Team name", editingTeam?.Name ?? defaultTeamName);

        identityGrid.AddChild(new Label { Text = "Coach Name" });
        _coachNameEdit = AddLineEdit(identityGrid, "Coach name", editingTeam?.CoachName ?? "Hotseat");

        identityGrid.AddChild(new Label { Text = "Roster" });
        _rosterOption = new OptionButton();
        _rosterOption.ItemSelected += _ => SelectRosterFromOption();
        identityGrid.AddChild(_rosterOption);

        var economyGrid = new GridContainer { Columns = 3 };
        AddChild(economyGrid);
        economyGrid.AddChild(new Label { Text = "Item", ThemeTypeVariation = "HeaderSmall" });
        economyGrid.AddChild(new Label { Text = "Cost", ThemeTypeVariation = "HeaderSmall" });
        economyGrid.AddChild(new Label { Text = "Count", ThemeTypeVariation = "HeaderSmall" });

        economyGrid.AddChild(new Label { Text = "Rerolls" });
        _rerollCostLabel = new Label { Text = "Select a roster" };
        economyGrid.AddChild(_rerollCostLabel);
        _rerollsSpin = CreateSpinBox(0, _ruleset.RerollCap, editingTeam?.Rerolls ?? 2);
        economyGrid.AddChild(_rerollsSpin);

        economyGrid.AddChild(new Label { Text = "Fan Factor" });
        economyGrid.AddChild(new Label { Text = FormatGold(FanFactorCost) });
        _fanFactorSpin = CreateSpinBox(1, 9, editingTeam?.FanFactor ?? 1);
        economyGrid.AddChild(_fanFactorSpin);

        economyGrid.AddChild(new Label { Text = "Cheerleaders" });
        economyGrid.AddChild(new Label { Text = FormatGold(CheerleaderCost) });
        _cheerleadersSpin = CreateSpinBox(0, 12, editingTeam?.Cheerleaders ?? 0);
        economyGrid.AddChild(_cheerleadersSpin);

        economyGrid.AddChild(new Label { Text = "Assistant Coaches" });
        economyGrid.AddChild(new Label { Text = FormatGold(AssistantCoachCost) });
        _assistantCoachesSpin = CreateSpinBox(0, 12, editingTeam?.AssistantCoaches ?? 0);
        economyGrid.AddChild(_assistantCoachesSpin);

        economyGrid.AddChild(new Label { Text = "Apothecary" });
        economyGrid.AddChild(new Label { Text = FormatGold(ApothecaryCost) });
        _apothecariesSpin = CreateSpinBox(0, 1, editingTeam?.Apothecaries ?? 0);
        economyGrid.AddChild(_apothecariesSpin);

        _positionGrid = new GridContainer { Columns = 10 };
        AddChild(_positionGrid);

        _summaryLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_summaryLabel);

        _saveButton = AddButton("Save Team", async () => await SaveAsync(), disabled: true);
        AddButton("Back", back);

        _statusLabel = new Label
        {
            Text = "Build a roster for this league.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        AddChild(_statusLabel);

        PopulateRosterOptions();
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private void PopulateRosterOptions()
    {
        _rosterOption.Clear();
        var rosterOptions = _rosterSet.Rosters
            .Select((roster, index) => (Roster: roster, Index: index))
            .OrderBy(option => option.Roster.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var i = 0; i < rosterOptions.Length; i++)
        {
            _rosterOption.AddItem(rosterOptions[i].Roster.Name, rosterOptions[i].Index);
        }

        var selectedRosterOption = _editingTeam is null
            ? 0
            : Math.Max(0, Array.FindIndex(rosterOptions, option => string.Equals(option.Roster.Id, _editingTeam.RosterId, StringComparison.OrdinalIgnoreCase)));
        _rosterOption.Selected = selectedRosterOption;
        SelectRosterFromOption();
    }

    private void SelectRosterFromOption()
    {
        if (_rosterOption.Selected < 0)
        {
            return;
        }

        var rosterIndex = _rosterOption.GetItemId(_rosterOption.Selected);
        if (rosterIndex < 0 || rosterIndex >= _rosterSet.Rosters.Count)
        {
            return;
        }

        _selectedRoster = _rosterSet.Rosters[rosterIndex];
        UpdateRerollCostLabel();
        BuildPositionRows();
        UpdateDraftSummary();
    }

    private void BuildPositionRows()
    {
        foreach (var child in _positionGrid.GetChildren())
        {
            child.QueueFree();
        }

        _positionCounts.Clear();
        if (_selectedRoster is null)
        {
            return;
        }

        AddPositionHeader("Min-Max");
        AddPositionHeader("Position");
        AddPositionHeader("Cost");
        AddPositionHeader("MA");
        AddPositionHeader("ST");
        AddPositionHeader("AG");
        AddPositionHeader("PA");
        AddPositionHeader("AV");
        AddPositionHeader("Skills");
        AddPositionHeader("Count");

        foreach (var position in _selectedRoster.Positions)
        {
            _positionGrid.AddChild(new Label { Text = $"{position.Min}-{position.Max}" });
            _positionGrid.AddChild(new Label { Text = position.Name });
            _positionGrid.AddChild(new Label { Text = FormatGold(position.Cost) });
            _positionGrid.AddChild(new Label { Text = position.Stats.Movement.ToString() });
            _positionGrid.AddChild(new Label { Text = position.Stats.Strength.ToString() });
            _positionGrid.AddChild(new Label { Text = $"{position.Stats.Agility}+" });
            _positionGrid.AddChild(new Label { Text = $"{position.Stats.Passing}+" });
            _positionGrid.AddChild(new Label { Text = $"{position.Stats.Armor}+" });
            _positionGrid.AddChild(new Label { Text = position.StartingSkills.Count == 0 ? "-" : string.Join(", ", position.StartingSkills) });

            var existingCount = ExistingPositionCount(position.Id);
            var defaultCount = _editingTeam is null
                ? position.Id == "lineman" ? 11 : position.Min
                : existingCount;
            var count = CreateSpinBox(position.Min, position.Max, defaultCount);
            _positionCounts[position.Id] = count;
            _positionGrid.AddChild(count);
        }
    }

    private async Task SaveAsync()
    {
        if (_selectedRoster is null)
        {
            SetStatus("Choose a roster before saving.");
            return;
        }

        var request = new TeamDraftRequest(
            _editingTeam?.Id,
            _teamNameEdit.Text,
            _coachNameEdit.Text,
            _selectedRoster,
            CreateDraft(_selectedRoster),
            (int)_rerollsSpin.Value,
            (int)_fanFactorSpin.Value,
            (int)_cheerleadersSpin.Value,
            (int)_assistantCoachesSpin.Value,
            (int)_apothecariesSpin.Value);

        await _saveTeam(request);
    }

    private IReadOnlyList<PlayerDraftPick> CreateDraft(TeamRoster roster)
    {
        var draft = new List<PlayerDraftPick>();
        foreach (var position in roster.Positions)
        {
            var count = _positionCounts.TryGetValue(position.Id, out var spinBox) ? (int)spinBox.Value : 0;
            for (var i = 1; i <= count; i++)
            {
                draft.Add(new PlayerDraftPick($"{position.Name} {i}", position.Id));
            }
        }

        return draft;
    }

    private int ExistingPositionCount(string positionId)
    {
        if (_editingTeam is null || _selectedRoster is null || !string.Equals(_editingTeam.RosterId, _selectedRoster.Id, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return _editingTeam.Players.Count(player => string.Equals(player.PositionId, positionId, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdateDraftSummary()
    {
        if (_selectedRoster is null || _summaryLabel is null)
        {
            return;
        }

        var playerCount = 0;
        var playerCost = 0;
        foreach (var position in _selectedRoster.Positions)
        {
            var count = _positionCounts.TryGetValue(position.Id, out var spinBox) ? (int)spinBox.Value : 0;
            playerCount += count;
            playerCost += count * position.Cost;
        }

        var rerollCost = (int)_rerollsSpin.Value * _selectedRoster.RerollCost;
        var fanFactorCost = Math.Max(0, (int)_fanFactorSpin.Value - 1) * FanFactorCost;
        var staffCost = ((int)_cheerleadersSpin.Value * CheerleaderCost) +
            ((int)_assistantCoachesSpin.Value * AssistantCoachCost) +
            ((int)_apothecariesSpin.Value * ApothecaryCost);
        var totalCost = playerCost + rerollCost + fanFactorCost + staffCost;
        var treasury = _ruleset.StartingTreasury - totalCost;
        var isReady = playerCount >= _ruleset.PlayersPerSide && playerCount <= MaximumRosterPlayers && treasury >= 0;
        var status = isReady ? "Ready" : "Needs work";

        _summaryLabel.Text = $"{status}: {playerCount} players ({_ruleset.PlayersPerSide}-{MaximumRosterPlayers}), team value {FormatGold(totalCost)}, treasury {FormatGold(treasury)}.";
        _saveButton.Disabled = !isReady;
        UpdateAffordableMaximums(treasury, playerCount);
    }

    private void UpdateAffordableMaximums(int treasury, int playerCount)
    {
        if (_selectedRoster is null)
        {
            return;
        }

        var rerollValue = (int)_rerollsSpin.Value;
        _rerollsSpin.MaxValue = treasury >= _selectedRoster.RerollCost
            ? _ruleset.RerollCap
            : rerollValue;

        var fanFactorValue = (int)_fanFactorSpin.Value;
        _fanFactorSpin.MaxValue = treasury >= FanFactorCost
            ? 9
            : fanFactorValue;

        var cheerleaderValue = (int)_cheerleadersSpin.Value;
        _cheerleadersSpin.MaxValue = treasury >= CheerleaderCost
            ? 12
            : cheerleaderValue;

        var assistantCoachValue = (int)_assistantCoachesSpin.Value;
        _assistantCoachesSpin.MaxValue = treasury >= AssistantCoachCost
            ? 12
            : assistantCoachValue;

        var apothecaryValue = (int)_apothecariesSpin.Value;
        _apothecariesSpin.MaxValue = treasury >= ApothecaryCost
            ? 1
            : apothecaryValue;

        foreach (var position in _selectedRoster.Positions)
        {
            if (!_positionCounts.TryGetValue(position.Id, out var spinBox))
            {
                continue;
            }

            var currentValue = (int)spinBox.Value;
            spinBox.MaxValue = treasury >= position.Cost && playerCount < MaximumRosterPlayers
                ? position.Max
                : currentValue;
        }
    }

    private LineEdit AddLineEdit(Container parent, string placeholder, string text)
    {
        var edit = new LineEdit
        {
            PlaceholderText = placeholder,
            Text = text
        };
        edit.TextChanged += _ => UpdateDraftSummary();
        parent.AddChild(edit);
        return edit;
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

    private void AddPositionHeader(string text)
    {
        _positionGrid.AddChild(new Label
        {
            Text = text,
            ThemeTypeVariation = "HeaderSmall"
        });
    }

    private void UpdateRerollCostLabel()
    {
        if (_selectedRoster is not null && _rerollCostLabel is not null)
        {
            _rerollCostLabel.Text = FormatGold(_selectedRoster.RerollCost);
        }
    }

    private Button AddButton(string text, Action pressed, bool disabled = false)
    {
        var button = new Button { Text = text, Disabled = disabled };
        button.Pressed += pressed;
        AddChild(button);
        return button;
    }

    private Button AddButton(string text, Func<Task> pressed, bool disabled = false)
    {
        var button = new Button { Text = text, Disabled = disabled };
        button.Pressed += async () => await pressed();
        AddChild(button);
        return button;
    }

    private SpinBox CreateSpinBox(double min, double max, double value)
    {
        var spinBox = new SpinBox
        {
            MinValue = min,
            MaxValue = max,
            Value = value,
            Step = 1,
            AllowGreater = false,
            AllowLesser = false
        };
        spinBox.ValueChanged += _ => UpdateDraftSummary();
        return spinBox;
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private static string FormatGold(int value)
    {
        return $"{value:N0} gp";
    }
}
