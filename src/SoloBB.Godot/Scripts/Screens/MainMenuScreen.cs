using Godot;
using System;
using System.Threading.Tasks;

namespace SoloBB.Godot.Scripts.Screens;

public partial class MainMenuScreen : VBoxContainer
{
    private Label _statusLabel = null!;

    public void Setup(Action newLeague, Func<Task> loadLeague, Action quit, string status)
    {
        Clear();

        var title = new Label
        {
            Text = "Solo BB",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 42);
        AddChild(title);

        AddButton("New League", newLeague);
        AddButton("Load League", () => { _ = loadLeague(); });
        AddButton("Quit", quit);

        _statusLabel = new Label
        {
            Text = status,
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

    private void AddButton(string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        AddChild(button);
    }

    private void AddButton(string text, Func<Task> pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += async () => await pressed();
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
