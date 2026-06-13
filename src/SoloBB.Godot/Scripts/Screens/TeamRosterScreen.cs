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
    private Func<Guid, string, Task> _renamePlayer = (_, _) => Task.CompletedTask;
    private Func<Guid, string, Task> _purchaseSelectedSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, bool, Task> _purchaseRandomSkill = (_, _) => Task.CompletedTask;
    private Func<Guid, Task<CharacteristicAdvancementRoll>> _rollCharacteristic =
        _ => Task.FromResult(new CharacteristicAdvancementRoll(0, 0, Array.Empty<PlayerCharacteristic>()));
    private Func<Guid, int, PlayerCharacteristic, Task> _applyCharacteristic = (_, _, _) => Task.CompletedTask;
    private Func<Guid, bool, Task> _movePlayer = (_, _) => Task.CompletedTask;

    private Tree _playerTree = null!;
    private Label _inspectorTitle = null!;
    private Label _inspectorMeta = null!;
    private LineEdit _nameEdit = null!;
    private OptionButton _skillOption = null!;
    private Label _statsLabel = null!;
    private Label _developmentLabel = null!;
    private Label _healthLabel = null!;
    private Label _statusLabel = null!;
    private Button _renameButton = null!;
    private Button _selectedSkillButton = null!;
    private Button _randomSkillButton = null!;
    private Button _randomSecondaryButton = null!;
    private Button _characteristicButton = null!;
    private Button _moveUpButton = null!;
    private Button _moveDownButton = null!;

    public void Setup(
        Ruleset ruleset,
        RosterSet rosterSet,
        LeagueTeam team,
        Func<Guid, string, Task> renamePlayer,
        Func<Guid, string, Task> purchaseSelectedSkill,
        Func<Guid, bool, Task> purchaseRandomSkill,
        Func<Guid, Task<CharacteristicAdvancementRoll>> rollCharacteristic,
        Func<Guid, int, PlayerCharacteristic, Task> applyCharacteristic,
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
        _renamePlayer = renamePlayer;
        _purchaseSelectedSkill = purchaseSelectedSkill;
        _purchaseRandomSkill = purchaseRandomSkill;
        _rollCharacteristic = rollCharacteristic;
        _applyCharacteristic = applyCharacteristic;
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
    }

    private Control BuildPlayerTable()
    {
        _playerTree = new Tree
        {
            Columns = 7,
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
        _playerTree.SetColumnTitle(5, "Progress");
        _playerTree.SetColumnTitle(6, "Next Action");
        _playerTree.SetColumnExpand(0, true);
        _playerTree.SetColumnCustomMinimumWidth(0, 190);
        _playerTree.SetColumnCustomMinimumWidth(1, 120);
        _playerTree.SetColumnCustomMinimumWidth(2, 110);
        _playerTree.SetColumnCustomMinimumWidth(3, 90);
        _playerTree.SetColumnCustomMinimumWidth(4, 52);
        _playerTree.SetColumnCustomMinimumWidth(5, 95);
        _playerTree.SetColumnCustomMinimumWidth(6, 110);
        _playerTree.SetColumnTitleAlignment(4, HorizontalAlignment.Right);
        _playerTree.ItemSelected += UpdateInspector;
        return _playerTree;
    }

    private Control BuildLevelQueue()
    {
        var stack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddThemeConstantOverride("separation", 8);

        var queued = _team.Players.Where(CanLevelUp).ToArray();
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

            var button = ScreenStyles.StyledButton("Select", primary: true);
            var playerId = player.Id;
            button.Pressed += () => SelectPlayer(playerId);
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

        _skillOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        stack.AddChild(_skillOption);

        _selectedSkillButton = ScreenStyles.StyledButton("Spend SPP", primary: true);
        _selectedSkillButton.Pressed += async () => await PurchaseSelectedSkillAsync();
        stack.AddChild(_selectedSkillButton);

        _randomSkillButton = ScreenStyles.StyledButton("Buy Random Primary");
        _randomSkillButton.Pressed += async () => await PurchaseRandomSkillAsync(secondary: false);
        stack.AddChild(_randomSkillButton);

        _randomSecondaryButton = ScreenStyles.StyledButton("Buy Random Secondary");
        _randomSecondaryButton.Pressed += async () => await PurchaseRandomSkillAsync(secondary: true);
        stack.AddChild(_randomSecondaryButton);

        _characteristicButton = ScreenStyles.StyledButton("Improve Characteristic");
        _characteristicButton.Pressed += async () => await ImproveCharacteristicAsync();
        stack.AddChild(_characteristicButton);

        return stack;
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

    private void RefreshPlayers()
    {
        _playerTree.Clear();
        var root = _playerTree.CreateItem();
        foreach (var player in _team.Players.OrderBy(player => player.Number))
        {
            var item = _playerTree.CreateItem(root);
            item.SetText(0, $"#{player.Number} {player.Name}");
            item.SetText(1, FindPosition(player.PositionId).Name);
            item.SetText(2, LeagueService.PlayerTitle(_roster, player));
            item.SetText(3, FormatStatus(player.Status));
            item.SetText(4, player.StarPlayerPoints.ToString());
            item.SetText(5, ProgressText(player));
            item.SetText(6, CanLevelUp(player) ? "Spend SPP" : "Rename");
            item.SetMetadata(0, Variant.From(player.Id.ToString()));
            item.SetTextAlignment(4, HorizontalAlignment.Right);
        }

        _healthLabel.Text = $"Ready: {_team.Players.Count(player => player.Status == PlayerStatus.Available)}\nMissing next game: {_team.Players.Count(player => player.Status == PlayerStatus.MissNextGame)}\nCan level up: {_team.Players.Count(CanLevelUp)}\nTotal SPP: {_team.Players.Sum(player => player.StarPlayerPoints)}";
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

    private void UpdateInspector()
    {
        var player = SelectedPlayer();
        if (player is null)
        {
            _renameButton.Disabled = true;
            _selectedSkillButton.Disabled = true;
            _randomSkillButton.Disabled = true;
            _randomSecondaryButton.Disabled = true;
            _characteristicButton.Disabled = true;
            _moveUpButton.Disabled = true;
            _moveDownButton.Disabled = true;
            return;
        }

        _moveUpButton.Disabled = player.Number <= _team.Players.Min(current => current.Number);
        _moveDownButton.Disabled = player.Number >= _team.Players.Max(current => current.Number);

        var position = FindPosition(player.PositionId);
        _inspectorTitle.Text = player.Name;
        _inspectorMeta.Text = $"{position.Name} - {LeagueService.PlayerTitle(_roster, player)} - {FormatStatus(player.Status)}";
        _nameEdit.Text = player.Name;
        _statsLabel.Text = $"MA {player.Stats.Movement}   ST {player.Stats.Strength}   AG {player.Stats.Agility}+   PA {player.Stats.Passing}+   AV {player.Stats.Armor}+";
        _developmentLabel.Text = $"{player.StarPlayerPoints} SPP. Next advancement threshold: {AdvancementCost(player)} SPP.\nSkills: {FormatSkills(player)}";
        PopulateSkillOptions(player, position);

        _renameButton.Disabled = false;
        var canLevel = CanLevelUp(player);
        _selectedSkillButton.Disabled = !canLevel || _skillOption.ItemCount == 0;
        _randomSkillButton.Disabled = !canLevel;
        _randomSecondaryButton.Disabled = !canLevel || position.SecondarySkillCategories.Count == 0;

        var characteristicCost = CharacteristicCost();
        _characteristicButton.Disabled = player.StarPlayerPoints < characteristicCost;
        _characteristicButton.Text = $"Improve Characteristic ({characteristicCost} SPP)";

        _statusLabel.Text = canLevel
            ? $"{player.Name} can spend SPP now."
            : $"{player.Name} needs {Math.Max(0, AdvancementCost(player) - player.StarPlayerPoints)} more SPP for the next advancement.";
    }

    private void PopulateSkillOptions(Player player, PositionTemplate position)
    {
        _skillOption.Clear();
        var eligible = _ruleset.Skills
            .Where(skill => position.PrimarySkillCategories.Contains(skill.Category, StringComparer.OrdinalIgnoreCase))
            .Where(skill => !player.Skills.Contains(skill.Id, StringComparer.OrdinalIgnoreCase))
            .OrderBy(skill => skill.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var index = 0; index < eligible.Length; index++)
        {
            _skillOption.AddItem(eligible[index].Name);
            _skillOption.SetItemMetadata(index, Variant.From(eligible[index].Id));
        }
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

    private async Task PurchaseSelectedSkillAsync()
    {
        var player = SelectedPlayer();
        if (player is null || _skillOption.Selected < 0)
        {
            return;
        }

        await _purchaseSelectedSkill(player.Id, _skillOption.GetItemMetadata(_skillOption.Selected).AsString());
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
        content.AddChild(new Label { Text = $"Rolled {roll.Roll} on the D16 table." });

        if (roll.Options.Count == 0)
        {
            content.AddChild(new Label
            {
                Text = "This roll unlocks no characteristic this player can still improve. No SPP was spent.",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });
        }
        else
        {
            content.AddChild(ScreenStyles.MutedLabel("Choose a characteristic to raise:"));
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
        return _ruleset.AdvancementThresholds.TryGetValue("randomPrimary", out var cost) ? cost : int.MaxValue;
    }

    private int CharacteristicCost()
    {
        return _ruleset.AdvancementThresholds.TryGetValue("characteristic", out var cost) ? cost : int.MaxValue;
    }

    private string ProgressText(Player player)
    {
        var cost = AdvancementCost(player);
        return player.StarPlayerPoints >= cost ? "Level available" : $"{player.StarPlayerPoints}/{cost}";
    }

    private string LevelQueueBadge()
    {
        var count = _team.Players.Count(CanLevelUp);
        return count == 0 ? "None" : $"{count} available";
    }

    private string HealthBadge()
    {
        var missing = _team.Players.Count(player => player.Status == PlayerStatus.MissNextGame);
        return missing == 0 ? "Healthy" : $"{missing} MNG";
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

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
