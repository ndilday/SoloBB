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
    int DedicatedFans,
    int Cheerleaders,
    int AssistantCoaches,
    int Apothecaries);

public sealed record TeamManagementRequest(
    Guid TeamId,
    string TeamName,
    string CoachName,
    TeamRoster Roster,
    int Rerolls,
    int DedicatedFans,
    int Cheerleaders,
    int AssistantCoaches,
    int Apothecaries);

public partial class TeamCreationScreen : VBoxContainer
{
    private const int MaximumRosterPlayers = 16;
    private const int DedicatedFanCost = 10_000;
    private const int CheerleaderCost = 10_000;
    private const int AssistantCoachCost = 10_000;
    private const int ApothecaryCost = 50_000;

    private readonly Dictionary<string, SpinBox> _positionCounts = new(StringComparer.OrdinalIgnoreCase);

    private Ruleset _ruleset = null!;
    private RosterSet _rosterSet = null!;
    private League? _league;
    private TeamRoster? _selectedRoster;
    private LeagueTeam? _editingTeam;
    private string _latestSeasonName = "";
    private Func<TeamDraftRequest, Task> _saveTeam = _ => Task.CompletedTask;
    private Func<TeamManagementRequest, Task> _saveManagement = _ => Task.CompletedTask;
    private Func<Guid, string, Task> _renamePlayer = (_, _) => Task.CompletedTask;
    private Func<Guid, string, Task> _purchaseSelectedSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, bool, Task> _purchaseRandomSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, Task<CharacteristicAdvancementRoll>> _rollCharacteristic =
        _ => Task.FromResult(new CharacteristicAdvancementRoll(0, 0, Array.Empty<PlayerCharacteristic>()));
    private Func<Guid, int, PlayerCharacteristic, Task> _applyCharacteristic = (_, _, _) => Task.CompletedTask;
    private Func<Guid, string, Task> _applyCharacteristicSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, bool, Task> _movePlayer = (_, _) => Task.CompletedTask;

    private LineEdit _teamNameEdit = null!;
    private LineEdit _coachNameEdit = null!;
    private OptionButton _rosterOption = null!;
    private Label _rerollCostLabel = null!;
    private SpinBox _rerollsSpin = null!;
    private SpinBox _dedicatedFansSpin = null!;
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
    private Tree _playerTree = null!;
    private Label _playerDetailTitle = null!;
    private Label _playerDetailMeta = null!;
    private Label _playerStatsLabel = null!;
    private Label _playerSppLabel = null!;
    private Label _playerSkillsLabel = null!;
    private LineEdit _playerNameEdit = null!;
    private Button _playerNameButton = null!;
    private Guid? _playerDetailPlayerId;
    private Button _spendSppButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;
    private Button _playerHistoryButton = null!;
    private AcceptDialog _developmentDialog = null!;
    private AcceptDialog _previousPlayersDialog = null!;
    private AcceptDialog _playerHistoryDialog = null!;
    private MarginContainer _playerHistoryContent = null!;
    private Label _developmentDialogLabel = null!;
    private OptionButton _skillOption = null!;
    private OptionButton _secondarySkillOption = null!;
    private Button _selectedSkillButton = null!;
    private Button _selectedSecondarySkillButton = null!;
    private Button _randomSkillButton = null!;
    private Button _randomSecondaryButton = null!;
    private Button _characteristicButton = null!;
    private RichTextLabel _primarySkillsLabel = null!;
    private RichTextLabel _secondarySkillsLabel = null!;

