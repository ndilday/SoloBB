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
        AddThemeConstantOverride("separation", 12);
        AddThemeStyleboxOverride("panel", ScreenStyles.FlatStyle(ScreenStyles.ScreenBackground));

        var headerBadge = ScreenStyles.Badge("BB2020", ScreenStyles.Brass);
        AddChild(ScreenStyles.ScreenHeader(
            "Solo League Manager",
            "Solo BB",
            "Run a complete hotseat league from team drafting through the final whistle.",
            headerBadge));

        var body = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        body.AddThemeConstantOverride("separation", 12);
        AddChild(body);

        var actionStack = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.5f };
        actionStack.AddThemeConstantOverride("separation", 10);
        actionStack.AddChild(MenuAction(
            "New League",
            "Choose the league size, draft every team, and generate a fresh season schedule.",
            "Create League",
            newLeague,
            primary: true));
        actionStack.AddChild(MenuAction(
            "Continue a League",
            "Open a saved competition and return to its table, teams, and scheduled matches.",
            "Load League",
            () => { _ = loadLeague(); }));
        body.AddChild(ScreenStyles.Panel("League Desk", actionStack, "Hotseat", ScreenStyles.Brass));

        var guide = new VBoxContainer
        {
            CustomMinimumSize = new Vector2(300, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.8f
        };
        guide.AddThemeConstantOverride("separation", 12);
        guide.AddChild(GuideStep("1", "Create", "Set up the league and draft its teams."));
        guide.AddChild(GuideStep("2", "Schedule", "Generate weekly matchups for the season."));
        guide.AddChild(GuideStep("3", "Play", "Resolve each match in local hotseat play."));
        body.AddChild(ScreenStyles.Panel("Season Flow", guide, "3 steps"));

        _statusLabel = new Label
        {
            Text = status,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _statusLabel.AddThemeColorOverride("font_color", ScreenStyles.MutedText);
        AddChild(ScreenStyles.Panel("Rules Catalog", _statusLabel, "Local data", ScreenStyles.Good));

        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var quitButton = ScreenStyles.StyledButton("Quit", danger: true);
        quitButton.CustomMinimumSize = new Vector2(120, 34);
        quitButton.Pressed += quit;
        footer.AddChild(quitButton);
        AddChild(footer);
    }

    public void SetStatus(string status)
    {
        if (_statusLabel is not null)
        {
            _statusLabel.Text = status;
        }
    }

    private static Control MenuAction(string title, string detail, string buttonText, Action pressed, bool primary = false)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 14);

        var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        copy.AddThemeConstantOverride("separation", 4);
        var titleLabel = new Label { Text = title };
        titleLabel.AddThemeColorOverride("font_color", ScreenStyles.Text);
        titleLabel.AddThemeFontSizeOverride("font_size", 17);
        copy.AddChild(titleLabel);
        var detailLabel = ScreenStyles.MutedLabel(detail);
        detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        copy.AddChild(detailLabel);
        row.AddChild(copy);

        var button = ScreenStyles.StyledButton(buttonText, primary);
        button.CustomMinimumSize = new Vector2(150, 38);
        button.Pressed += pressed;
        row.AddChild(button);
        return ScreenStyles.Inset(row);
    }

    private static Control GuideStep(string number, string title, string detail)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        row.AddChild(ScreenStyles.Badge(number, ScreenStyles.Brass));

        var copy = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        copy.AddChild(new Label { Text = title });
        var detailLabel = ScreenStyles.MutedLabel(detail);
        detailLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        copy.AddChild(detailLabel);
        row.AddChild(copy);
        return row;
    }

    private void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }
}
