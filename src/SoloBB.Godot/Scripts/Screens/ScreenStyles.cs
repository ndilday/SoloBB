using System.Collections.Generic;
using Godot;

namespace SoloBB.Godot.Scripts.Screens;

internal static class ScreenStyles
{
    public static readonly Color ScreenBackground = new("17211d");
    public static readonly Color PanelBackground = new("202522");
    public static readonly Color PanelHeaderBackground = new("292d28");
    public static readonly Color PanelBorder = new("4d554b");
    public static readonly Color PanelBorderSoft = new("343b35");
    public static readonly Color Text = new("f0ead8");
    public static readonly Color MutedText = new("b8b19f");
    public static readonly Color DimText = new("817b70");
    public static readonly Color Brass = new("d9aa4e");
    public static readonly Color Good = new("8bc982");
    public static readonly Color Warning = new("f0c45b");
    public static readonly Color Danger = new("d66a58");
    public static readonly string TeamManagementTexturePath = "res://assets/ui/team_management_texture.png";

    public static AcceptDialog Dialog(string title, Vector2I minSize)
    {
        var dialog = new AcceptDialog
        {
            Title = title,
            Unresizable = false,
            MinSize = minSize
        };
        ApplyDialogStyle(dialog);
        dialog.GetOkButton().Text = "Close";
        return dialog;
    }

    public static MarginContainer DialogContent()
    {
        var content = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        content.AddThemeConstantOverride("margin_left", 12);
        content.AddThemeConstantOverride("margin_top", 12);
        content.AddThemeConstantOverride("margin_right", 12);
        content.AddThemeConstantOverride("margin_bottom", 12);
        return content;
    }

    public static void ApplyDialogStyle(AcceptDialog dialog)
    {
        dialog.AddThemeColorOverride("title_color", Text);
        dialog.AddThemeColorOverride("close_hl_color", Text);
        dialog.AddThemeStyleboxOverride("embedded_border", FlatStyle(PanelBackground, PanelBorder, 2));
        ApplyButtonStyle(dialog.GetOkButton());
    }

    public static PanelContainer ScreenHeader(string eyebrow, string title, string subtitle, Control? actions = null)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var headerStyle = FlatStyle(new Color("202720"), PanelBorderSoft);
        headerStyle.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", headerStyle);

