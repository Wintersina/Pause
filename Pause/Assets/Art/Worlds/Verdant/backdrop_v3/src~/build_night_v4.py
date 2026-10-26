"""Rebuild Verdant v4 (night forest) sky/far/mid from the Codex repaints in night_v4/ (flow.png kept).
Tree / flower diversity, sakura in the far source; little haze (sky only); night targets and the .53 cap unchanged.
Run: /usr/local/bin/python3 build_night_v4.py"""
from pathlib import Path
import sys
import numpy as np
from PIL import Image
sys.path.insert(0, str(Path(__file__).resolve().parent))
import build as B
W, H = B.W, B.H
HERE = Path(__file__).resolve().parent
OUT = Path(__file__).resolve().parents[5] / "Art/Backgrounds/Resources/Worlds/Verdant/Backdrop3/v4"
TARGET = {"sky": .335, "far": .365, "mid": .395}
SAT = {"sky": .95, "far": 1.1, "mid": .95}
CONTRAST = {"sky": .30, "far": .55, "mid": .70}
HAZE = {"sky": .30, "far": .10, "mid": 0.0}
FOG = (.08, .22, .24)


def src(n):
    return np.asarray(Image.open(HERE / "night_v4" / n).convert("RGB").resize((W, H), Image.Resampling.NEAREST)).copy()


def ground(role):
    a, b = src("night_a2.png"), src("night_b3.png")
    w = {"sky": .32, "far": 1.0, "mid": .16}[role]
    f = (a.astype(np.float32) * (1 - w) + b.astype(np.float32) * w) / 255
    g = f.mean(axis=2, keepdims=True)
    f = g + (f - g) * SAT[role]
    f = f * (1 - HAZE[role]) + np.array(FOG, np.float32) * HAZE[role]
    rgb = np.uint8(np.clip(np.rint(f * 255), 0, 255))
    t, cap = TARGET[role], .53
    rgb = B.palette_and_value(rgb, t, CONTRAST[role], 0, cap, colors=42 if role == "sky" else (88 if role == "far" else 56))
    rgba = B.wrap_blend(np.dstack((rgb, np.full((H, W), 255, np.uint8))))
    rgb = B.palette_and_value(rgba[:, :, :3], t, 1.0, 0, cap, colors=88 if role == "far" else 56)
    rgba = np.dstack((rgb, np.full((H, W), 255, np.uint8)))
    if role == "far":
        # flowers / sakura: palette indexing flattens small bright colour (pink, red, blue, violet) into
        # the teal-olive palette, so carry the painted flower pixels over (dimmed, a little desaturated:
        # soft jewel colours, never a saturated dot), away from the 70 px wrap band so the seams stay identical.
        # Hues kept: red / pink / violet / blue (orange and gold are left out: star dust and atoms are orange / gold).
        sb = src("night_b3.png").astype(np.float32) / 255
        mx, mn = sb.max(axis=2), sb.min(axis=2)
        sat = (mx - mn) / np.maximum(mx, 1e-4)
        r, g, b = sb[..., 0], sb[..., 1], sb[..., 2]
        warm = (r > g + .18) & (r >= b)                      # reds / crimsons / pink-reds
        cool = (b > g + .10) & (b > r - .05)                 # blues / violets / lavender pinks
        pale = (r > g + .06) & (b > g + .03)                 # sakura-like pale pink
        flower = (warm | cool | pale) & (mx > .42) & (sat > .18) & ~((r > g + .10) & (g > b + .06) & (mx < .6))  # not bark orange-brown
        flower[:70] = False; flower[-70:] = False; flower[:, :70] = False; flower[:, -70:] = False
        gr = sb.mean(axis=2, keepdims=True)
        soft = np.clip((gr + (sb - gr) * .85) * .82, 0, 1) * 255
        rgba[..., :3] = np.where(flower[..., None], np.uint8(soft), rgba[..., :3])
    rgba[:, -1] = rgba[:, 0]; rgba[-1] = rgba[0]
    return Image.fromarray(rgba, "RGBA")


if __name__ == "__main__":
    for role in ("sky", "far", "mid"):
        im = ground(role)
        im.save(OUT / f"{role}.png", optimize=True)
        print(role, im.size, B.stats(im))
