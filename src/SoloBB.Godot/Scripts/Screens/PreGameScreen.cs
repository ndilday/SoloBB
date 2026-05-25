using Godot;
using System;
using System.Linq;
using SoloBB.Core.Domain;

namespace SoloBB.Godot.Scripts.Screens;

public partial class PreGameScreen : VBoxContainer
{
    public void Setup(League league, ScheduledMatch scheduledMatch, Action done, Action back)
    {
        Clear();

        AddTitle("Pre-Game");
        AddChild(new Label { Text = $"Week {scheduledMatch.Week}" });
        AddChild(new Label { Text = $"{TeamName(league, scheduledMatch.HomeTeamId)} vs {TeamName(league, scheduledMatch.AwayTeamId)}" });
        AddChild(new Label { Text = "Inducements and journeymen will be handled here later.", AutowrapMode = TextServer.AutowrapMode.WordSmart });

        var doneButton = new Button { Text = "Done" };
        doneButton.Pressed += done;
        AddChild(doneButton);

        var backButton = new Button { Text = "Back" };
        backButton.Pressed += back;
        AddChild(backButton);
    }

    private static string TeamName(League league, Guid teamId)
    {
        return league.Teams.FirstOrDefault(team => team.Id == teamId)?.Name ?? "Unknown";
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

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
