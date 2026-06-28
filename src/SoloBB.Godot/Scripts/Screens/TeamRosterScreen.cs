using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public partial class TeamRosterScreen : VBoxContainer
{
    private Ruleset _ruleset = null!;
    private RosterSet _rosterSet = null!;
    private LeagueTeam _team = null!;
    private TeamRoster _roster = null!;
    private string _latestSeasonName = "";
    private Func<Guid, string, Task> _renamePlayer = (_, _) => Task.CompletedTask;
    private Func<Guid, string, Task> _purchaseSelectedSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, bool, Task> _purchaseRandomSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, Task<CharacteristicAdvancementRoll>> _rollCharacteristic =
        _ => Task.FromResult(new CharacteristicAdvancementRoll(0, 0, Array.Empty<PlayerCharacteristic>()));
    private Func<Guid, int, PlayerCharacteristic, Task> _applyCharacteristic = (_, _, _) => Task.CompletedTask;
    private Func<Guid, string, Task> _applyCharacteristicSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, bool, Task> _movePlayer = (_, _) => Task.CompletedTask;

    private Tree _playerTree = null!;
    private Label _inspectorTitle = null!;
    private Label _inspectorMeta = null!;
    private LineEdit _nameEdit = null!;
    private OptionButton _skillOption = null!;
    private OptionButton _secondarySkillOption = null!;
    private Label _statsLabel = null!;
    private Label _developmentLabel = null!;
    private Label _developmentDialogLabel = null!;
    private RichTextLabel _primarySkillsLabel = null!;
    private RichTextLabel _secondarySkillsLabel = null!;
    private Label _healthLabel = null!;
    private Label _statusLabel = null!;
    private Button _renameButton = null!;
    private Button _openDevelopmentButton = null!;
    private Button _selectedSkillButton = null!;
    private Button _selectedSecondarySkillButton = null!;
    private Button _randomSkillButton = null!;
    private Button _randomSecondaryButton = null!;
    private Button _characteristicButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;
    private AcceptDialog _developmentDialog = null!;
    private AcceptDialog _previousPlayersDialog = null!;

    public void Setup(
        Ruleset ruleset,
        RosterSet rosterSet,
        LeagueTeam team,
        string latestSeasonName,
        Func<Guid, string, Task> renamePlayer,
        Func<Guid, string, Task> purchaseSelectedSkill,
        Func<Guid, bool, Task> purchaseRandomSkill,
        Func<Guid, Task<CharacteristicAdvancementRoll>> rollCharacteristic,
        Func<Guid, int, PlayerCharacteristic, Task> applyCharacteristic,
        Func<Guid, string, Task> applyCharacteristicSkill,
        Func<Guid, bool, Task> movePlayer,
        Action back)
    {
        Clear();
        AddThemeConstantOverride("separation", 14);
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));

        _ruleset = ruleset;
        _rosterSet = rosterSet;
        _team = team;
        _roster = rosterSet.Rosters.First(roster => string.Equals(roster.Id, team.RosterId, StringComparison.OrdinalIgnoreCase));
        _latestSeasonName = latestSeasonName;
        _renamePlayer = renamePlayer;
        _purchaseSelectedSkill = purchaseSelectedSkill;
        _purchaseRandomSkill = purchaseRandomSkill;
        _rollCharacteristic = rollCharacteristic;
        _applyCharacteristic = applyCharacteristic;
        _applyCharacteristicSkill = applyCharacteristicSkill;
        _movePlayer = movePlayer;

        AddHeader(back);
        BuildBody();
        RefreshPlayers();
        SelectFirstPlayer();
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private void AddHeader(Action back)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(new Color("202720"), ScreenStyles.PanelBorderSoft));
        AddChild(panel);

        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 12);
        panel.AddChild(row);

        var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        copy.AddThemeConstantOverride("separation", 4);
        row.AddChild(copy);

        var eyebrow = ScreenStyles.MutedLabel("ROSTER MANAGEMENT");
        eyebrow.AddThemeColorOverride("font_color", ScreenStyles.Brass);
        copy.AddChild(eyebrow);
        copy.AddChild(ScreenStyles.Title($"{_team.Name} Roster"));
        copy.AddChild(ScreenStyles.MutedLabel("Manage player names, SPP, level-ups, status, and retirement decisions."));

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        actions.AddThemeConstantOverride("separation", 8);
        row.AddChild(actions);

        var backButton = ScreenStyles.StyledButton("Back to Team");
        backButton.Pressed += back;
        actions.AddChild(backButton);

        var previousButton = ScreenStyles.StyledButton("Previous Players");
        previousButton.Pressed += OpenPreviousPlayers;
        actions.AddChild(previousButton);
    }

    private void BuildBody()
    {
        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 16);
        AddChild(body);

        var mainColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.7f
        };
        mainColumn.AddThemeConstantOverride("separation", 14);
        body.AddChild(mainColumn);

        var sideColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(340, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.85f
        };
        sideColumn.AddThemeConstantOverride("separation", 14);
        body.AddChild(sideColumn);

        mainColumn.AddChild(ScreenStyles.Panel("Players", BuildPlayerTable(), "All"));
        mainColumn.AddChild(ScreenStyles.Panel("SPP and Level Queue", BuildLevelQueue(), LevelQueueBadge(), ScreenStyles.Warning));

        sideColumn.AddChild(ScreenStyles.Panel("Player Inspector", BuildInspector(), "Ready", ScreenStyles.Good));
        sideColumn.AddChild(ScreenStyles.Panel("Development", BuildDevelopmentPanel()));
        sideColumn.AddChild(ScreenStyles.Panel("Roster Health", BuildHealthPanel(), HealthBadge(), ScreenStyles.Warning));

        _developmentDialog = BuildDevelopmentDialog();
        AddChild(_developmentDialog);
        _previousPlayersDialog = BuildPreviousPlayersDialog();
        AddChild(_previousPlayersDialog);
    }

    private Control BuildPlayerTable()
    {
        _playerTree = new Tree
        {
            Columns = 6,
            HideRoot = true,
            ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(620, 280),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _playerTree.SetColumnTitle(0, "Player");
        _playerTree.SetColumnTitle(1, "Position");
        _playerTree.SetColumnTitle(2, "Title");
        _playerTree.SetColumnTitle(3, "Status");
        _playerTree.SetColumnTitle(4, "SPP");
        _playerTree.SetColumnTitle(5, "Next Action");

        int[] columnExpandRatios = [5, 4, 2, 0, 0, 3];
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
                column == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        }

        _playerTree.SetColumnCustomMinimumWidth(0, 190);
        _playerTree.SetColumnCustomMinimumWidth(1, 140);
        _playerTree.SetColumnCustomMinimumWidth(2, 80);
        _playerTree.SetColumnCustomMinimumWidth(3, 82);
        _playerTree.SetColumnCustomMinimumWidth(4, 48);
        _playerTree.SetColumnCustomMinimumWidth(5, 125);
        _playerTree.AddThemeConstantOverride("h_separation", 10);
        _playerTree.AddThemeConstantOverride("v_separation", 5);
        _playerTree.ItemSelected += UpdateInspector;
        _playerTree.ItemMouseSelected += OnPlayerTreeItemMouseSelected;
        return _playerTree;
    }

    private Control BuildLevelQueue()
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);

        var queued = CurrentPlayers().Where(CanLevelUp).ToArray();
        if (queued.Length == 0)
        {
            stack.AddChild(ScreenStyles.MutedLabel("No players currently have enough SPP for an advancement."));
            return stack;
        }

        foreach (var player in queued)
        {
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 10);
            var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            copy.AddChild(new Label { Text = $"{player.Name} can advance" });
            copy.AddChild(ScreenStyles.MutedLabel($"{player.StarPlayerPoints} SPP available. Primary categories: {string.Join(", ", FindPosition(player.PositionId).PrimarySkillCategories)}."));
            row.AddChild(copy);

            var button = ScreenStyles.StyledButton("Level Available", primary: true);
            var playerId = player.Id;
            button.Pressed += () => OpenDevelopmentForPlayer(playerId);
            row.AddChild(button);
            stack.AddChild(row);
        }

        return stack;
    }

    private Control BuildInspector()
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 10);

        _inspectorTitle = new Label { Text = "Select a player" };
        _inspectorTitle.AddThemeFontSizeOverride("font_size", 18);
        stack.AddChild(_inspectorTitle);

        _inspectorMeta = ScreenStyles.MutedLabel("");
        stack.AddChild(_inspectorMeta);

        var nameGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nameGrid.AddThemeConstantOverride("h_separation", 8);
        nameGrid.AddChild(ScreenStyles.MutedLabel("Display Name"));
        _nameEdit = new LineEdit { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        nameGrid.AddChild(_nameEdit);
        stack.AddChild(nameGrid);

        _renameButton = ScreenStyles.StyledButton("Rename");
        _renameButton.Pressed += async () => await RenameSelectedAsync();
        stack.AddChild(_renameButton);

        var moveRow = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        moveRow.AddThemeConstantOverride("separation", 8);
        _moveUpButton = ScreenStyles.StyledButton("Move Up");
        _moveUpButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _moveUpButton.Pressed += async () => await MoveSelectedAsync(up: true);
        moveRow.AddChild(_moveUpButton);
        _moveDownButton = ScreenStyles.StyledButton("Move Down");
        _moveDownButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _moveDownButton.Pressed += async () => await MoveSelectedAsync(up: false);
        moveRow.AddChild(_moveDownButton);
        stack.AddChild(moveRow);

        _statsLabel = new Label { Text = "MA -   ST -   AG -   PA -   AV -" };
        _statsLabel.AddThemeColorOverride("font_color", ScreenStyles.Text);
        stack.AddChild(_statsLabel);

        return stack;
    }

    private Control BuildDevelopmentPanel()
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);
        _developmentLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _developmentLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        stack.AddChild(_developmentLabel);

        _openDevelopmentButton = ScreenStyles.StyledButton("Level Available", primary: true);
        _openDevelopmentButton.Pressed += OpenDevelopmentForSelectedPlayer;
        stack.AddChild(_openDevelopmentButton);

        return stack;
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
        return ScreenStyles.Inset(margin);
    }

    private Control BuildHealthPanel()
    {
        _healthLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _healthLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        _statusLabel = new Label
        {
            Text = "Select a player to manage roster details.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _statusLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 8);
        stack.AddChild(_healthLabel);
        stack.AddChild(_statusLabel);
        return stack;
    }

    private AcceptDialog BuildPreviousPlayersDialog()
    {
        var popup = new AcceptDialog
        {
            Title = "Previous Players",
            Unresizable = false,
            MinSize = new Vector2I(760, 460)
        };
        popup.GetOkButton().Text = "Close";

        var tree = new Tree
        {
            Columns = 5,
            HideRoot = true,
            ColumnTitlesVisible = true,
            CustomMinimumSize = new Vector2(720, 360),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        tree.SetColumnTitle(0, "Player");
        tree.SetColumnTitle(1, "Position");
        tree.SetColumnTitle(2, "Title");
        tree.SetColumnTitle(3, "Status");
        tree.SetColumnTitle(4, "SPP");

        int[] columnExpandRatios = [5, 4, 2, 4, 0];
        for (var column = 0; column < tree.Columns; column++)
        {
            var ratio = columnExpandRatios[column];
            var expands = ratio > 0;
            tree.SetColumnExpand(column, expands);
            if (expands)
            {
                tree.SetColumnExpandRatio(column, ratio);
            }

            tree.SetColumnTitleAlignment(
                column,
                column == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
        }

        tree.SetColumnCustomMinimumWidth(0, 190);
        tree.SetColumnCustomMinimumWidth(1, 140);
        tree.SetColumnCustomMinimumWidth(2, 80);
        tree.SetColumnCustomMinimumWidth(3, 190);
        tree.SetColumnCustomMinimumWidth(4, 48);
        tree.AddThemeConstantOverride("h_separation", 10);
        tree.AddThemeConstantOverride("v_separation", 5);

        var root = tree.CreateItem();
        foreach (var player in PreviousPlayers().OrderBy(player => player.Number).ThenBy(player => player.Name, StringComparer.OrdinalIgnoreCase))
        {
            var item = tree.CreateItem(root);
            item.SetText(0, $"#{player.Number} {player.Name}");
            item.SetText(1, FindPosition(player.PositionId).Name);
            item.SetText(2, LeagueService.PlayerTitle(_roster, player));
            item.SetText(3, FormatPreviousStatus(player));
            item.SetText(4, player.StarPlayerPoints.ToString());
            for (var column = 0; column < tree.Columns; column++)
            {
                item.SetTextAlignment(
                    column,
                    column == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            }

            item.SetCustomMinimumHeight(28);
        }

        if (PreviousPlayers().Length == 0)
        {
            var item = tree.CreateItem(root);
            item.SetText(0, "No previous players yet.");
            item.SetCustomColor(0, ScreenStyles.MutedText);
        }

        popup.AddChild(tree);
        return popup;
    }

    private void RefreshPlayers()
    {
        _playerTree.Clear();
        var root = _playerTree.CreateItem();
        var currentPlayers = CurrentPlayers();
        foreach (var player in currentPlayers.OrderBy(player => player.Number))
        {
            var item = _playerTree.CreateItem(root);
            item.SetText(0, $"#{player.Number} {player.Name}");
            item.SetText(1, FindPosition(player.PositionId).Name);
            item.SetText(2, LeagueService.PlayerTitle(_roster, player));
            item.SetText(3, FormatStatus(player.Status));
            item.SetText(4, player.StarPlayerPoints.ToString());
            item.SetText(5, CanLevelUp(player) ? "Spend SPP" : "Rename");
            item.SetMetadata(0, Variant.From(player.Id.ToString()));
            if (CanLevelUp(player))
            {
                item.SetCustomColor(5, ScreenStyles.Brass);
                item.SetTooltipText(5, "Open this player's advancement choices.");
            }
            for (var column = 0; column < _playerTree.Columns; column++)
            {
                item.SetTextAlignment(
                    column,
                    column == 4 ? HorizontalAlignment.Right : HorizontalAlignment.Left);
            }

            item.SetCustomMinimumHeight(28);
        }

        _healthLabel.Text = $"Ready: {currentPlayers.Count(player => player.Status == PlayerStatus.Available)}\nMissing next game: {currentPlayers.Count(player => player.Status == PlayerStatus.MissNextGame)}\nCan level up: {currentPlayers.Count(CanLevelUp)}\nTotal SPP: {currentPlayers.Sum(player => player.StarPlayerPoints)}";
    }

    private void OnPlayerTreeItemMouseSelected(Vector2 mousePosition, long mouseButtonIndex)
    {
        if (mouseButtonIndex != (long)MouseButton.Left || _playerTree.GetColumnAtPosition(mousePosition) != 5)
        {
            return;
        }

        var item = _playerTree.GetItemAtPosition(mousePosition);
        if (item is null || !Guid.TryParse(item.GetMetadata(0).AsString(), out var playerId))
        {
            return;
        }

        var player = _team.Players.FirstOrDefault(current => current.Id == playerId);
        if (player is not null && CanLevelUp(player))
        {
            OpenDevelopmentForPlayer(playerId);
        }
    }

    private void SelectFirstPlayer()
    {
        var root = _playerTree.GetRoot();
        var first = root?.GetFirstChild();
        if (first is not null)
        {
            first.Select(0);
            UpdateInspector();
        }
    }

    public void SelectPlayerById(Guid playerId) => SelectPlayer(playerId);

    private void SelectPlayer(Guid playerId)
    {
        var root = _playerTree.GetRoot();
        var item = root?.GetFirstChild();
        while (item is not null)
        {
            if (Guid.TryParse(item.GetMetadata(0).AsString(), out var currentId) && currentId == playerId)
            {
                item.Select(0);
                UpdateInspector();
                return;
            }

            item = item.GetNext();
        }
    }

    private void OpenDevelopmentForPlayer(Guid playerId)
    {
        SelectPlayer(playerId);
        OpenDevelopmentForSelectedPlayer();
    }

    private void OpenDevelopmentForSelectedPlayer()
    {
        var player = SelectedPlayer();
        if (player is null || !CanLevelUp(player))
        {
            return;
        }

        _developmentDialog.Title = $"{player.Name} Development";
        _developmentDialog.PopupCentered(new Vector2I(760, 660));
    }

    private void UpdateInspector()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            _renameButton.Disabled = true;
            _openDevelopmentButton.Disabled = true;
            _skillOption.Disabled = true;
            _secondarySkillOption.Disabled = true;
            _selectedSkillButton.Disabled = true;
            _selectedSecondarySkillButton.Disabled = true;
            _randomSkillButton.Disabled = true;
            _randomSecondaryButton.Disabled = true;
            _characteristicButton.Disabled = true;
            _moveUpButton.Disabled = true;
            _moveDownButton.Disabled = true;
            return;
        }

        var currentPlayers = CurrentPlayers();
        _moveUpButton.Disabled = player.Number <= currentPlayers.Min(current => current.Number);
        _moveDownButton.Disabled = player.Number >= currentPlayers.Max(current => current.Number);

        var position = FindPosition(player.PositionId);
        _inspectorTitle.Text = player.Name;
        _inspectorMeta.Text = $"{position.Name} - {LeagueService.PlayerTitle(_roster, player)} - {FormatStatus(player.Status)}";
        _nameEdit.Text = player.Name;
        _statsLabel.Text = $"MA {player.Stats.Movement}   ST {player.Stats.Strength}   AG {player.Stats.Agility}+   PA {player.Stats.Passing}+   AV {player.Stats.Armor}+";
        _developmentLabel.Text = $"{player.StarPlayerPoints} SPP available.\nCurrent skills: {FormatSkills(player)}";
        _developmentDialogLabel.Text = $"{player.Name} has {player.StarPlayerPoints} SPP available.\nCurrent skills: {FormatSkills(player)}";
        PopulateSkillOptions(player, position);

        _renameButton.Disabled = false;
        var randomPrimaryCost = AdvancementCost("randomPrimary");
        var chosenPrimaryCost = AdvancementCost("chosenPrimary");
        var randomSecondaryCost = AdvancementCost("randomSecondary");
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

        var canLevel = CanLevelUp(player);
        _openDevelopmentButton.Disabled = !canLevel;
        _openDevelopmentButton.Text = canLevel
            ? "Level Available"
            : $"Next Advancement: {AdvancementCost(player)} SPP";
        _openDevelopmentButton.TooltipText = canLevel
            ? "Open this player's advancement choices."
            : AdvancementTooltip(player, AdvancementCost(player), "an advancement");

        _statusLabel.Text = canLevel
            ? $"{player.Name} can spend SPP now."
            : $"{player.Name} needs {Math.Max(0, AdvancementCost(player) - player.StarPlayerPoints)} more SPP for the next advancement.";
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
        var player = _team.Players.FirstOrDefault(current => current.Id == playerId);
        if (player is null)
        {
            return Array.Empty<SkillDefinition>();
        }

        var position = FindPosition(player.PositionId);
        return _ruleset.Skills
            .Where(skill => position.SecondarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase))
            .Where(skill => !skill.DataOnly && !skill.Compulsory)
            .Where(skill => !player.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

    private async Task RenameSelectedAsync()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        await _renamePlayer(player.Id, _nameEdit.Text);
    }

    private async Task PurchaseSelectedSkillAsync(OptionButton option)
    {
        var player = SelectedPlayer();
        if (player is null || option.Selected < 0)
        {
            return;
        }

        await _purchaseSelectedSkill(player.Id, option.GetItemMetadata(option.Selected).AsString());
    }

    private async Task PurchaseRandomSkillAsync(bool secondary)
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        await _purchaseRandomSkill(player.Id, secondary);
    }

    private async Task ImproveCharacteristicAsync()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            return;
        }

        // BB2020: spend the SPP to roll the D16, then choose from the characteristics it unlocks.
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
                    await _applyCharacteristic(playerId, roll.Roll, characteristic);
                };
                content.AddChild(button);
            }
        }

        // BB2020: a characteristic that cannot be (or the coach does not wish to) improve may always be
        // exchanged for a Chosen Secondary skill, for the same 18 SPP.
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

    private static string CharacteristicLabel(PlayerCharacteristic characteristic) => characteristic switch
    {
        PlayerCharacteristic.Movement => "Movement Allowance (+1 MA)",
        PlayerCharacteristic.Strength => "Strength (+1 ST)",
        PlayerCharacteristic.Agility => "Agility (improve AG)",
        PlayerCharacteristic.Passing => "Passing Ability (improve PA)",
        PlayerCharacteristic.Armor => "Armour Value (+1 AV)",
        _ => characteristic.ToString()
    };

    private Player? SelectedPlayer()
    {
        var selected = _playerTree.GetSelected();
        if (selected is null || !Guid.TryParse(selected.GetMetadata(0).AsString(), out var playerId))
        {
            return null;
        }

        return _team.Players.FirstOrDefault(player => player.Id == playerId);
    }

    private PositionTemplate FindPosition(string positionId)
    {
        return _roster.Positions.FirstOrDefault(position => string.Equals(position.Id, positionId, StringComparison.OrdinalIgnoreCase))
            ?? _roster.Positions.First();
    }

    private bool CanLevelUp(Player player)
    {
        return player.StarPlayerPoints >= AdvancementCost(player);
    }

    private int AdvancementCost(Player player)
    {
        // BB2020: the cheapest advancement available is a Randomly Selected Primary skill.
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

    private string LevelQueueBadge()
    {
        var count = CurrentPlayers().Count(CanLevelUp);
        return count == 0 ? "None" : $"{count} available";
    }

    private string HealthBadge()
    {
        var missing = CurrentPlayers().Count(player => player.Status == PlayerStatus.MissNextGame);
        return missing == 0 ? "Healthy" : $"{missing} MNG";
    }

    private void OpenPreviousPlayers()
    {
        _previousPlayersDialog.PopupCentered(new Vector2I(780, 500));
    }

    private Player[] CurrentPlayers()
    {
        return _team.Players.Where(IsCurrentPlayer).ToArray();
    }

    private Player[] PreviousPlayers()
    {
        return _team.Players.Where(player => !IsCurrentPlayer(player)).ToArray();
    }

    private static bool IsCurrentPlayer(Player player)
    {
        return player.Status is not PlayerStatus.Dead and not PlayerStatus.Retired;
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

    private static string FormatSkills(Player player)
    {
        return player.Skills.Count == 0 ? "-" : string.Join(", ", player.Skills);
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
