using Godot;
using System;
using SoloBB.Core.Domain;

namespace SoloBB.Godot.Scripts.Screens;

public partial class TeamHomeScreen : VBoxContainer
{
    public void Setup(LeagueTeam team, Action back)
    {
        Clear();

        var title = new Label
        {
            Text = team.Name,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 32);
        AddChild(title);

        AddChild(new Label { Text = $"Coach: {team.CoachName}" });
        AddChild(new Label { Text = $"Team Value: {FormatGold(team.TeamValue)}" });
        AddChild(new Label { Text = $"Treasury: {FormatGold(team.Treasury)}" });
        AddChild(new Label { Text = $"Players: {team.Players.Count}" });

        var backButton = new Button { Text = "Back" };
        backButton.Pressed += back;
        AddChild(backButton);
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