    public void Setup(
        Ruleset ruleset,
        RosterSet rosterSet,
        string defaultTeamName,
        Func<TeamDraftRequest, Task> saveTeam,
        Action back,
        League? league = null,
        string latestSeasonName = "",
        LeagueTeam? editingTeam = null,
        TeamRecord? record = null,
        Func<TeamManagementRequest, Task>? saveManagement = null,
        Func<Guid, string, Task>? renamePlayer = null,
        Func<Guid, string, Task>? purchaseSelectedSkill = null,
        Func<Guid, bool, Task>? purchaseRandomSkill = null,
        Func<Guid, Task<CharacteristicAdvancementRoll>>? rollCharacteristic = null,
        Func<Guid, int, PlayerCharacteristic, Task>? applyCharacteristic = null,
        Func<Guid, string, Task>? applyCharacteristicSkill = null,
        Func<Guid, bool, Task>? movePlayer = null)
    {
        Clear();
        AddThemeConstantOverride("separation", 8);
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));

        _ruleset = ruleset;
        _rosterSet = rosterSet;
        _league = league;
        _editingTeam = editingTeam;
        _latestSeasonName = latestSeasonName;
        _saveTeam = saveTeam;
        _saveManagement = saveManagement ?? (_ => Task.CompletedTask);
        _renamePlayer = renamePlayer ?? ((_, _) => Task.CompletedTask);
        _purchaseSelectedSkill = purchaseSelectedSkill ?? ((_, _) => Task.CompletedTask);
        _purchaseRandomSkill = purchaseRandomSkill ?? ((_, _) => Task.CompletedTask);
        _rollCharacteristic = rollCharacteristic ??
            (_ => Task.FromResult(new CharacteristicAdvancementRoll(0, 0, Array.Empty<PlayerCharacteristic>())));
        _applyCharacteristic = applyCharacteristic ?? ((_, _, _) => Task.CompletedTask);
        _applyCharacteristicSkill = applyCharacteristicSkill ?? ((_, _) => Task.CompletedTask);
        _movePlayer = movePlayer ?? ((_, _) => Task.CompletedTask);

        if (editingTeam is null)
        {
            BuildCreateLayout(defaultTeamName, back);
        }
        else
        {
            BuildEditLayout(editingTeam, record ?? new TeamRecord(0, 0, 0), back);
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

    private void BuildEditLayout(LeagueTeam team, TeamRecord record, Action back)
    {
        AddScreenHeader(
            $"{team.Name} ({FormatRecord(record)})",
            "Save Changes",
            async () => await SaveManagementAsync(),
            back,
            OpenPreviousPlayers);

        _selectedRoster = FindRoster(team.RosterId);

        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        AddChild(body);

        var mainColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.8f
        };
        mainColumn.AddThemeConstantOverride("separation", 8);
        body.AddChild(mainColumn);

        var sideColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(260, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.7f
        };
        sideColumn.AddThemeConstantOverride("separation", 8);
        body.AddChild(sideColumn);

        mainColumn.AddChild(ScreenStyles.Panel("Team Details", BuildTeamDetailsPanel(team), _selectedRoster?.Name ?? team.RosterId));
        var rosterPanel = ScreenStyles.Panel("Player Roster", BuildTeamRosterPanel(team), RosterAttentionBadge(team), ScreenStyles.Warning);
        rosterPanel.SizeFlagsVertical = SizeFlags.ExpandFill;
        mainColumn.AddChild(rosterPanel);

        sideColumn.AddChild(ScreenStyles.Panel("Team Value", BuildEditValuePanel(team)));
        sideColumn.AddChild(ScreenStyles.Panel("Roster Summary", BuildRosterSummaryPanel(team)));
        sideColumn.AddChild(ScreenStyles.Panel("Player Detail", BuildPlayerDetailPanel()));

        _rerollsSpin.Value = team.Rerolls;
        _dedicatedFansSpin.Value = team.DedicatedFans;
        _cheerleadersSpin.Value = team.Cheerleaders;
        _assistantCoachesSpin.Value = team.AssistantCoaches;
        _apothecariesSpin.Value = team.Apothecaries;
        UpdateDraftSummary();

        _developmentDialog = BuildDevelopmentDialog();
        AddChild(_developmentDialog);
        _previousPlayersDialog = BuildPreviousPlayersDialog();
        AddChild(_previousPlayersDialog);
        _playerHistoryDialog = BuildPlayerHistoryDialog();
        AddChild(_playerHistoryDialog);
        SelectFirstPlayer();
    }

    private void AddScreenHeader(
        string title,
        string primaryAction,
        Func<Task> save,
        Action back,
        Action? previousPlayers = null)
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

        if (previousPlayers is not null)
        {
            var previousButton = ScreenStyles.StyledButton("Previous Players");
            previousButton.Pressed += previousPlayers;
            actions.AddChild(previousButton);
        }

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

    private Control BuildTeamDetailsPanel(LeagueTeam team)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(BuildTeamIdentityColumn(team));
        AddAssetControls(row, compact: true);
        return row;
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
        AddAssetControls(grid, compact: false);
        return grid;
    }

    private void AddAssetControls(Container parent, bool compact)
    {
        _rerollCostLabel = ScreenStyles.MutedLabel(_selectedRoster is null ? "Select a roster" : FormatGold(_selectedRoster.RerollCost));
        _rerollsSpin = CreateSpinBox(0, _ruleset.RerollCap, _editingTeam?.Rerolls ?? 2);
        parent.AddChild(AssetControl("Rerolls", _rerollCostLabel, _rerollsSpin, compact));

        _dedicatedFansSpin = CreateSpinBox(1, 9, _editingTeam?.DedicatedFans ?? 1);
        parent.AddChild(AssetControl("Dedicated Fans", ScreenStyles.MutedLabel(FormatGold(DedicatedFanCost)), _dedicatedFansSpin, compact));

        _assistantCoachesSpin = CreateSpinBox(0, 12, _editingTeam?.AssistantCoaches ?? 0);
        parent.AddChild(AssetControl("Assistants", ScreenStyles.MutedLabel(FormatGold(AssistantCoachCost)), _assistantCoachesSpin, compact));

        _cheerleadersSpin = CreateSpinBox(0, 12, _editingTeam?.Cheerleaders ?? 0);
        parent.AddChild(AssetControl("Cheerleaders", ScreenStyles.MutedLabel(FormatGold(CheerleaderCost)), _cheerleadersSpin, compact));

        _apothecariesSpin = CreateSpinBox(0, 1, _editingTeam?.Apothecaries ?? 0);
        parent.AddChild(AssetControl("Apothecary", ScreenStyles.MutedLabel(FormatGold(ApothecaryCost)), _apothecariesSpin, compact));
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
        return stack;
    }

    private Control BuildTeamRosterPanel(LeagueTeam team)
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);

        _playerTree = new Tree
        {
            Columns = 7,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SelectMode = Tree.SelectModeEnum.Row,
            CustomMinimumSize = new Vector2(680, 240),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _playerTree.SetColumnTitle(0, "#");
        _playerTree.SetColumnTitle(1, "Player");
        _playerTree.SetColumnTitle(2, "Position");
        _playerTree.SetColumnTitle(3, "Title");
        _playerTree.SetColumnTitle(4, "Status");
        _playerTree.SetColumnTitle(5, "SPP");
        _playerTree.SetColumnTitle(6, "Skills");

        int[] columnExpandRatios = [0, 4, 3, 2, 0, 0, 5];
        for (var column = 0; column < _playerTree.Columns; column++)
        {
            var ratio = columnExpandRatios[column];
            var expands = ratio > 0;
            _playerTree.SetColumnExpand(column, expands);
            if (expands)
            {
                _playerTree.SetColumnExpandRatio(column, ratio);
            }

            _playerTree.SetColumnTitleAlignment(
                column,
                column is 0 or 5 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        }

        _playerTree.SetColumnCustomMinimumWidth(0, 42);
        _playerTree.SetColumnCustomMinimumWidth(1, 160);
        _playerTree.SetColumnCustomMinimumWidth(2, 120);
        _playerTree.SetColumnCustomMinimumWidth(3, 92);
        _playerTree.SetColumnCustomMinimumWidth(4, 82);
        _playerTree.SetColumnCustomMinimumWidth(5, 48);
        _playerTree.SetColumnCustomMinimumWidth(6, 220);
        _playerTree.AddThemeConstantOverride("h_separation", 10);
        _playerTree.AddThemeConstantOverride("v_separation", 5);
        _playerTree.ItemSelected += UpdatePlayerDetail;

        var root = _playerTree.CreateItem();
        foreach (var player in team.Players.Where(IsCurrentPlayer).OrderBy(player => player.Number))
        {
            var item = _playerTree.CreateItem(root);
            item.SetText(0, player.Number.ToString());
            item.SetText(1, player.Name);
            item.SetText(2, PositionName(player.PositionId));
            item.SetText(3, LeagueService.PlayerTitle(_selectedRoster!, player));
            item.SetText(4, FormatStatus(player.Status));
            item.SetText(5, player.StarPlayerPoints.ToString());
            item.SetText(6, FormatSkills(player));
            item.SetMetadata(0, Variant.From(player.Id.ToString()));
            if (CanLevelUp(player))
            {
                item.SetCustomColor(5, ScreenStyles.Brass);
                item.SetTooltipText(5, "This player can spend SPP.");
            }

            for (var column = 0; column < _playerTree.Columns; column++)
            {
                item.SetTextAlignment(
                    column,
                    column is 0 or 5 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            }

            item.SetCustomMinimumHeight(28);
        }

        stack.AddChild(_playerTree);
        return stack;
    }

    private Control BuildPlayerDetailPanel()
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);

        _playerDetailTitle = new Label { Text = "Select a player" };
        _playerDetailTitle.AddThemeFontSizeOverride("font_size", 18);
        _playerDetailTitle.AddThemeColorOverride("font_color", ScreenStyles.Text);
        stack.AddChild(_playerDetailTitle);

        _playerDetailMeta = ScreenStyles.MutedLabel("");
        stack.AddChild(_playerDetailMeta);

        var nameRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nameRow.AddThemeConstantOverride("separation", 8);
        nameRow.AddChild(ScreenStyles.MutedLabel("Name"));
        _playerNameEdit = new LineEdit
        {
            Editable = false,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        nameRow.AddChild(_playerNameEdit);
        _playerNameButton = ScreenStyles.StyledButton("Edit");
        _playerNameButton.Pressed += async () => await TogglePlayerNameEditAsync();
        nameRow.AddChild(_playerNameButton);
        stack.AddChild(nameRow);

        _playerStatsLabel = new Label { Text = "MA -   ST -   AG -   PA -   AV -" };
        _playerStatsLabel.AddThemeColorOverride("font_color", ScreenStyles.Text);
        stack.AddChild(_playerStatsLabel);

        _playerSkillsLabel = new Label
        {
            Text = "Skills: -",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _playerSkillsLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        stack.AddChild(_playerSkillsLabel);

        _playerSppLabel = ScreenStyles.MutedLabel("");
        stack.AddChild(_playerSppLabel);

        _spendSppButton = ScreenStyles.StyledButton("Spend SPP", primary: true, disabled: true);
        _spendSppButton.Pressed += OpenDevelopmentForSelectedPlayer;
        stack.AddChild(_spendSppButton);

        var moveRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        moveRow.AddThemeConstantOverride("separation", 8);
        _moveUpButton = ScreenStyles.StyledButton("Move Up", disabled: true);
        _moveUpButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _moveUpButton.Pressed += async () => await MoveSelectedAsync(up: true);
        moveRow.AddChild(_moveUpButton);
        _moveDownButton = ScreenStyles.StyledButton("Move Down", disabled: true);
        _moveDownButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _moveDownButton.Pressed += async () => await MoveSelectedAsync(up: false);
        moveRow.AddChild(_moveDownButton);
        stack.AddChild(moveRow);

        _playerHistoryButton = ScreenStyles.StyledButton("Player History", disabled: true);
        _playerHistoryButton.Pressed += OpenPlayerHistory;
        stack.AddChild(_playerHistoryButton);

        return stack;
    }

    private Control BuildTeamIdentityColumn(LeagueTeam team)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(230, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.7f
        };
        panel.AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(new Color("181e1a"), ScreenStyles.PanelBorderSoft));

        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", 8);
        grid.AddThemeConstantOverride("v_separation", 4);
        panel.AddChild(grid);

        grid.AddChild(CompactDetailLabel("Team Name"));
        _teamNameEdit = AddLineEdit(grid, "Team name", team.Name);
        _teamNameEdit.CustomMinimumSize = new Vector2(130, 28);
        grid.AddChild(CompactDetailLabel("Coach"));
        _coachNameEdit = AddLineEdit(grid, "Coach name", team.CoachName);
        _coachNameEdit.CustomMinimumSize = new Vector2(130, 28);
        grid.AddChild(CompactDetailLabel("Treasury"));
        grid.AddChild(DetailValue(FormatGold(team.Treasury)));
        return panel;
    }

    private Control AssetControl(string title, Label costLabel, SpinBox spinBox, bool compact = false)
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(compact ? 112 : 120, 0),
            SizeFlagsHorizontal = compact ? SizeFlags.Fill : SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(new Color("181e1a"), ScreenStyles.PanelBorderSoft));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", compact ? 3 : 6);
        panel.AddChild(stack);
        stack.AddChild(ScreenStyles.MutedLabel(title));
        stack.AddChild(costLabel);
        if (compact)
        {
            spinBox.CustomMinimumSize = new Vector2(78, 28);
        }

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

    private static Label DetailValue(string text)
    {
        var label = new Label
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.AddThemeColorOverride("font_color", ScreenStyles.Text);
        return label;
    }

    private static Label CompactDetailLabel(string text)
    {
        var label = ScreenStyles.MutedLabel(text);
        label.VerticalAlignment = VerticalAlignment.Center;
        label.CustomMinimumSize = new Vector2(72, 28);
        return label;
    }

    private AcceptDialog BuildDevelopmentDialog()
    {
        var popup = new AcceptDialog
        {
            Title = "Player Development",
            Unresizable = false,
            MinSize = new Vector2I(720, 620)
        };
        popup.GetOkButton().Text = "Close";

        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        popup.AddChild(margin);

        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 10);
        margin.AddChild(stack);

        _developmentDialogLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _developmentDialogLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        stack.AddChild(_developmentDialogLabel);

        stack.AddChild(DevelopmentHeading("Random skill"));
        stack.AddChild(DevelopmentHelp("Roll from a Primary or Secondary category. The result is chosen for you."));

        var randomRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        randomRow.AddThemeConstantOverride("separation", 8);
        _randomSkillButton = ScreenStyles.StyledButton("Random Primary", primary: true);
        _randomSkillButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _randomSkillButton.Pressed += async () => await PurchaseRandomSkillAsync(secondary: false);
        randomRow.AddChild(_randomSkillButton);

        _randomSecondaryButton = ScreenStyles.StyledButton("Random Secondary");
        _randomSecondaryButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _randomSecondaryButton.Pressed += async () => await PurchaseRandomSkillAsync(secondary: true);
        randomRow.AddChild(_randomSecondaryButton);
        stack.AddChild(randomRow);

        stack.AddChild(new HSeparator());
        stack.AddChild(DevelopmentHeading("Choose a skill"));
        stack.AddChild(DevelopmentHelp("Select the exact skill to add. Secondary skills cost more SPP."));

        var chosenSkillRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        chosenSkillRow.AddThemeConstantOverride("separation", 8);
        chosenSkillRow.AddChild(DevelopmentChoiceLabel("Primary"));
        _skillOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        chosenSkillRow.AddChild(_skillOption);

        _selectedSkillButton = ScreenStyles.StyledButton("Buy");
        _selectedSkillButton.Pressed += async () => await PurchaseSelectedSkillAsync(_skillOption);
        chosenSkillRow.AddChild(_selectedSkillButton);
        stack.AddChild(chosenSkillRow);

        var chosenSecondaryRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        chosenSecondaryRow.AddThemeConstantOverride("separation", 8);
        chosenSecondaryRow.AddChild(DevelopmentChoiceLabel("Secondary"));
        _secondarySkillOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        chosenSecondaryRow.AddChild(_secondarySkillOption);

        _selectedSecondarySkillButton = ScreenStyles.StyledButton("Buy");
        _selectedSecondarySkillButton.Pressed += async () => await PurchaseSelectedSkillAsync(_secondarySkillOption);
        chosenSecondaryRow.AddChild(_selectedSecondarySkillButton);
        stack.AddChild(chosenSecondaryRow);

        stack.AddChild(new HSeparator());
        stack.AddChild(DevelopmentHeading("Characteristic"));
        stack.AddChild(DevelopmentHelp("Roll on the Characteristic Improvement table, then choose an available result."));
        _characteristicButton = ScreenStyles.StyledButton("Improve Characteristic");
        _characteristicButton.Pressed += async () => await ImproveCharacteristicAsync();
        stack.AddChild(_characteristicButton);

        stack.AddChild(new HSeparator());
        stack.AddChild(DevelopmentHeading("Available skills"));

        var availableSkills = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        availableSkills.AddThemeConstantOverride("separation", 10);
        _primarySkillsLabel = new RichTextLabel();
        _secondarySkillsLabel = new RichTextLabel();
        availableSkills.AddChild(DevelopmentSkillColumn("Primary", _primarySkillsLabel));
        availableSkills.AddChild(DevelopmentSkillColumn("Secondary", _secondarySkillsLabel));
        stack.AddChild(availableSkills);

        return popup;
    }

    private static Label DevelopmentHeading(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", ScreenStyles.Text);
        label.AddThemeFontSizeOverride("font_size", 14);
        return label;
    }

    private static Label DevelopmentHelp(string text)
    {
        var label = ScreenStyles.MutedLabel(text);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        return label;
    }

    private static Label DevelopmentChoiceLabel(string text)
    {
        var label = ScreenStyles.MutedLabel(text);
        label.CustomMinimumSize = new Vector2(78, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    private static Control DevelopmentSkillColumn(string heading, RichTextLabel skillsLabel)
    {
        var stack = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f
        };
        stack.AddThemeConstantOverride("separation", 5);
        stack.AddChild(DevelopmentHeading(heading));

        skillsLabel.BbcodeEnabled = true;
        skillsLabel.FitContent = true;
        skillsLabel.ScrollActive = false;
        skillsLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        skillsLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        skillsLabel.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(skillsLabel);

        var margin = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        margin.AddChild(stack);

        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(new Color("181e1a"), ScreenStyles.PanelBorderSoft));
        panel.AddChild(margin);
        return panel;
    }

    private AcceptDialog BuildPreviousPlayersDialog()
    {
        var popup = new AcceptDialog
        {
            Title = "Previous Players",
            Unresizable = false,
            MinSize = new Vector2I(780, 460)
        };
        popup.GetOkButton().Text = "Close";

        var tree = new Tree
        {
            Columns = 5,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        tree.SetColumnTitle(0, "Player");
        tree.SetColumnTitle(1, "Position");
        tree.SetColumnTitle(2, "Title");
        tree.SetColumnTitle(3, "Status");
        tree.SetColumnTitle(4, "SPP");
        tree.SetColumnCustomMinimumWidth(0, 220);
        tree.SetColumnCustomMinimumWidth(1, 140);
        tree.SetColumnCustomMinimumWidth(2, 120);
        tree.SetColumnCustomMinimumWidth(3, 180);
        tree.SetColumnCustomMinimumWidth(4, 52);

        var root = tree.CreateItem();
        var previousPlayers = PreviousPlayers();
        foreach (var player in previousPlayers.OrderBy(player => player.Number))
        {
            var item = tree.CreateItem(root);
            item.SetText(0, $"#{player.Number} {player.Name}");
            item.SetText(1, PositionName(player.PositionId));
            item.SetText(2, _selectedRoster is null ? "" : LeagueService.PlayerTitle(_selectedRoster, player));
            item.SetText(3, FormatPreviousStatus(player));
            item.SetText(4, player.StarPlayerPoints.ToString());
            for (var column = 0; column < tree.Columns; column++)
            {
                item.SetTextAlignment(column, column == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            }

            item.SetCustomMinimumHeight(28);
        }

        if (previousPlayers.Length == 0)
        {
            var item = tree.CreateItem(root);
            item.SetText(0, "No previous players yet.");
            item.SetCustomColor(0, ScreenStyles.MutedText);
        }

        popup.AddChild(tree);
        return popup;
    }

    private AcceptDialog BuildPlayerHistoryDialog()
    {
        var popup = new AcceptDialog
        {
            Title = "Player History",
            Unresizable = false,
            MinSize = new Vector2I(860, 500)
        };
        popup.GetOkButton().Text = "Close";

        _playerHistoryContent = new MarginContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _playerHistoryContent.AddThemeConstantOverride("margin_left", 8);
        _playerHistoryContent.AddThemeConstantOverride("margin_top", 8);
        _playerHistoryContent.AddThemeConstantOverride("margin_right", 8);
        _playerHistoryContent.AddThemeConstantOverride("margin_bottom", 8);
        popup.AddChild(_playerHistoryContent);
        return popup;
    }

    private void OpenPreviousPlayers()
    {
        _previousPlayersDialog.PopupCentered(new Vector2I(780, 500));
    }

    private void OpenPlayerHistory()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        foreach (var child in _playerHistoryContent.GetChildren())
        {
            _playerHistoryContent.RemoveChild(child);
            child.QueueFree();
        }

        _playerHistoryDialog.Title = $"{player.Name} History";
        _playerHistoryContent.AddChild(BuildPlayerHistoryTable(player));
        _playerHistoryDialog.PopupCentered(new Vector2I(860, 500));
    }

    private Control BuildPlayerHistoryTable(Player player)
    {
        var history = PlayerHistory(player);
        var tree = new Tree
        {
            Columns = 6,
            HideRoot = true,
            ColumnTitlesVisible = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        tree.SetColumnTitle(0, "Game");
        tree.SetColumnTitle(1, "Opponent");
        tree.SetColumnTitle(2, "Result");
        tree.SetColumnTitle(3, "TD");
        tree.SetColumnTitle(4, "Cas");
        tree.SetColumnTitle(5, "SPP");
        tree.SetColumnCustomMinimumWidth(0, 110);
        tree.SetColumnCustomMinimumWidth(1, 180);
        tree.SetColumnCustomMinimumWidth(2, 100);
        tree.SetColumnCustomMinimumWidth(3, 48);
        tree.SetColumnCustomMinimumWidth(4, 48);
        tree.SetColumnCustomMinimumWidth(5, 52);

        var root = tree.CreateItem();
        if (history.Length == 0)
        {
            var empty = tree.CreateItem(root);
            empty.SetText(0, "No completed match history yet.");
            empty.SetCustomColor(0, ScreenStyles.MutedText);
            return tree;
        }

        foreach (var row in history)
        {
            var item = tree.CreateItem(root);
            item.SetText(0, row.Game);
            item.SetText(1, row.Opponent);
            item.SetText(2, row.Result);
            item.SetText(3, row.Touchdowns.ToString());
            item.SetText(4, row.Casualties.ToString());
            item.SetText(5, row.StarPlayerPoints.ToString());
            for (var column = 0; column < tree.Columns; column++)
            {
                item.SetTextAlignment(column, column >= 3 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            }

            if (row.StarPlayerPoints > 0)
            {
                item.SetCustomColor(5, ScreenStyles.Brass);
            }

            item.SetCustomMinimumHeight(28);
        }

        return tree;
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
            (int)_dedicatedFansSpin.Value,
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
            (int)_dedicatedFansSpin.Value,
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
        var dedicatedFansCost = Math.Max(0, (int)_dedicatedFansSpin.Value - 1) * DedicatedFanCost;
        var staffCost = ((int)_cheerleadersSpin.Value * CheerleaderCost) +
            ((int)_assistantCoachesSpin.Value * AssistantCoachCost) +
            ((int)_apothecariesSpin.Value * ApothecaryCost);
        var totalCost = playerCost + rerollCost + dedicatedFansCost + staffCost;
        var treasury = _ruleset.StartingTreasury - totalCost;
        var isReady = _editingTeam is not null || (playerCount >= _ruleset.PlayersPerSide && playerCount <= MaximumRosterPlayers && treasury >= 0);

        _budgetPlayersLabel.Text = FormatGold(playerCost);
        _budgetRerollsLabel.Text = FormatGold(rerollCost);
        _budgetStaffLabel.Text = FormatGold(dedicatedFansCost + staffCost);
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
        SetAffordableMax(_dedicatedFansSpin, 9, DedicatedFanCost, treasury);
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

    public void SelectPlayerById(Guid playerId) => SelectPlayer(playerId);

    private void SelectFirstPlayer()
    {
        var root = _playerTree.GetRoot();
        var first = root?.GetFirstChild();
        if (first is not null)
        {
            first.Select(0);
            UpdatePlayerDetail();
        }
    }

    private void SelectPlayer(Guid playerId)
    {
        var root = _playerTree.GetRoot();
        var item = root?.GetFirstChild();
        while (item is not null)
        {
            if (Guid.TryParse(item.GetMetadata(0).AsString(), out var currentId) && currentId == playerId)
            {
                item.Select(0);
                UpdatePlayerDetail();
                return;
            }

            item = item.GetNext();
        }
    }

    private Player? SelectedPlayer()
    {
        var selected = _playerTree.GetSelected();
        if (selected is null || !Guid.TryParse(selected.GetMetadata(0).AsString(), out var playerId))
        {
            return null;
        }

        return _editingTeam?.Players.FirstOrDefault(player => player.Id == playerId);
    }

    private void UpdatePlayerDetail()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            _playerDetailTitle.Text = "Select a player";
            _playerDetailMeta.Text = "";
            _playerDetailPlayerId = null;
            _playerNameEdit.Text = "";
            _playerNameEdit.Editable = false;
            _playerNameButton.Text = "Edit";
            _playerNameButton.Disabled = true;
            _playerStatsLabel.Text = "MA -   ST -   AG -   PA -   AV -";
            _playerSppLabel.Text = "";
            _playerSkillsLabel.Text = "Skills: -";
            _spendSppButton.Disabled = true;
            _moveUpButton.Disabled = true;
            _moveDownButton.Disabled = true;
            _playerHistoryButton.Disabled = true;
            return;
        }

        var position = FindPositionTemplate(player.PositionId);
        var currentPlayers = CurrentPlayers();
        var selectionChanged = _playerDetailPlayerId != player.Id;
        _playerDetailPlayerId = player.Id;
        _playerDetailTitle.Text = $"#{player.Number} {player.Name}";
        _playerDetailMeta.Text = $"{position.Name} - {LeagueService.PlayerTitle(_selectedRoster!, player)} - {FormatStatus(player.Status)}";
        if (selectionChanged || !_playerNameEdit.Editable)
        {
            _playerNameEdit.Text = player.Name;
            _playerNameEdit.Editable = false;
            _playerNameButton.Text = "Edit";
        }

        _playerNameButton.Disabled = false;
        _playerStatsLabel.Text = $"MA {player.Stats.Movement}   ST {player.Stats.Strength}   AG {player.Stats.Agility}+   PA {player.Stats.Passing}+   AV {player.Stats.Armor}+";
        _playerSppLabel.Text = $"{player.StarPlayerPoints} SPP available";
        _playerSkillsLabel.Text = $"Skills: {FormatSkills(player)}";
        _developmentDialogLabel.Text = $"{player.Name} has {player.StarPlayerPoints} SPP available.\nCurrent skills: {FormatSkills(player)}";
        PopulateSkillOptions(player, position);

        var canLevel = CanLevelUp(player);
        _spendSppButton.Disabled = !canLevel;
        _spendSppButton.TooltipText = canLevel
            ? "Open this player's advancement choices."
            : AdvancementTooltip(player, AdvancementCost(player), "an advancement");
        _moveUpButton.Disabled = player.Number <= currentPlayers.Min(current => current.Number);
        _moveDownButton.Disabled = player.Number >= currentPlayers.Max(current => current.Number);
        _playerHistoryButton.Disabled = false;
    }

    private async Task TogglePlayerNameEditAsync()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        if (!_playerNameEdit.Editable)
        {
            _playerNameEdit.Editable = true;
            _playerNameEdit.GrabFocus();
            _playerNameButton.Text = "Save";
            return;
        }

        _playerNameEdit.Editable = false;
        _playerNameButton.Text = "Edit";
        await _renamePlayer(player.Id, _playerNameEdit.Text);
    }

    private async Task MoveSelectedAsync(bool up)
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        await _movePlayer(player.Id, up);
    }

    private void OpenDevelopmentForSelectedPlayer()
    {
        var player = SelectedPlayer();
        if (player is null || !CanLevelUp(player))
        {
            return;
        }

        UpdatePlayerDetail();
        _developmentDialog.Title = $"{player.Name} Development";
        _developmentDialog.PopupCentered(new Vector2I(760, 660));
    }

    private void PopulateSkillOptions(Player player, PositionTemplate position)
    {
        var primarySkills = _ruleset.Skills
            .Where(skill => position.PrimarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase))
            .Where(skill => !skill.DataOnly && !skill.Compulsory)
            .Where(skill => !player.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var secondarySkills = EligibleSecondarySkills(player.Id);

        PopulateSkillOption(_skillOption, primarySkills);
        PopulateSkillOption(_secondarySkillOption, secondarySkills);
        _primarySkillsLabel.Text = FormatAvailableSkills(primarySkills);
        _secondarySkillsLabel.Text = FormatAvailableSkills(secondarySkills);

        var randomPrimaryCost = AdvancementCost("randomPrimary");
        var randomSecondaryCost = AdvancementCost("randomSecondary");
        var chosenPrimaryCost = AdvancementCost("chosenPrimary");
        var chosenSecondaryCost = AdvancementCost("chosenSecondary");

        _randomSkillButton.Text = $"Random Primary ({randomPrimaryCost} SPP)";
        _randomSkillButton.Disabled = player.StarPlayerPoints < randomPrimaryCost;
        _randomSkillButton.TooltipText = AdvancementTooltip(player, randomPrimaryCost, "a Random Primary skill");

        _randomSecondaryButton.Text = $"Random Secondary ({randomSecondaryCost} SPP)";
        _randomSecondaryButton.Disabled = player.StarPlayerPoints < randomSecondaryCost || position.SecondarySkillCategories.Count == 0;
        _randomSecondaryButton.TooltipText = position.SecondarySkillCategories.Count == 0
            ? "This position has no Secondary skill categories."
            : AdvancementTooltip(player, randomSecondaryCost, "a Random Secondary skill");

        _selectedSkillButton.Text = $"Buy ({chosenPrimaryCost} SPP)";
        _selectedSkillButton.Disabled = player.StarPlayerPoints < chosenPrimaryCost || _skillOption.ItemCount == 0;
        _selectedSkillButton.TooltipText = _skillOption.ItemCount == 0
            ? "No eligible Primary skills remain."
            : AdvancementTooltip(player, chosenPrimaryCost, "the selected Primary skill");
        _skillOption.Disabled = _selectedSkillButton.Disabled;
        _skillOption.TooltipText = _selectedSkillButton.TooltipText;

        _selectedSecondarySkillButton.Text = $"Buy ({chosenSecondaryCost} SPP)";
        _selectedSecondarySkillButton.Disabled = player.StarPlayerPoints < chosenSecondaryCost || _secondarySkillOption.ItemCount == 0;
        _selectedSecondarySkillButton.TooltipText = _secondarySkillOption.ItemCount == 0
            ? "No eligible Secondary skills remain."
            : AdvancementTooltip(player, chosenSecondaryCost, "the selected Secondary skill");
        _secondarySkillOption.Disabled = _selectedSecondarySkillButton.Disabled;
        _secondarySkillOption.TooltipText = _selectedSecondarySkillButton.TooltipText;

        var characteristicCost = CharacteristicCost();
        _characteristicButton.Disabled = player.StarPlayerPoints < characteristicCost;
        _characteristicButton.Text = $"Improve Characteristic ({characteristicCost} SPP)";
        _characteristicButton.TooltipText = AdvancementTooltip(player, characteristicCost, "a Characteristic Improvement");
    }

    private static void PopulateSkillOption(OptionButton option, SkillDefinition[] eligible)
    {
        option.Clear();
        for (var index = 0; index < eligible.Length; index++)
        {
            option.AddItem(eligible[index].Name);
            option.SetItemMetadata(index, Variant.From(eligible[index].Id));
        }
    }

    private static string FormatAvailableSkills(SkillDefinition[] skills)
    {
        if (skills.Length == 0)
        {
            return "No eligible skills remain.";
        }

        return string.Join(
            "\n----------------\n",
            skills
                .GroupBy(skill => skill.Category, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"[b]{group.Key.ToUpperInvariant()}[/b]\n{string.Join(", ", group.Select(skill => skill.Name))}"));
    }

    private SkillDefinition[] EligibleSecondarySkills(Guid playerId)
    {
        var player = _editingTeam?.Players.FirstOrDefault(current => current.Id == playerId);
        if (player is null)
        {
            return Array.Empty<SkillDefinition>();
        }

        var position = FindPositionTemplate(player.PositionId);
        return _ruleset.Skills
            .Where(skill => position.SecondarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase))
            .Where(skill => !skill.DataOnly && !skill.Compulsory)
            .Where(skill => !player.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private async Task PurchaseSelectedSkillAsync(OptionButton option)
    {
        var player = SelectedPlayer();
        if (player is null || option.Selected < 0)
        {
            return;
        }

        _developmentDialog.Hide();
        await _purchaseSelectedSkill(player.Id, option.GetItemMetadata(option.Selected).AsString());
    }

    private async Task PurchaseRandomSkillAsync(bool secondary)
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        _developmentDialog.Hide();
        await _purchaseRandomSkill(player.Id, secondary);
    }

    private async Task ImproveCharacteristicAsync()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        var roll = await _rollCharacteristic(player.Id);
        ShowCharacteristicChoice(player.Id, roll);
    }

    private void ShowCharacteristicChoice(Guid playerId, CharacteristicAdvancementRoll roll)
    {
        var popup = new AcceptDialog
        {
            Title = "Characteristic Improvement",
            Unresizable = false,
            MinSize = new Vector2I(320, 0)
        };

        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 8);
        content.AddChild(new Label { Text = $"Rolled {roll.Roll} on the D16 table. Spends {roll.Cost} SPP." });

        if (roll.Options.Count == 0)
        {
            content.AddChild(new Label
            {
                Text = "This roll unlocks no characteristic this player can improve.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
        }
        else
        {
            content.AddChild(ScreenStyles.MutedLabel("Raise a characteristic:"));
            foreach (var option in roll.Options)
            {
                var button = ScreenStyles.StyledButton(CharacteristicLabel(option), primary: true);
                button.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                var characteristic = option;
                button.Pressed += async () =>
                {
                    popup.Hide();
                    _developmentDialog.Hide();
                    await _applyCharacteristic(playerId, roll.Roll, characteristic);
                };
                content.AddChild(button);
            }
        }

        var secondarySkills = EligibleSecondarySkills(playerId);
        if (secondarySkills.Length > 0)
        {
            content.AddChild(new HSeparator());
            content.AddChild(ScreenStyles.MutedLabel("Or take a Chosen Secondary skill instead:"));
            var skillOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            for (var index = 0; index < secondarySkills.Length; index++)
            {
                skillOption.AddItem(secondarySkills[index].Name);
                skillOption.SetItemMetadata(index, Variant.From(secondarySkills[index].Id));
            }
            content.AddChild(skillOption);

            var skillButton = ScreenStyles.StyledButton("Take Secondary Skill");
            skillButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            skillButton.Pressed += async () =>
            {
                if (skillOption.Selected < 0)
                {
                    return;
                }

                var skillId = skillOption.GetItemMetadata(skillOption.Selected).AsString();
                popup.Hide();
                _developmentDialog.Hide();
                await _applyCharacteristicSkill(playerId, skillId);
            };
            content.AddChild(skillButton);
        }

        popup.AddChild(content);
        popup.Confirmed += popup.QueueFree;
        popup.Canceled += popup.QueueFree;
        AddChild(popup);
        popup.PopupCentered();
    }

    private PositionTemplate FindPositionTemplate(string positionId)
    {
        return _selectedRoster?.Positions.FirstOrDefault(position => string.Equals(position.Id, positionId, StringComparison.OrdinalIgnoreCase))
            ?? _selectedRoster?.Positions.First()
            ?? new PositionTemplate { Id = positionId, Name = positionId, Stats = new PlayerStats() };
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

    private int AdvancementCost(Player player)
    {
        return AdvancementCost("randomPrimary");
    }

    private int AdvancementCost(string advancementType)
    {
        return _ruleset.AdvancementThresholds.TryGetValue(advancementType, out var cost) ? cost : int.MaxValue;
    }

    private int CharacteristicCost()
    {
        return AdvancementCost("characteristic");
    }

    private static string AdvancementTooltip(Player player, int cost, string advancement)
    {
        var shortfall = cost - player.StarPlayerPoints;
        return shortfall <= 0
            ? $"Spend {cost} SPP to gain {advancement}."
            : $"Needs {shortfall} more SPP to gain {advancement}.";
    }

    private static string CharacteristicLabel(PlayerCharacteristic characteristic) => characteristic switch
    {
        PlayerCharacteristic.Movement => "Movement Allowance (+1 MA)",
        PlayerCharacteristic.Strength => "Strength (+1 ST)",
        PlayerCharacteristic.Agility => "Agility (improve AG)",
        PlayerCharacteristic.Passing => "Passing Ability (improve PA)",
        PlayerCharacteristic.Armor => "Armour Value (+1 AV)",
        _ => characteristic.ToString()
    };

    private string RosterAttentionBadge(LeagueTeam team)
    {
        var canLevel = team.Players.Count(CanLevelUp);
        return canLevel > 0 ? $"{canLevel} can level" : "Roster";
    }

    private string PositionName(string positionId)
    {
        return _selectedRoster?.Positions.FirstOrDefault(position => string.Equals(position.Id, positionId, StringComparison.OrdinalIgnoreCase))?.Name
            ?? positionId;
    }

    private Player[] CurrentPlayers()
    {
        return _editingTeam?.Players.Where(IsCurrentPlayer).ToArray() ?? Array.Empty<Player>();
    }

    private Player[] PreviousPlayers()
    {
        return _editingTeam?.Players.Where(player => !IsCurrentPlayer(player)).ToArray() ?? Array.Empty<Player>();
    }

    private PlayerHistoryRow[] PlayerHistory(Player player)
    {
        if (_league is null || _editingTeam is null)
        {
            return Array.Empty<PlayerHistoryRow>();
        }

        return _league.Seasons
            .SelectMany(season => season.Schedule
                .Where(match => match.Result is not null && (match.HomeTeamId == _editingTeam.Id || match.AwayTeamId == _editingTeam.Id))
                .Select(match => CreatePlayerHistoryRow(season.Name, match, player)))
            .ToArray();
    }

    private PlayerHistoryRow CreatePlayerHistoryRow(string seasonName, ScheduledMatch match, Player player)
    {
        var result = match.Result!;
        var isHome = match.HomeTeamId == _editingTeam!.Id;
        var opponentId = isHome ? match.AwayTeamId : match.HomeTeamId;
        var opponent = _league?.Teams.FirstOrDefault(team => team.Id == opponentId);
        var teamScore = isHome ? result.HomeScore : result.AwayScore;
        var opponentScore = isHome ? result.AwayScore : result.HomeScore;
        var outcome = teamScore > opponentScore
            ? "W"
            : teamScore < opponentScore
                ? "L"
                : "D";
        var awards = result.PlayerAwards
            .Where(award => award.TeamId == _editingTeam.Id && award.PlayerId == player.Id)
            .ToArray();

        return new PlayerHistoryRow(
            $"{seasonName} W{match.Week}",
            opponent?.Name ?? "Unknown Team",
            $"{outcome} {teamScore}-{opponentScore}",
            CountAwards(awards, MatchPlayerAwardKind.Casualty),
            CountAwards(awards, MatchPlayerAwardKind.Touchdown),
            awards.Sum(award => award.StarPlayerPoints));
    }

    private static int CountAwards(MatchPlayerAward[] awards, MatchPlayerAwardKind kind)
    {
        return awards.Count(award => award.Kind == kind);
    }

    private string FormatPreviousStatus(Player player)
    {
        return player.Status switch
        {
            PlayerStatus.Dead => "Dead",
            PlayerStatus.Retired when !string.IsNullOrWhiteSpace(_latestSeasonName) => $"Not Resigned After {_latestSeasonName}",
            PlayerStatus.Retired => "Not Resigned",
            _ => FormatStatus(player.Status)
        };
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

    private static string FormatRecord(TeamRecord record)
    {
        return $"{record.Wins}-{record.Draws}-{record.Losses}";
    }

    private static string FormatStatus(PlayerStatus status)
    {
        return status switch
        {
            PlayerStatus.Available => "Ready",
            PlayerStatus.MissNextGame => "MNG",
            _ => status.ToString()
        };
    }

    private static string FormatSkills(Player player)
    {
        return player.Skills.Count == 0 ? "-" : string.Join(", ", player.Skills);
    }

    private static bool IsCurrentPlayer(Player player)
    {
        return player.Status is not PlayerStatus.Dead and not PlayerStatus.Retired;
    }

    private sealed record PlayerHistoryRow(
        string Game,
        string Opponent,
        string Result,
        int Casualties,
        int Touchdowns,
        int StarPlayerPoints);

    private static string FormatGold(int value)
    {
        return $"{value:N0} gp";
    }
}
