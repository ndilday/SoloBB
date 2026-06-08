using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SoloBB.Core.Domain;
using SoloBB.Core.Services;

namespace SoloBB.Godot.Scripts.Screens;

public partial class MatchScreen : VBoxContainer
{
    private Control BuildMatchHud()
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("111614"), border: new Color("415044"), borderWidth: 2));

        var hud = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        hud.AddThemeConstantOverride("separation", 8);
        panel.AddChild(hud);

        _homeHudLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _homeHudLabel.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_homeHudLabel);

        _turnHudLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _turnHudLabel.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_turnHudLabel);

        _awayHudLabel = new Label
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _awayHudLabel.AddThemeFontSizeOverride("font_size", 14);
        hud.AddChild(_awayHudLabel);

        return panel;
    }

    private Control BuildRosterPanel()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(250, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(PanelBackground, border: new Color("506358"), borderWidth: 2));

        var stack = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        stack.AddThemeConstantOverride("separation", 5);
        panel.AddChild(stack);

        stack.AddChild(new Label
        {
            Text = "Active Roster",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _selectedLabel = new Label
        {
            Text = "No player selected.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _selectedLabel.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(_selectedLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        stack.AddChild(scroll);

        _rosterList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _rosterList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_rosterList);

        return panel;
    }

    private Control BuildPitchPanel()
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("18261c"), border: new Color("5a6a4f"), borderWidth: 2));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 4);
        panel.AddChild(stack);

        _pitchViewport = new Control
        {
            ClipContents = true,
            FocusMode = FocusModeEnum.All,
            MouseFilter = MouseFilterEnum.Stop,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _pitchViewport.Resized += OnPitchViewportResized;
        stack.AddChild(_pitchViewport);

        _pitchGrid = new GridContainer
        {
            Columns = _ruleset.PitchWidth,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
            SizeFlagsVertical = SizeFlags.ShrinkBegin,
            PivotOffset = Vector2.Zero,
            TextureFilter = TextureFilterEnum.Nearest
        };
        _pitchGrid.AddThemeConstantOverride("h_separation", -1);
        _pitchGrid.AddThemeConstantOverride("v_separation", -1);
        _pitchViewport.AddChild(_pitchGrid);
        BuildPitchGrid();
        InitializePitchZoom();

        return panel;
    }

    private bool HandlePitchKey(Key key)
    {
        if (key is Key.Plus or Key.Equal or Key.KpAdd)
        {
            ZoomPitch(PitchZoomStep, PitchViewportCenterGlobalPosition());
            return true;
        }

        if (key is Key.Minus or Key.KpSubtract)
        {
            ZoomPitch(1.0f / PitchZoomStep, PitchViewportCenterGlobalPosition());
            return true;
        }

        var panDelta = key switch
        {
            Key.W => new Vector2(0, KeyboardPanStep),
            Key.A => new Vector2(KeyboardPanStep, 0),
            Key.S => new Vector2(0, -KeyboardPanStep),
            Key.D => new Vector2(-KeyboardPanStep, 0),
            _ => Vector2.Zero
        };

        if (panDelta == Vector2.Zero)
        {
            return false;
        }

        PanPitch(panDelta);
        return true;
    }

    private void CenterPitch()
    {
        var baseSize = BasePitchSize();
        var viewportSize = _pitchViewport?.Size ?? Vector2.Zero;
        if (baseSize == Vector2.Zero || viewportSize == Vector2.Zero)
        {
            return;
        }

        _pitchPan = (viewportSize - (baseSize * _pitchZoom)) / 2.0f;
        ApplyPitchTransform();
    }

    private void InitializePitchZoom()
    {
        if (_pitchViewport is null || _pitchGrid is null || _pitchZoomInitialized)
        {
            return;
        }

        var viewportSize = _pitchViewport.Size;
        var baseSize = BasePitchSize();
        if (viewportSize.X <= 0 || viewportSize.Y <= 0 || baseSize.X <= 0 || baseSize.Y <= 0)
        {
            CallDeferred(nameof(InitializePitchZoom));
            return;
        }

        var fitZoom = Math.Min(viewportSize.X / baseSize.X, viewportSize.Y / baseSize.Y);
        _pitchZoom = Math.Clamp(fitZoom, MinPitchZoom, MaxPitchZoom);
        _pitchZoomInitialized = true;
        CenterPitch();
    }

    private void OnPitchViewportResized()
    {
        if (!_pitchZoomInitialized)
        {
            InitializePitchZoom();
            return;
        }

        ApplyPitchTransform();
    }

    private void PanPitch(Vector2 delta)
    {
        _pitchPan += delta;
        ApplyPitchTransform();
    }

    private void ZoomPitch(float factor, Vector2 globalAnchor)
    {
        var nextZoom = Math.Clamp(_pitchZoom * factor, MinPitchZoom, MaxPitchZoom);
        if (Math.Abs(nextZoom - _pitchZoom) < 0.001f)
        {
            return;
        }

        var localAnchor = globalAnchor - _pitchViewport.GlobalPosition;
        var pitchPoint = (localAnchor - _pitchPan) / _pitchZoom;
        _pitchZoom = nextZoom;
        _pitchPan = localAnchor - (pitchPoint * _pitchZoom);
        ApplyPitchTransform();
    }

    private void ApplyPitchTransform()
    {
        if (_pitchViewport is null || _pitchGrid is null)
        {
            return;
        }

        _pitchPan = ClampPitchPan(_pitchPan);
        _pitchGrid.Scale = new Vector2(_pitchZoom, _pitchZoom);
        _pitchGrid.Position = _pitchPan;
    }

    private Vector2 ClampPitchPan(Vector2 pan)
    {
        var viewportSize = _pitchViewport.Size;
        var contentSize = BasePitchSize() * _pitchZoom;

        return new Vector2(
            ClampPitchAxis(pan.X, viewportSize.X, contentSize.X),
            ClampPitchAxis(pan.Y, viewportSize.Y, contentSize.Y));
    }

    private static float ClampPitchAxis(float pan, float viewportLength, float contentLength)
    {
        if (contentLength <= viewportLength)
        {
            return (viewportLength - contentLength) / 2.0f;
        }

        return Math.Clamp(pan, viewportLength - contentLength, 0.0f);
    }

    private Vector2 BasePitchSize()
    {
        return new Vector2(
            (_ruleset.PitchWidth * BasePitchSquareSize) - ((_ruleset.PitchWidth - 1) * PitchSquareOverlap),
            (_ruleset.PitchHeight * BasePitchSquareSize) - ((_ruleset.PitchHeight - 1) * PitchSquareOverlap));
    }

    private Vector2 PitchViewportCenterGlobalPosition()
    {
        return _pitchViewport.GlobalPosition + (_pitchViewport.Size / 2.0f);
    }

    private Control BuildEventLogPanel()
    {
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(245, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        panel.AddThemeStyleboxOverride("panel", FlatStyle(new Color("1c2420"), border: new Color("4f5846"), borderWidth: 2));

        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 5);
        panel.AddChild(stack);

        stack.AddChild(new Label
        {
            Text = "Event Log",
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _lastEventLabel = new Label
        {
            Text = "No match events yet.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _lastEventLabel.AddThemeFontSizeOverride("font_size", 12);
        stack.AddChild(_lastEventLabel);

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        stack.AddChild(scroll);

        _eventLogList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _eventLogList.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_eventLogList);

        return panel;
    }

    private void BuildPitchGrid()
    {
        _pitchTiles.Clear();
        for (var y = 0; y < _ruleset.PitchHeight; y++)
        {
            for (var x = 0; x < _ruleset.PitchWidth; x++)
            {
                var square = new PitchSquare(x, y);
                var tile = new PitchTileView { TooltipText = $"{x + 1},{y + 1}" };
                ClearPitchButtonChrome(tile);
                tile.AddThemeFontSizeOverride("font_size", 10);
                tile.Pressed += async () => await HandlePitchSquareAsync(square);

                _pitchTiles[square] = tile;
                _pitchGrid.AddChild(tile);
            }
        }
    }

    private static void ClearPitchButtonChrome(Button button)
    {
        var empty = new StyleBoxEmpty();
        button.AddThemeStyleboxOverride("normal", empty);
        button.AddThemeStyleboxOverride("disabled", empty);
        button.AddThemeStyleboxOverride("hover", empty);
        button.AddThemeStyleboxOverride("pressed", empty);
        button.AddThemeStyleboxOverride("focus", empty);
    }


    private Button ActionButton(string text, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(primary ? 116 : 76, 30)
        };
        var background = primary ? new Color("4c4324") : new Color("253a32");
        var border = primary ? SelectedColor : new Color("5d6755");
        button.AddThemeStyleboxOverride("normal", FlatStyle(background, border, borderWidth: primary ? 2 : 1));
        button.AddThemeStyleboxOverride("hover", FlatStyle(background.Lightened(0.12f), SelectedColor, borderWidth: 2));
        button.AddThemeStyleboxOverride("pressed", FlatStyle(background.Darkened(0.12f), SelectedColor, borderWidth: 2));
        button.AddThemeStyleboxOverride("disabled", FlatStyle(new Color("242a26"), new Color("343a35")));
        return button;
    }

    private static StyleBoxFlat FlatStyle(Color background, Color? border = null, int borderWidth = 1)
    {
        var style = new StyleBoxFlat
        {
            BgColor = background,
            BorderColor = border ?? background.Darkened(0.25f)
        };
        style.SetBorderWidthAll(borderWidth);
        style.SetCornerRadiusAll(3);
        return style;
    }
}