        var row = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 16);
        panel.AddChild(row);

        var copy = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        copy.AddThemeConstantOverride("separation", 3);
        row.AddChild(copy);

        var eyebrowLabel = MutedLabel(eyebrow.ToUpperInvariant());
        eyebrowLabel.AddThemeColorOverride("font_color", Brass);
        copy.AddChild(eyebrowLabel);
        copy.AddChild(Title(title));

        var subtitleLabel = MutedLabel(subtitle);
        subtitleLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        copy.AddChild(subtitleLabel);

        if (actions is not null)
        {
            actions.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(actions);
        }

        return panel;
    }

    public static StyleBoxFlat FlatStyle(Color background, Color? border = null, int borderWidth = 1)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border ?? background.Darkened(0.25f)
        };
        style.SetBorderWidthAll(borderWidth);
        style.SetContentMarginAll(8);
        return style;
    }

    public static PanelContainer Panel(string title, Control body, string? badge = null, Color? badgeColor = null)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(PanelBackground, PanelBorder));

        var stack = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        stack.AddThemeConstantOverride("separation", 0);
        panel.AddChild(stack);

        var header = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        header.AddThemeConstantOverride("separation", 8);
        header.AddThemeStyleboxOverride("panel", FlatStyle(PanelHeaderBackground, PanelBorderSoft));

        var titleLabel = new Label
        {
            Text = title,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleLabel.AddThemeFontSizeOverride("font_size", 15);
        titleLabel.AddThemeColorOverride("font_color", Text);
        header.AddChild(titleLabel);

        if (!string.IsNullOrWhiteSpace(badge))
        {
            header.AddChild(Badge(badge, badgeColor));
        }

        var headerStyle = FlatStyle(PanelHeaderBackground, PanelBorderSoft);
        headerStyle.ContentMarginTop = 4;
        headerStyle.ContentMarginBottom = 4;

        var headerWrap = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        headerWrap.AddThemeStyleboxOverride("panel", headerStyle);
        headerWrap.AddChild(header);
        stack.AddChild(headerWrap);

        var margin = new MarginContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = body.SizeFlagsVertical
        };
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        margin.AddChild(body);
        stack.AddChild(margin);

        return panel;
    }

    public static PanelContainer Inset(Control body)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("181e1a"), PanelBorderSoft));
        panel.AddChild(body);
        return panel;
    }

    public static Label Badge(string text, Color? color = null)
    {
        var badge = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            CustomMinimumSize = new Vector2(72, 24)
        };
        var accent = color ?? MutedText;
        badge.AddThemeColorOverride("font_color", accent);
        badge.AddThemeFontSizeOverride("font_size", 12);
        badge.AddThemeStyleboxOverride("normal", FlatStyle(new Color("151b18"), accent.Darkened(0.35f)));
        return badge;
    }

    public static Button StyledButton(string text, bool primary = false, bool danger = false, bool disabled = false)
    {
        var button = new Button { Text = text, Disabled = disabled, CustomMinimumSize = new Vector2(0, 34) };
        ApplyButtonStyle(button, primary, danger);
        return button;
    }

    public static void ApplyButtonStyle(Button button, bool primary = false, bool danger = false)
    {
        var background = primary
            ? new Color("9b7430")
            : danger
                ? new Color("33201d")
                : new Color("38433b");
        var border = primary
            ? new Color("e0b65d")
            : danger
                ? new Color("9d5a4c")
                : new Color("79836f");

        button.AddThemeStyleboxOverride("normal", FlatStyle(background, border));
        button.AddThemeStyleboxOverride("hover", FlatStyle(background.Lightened(0.12f), border.Lightened(0.12f), 2));
        button.AddThemeStyleboxOverride("pressed", FlatStyle(background.Darkened(0.12f), border, 2));
        button.AddThemeStyleboxOverride("disabled", FlatStyle(new Color("242a26"), new Color("343a35")));
        button.AddThemeColorOverride("font_color", primary ? new Color("15110a") : Text);
        button.AddThemeColorOverride("font_disabled_color", DimText);
    }

    public static void StyleTable(Tree tree, IReadOnlyList<HorizontalAlignment> columnAlignments)
    {
        tree.AddThemeStyleboxOverride("panel", FlatStyle(new Color("181e1a"), PanelBorderSoft));
        tree.AddThemeStyleboxOverride("title_button_normal", FlatStyle(new Color("111412"), PanelBorderSoft));
        tree.AddThemeStyleboxOverride("title_button_hover", FlatStyle(new Color("202720"), PanelBorder));
        tree.AddThemeStyleboxOverride("title_button_pressed", FlatStyle(new Color("111412"), PanelBorderSoft));
        tree.AddThemeStyleboxOverride("selected", FlatStyle(new Color("626862"), new Color("737b71")));
        tree.AddThemeStyleboxOverride("selected_focus", FlatStyle(new Color("626862"), new Color("737b71")));
        tree.AddThemeStyleboxOverride("cursor", new StyleBoxEmpty());
        tree.AddThemeColorOverride("font_color", MutedText);
        tree.AddThemeColorOverride("font_selected_color", Text);
        tree.AddThemeColorOverride("title_button_color", Text);
        tree.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0));
        tree.AddThemeConstantOverride("h_separation", 10);
        tree.AddThemeConstantOverride("v_separation", 5);

        for (var column = 0; column < tree.Columns && column < columnAlignments.Count; column++)
        {
            tree.SetColumnTitleAlignment(column, columnAlignments[column]);
        }
    }

    public static Label MutedLabel(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeColorOverride("font_color", MutedText);
        label.AddThemeFontSizeOverride("font_size", 12);
        return label;
    }

    public static Label Title(string text)
    {
        var title = new Label { Text = text };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", Text);
        return title;
    }
}
