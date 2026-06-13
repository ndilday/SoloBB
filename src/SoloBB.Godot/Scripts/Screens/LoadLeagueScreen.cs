using Godot;
using System;
using System.Collections.Generic;
using SoloBB.Core.Domain;

namespace SoloBB.Godot.Scripts.Screens;

public partial class LoadLeagueScreen : VBoxContainer
{
    private Label _statusLabel = null!;

    public void Setup(IReadOnlyList<League> leagues, Action<League> loadLeague, Action<League> deleteLeague, Action back)
    {
        Clear();
        AddThemeConstantOverride("separation", 12);
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));

        var headerActions = new HBoxContainer();
        var backButton = ScreenStyles.StyledButton("Back");
        backButton.CustomMinimumSize = new Vector2(110, 34);
        backButton.Pressed += back;
        headerActions.AddChild(backButton);
        AddChild(ScreenStyles.ScreenHeader(
            "League Archive",
            "Load League",
            "Choose a saved competition to continue, or remove one you no longer need.",
            headerActions));

        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeConstantOverride("separation", 8);

        if (leagues.Count == 0)
        {
            var empty = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            empty.CustomMinimumSize = new Vector2(0, 210);
            var emptyTitle = new Label
            {
                Text = "No saved leagues found",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyTitle.AddThemeColorOverride("font_color", ScreenStyles.Text);
            emptyTitle.AddThemeFontSizeOverride("font_size", 20);
            empty.AddChild(emptyTitle);
            var emptyDetail = ScreenStyles.MutedLabel("Return to the start screen and create a league to begin a season.");
            emptyDetail.HorizontalAlignment = HorizontalAlignment.Center;
            empty.AddChild(emptyDetail);
            list.AddChild(empty);
        }
        else
        {
            var scroll = new ScrollContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 300)
            };
            list.AddChild(scroll);

            var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            rows.AddThemeConstantOverride("separation", 8);
            scroll.AddChild(rows);

            foreach (var league in leagues)
            {
                rows.AddChild(LeagueRow(league, loadLeague, deleteLeague));
            }
        }

        AddChild(ScreenStyles.Panel(
            "Saved Competitions",
            list,
            leagues.Count == 1 ? "1 league" : $"{leagues.Count} leagues",
            leagues.Count > 0 ? ScreenStyles.Good : ScreenStyles.MutedText));

        _statusLabel = new Label
        {
            Text = leagues.Count == 0 ? "The league archive is empty." : "Select a league to return to its current season.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _statusLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        AddChild(ScreenStyles.Panel("Status", _statusLabel));
    }

    private static Control LeagueRow(League league, Action<League> loadLeague, Action<League> deleteLeague)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 14);

        var details = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        details.AddThemeConstantOverride("separation", 4);
        var nameLabel = new Label { Text = league.Name };
        nameLabel.AddThemeColorOverride("font_color", ScreenStyles.Text);
        nameLabel.AddThemeFontSizeOverride("font_size", 18);
        details.AddChild(nameLabel);

        var stage = league.Seasons.Count == 0
            ? "Setup in progress"
            : $"Season {league.Seasons.Count}, week {league.Seasons[^1].CurrentWeek}";
        details.AddChild(ScreenStyles.MutedLabel($"{league.Teams.Count}/{league.TargetTeamCount} teams  |  {stage}"));
        row.AddChild(details);

        var loadButton = ScreenStyles.StyledButton("Load", primary: true);
        loadButton.CustomMinimumSize = new Vector2(100, 34);
        loadButton.Pressed += () => loadLeague(league);
        row.AddChild(loadButton);

        var deleteButton = ScreenStyles.StyledButton("Delete", danger: true);
        deleteButton.CustomMinimumSize = new Vector2(100, 34);
        deleteButton.Pressed += () => deleteLeague(league);
        row.AddChild(deleteButton);

        return ScreenStyles.Inset(row);
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
