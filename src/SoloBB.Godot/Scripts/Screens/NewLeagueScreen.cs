using Godot;
using System;

namespace SoloBB.Godot.Scripts.Screens;

public partial class NewLeagueScreen : VBoxContainer
{
    private LineEdit _leagueNameEdit = null!;
    private SpinBox _teamCountSpin = null!;
    private Label _statusLabel = null!;

    public void Setup(int defaultTeamCount, Action<string, int> createLeague, Action back)
    {
        Clear();
        AddTitle("New League");

        _leagueNameEdit = new LineEdit
        {
            PlaceholderText = "League name",
            Text = "Solo Hotseat League"
        };
        AddChild(_leagueNameEdit);

        var grid = new GridContainer { Columns = 2 };
        AddChild(grid);
        grid.AddChild(new Label { Text = "Teams" });
        _teamCountSpin = new SpinBox
        {
            MinValue = 2,
            MaxValue = 32,
            Value = defaultTeamCount,
            Step = 2,
            AllowGreater = false,
            AllowLesser = false
        };
        grid.AddChild(_teamCountSpin);

        AddButton("Next", () => createLeague(_leagueNameEdit.Text, (int)_teamCountSpin.Value));
        AddButton("Back", back);

        _statusLabel = new Label
        {
            Text = "Choose how many teams this league will contain. Scheduling currently uses even team counts.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
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
