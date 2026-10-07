#!/usr/bin/env python3
"""Build four transparent smoke/beacon frames for each still Space asteroid."""
import json
from pathlib import Path

from PIL import Image, ImageOps

OUT = Path(__file__).resolve().parents[3] / "Backgrounds/Resources/Worlds/Space/Backdrop"
CELL = 256


def cut(image, manifest, name):
    r = next(r for r in manifest["sprites"] if r["n"] == name)
    top = image.height - r["y"] - r["h"]
    return image.crop((r["x"], top, r["x"] + r["w"], top + r["h"]))


def translucent(image, strength):
    result = image.copy()
    result.putalpha(result.getchannel("A").point(lambda a: round(a * strength)))
    return result


def main():
    fx = Image.open(OUT / "fx.png").convert("RGBA")
    manifest = json.loads((OUT / "fx.json").read_text())
    cloud = cut(fx, manifest, "wisp0").resize((108, 108), Image.Resampling.LANCZOS)
    beacon = cut(fx, manifest, "star").resize((26, 26), Image.Resampling.LANCZOS)
    smoke_color = ImageOps.colorize(ImageOps.grayscale(cloud), black="#15202e", white="#7196a9")
    smoke_color.putalpha(cloud.getchannel("A"))
    cloud = smoke_color
    magenta = Image.new("RGBA", beacon.size, (255, 72, 190, 0))
    magenta.putalpha(beacon.getchannel("A"))
    beacon = magenta
    sheet = Image.new("RGBA", (CELL * 4, CELL * 3))
    entries = []
    smoke_x = (64, 107, 121)
    smoke_y = (31, 27, 40)
    light_xy = ((141, 156), (113, 146), (156, 133))
    for rock in range(3):
        for frame in range(4):
            cel = Image.new("RGBA", (CELL, CELL))
            puff = translucent(cloud, (0.12, 0.23, 0.29, 0.17)[frame])
            cel.alpha_composite(puff, (smoke_x[rock] + frame * 3, smoke_y[rock] - frame * 7))
            glow = translucent(beacon, (0.0, 0.12, 0.95, 0.18)[frame])
            bx, by = light_xy[rock]
            cel.alpha_composite(glow, (bx - 13, by - 13))
            sheet.alpha_composite(cel, (frame * CELL, rock * CELL))
            entries.append({"n": f"asteroidfx{rock}_{frame:02d}", "x": frame * CELL,
                            "y": (2 - rock) * CELL, "w": CELL, "h": CELL})
    sheet.save(OUT / "asteroid_fx.png")
    (OUT / "asteroid_fx.json").write_text(json.dumps({"sprites": entries}, indent=2) + "\n")


if __name__ == "__main__":
    main()
