using System;
using Godot;

namespace SoloBB.Godot.Scripts.Screens;

/// <summary>
/// A single pitch square: a button hosting the stacked render layers
/// (tile, highlight, marking, piece, overlay). Owns its layers and exposes
/// typed setters so callers never touch the raw <see cref="TextureRect"/>s.
/// </summary>
public partial class PitchTileView : Button
{
    private readonly TextureRect _tileLayer;
    private readonly TextureRect _highlightLayer;
    private readonly TextureRect _markingLayer;
    private readonly TextureRect _pieceLayer;
    private readonly TextureRect _overlayLayer;
    private readonly TextureRect _statusLayer;

    /// <summary>
    /// Raised when the tile is clicked, carrying the mouse button used. Unlike the
    /// base <see cref="BaseButton.Pressed"/> signal this distinguishes left from right
    /// clicks, which the match screen relies on to separate movement/selection (left)
    /// from Pass / Hand-off targeting (right).
    /// </summary>
    public event Action<MouseButton>? Clicked;

    public PitchTileView()
    {
        Text = "";
        CustomMinimumSize = new Vector2(32, 32);
        FocusMode = FocusModeEnum.None;
        ClipContents = false;
        TextureFilter = TextureFilterEnum.Nearest;

        _tileLayer = BuildLayer(TextureRect.StretchModeEnum.Scale);
        _highlightLayer = BuildLayer(TextureRect.StretchModeEnum.Scale);
        _markingLayer = BuildLayer(TextureRect.StretchModeEnum.Scale);
        _pieceLayer = BuildLayer(TextureRect.StretchModeEnum.KeepAspectCentered);
        _overlayLayer = BuildLayer(TextureRect.StretchModeEnum.KeepAspectCentered);
        _statusLayer = BuildStatusLayer();
        _pieceLayer.ZIndex = 10;
        _overlayLayer.ZIndex = 20;
        _statusLayer.ZIndex = 30;
        AddChild(_tileLayer);
        AddChild(_highlightLayer);
        AddChild(_markingLayer);
        AddChild(_pieceLayer);
        AddChild(_overlayLayer);
        AddChild(_statusLayer);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Disabled)
        {
            return;
        }

        if (@event is InputEventMouseButton { Pressed: true } mouseButton &&
            mouseButton.ButtonIndex is MouseButton.Left or MouseButton.Right)
        {
            Clicked?.Invoke(mouseButton.ButtonIndex);
            AcceptEvent();
        }
    }

    public void SetTile(Texture2D? texture) => _tileLayer.Texture = texture;

    public void SetHighlight(Texture2D? texture, Color? modulate = null)
    {
        _highlightLayer.Texture = texture;
        _highlightLayer.Modulate = modulate ?? Colors.White;
    }

    public void SetMarking(Texture2D? texture) => _markingLayer.Texture = texture;

    public void SetPiece(Texture2D? texture, Color? modulate = null, bool large = false)
    {
        _pieceLayer.Texture = texture;
        _pieceLayer.Modulate = modulate ?? Colors.White;
        _pieceLayer.OffsetLeft = large ? -16 : 0;
        _pieceLayer.OffsetTop = large ? -32 : 0;
        _pieceLayer.OffsetRight = large ? 16 : 0;
        _pieceLayer.OffsetBottom = 0;
    }

    public void SetOverlay(Texture2D? texture, Color? modulate = null, float inset = 0, Vector2? offset = null)
    {
        var shift = offset ?? Vector2.Zero;
        _overlayLayer.Texture = texture;
        _overlayLayer.Modulate = modulate ?? Colors.White;
        _overlayLayer.OffsetLeft = inset + shift.X;
        _overlayLayer.OffsetTop = inset + shift.Y;
        _overlayLayer.OffsetRight = -inset + shift.X;
        _overlayLayer.OffsetBottom = -inset + shift.Y;
    }

    /// <summary>
    /// Sets the status overlay (e.g. the stunned stars), anchored to the top half of
    /// the tile so it sits above a downed player's body rather than over or below it.
    /// </summary>
    public void SetStatus(Texture2D? texture) => _statusLayer.Texture = texture;

    private static TextureRect BuildStatusLayer()
    {
        return BuildLayer(TextureRect.StretchModeEnum.KeepAspectCentered);
    }

    private static TextureRect BuildLayer(TextureRect.StretchModeEnum stretchMode)
    {
        var layer = new TextureRect
        {
            MouseFilter = MouseFilterEnum.Ignore,
            StretchMode = stretchMode,
            TextureFilter = TextureFilterEnum.Nearest
        };
        layer.SetAnchorsPreset(LayoutPreset.FullRect);
        layer.OffsetLeft = 0;
        layer.OffsetTop = 0;
        layer.OffsetRight = stretchMode == TextureRect.StretchModeEnum.Scale ? 1 : 0;
        layer.OffsetBottom = stretchMode == TextureRect.StretchModeEnum.Scale ? 1 : 0;
        return layer;
    }
}
