using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// The objective chosen on the phone, on the game's screen: its name, how far it is over the
/// ground, and how far up or down. The trail in the world says which way; this says how much
/// further, which the world cannot.
///
/// One control that paints itself rather than a panel of labels: the arrow is drawn, since Blish
/// HUD's fonts have no arrows, and the card sizes itself to what it says.
///
/// Dragged by the player to wherever their interface leaves room; <see cref="Dropped"/> says where.
/// </summary>
internal sealed class ObjectiveCard : Control
{
    private static readonly Color Brass = new Color(201, 162, 39);
    private static readonly Color Glint = new Color(255, 221, 120);
    private static readonly Color Vellum = new Color(236, 228, 210);

    private const int Edge = 3;
    private const int PadX = 12;
    private const int PadY = 7;
    private const int Gap = 3;
    /// <summary>The arrow's width in pixels; it is half as tall, plus one. Sized to sit beside the
    /// distance as its equal, not as a footnote to it.</summary>
    private const int Arrow = 15;
    private const int MinWidth = 150;

    private readonly BitmapFont _nameFont = GameService.Content.DefaultFont16;
    private readonly BitmapFont _climbFont =
        GameService.Content.GetFont(ContentService.FontFace.Menomonia, ContentService.FontSize.Size18, ContentService.FontStyle.Regular);
    private readonly BitmapFont _figureFont =
        GameService.Content.GetFont(ContentService.FontFace.Menomonia, ContentService.FontSize.Size20, ContentService.FontStyle.Regular);

    private string _name = string.Empty;
    private string _figure = string.Empty;
    private string _climb = string.Empty;
    private int _climbWay;
    private bool _quiet;

    /// <summary>Where the grab started, against the card's corner; null when not being dragged.</summary>
    private Point? _grab;

    /// <summary>Raised once a drag ends, with where the card now is.</summary>
    public event Action<Point>? Dropped;

    public ObjectiveCard()
    {
        ClipsBounds = false;
        Resize();
    }

    /// <summary>What the card says: the objective, the figure under it, and how far up or down.</summary>
    /// <param name="figure">The distance, or a word in its place: arrived, on another map.</param>
    /// <param name="climb">Whole metres up, negative down; null for level or unknown.</param>
    /// <param name="quiet">Said in the dimmer colour: nothing to walk towards from here.</param>
    public void Say(string? name, string figure, int? climb, bool quiet)
    {
        var climbText = climb is { } c ? CardText.ClimbText(c) : string.Empty;
        var way = climb is { } d ? Math.Sign(d) : 0;
        name ??= string.Empty;

        if (name == _name && figure == _figure && climbText == _climb && way == _climbWay && quiet == _quiet) return;

        _name = name;
        _figure = figure;
        _climb = climbText;
        _climbWay = way;
        _quiet = quiet;

        Resize();
    }

    private void Resize()
    {
        var nameWidth = _name.Length == 0 ? 0 : (int)Math.Ceiling(_nameFont.MeasureString(_name).Width);
        var figureWidth = (int)Math.Ceiling(_figureFont.MeasureString(_figure).Width);
        var climbWidth = _climb.Length == 0 ? 0 : Gap * 4 + Arrow + Gap * 2 + (int)Math.Ceiling(_climbFont.MeasureString(_climb).Width);

        var nameHeight = _name.Length == 0 ? 0 : _nameFont.LineHeight + Gap;

        Size = new Point(
            Math.Max(MinWidth, Edge + PadX * 2 + Math.Max(nameWidth, figureWidth + climbWidth)),
            PadY * 2 + nameHeight + _figureFont.LineHeight);
    }

    protected override void OnLeftMouseButtonPressed(MouseEventArgs e)
    {
        _grab = GameService.Input.Mouse.Position - Location;
        base.OnLeftMouseButtonPressed(e);
    }

    /// <summary>Follows the mouse while the button is held. Called every frame by the module.</summary>
    public void Drag()
    {
        if (_grab is not { } grab) return;

        if (GameService.Input.Mouse.State.LeftButton != Microsoft.Xna.Framework.Input.ButtonState.Pressed)
        {
            _grab = null;
            Dropped?.Invoke(Location);
            return;
        }

        Location = Keep(GameService.Input.Mouse.Position - grab);
    }

    /// <summary>A place on the screen with the whole card on it.</summary>
    public Point Keep(Point at)
    {
        var screen = GameService.Graphics.SpriteScreen.Size;

        return new Point(
            MathHelper.Clamp(at.X, 0, Math.Max(0, screen.X - Width)),
            MathHelper.Clamp(at.Y, 0, Math.Max(0, screen.Y - Height)));
    }

    protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
    {
        var pixel = ContentService.Textures.Pixel;

        spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(0, 0, Width, Height), Color.Black * 0.6f);
        spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(0, 0, Edge, Height), Brass);

        var x = Edge + PadX;
        var y = PadY;

        if (_name.Length > 0)
        {
            spriteBatch.DrawStringOnCtrl(this, _name, _nameFont,
                new Rectangle(x, y, Width - x - PadX, _nameFont.LineHeight), Vellum, false, true);
            y += _nameFont.LineHeight + Gap;
        }

        var figureColour = _quiet ? Vellum * 0.7f : Glint;
        var figureWidth = (int)Math.Ceiling(_figureFont.MeasureString(_figure).Width);

        spriteBatch.DrawStringOnCtrl(this, _figure, _figureFont,
            new Rectangle(x, y, figureWidth + 2, _figureFont.LineHeight), figureColour, false, true);

        if (_climb.Length == 0) return;

        // The arrow, then how far: level with the figure's middle.
        var arrowX = x + figureWidth + Gap * 4;
        var middle = y + _figureFont.LineHeight / 2;

        PaintArrow(spriteBatch, pixel, arrowX, middle, _climbWay > 0);

        spriteBatch.DrawStringOnCtrl(this, _climb, _climbFont,
            new Rectangle(arrowX + Arrow + Gap * 2, y, Width - arrowX, _figureFont.LineHeight), Vellum, false, true,
            1, HorizontalAlignment.Left, VerticalAlignment.Middle);
    }

    /// <summary>A small solid triangle, point up or down, centred on <paramref name="middle"/>: one
    /// row of pixels at a time, which is all a triangle this size is.</summary>
    private void PaintArrow(SpriteBatch spriteBatch, Texture2D pixel, int left, int middle, bool up)
    {
        var rows = (Arrow + 1) / 2;
        var top = middle - rows / 2;

        for (var row = 0; row < rows; row++)
        {
            var width = up ? 1 + row * 2 : Arrow - row * 2;
            var x = left + (Arrow - width) / 2;

            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(x, top + row, width, 1), Vellum);
        }
    }
}
