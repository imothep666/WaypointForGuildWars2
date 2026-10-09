"""Makes the desktop app's icon from the Android launcher icon's layers.

The phone's adaptive icon is the source: its two xxxhdpi layers (432 px, of which a launcher
shows the middle 288) are stacked, cut to that middle part and given the same rounded corners as
the flat launcher icon. Run tools/render-appicon.py first if the design changed.

    python tools/render-desktop-icon.py     (not -I: Pillow is usually a per-user install)

Writes
    GuildWarsCompanion.Avalonia.Desktop/Assets/waypoint.ico   the executable's icon, every size
    GuildWarsCompanion.Avalonia/Assets/Images/app_icon.png    the window's icon and title bar mark
"""
import pathlib

from PIL import Image, ImageDraw

root = pathlib.Path(__file__).resolve().parent.parent
layers = root / "GuildWarsCompanion.Avalonia.Android" / "Resources" / "mipmap-xxxhdpi"

background = Image.open(layers / "appicon_background.png").convert("RGBA")
foreground = Image.open(layers / "appicon_foreground.png").convert("RGBA")
whole = Image.alpha_composite(background, foreground)

# The middle 72 of 108, as a launcher shows it.
side = whole.width * 72 // 108
inset = (whole.width - side) // 2
icon = whole.crop((inset, inset, inset + side, inset + side)).resize((256, 256), Image.LANCZOS)

# The flat launcher icon's 18% corners, drawn four times over and scaled down for a clean edge.
mask = Image.new("L", (1024, 1024), 0)
ImageDraw.Draw(mask).rounded_rectangle((0, 0, 1023, 1023), radius=round(1024 * 0.18), fill=255)
icon.putalpha(mask.resize((256, 256), Image.LANCZOS))

icon.save(root / "GuildWarsCompanion.Avalonia.Desktop" / "Assets" / "waypoint.ico",
          sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
icon.resize((128, 128), Image.LANCZOS).save(root / "GuildWarsCompanion.Avalonia" / "Assets" / "Images" / "app_icon.png",
                                            optimize=True)
print("done")
