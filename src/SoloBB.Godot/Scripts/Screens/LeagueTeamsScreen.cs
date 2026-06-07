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

        _league = league;
        _commit = commit;
        _editTeam = editTeam;
        _deleteTeam = deleteTeam;

        AddTitle("New League");

        var setupGrid = new GridContainer { Columns = 2 };
        AddChild(setupGrid);

        setupGrid.AddChild(new Label { Text = "League Name" });
        _leagueNameEdit = new LineEdit
        {
            PlaceholderText = "League name",
            Text = league.Name,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _leagueNameEdit.TextChanged += _ => CommitAndRefresh();
        setupGrid.AddChild(_leagueNameEdit);

        setupGrid.AddChild(new Label { Text = "Teams" });
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

        var body = new HBoxContainer();
        AddChild(body);

        var listColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(listColumn);
        listColumn.AddChild(new Label { Text = "League Teams" });

        _teamList = new ItemList
        {
            CustomMinimumSize = new Vector2(280, 220),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _teamList.ItemSelected += _ => UpdateActions();
        listColumn.AddChild(_teamList);

        _teamCountLabel = new Label();
        listColumn.AddChild(_teamCountLabel);

        var actionColumn = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(actionColumn);
        _editTeamButton = AddButtonTo(actionColumn, "Edit Team", EditSelectedTeam, disabled: true);
        _deleteTeamButton = AddButtonTo(actionColumn, "Delete Team", async () => await DeleteSelectedTeamAsync(), disabled: true);
        _createTeamButton = AddButtonTo(actionColumn, "Create New Team", () =>
        {
            CommitAndRefresh();
            createTeam();
        });
        _createLeagueButton = AddButtonTo(actionColumn, "Create League", () =>
        {
            CommitAndRefresh();
            startLeague();
        }, disabled: true);
        AddButtonTo(actionColumn, "Back", back);

        _statusLabel = new Label
        {
            Text = "Name the league and choose how many teams it will contain. Scheduling currently uses even team counts.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        AddChild(_statusLabel);

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
        _teamCountLabel.Text = $"{_league.Teams.Count}/{targetTeamCount}";
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

    private Button AddButtonTo(Container parent, string text, Action pressed, bool disabled = false)
    {
        var button = new Button { Text = text, Disabled = disabled };
        button.Pressed += pressed;
        parent.AddChild(button);
        return button;
    }

    private Button AddButtonTo(Container parent, string text, Func<Task> pressed, bool disabled = false)
    {
        var button = new Button { Text = text, Disabled = disabled };
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
