"""Renders the launcher icon, tools/appicon/layers.html, into the Android mipmaps.

An adaptive icon is two layers, background and foreground, at 108 dp of which the launcher shows
the middle 72; older launchers take a flat square and a round icon instead, made here from the
same middle part. Chrome does the drawing: the layers are cut from tools/appicon/design.png on a
canvas.

    python -I tools/render-appicon.py
"""
import pathlib
import subprocess
import tempfile

root = pathlib.Path(__file__).resolve().parent.parent
page = (root / "tools" / "appicon" / "layers.html").as_uri()
res = root / "GuildWarsCompanion.Avalonia.Android" / "Resources"
chrome = r"C:\Program Files\Google\Chrome\Application\chrome.exe"

# density: (adaptive layer px, legacy icon px)
DENSITIES = {"mdpi": (108, 48), "hdpi": (162, 72), "xhdpi": (216, 96), "xxhdpi": (324, 144), "xxxhdpi": (432, 192)}

work = pathlib.Path(tempfile.mkdtemp(prefix="appicon-"))


def shot(url, size, out):
    subprocess.run([chrome, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--allow-file-access-from-files",
                    "--virtual-time-budget=5000", "--default-background-color=00000000", f"--window-size={size},{size}",
                    f"--screenshot={out}", url],
                   check=True, capture_output=True)


for density, (layer, legacy) in DENSITIES.items():
    folder = res / f"mipmap-{density}"
    for name in ("background", "foreground"):
        shot(f"{page}?layer={name}&size={layer}", layer, folder / f"appicon_{name}.png")

    # The middle two thirds, as a launcher would show it, flat and round: both layers at the size
    # where that middle part is the legacy icon, then cropped to it.
    whole = round(legacy * 108 / 72)
    for name in ("background", "foreground"):
        shot(f"{page}?layer={name}&size={whole}", whole, work / f"{name}.png")
    inset = (whole - legacy) / 2
    for out, radius in (("appicon.png", "18%"), ("appicon_round.png", "50%")):
        frame = work / "frame.html"
        frame.write_text(
            '<!DOCTYPE html><html><head><style>html,body{margin:0;background:transparent;overflow:hidden}'
            f'img{{position:absolute;left:-{inset}px;top:-{inset}px;width:{whole}px;height:{whole}px}}</style></head><body>'
            f'<div style="position:relative;width:{legacy}px;height:{legacy}px;overflow:hidden;border-radius:{radius}">'
            f'<img src="{(work / "background.png").as_uri()}"><img src="{(work / "foreground.png").as_uri()}"></div></body></html>',
            encoding="utf-8")
        shot(frame.as_uri(), legacy, folder / out)
    print(density, "done")
