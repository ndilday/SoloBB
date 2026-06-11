using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public sealed record TeamDraftRequest(
    string TeamName,
    string CoachName,
    TeamRoster Roster,
    IReadOnlyList<PlayerDraftPick> Draft,
    int Rerolls,
    int FanFactor,
    int Cheerleaders,
    int AssistantCoaches,
    int Apothecaries);

public sealed record TeamManagementRequest(
    Guid TeamId,
    string TeamName,
    string CoachName,
    TeamRoster Roster,
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
    private LeagueTeam? _editingTeam;
    private Func<TeamDraftRequest, Task> _saveTeam = _ => Task.CompletedTask;
    private Func<TeamManagementRequest, Task> _saveManagement = _ => Task.CompletedTask;
    private Action<Guid> _openRoster = _ => { };

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
    private Label _budgetPlayersLabel = null!;
    private Label _budgetRerollsLabel = null!;
    private Label _budgetStaffLabel = null!;
    private Label _budgetRemainingLabel = null!;
    private Label _statusLabel = null!;
    private Label _rosterPreviewLabel = null!;
    private Button _saveButton = null!;

    public void Setup(
        Ruleset ruleset,
        RosterSet rosterSet,
        string defaultTeamName,
        Func<TeamDraftRequest, Task> saveTeam,
        Action back,
        LeagueTeam? editingTeam = null,
        Func<TeamManagementRequest, Task>? saveManagement = null,
        Action<Guid>? openRoster = null)
    {
        Clear();
        AddThemeConstantOverride("separation", 8);
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));

        _ruleset = ruleset;
        _rosterSet = rosterSet;
        _editingTeam = editingTeam;
        _saveTeam = saveTeam;
        _saveManagement = saveManagement ?? (_ => Task.CompletedTask);
        _openRoster = openRoster ?? (_ => { });

        if (editingTeam is null)
        {
            BuildCreateLayout(defaultTeamName, back);
        }
        else
        {
            BuildEditLayout(editingTeam, back);
        }
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private void BuildCreateLayout(string defaultTeamName, Action back)
    {
        AddScreenHeader("Create Team", "Save Team", async () => await SaveDraftAsync(), back);

        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        AddChild(body);

        var mainColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.9f
        };
        mainColumn.AddThemeConstantOverride("separation", 8);
        body.AddChild(mainColumn);

        var sideColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(260, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.7f
        };
        sideColumn.AddThemeConstantOverride("separation", 8);
        body.AddChild(sideColumn);

        mainColumn.AddChild(ScreenStyles.Panel("Identity", BuildIdentityPanel(defaultTeamName, "Hotseat", showRosterPicker: true)));
        mainColumn.AddChild(ScreenStyles.Panel("Position Draft", BuildPositionDraftPanel()));
        mainColumn.AddChild(ScreenStyles.Panel("Team Assets", BuildAssetPanel()));

        sideColumn.AddChild(ScreenStyles.Panel("Budget", BuildBudgetPanel()));
        sideColumn.AddChild(ScreenStyles.Panel("Status", BuildStatusPanel()));
        sideColumn.AddChild(ScreenStyles.Panel("Roster Preview", BuildRosterPreviewPanel()));

        PopulateRosterOptions();
    }

    private void BuildEditLayout(LeagueTeam team, Action back)
    {
        AddScreenHeader($"Edit {team.Name}", "Save Changes", async () => await SaveManagementAsync(), back);

        _selectedRoster = FindRoster(team.RosterId);

        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        AddChild(body);

        var mainColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.8f
        };
        mainColumn.AddThemeConstantOverride("separation", 8);
        body.AddChild(mainColumn);

        var sideColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(260, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.7f
        };
        sideColumn.AddThemeConstantOverride("separation", 8);
        body.AddChild(sideColumn);

        mainColumn.AddChild(ScreenStyles.Panel("Team Snapshot", BuildTeamSnapshotPanel(team), _selectedRoster?.Name ?? team.RosterId));
        mainColumn.AddChild(ScreenStyles.Panel("Team Assets", BuildAssetPanel(), "Staff and sideline"));
        mainColumn.AddChild(ScreenStyles.Panel("Team Actions", BuildTeamActionsPanel(team), RosterAttentionBadge(team), ScreenStyles.Warning));

        sideColumn.AddChild(ScreenStyles.Panel("Team Value", BuildEditValuePanel(team), "Preview"));
        sideColumn.AddChild(ScreenStyles.Panel("Roster Summary", BuildRosterSummaryPanel(team)));
        sideColumn.AddChild(ScreenStyles.Panel("Save Status", BuildStatusPanel(), "Changed", ScreenStyles.Warning));

        _rerollsSpin.Value = team.Rerolls;
        _fanFactorSpin.Value = team.FanFactor;
        _cheerleadersSpin.Value = team.Cheerleaders;
        _assistantCoachesSpin.Value = team.AssistantCoaches;
        _apothecariesSpin.Value = team.Apothecaries;
        UpdateDraftSummary();
    }

    private void AddScreenHeader(string title, string primaryAction, Func<Task> save, Action back)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var headerStyle = ScreenStyles.FlatStyle(new Color("202720"), ScreenStyles.PanelBorderSoft);
        headerStyle.SetContentMarginAll(6);
        panel.AddThemeStyleboxOverride("panel", headerStyle);
        AddChild(panel);

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);

        var titleLabel = new Label
        {
            Text = title,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 24);
        titleLabel.AddThemeColorOverride("font_color", ScreenStyles.Text);
        row.AddChild(titleLabel);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 8);
        row.AddChild(actions);

        var texture = GD.Load<Texture2D>(ScreenStyles.TeamManagementTexturePath);
        if (texture is not null)
        {
            var stamp = new TextureRect
            {
                Texture = texture,
                CustomMinimumSize = new Vector2(64, 36),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                Modulate = new Color(1, 1, 1, 0.28f)
            };
            actions.AddChild(stamp);
        }

        var backButton = ScreenStyles.StyledButton("Back");
        backButton.Pressed += back;
        actions.AddChild(backButton);

        _saveButton = ScreenStyles.StyledButton(primaryAction, primary: true, disabled: true);
        _saveButton.Pressed += async () => await save();
        actions.AddChild(_saveButton);
    }

    private Control BuildIdentityPanel(string teamName, string coachName, bool showRosterPicker)
    {
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 12);
        grid.AddThemeConstantOverride("v_separation", 8);

        grid.AddChild(ScreenStyles.MutedLabel("Team Name"));
        _teamNameEdit = AddLineEdit(grid, "Team name", _editingTeam?.Name ?? teamName);

        grid.AddChild(ScreenStyles.MutedLabel("Coach"));
        _coachNameEdit = AddLineEdit(grid, "Coach name", _editingTeam?.CoachName ?? coachName);

        if (showRosterPicker)
        {
            grid.AddChild(ScreenStyles.MutedLabel("Roster"));
            _rosterOption = new OptionButton();
            _rosterOption.ItemSelected += _ => SelectRosterFromOption();
            grid.AddChild(_rosterOption);
        }

        return grid;
    }

    private Control BuildTeamSnapshotPanel(LeagueTeam team)
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 10);
        stack.AddChild(BuildIdentityPanel(team.Name, team.CoachName, showRosterPicker: false));

        var stats = new GridContainer { Columns = 4, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stats.AddThemeConstantOverride("h_separation", 8);
        stats.AddThemeConstantOverride("v_separation", 8);
        stats.AddChild(Metric("TV", FormatGold(team.TeamValue)));
        stats.AddChild(Metric("Treasury", FormatGold(team.Treasury)));
        stats.AddChild(Metric("Players", team.Players.Count.ToString()));
        stats.AddChild(Metric("Record", "0-0-0"));
        stack.AddChild(stats);

        return stack;
    }

    private Control BuildPositionDraftPanel()
    {
        _positionGrid = new GridContainer { Columns = 6, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _positionGrid.AddThemeConstantOverride("h_separation", 6);
        _positionGrid.AddThemeConstantOverride("v_separation", 4);
        return _positionGrid;
    }

    private Control BuildAssetPanel()
    {
        var grid = new GridContainer { Columns = 5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 10);

        _rerollCostLabel = ScreenStyles.MutedLabel("Select a roster");
        _rerollsSpin = CreateSpinBox(0, _ruleset.RerollCap, _editingTeam?.Rerolls ?? 2);
        grid.AddChild(AssetControl("Rerolls", _rerollCostLabel, _rerollsSpin));

        _fanFactorSpin = CreateSpinBox(1, 9, _editingTeam?.FanFactor ?? 1);
        grid.AddChild(AssetControl("Fan Factor", ScreenStyles.MutedLabel(FormatGold(FanFactorCost)), _fanFactorSpin));

        _assistantCoachesSpin = CreateSpinBox(0, 12, _editingTeam?.AssistantCoaches ?? 0);
        grid.AddChild(AssetControl("Assistants", ScreenStyles.MutedLabel(FormatGold(AssistantCoachCost)), _assistantCoachesSpin));

        _cheerleadersSpin = CreateSpinBox(0, 12, _editingTeam?.Cheerleaders ?? 0);
        grid.AddChild(AssetControl("Cheerleaders", ScreenStyles.MutedLabel(FormatGold(CheerleaderCost)), _cheerleadersSpin));

        _apothecariesSpin = CreateSpinBox(0, 1, _editingTeam?.Apothecaries ?? 0);
        grid.AddChild(AssetControl("Apothecary", ScreenStyles.MutedLabel(FormatGold(ApothecaryCost)), _apothecariesSpin));

        return grid;
    }

    private Control BuildBudgetPanel()
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(BudgetRow("Starting", FormatGold(_ruleset.StartingTreasury)));
        _budgetPlayersLabel = AddBudgetValue(stack, "Players");
        _budgetRerollsLabel = AddBudgetValue(stack, "Rerolls");
        _budgetStaffLabel = AddBudgetValue(stack, "Staff");
        _budgetRemainingLabel = AddBudgetValue(stack, "Remaining");
        return stack;
    }

    private Control BuildEditValuePanel(LeagueTeam team)
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(BudgetRow("Current TV", FormatGold(team.TeamValue)));
        _budgetPlayersLabel = AddBudgetValue(stack, "Players");
        _budgetRerollsLabel = AddBudgetValue(stack, "Rerolls");
        _budgetStaffLabel = AddBudgetValue(stack, "Staff");
        _budgetRemainingLabel = AddBudgetValue(stack, "New TV");
        return stack;
    }

    private Control BuildStatusPanel()
    {
        _statusLabel = new Label
        {
            Text = _editingTeam is null ? "Build a legal roster before saving." : "Review team-level changes before saving.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _statusLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        return _statusLabel;
    }

    private Control BuildRosterPreviewPanel()
    {
        _rosterPreviewLabel = new Label
        {
            Text = "Select a roster to preview position counts.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _rosterPreviewLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        return _rosterPreviewLabel;
    }

    private Control BuildRosterSummaryPanel(LeagueTeam team)
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(BudgetRow("Ready players", team.Players.Count(player => player.Status == PlayerStatus.Available).ToString()));
        stack.AddChild(BudgetRow("Missing next game", team.Players.Count(player => player.Status == PlayerStatus.MissNextGame).ToString()));
        stack.AddChild(BudgetRow("Can level up", team.Players.Count(CanLevelUp).ToString()));
        var openButton = ScreenStyles.StyledButton("Open Roster", primary: true);
        openButton.Pressed += () => _openRoster(team.Id);
        stack.AddChild(openButton);
        return stack;
    }

    private Control BuildTeamActionsPanel(LeagueTeam team)
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 10);
        stack.AddChild(ActionRow("Open Roster", "Manage player names, numbers, SPP spending, level-ups, injuries, and retirements.", () => _openRoster(team.Id), primary: true));
        stack.AddChild(ActionRow("Transactions", "Hire and fire player flow will live here once treasury purchases are split from setup drafting.", () => SetStatus("Transactions are planned as a dedicated follow-up flow.")));
        return stack;
    }

    private Control ActionRow(string title, string detail, Action pressed, bool primary = false)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);

        var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        copy.AddChild(new Label { Text = title });
        copy.AddChild(ScreenStyles.MutedLabel(detail));
        row.AddChild(copy);

        var button = ScreenStyles.StyledButton(title, primary);
        button.Pressed += pressed;
        row.AddChild(button);
        return row;
    }

    private Control AssetControl(string title, Label costLabel, SpinBox spinBox)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(120, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(new Color("181e1a"), ScreenStyles.PanelBorderSoft));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 6);
        panel.AddChild(stack);
        stack.AddChild(ScreenStyles.MutedLabel(title));
        stack.AddChild(costLabel);
        stack.AddChild(spinBox);
        return panel;
    }

    private Control Metric(string label, string value)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(new Color("171d19"), ScreenStyles.PanelBorderSoft));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 3);
        panel.AddChild(stack);
        stack.AddChild(ScreenStyles.MutedLabel(label.ToUpperInvariant()));
        var valueLabel = new Label { Text = value };
        valueLabel.AddThemeColorOverride("font_color", ScreenStyles.Text);
        stack.AddChild(valueLabel);
        return panel;
    }

    private Label AddBudgetValue(VBoxContainer stack, string label)
    {
        var value = new Label
        {
            Text = "0 gp",
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        stack.AddChild(BudgetRow(label, value));
        return value;
    }

    private Control BudgetRow(string label, string value)
    {
        var valueLabel = new Label
        {
            Text = value,
            HorizontalAlignment = HorizontalAlignment.Right,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        return BudgetRow(label, valueLabel);
    }

    private Control BudgetRow(string label, Label value)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(ScreenStyles.MutedLabel(label));
        value.AddThemeColorOverride("font_color", ScreenStyles.Text);
        row.AddChild(value);
        return row;
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

        _rosterOption.Selected = 0;
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
        AddPositionHeader("Stats");
        AddPositionHeader("Skills", expand: true);
        AddPositionHeader("Count");

        foreach (var position in _selectedRoster.Positions)
        {
            _positionGrid.AddChild(new Label { Text = $"{position.Min}-{position.Max}" });
            _positionGrid.AddChild(new Label { Text = position.Name });
            _positionGrid.AddChild(new Label { Text = FormatGold(position.Cost) });
            _positionGrid.AddChild(new Label { Text = FormatStats(position.Stats) });
            _positionGrid.AddChild(new Label
            {
                Text = position.StartingSkills.Count == 0 ? "-" : string.Join(", ", position.StartingSkills),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(140, 0)
            });

            var defaultCount = position.Id == "lineman" ? 11 : position.Min;
            var count = CreateSpinBox(position.Min, position.Max, defaultCount);
            _positionCounts[position.Id] = count;
            _positionGrid.AddChild(count);
        }
    }

    private async Task SaveDraftAsync()
    {
        if (_selectedRoster is null)
        {
            SetStatus("Choose a roster before saving.");
            return;
        }

        var request = new TeamDraftRequest(
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

    private async Task SaveManagementAsync()
    {
        if (_editingTeam is null || _selectedRoster is null)
        {
            SetStatus("Team data is not ready.");
            return;
        }

        var request = new TeamManagementRequest(
            _editingTeam.Id,
            _teamNameEdit.Text,
            _coachNameEdit.Text,
            _selectedRoster,
            (int)_rerollsSpin.Value,
            (int)_fanFactorSpin.Value,
            (int)_cheerleadersSpin.Value,
            (int)_assistantCoachesSpin.Value,
            (int)_apothecariesSpin.Value);

        await _saveManagement(request);
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

    private void UpdateDraftSummary()
    {
        if (_selectedRoster is null)
        {
            return;
        }

        var (playerCount, playerCost) = _editingTeam is null
            ? DraftPlayerSummary(_selectedRoster)
            : ExistingPlayerSummary(_selectedRoster, _editingTeam);

        var rerollCost = (int)_rerollsSpin.Value * _selectedRoster.RerollCost;
        var fanFactorCost = Math.Max(0, (int)_fanFactorSpin.Value - 1) * FanFactorCost;
        var staffCost = ((int)_cheerleadersSpin.Value * CheerleaderCost) +
            ((int)_assistantCoachesSpin.Value * AssistantCoachCost) +
            ((int)_apothecariesSpin.Value * ApothecaryCost);
        var totalCost = playerCost + rerollCost + fanFactorCost + staffCost;
        var treasury = _ruleset.StartingTreasury - totalCost;
        var isReady = _editingTeam is not null || (playerCount >= _ruleset.PlayersPerSide && playerCount <= MaximumRosterPlayers && treasury >= 0);

        _budgetPlayersLabel.Text = FormatGold(playerCost);
        _budgetRerollsLabel.Text = FormatGold(rerollCost);
        _budgetStaffLabel.Text = FormatGold(fanFactorCost + staffCost);
        _budgetRemainingLabel.Text = _editingTeam is null ? FormatGold(treasury) : FormatGold(totalCost);

        if (_rosterPreviewLabel is not null)
        {
            _rosterPreviewLabel.Text = string.Join("\n", _selectedRoster.Positions
                .Select(position => (Position: position, Count: _positionCounts.TryGetValue(position.Id, out var spinBox) ? (int)spinBox.Value : 0))
                .Where(row => row.Count > 0)
                .Select(row => $"{row.Position.Name} x{row.Count}"));
        }

        if (_statusLabel is not null)
        {
            _statusLabel.Text = _editingTeam is null
                ? isReady
                    ? $"Ready: {playerCount} players, team value {FormatGold(totalCost)}, treasury {FormatGold(treasury)}."
                    : $"Needs work: {playerCount} players ({_ruleset.PlayersPerSide}-{MaximumRosterPlayers}), team value {FormatGold(totalCost)}, treasury {FormatGold(treasury)}."
                : $"Legal: roster remains {playerCount} players. New team value preview is {FormatGold(totalCost)}.";
        }

        _saveButton.Disabled = !isReady;
        if (_editingTeam is null)
        {
            UpdateAffordableMaximums(treasury, playerCount);
        }
    }

    private (int PlayerCount, int PlayerCost) DraftPlayerSummary(TeamRoster roster)
    {
        var playerCount = 0;
        var playerCost = 0;
        foreach (var position in roster.Positions)
        {
            var count = _positionCounts.TryGetValue(position.Id, out var spinBox) ? (int)spinBox.Value : 0;
            playerCount += count;
            playerCost += count * position.Cost;
        }

        return (playerCount, playerCost);
    }

    private static (int PlayerCount, int PlayerCost) ExistingPlayerSummary(TeamRoster roster, LeagueTeam? team)
    {
        if (team is null)
        {
            return (0, 0);
        }

        var cost = team.Players.Sum(player => roster.Positions.FirstOrDefault(position => string.Equals(position.Id, player.PositionId, StringComparison.OrdinalIgnoreCase))?.Cost ?? 0);
        return (team.Players.Count, cost);
    }

    private void UpdateAffordableMaximums(int treasury, int playerCount)
    {
        if (_selectedRoster is null)
        {
            return;
        }

        SetAffordableMax(_rerollsSpin, _ruleset.RerollCap, _selectedRoster.RerollCost, treasury);
        SetAffordableMax(_fanFactorSpin, 9, FanFactorCost, treasury);
        SetAffordableMax(_cheerleadersSpin, 12, CheerleaderCost, treasury);
        SetAffordableMax(_assistantCoachesSpin, 12, AssistantCoachCost, treasury);
        SetAffordableMax(_apothecariesSpin, 1, ApothecaryCost, treasury);

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

    private static void SetAffordableMax(SpinBox spinBox, double cap, int cost, int treasury)
    {
        var currentValue = (int)spinBox.Value;
        spinBox.MaxValue = treasury >= cost ? cap : currentValue;
    }

    private LineEdit AddLineEdit(Container parent, string placeholder, string text)
    {
        var edit = new LineEdit
        {
            PlaceholderText = placeholder,
            Text = text,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        edit.TextChanged += _ => UpdateDraftSummary();
        parent.AddChild(edit);
        return edit;
    }

    private void AddPositionHeader(string text, bool expand = false)
    {
        var label = ScreenStyles.MutedLabel(text.ToUpperInvariant());
        label.AddThemeColorOverride("font_color", ScreenStyles.Brass);
        if (expand)
        {
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }
        _positionGrid.AddChild(label);
    }

    private void UpdateRerollCostLabel()
    {
        if (_selectedRoster is not null && _rerollCostLabel is not null)
        {
            _rerollCostLabel.Text = FormatGold(_selectedRoster.RerollCost);
        }
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
            AllowLesser = false,
            CustomMinimumSize = new Vector2(88, 30)
        };
        spinBox.ValueChanged += _ => UpdateDraftSummary();
        return spinBox;
    }

    private TeamRoster? FindRoster(string rosterId)
    {
        return _rosterSet.Rosters.FirstOrDefault(roster => string.Equals(roster.Id, rosterId, StringComparison.OrdinalIgnoreCase));
    }

    private bool CanLevelUp(Player player)
    {
        if (_selectedRoster is null)
        {
            return false;
        }

        var position = _selectedRoster.Positions.FirstOrDefault(current => string.Equals(current.Id, player.PositionId, StringComparison.OrdinalIgnoreCase));
        if (position is null)
        {
            return false;
        }

        // BB2020: a player can advance once they can afford the cheapest option, a Randomly
        // Selected Primary skill.
        return _ruleset.AdvancementThresholds.TryGetValue("randomPrimary", out var cost) && player.StarPlayerPoints >= cost;
    }

    private string RosterAttentionBadge(LeagueTeam team)
    {
        var canLevel = team.Players.Count(CanLevelUp);
        return canLevel > 0 ? $"{canLevel} can level" : "Open roster";
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }

    private static string FormatStats(PlayerStats stats)
    {
        return $"{stats.Movement} {stats.Strength} {stats.Agility}+ {stats.Passing}+ {stats.Armor}+";
    }

    private static string FormatGold(int value)
    {
        return $"{value:N0} gp";
    }
}
