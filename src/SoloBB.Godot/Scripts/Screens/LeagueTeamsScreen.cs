using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;

namespace SoloBB.Godot.Scripts.Screens;

public partial class LeagueTeamsScreen : VBoxContainer
{
    private LineEdit _leagueNameEdit = null!;
    private SpinBox _teamCountSpin = null!;
    private ItemList _teamList = null!;
    private Label _teamCountLabel = null!;
    private Button _editTeamButton = null!;
    private Button _deleteTeamButton = null!;
    private Button _createTeamButton = null!;
    private Button _createLeagueButton = null!;
    private Label _statusLabel = null!;
    private League _league = null!;
    private Action<string, int> _commit = (_, _) => { };
    private Action<Guid> _editTeam = _ => { };
    private Func<Guid, Task> _deleteTeam = _ => Task.CompletedTask;

    public void Setup(
        League league,
        Action<string, int> commit,
        Action createTeam,
        Action startLeague,
        Action<Guid> editTeam,
        Func<Guid, Task> deleteTeam,
        Action back)
    {
        Clear();
        AddThemeConstantOverride("separation", 12);
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));

        _league = league;
        _commit = commit;
        _editTeam = editTeam;
        _deleteTeam = deleteTeam;

        var headerActions = new HBoxContainer();
        headerActions.AddThemeConstantOverride("separation", 8);
        var backButton = ScreenStyles.StyledButton("Back");
        backButton.Pressed += back;
        headerActions.AddChild(backButton);
        _createLeagueButton = ScreenStyles.StyledButton("Create League", primary: true, disabled: true);
        _createLeagueButton.Pressed += () =>
        {
            CommitAndRefresh();
            startLeague();
        };
        headerActions.AddChild(_createLeagueButton);
        AddChild(ScreenStyles.ScreenHeader(
            "League Setup",
            "Create League",
            "Name the competition, choose its size, and draft every team before generating the schedule.",
            headerActions));

        var setupGrid = new GridContainer { Columns = 2, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        setupGrid.AddThemeConstantOverride("h_separation", 14);
        setupGrid.AddThemeConstantOverride("v_separation", 10);

        setupGrid.AddChild(ScreenStyles.MutedLabel("League Name"));
        _leagueNameEdit = new LineEdit
        {
            PlaceholderText = "League name",
            Text = league.Name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _leagueNameEdit.TextChanged += _ => CommitAndRefresh();
        setupGrid.AddChild(_leagueNameEdit);

        setupGrid.AddChild(ScreenStyles.MutedLabel("Team Slots"));
        _teamCountSpin = new SpinBox
        {
            MinValue = TeamCountFloor(league.Teams.Count),
            MaxValue = 32,
            Step = 2,
            AllowGreater = false,
            AllowLesser = false
        };
        _teamCountSpin.Value = Math.Max(league.TargetTeamCount, _teamCountSpin.MinValue);
        _teamCountSpin.ValueChanged += _ => CommitAndRefresh();
        setupGrid.AddChild(_teamCountSpin);
        AddChild(ScreenStyles.Panel("Competition", setupGrid, "Even teams", ScreenStyles.Brass));

        var body = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 12);
        AddChild(body);

        var listColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.7f
        };
        listColumn.AddThemeConstantOverride("separation", 8);

        _teamList = new ItemList
        {
            CustomMinimumSize = new Vector2(420, 280),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _teamList.ItemSelected += _ => UpdateActions();
        listColumn.AddChild(_teamList);
        body.AddChild(ScreenStyles.Panel("League Teams", listColumn, "Draft roster"));

        var sideColumn = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(300, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.8f
        };
        sideColumn.AddThemeConstantOverride("separation", 12);
        body.AddChild(sideColumn);

        var progress = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        progress.AddThemeConstantOverride("separation", 6);
        _teamCountLabel = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _teamCountLabel.AddThemeColorOverride("font_color", ScreenStyles.Brass);
        _teamCountLabel.AddThemeFontSizeOverride("font_size", 30);
        progress.AddChild(_teamCountLabel);
        var progressDetail = ScreenStyles.MutedLabel("teams drafted");
        progressDetail.HorizontalAlignment = HorizontalAlignment.Center;
        progress.AddChild(progressDetail);
        sideColumn.AddChild(ScreenStyles.Panel("Draft Progress", progress, "Required"));

        var actionColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        actionColumn.AddThemeConstantOverride("separation", 8);
        _editTeamButton = AddButtonTo(actionColumn, "Edit Team", EditSelectedTeam, disabled: true);
        _deleteTeamButton = AddButtonTo(actionColumn, "Delete Team", async () => await DeleteSelectedTeamAsync(), disabled: true, danger: true);
        _createTeamButton = AddButtonTo(actionColumn, "Draft New Team", () =>
        {
            CommitAndRefresh();
            createTeam();
        }, primary: true);
        sideColumn.AddChild(ScreenStyles.Panel("Team Actions", actionColumn));

        _statusLabel = new Label
        {
            Text = "Name the league and choose how many teams it will contain. Scheduling currently uses even team counts.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _statusLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        AddChild(ScreenStyles.Panel("Setup Status", _statusLabel));

        Refresh();
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private void CommitAndRefresh()
    {
        _commit(_leagueNameEdit.Text, (int)_teamCountSpin.Value);
        Refresh();
    }

    private void Refresh()
    {
        _teamList.Clear();
        foreach (var team in _league.Teams)
        {
            _teamList.AddItem($"{team.Name} - TV {FormatGold(team.TeamValue)} ({team.CoachName})");
        }

        var targetTeamCount = (int)_teamCountSpin.Value;
        _teamCountLabel.Text = $"{_league.Teams.Count} / {targetTeamCount}";
        _createTeamButton.Disabled = _league.Teams.Count >= targetTeamCount;
        _createLeagueButton.Disabled = _league.Teams.Count != targetTeamCount;
        UpdateActions();
    }

    private void EditSelectedTeam()
    {
        if (SelectedTeamId() is Guid teamId)
        {
            CommitAndRefresh();
            _editTeam(teamId);
        }
    }

    private async Task DeleteSelectedTeamAsync()
    {
        if (SelectedTeamId() is Guid teamId)
        {
            await _deleteTeam(teamId);
        }
    }

    private Guid? SelectedTeamId()
    {
        var selected = _teamList.GetSelectedItems();
        if (selected.Length == 0 || selected[0] < 0 || selected[0] >= _league.Teams.Count)
        {
            return null;
        }

        return _league.Teams[selected[0]].Id;
    }

    private void UpdateActions()
    {
        var hasSelection = _teamList.GetSelectedItems().Any();
        _editTeamButton.Disabled = !hasSelection;
        _deleteTeamButton.Disabled = !hasSelection;
    }

    private static int TeamCountFloor(int teamCount)
    {
        return Math.Max(2, teamCount + (teamCount % 2));
    }

    private Button AddButtonTo(Container parent, string text, Action pressed, bool disabled = false, bool primary = false, bool danger = false)
    {
        var button = ScreenStyles.StyledButton(text, primary, danger, disabled);
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private Button AddButtonTo(Container parent, string text, Func<Task> pressed, bool disabled = false, bool primary = false, bool danger = false)
    {
        var button = ScreenStyles.StyledButton(text, primary, danger, disabled);
        button.Pressed += async () => await pressed();
        parent.AddChild(button);
        return button;
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
