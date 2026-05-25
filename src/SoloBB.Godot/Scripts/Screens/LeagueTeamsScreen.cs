using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using SoloBB.Core.Domain;

namespace SoloBB.Godot.Scripts.Screens;

public partial class LeagueTeamsScreen : VBoxContainer
{
    private ItemList _teamList = null!;
    private Label _teamCountLabel = null!;
    private Button _editTeamButton = null!;
    private Button _deleteTeamButton = null!;
    private Button _createTeamButton = null!;
    private Button _createLeagueButton = null!;
    private Label _statusLabel = null!;
    private League _league = null!;
    private Action<Guid> _editTeam = _ => { };
    private Func<Guid, Task> _deleteTeam = _ => Task.CompletedTask;

    public void Setup(
        League league,
        int targetTeamCount,
        Action createTeam,
        Action createLeague,
        Action<Guid> editTeam,
        Func<Guid, Task> deleteTeam,
        Action back)
    {
        Clear();

        _league = league;
        _editTeam = editTeam;
        _deleteTeam = deleteTeam;

        AddTitle("League Teams");

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
        _createTeamButton = AddButtonTo(actionColumn, "Create New Team", createTeam);
        _createLeagueButton = AddButtonTo(actionColumn, "Create League", createLeague, disabled: true);
        AddButtonTo(actionColumn, "Back", back);

        _statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_statusLabel);

        Refresh(targetTeamCount);
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private void Refresh(int targetTeamCount)
    {
        _teamList.Clear();
        foreach (var team in _league.Teams)
        {
            _teamList.AddItem($"{team.Name} - TV {FormatGold(team.TeamValue)} ({team.CoachName})");
        }

        _teamCountLabel.Text = $"{_league.Teams.Count}/{targetTeamCount}";
        _createTeamButton.Disabled = _league.Teams.Count >= targetTeamCount;
        _createLeagueButton.Disabled = _league.Teams.Count != targetTeamCount;
        UpdateActions();
    }

    private void EditSelectedTeam()
    {
        if (SelectedTeamId() is Guid teamId)
        {
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
