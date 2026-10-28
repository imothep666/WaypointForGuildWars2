using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;

namespace GuildWarsCompanion.Blish;

/// <summary>
/// An alert from the phone, on the game's screen: its title, and the line under it broken to fit.
///
/// In place of Blish HUD's own <see cref="ScreenNotification"/>, which writes one line in a large
/// font across a fixed 1024 pixels and never breaks it: a price alert with its detail ran off both
/// sides of the screen. This box is as wide as its longest line, no wider than <see cref="MaxText"/>,
/// and as tall as its lines; it looks like the objective card, and fades in and out where Blish
/// HUD's would, a quarter of the way down.
///
/// Several at once stand one under the other, the newest at the bottom; <see cref="Arrange"/> puts
/// them there, and closes the gap when one fades.
/// </summary>
internal sealed class AlertBox : Control
{
    private static readonly Color Brass = new Color(201, 162, 39);
    private static readonly Color Glint = new Color(255, 221, 120);
    private static readonly Color Vellum = new Color(236, 228, 210);

    private const int Edge = 3;
    private const int PadX = 14;
    private const int PadY = 9;
    private const int Gap = 4;

    /// <summary>The widest a line of text may be, in pixels, on a screen with room for it.</summary>
    private const int MaxText = 620;

    /// <summary>Between one box and the next when several are up.</summary>
    private const int Between = 6;

    private readonly BitmapFont _titleFont =
        GameService.Content.GetFont(ContentService.FontFace.Menomonia, ContentService.FontSize.Size22, ContentService.FontStyle.Regular);
    private readonly BitmapFont _detailFont = GameService.Content.DefaultFont16;

    private readonly string _title;
    private readonly string _detail;

    private IReadOnlyList<string> _titleLines = Array.Empty<string>();
    private IReadOnlyList<string> _detailLines = Array.Empty<string>();

    /// <summary>The screen width the lines were broken for; they are broken again when it changes.</summary>
    private int _brokenFor = -1;

    public AlertBox(string title, string detail)
    {
        _title = title;
        _detail = detail;

        ClipsBounds = false;
        Opacity = 0;
        Resize();
    }

    /// <summary>Fades in, stays <paramref name="seconds"/>, fades out and is disposed, after which
    /// <paramref name="gone"/> is called.</summary>
    public void Show(float seconds, Action gone)
    {
        GameService.Animation.Tweener
            .Tween(this, new { Opacity = 1f }, 0.2f)
            .Repeat(1)
            .RepeatDelay(seconds)
            .Reflect()
            .OnComplete(() =>
            {
                Dispose();
                gone();
            });
    }

    /// <summary>The boxes still up, one under the other from a quarter of the way down, each
    /// centred. Called every frame by the module, so a resized window or a faded box is
    /// caught up with.</summary>
    public static void Arrange(IEnumerable<AlertBox> boxes)
    {
        var screen = GameService.Graphics.SpriteScreen.Size;
        var top = screen.Y / 4;

        foreach (var box in boxes)
        {
            box.Resize();
            box.Location = new Point((screen.X - box.Width) / 2, top);
            top += box.Height + Between;
        }
    }

    /// <summary>Lets clicks through to the game: an alert is read, not handled.</summary>
    protected override CaptureType CapturesInput() => CaptureType.Filter;

    private void Resize()
    {
        var screen = GameService.Graphics.SpriteScreen.Width;

        if (screen == _brokenFor) return;
        _brokenFor = screen;

        var room = Math.Max(120, Math.Min(MaxText, screen - (Edge + PadX * 2) * 2));

        _titleLines = AlertText.Wrap(_title, room, s => _titleFont.MeasureString(s).Width);
        _detailLines = AlertText.Wrap(_detail, room, s => _detailFont.MeasureString(s).Width);

        var widest = _titleLines.Select(l => _titleFont.MeasureString(l).Width)
            .Concat(_detailLines.Select(l => _detailFont.MeasureString(l).Width))
            .DefaultIfEmpty(0)
            .Max();

        var between = _titleLines.Count > 0 && _detailLines.Count > 0 ? Gap : 0;

        Size = new Point(
            Edge + PadX * 2 + (int)Math.Ceiling(widest),
            PadY * 2 + _titleLines.Count * _titleFont.LineHeight + between + _detailLines.Count * _detailFont.LineHeight);
    }

    protected override void Paint(SpriteBatch spriteBatch, Rectangle bounds)
    {
        var pixel = ContentService.Textures.Pixel;

        spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(0, 0, Width, Height), Color.Black * 0.7f);
        spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(0, 0, Edge, Height), Brass);

        var x = Edge + PadX;
        var y = PadY;
        var width = Width - x - PadX;

        foreach (var line in _titleLines)
        {
            spriteBatch.DrawStringOnCtrl(this, line, _titleFont,
                new Rectangle(x, y, width, _titleFont.LineHeight), Glint, false, true);
            y += _titleFont.LineHeight;
        }

        if (_titleLines.Count > 0 && _detailLines.Count > 0) y += Gap;

        foreach (var line in _detailLines)
        {
            spriteBatch.DrawStringOnCtrl(this, line, _detailFont,
                new Rectangle(x, y, width, _detailFont.LineHeight), Vellum, false, true);
            y += _detailFont.LineHeight;
        }
    }
}
