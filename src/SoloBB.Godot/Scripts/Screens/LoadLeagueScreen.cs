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
        AddTitle("Load League");

        if (leagues.Count == 0)
        {
            AddChild(new Label { Text = "No saved leagues found." });
        }
        else
        {
            var scroll = new ScrollContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 200)
            };
            AddChild(scroll);

            var list = new VBoxContainer();
            scroll.AddChild(list);

            foreach (var league in leagues)
            {
                var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

                var nameLabel = new Label
                {
                    Text = league.Name,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                row.AddChild(nameLabel);

                var teamCount = new Label
                {
                    Text = $"{league.Teams.Count} teams",
                    CustomMinimumSize = new Vector2(80, 0),
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                row.AddChild(teamCount);

                var capturedLeague = league;
                var loadButton = new Button { Text = "Load" };
                loadButton.Pressed += () => loadLeague(capturedLeague);
                row.AddChild(loadButton);

                var deleteButton = new Button { Text = "Delete" };
                deleteButton.Pressed += () => deleteLeague(capturedLeague);
                row.AddChild(deleteButton);

                list.AddChild(row);
            }
        }

        AddButton("Back", back);

        _statusLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        AddChild(_statusLabel);
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
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

    private void AddButton(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        AddChild(button);
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
