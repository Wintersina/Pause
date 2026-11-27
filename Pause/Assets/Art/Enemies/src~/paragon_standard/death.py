#!/usr/bin/env python3
"""Death flipbooks for the Paragon-standard redraw, every world: Resources/Enemies/Death/<key>.png (3 square cells).

One shared method for all seven worlds (so every death reads as the same family): build_space_enemy_death.py's
burst / rupture / debris cells, scripted from the REAL installed idle cell 3, re-coloured per world from its cue hue
(WorldCue.Hue: the same colour the world's enemies glow and shoot in). Per-unit anatomy comes from the role.

    python3 death.py <key> [key ...]
"""
import colorsys, sys
from pathlib import Path
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parent))
import build_space_enemy_death as D            # noqa: E402

D.OUT = HERE / "death_preview"
D.OUT.mkdir(exist_ok=True)
CUE = {"space": 318, "frost": 195, "verdant": 135, "ember": 32, "tide": 188, "storm": 258, "desert": 45}


def rgb(h, s, v):
    return tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb((h % 360) / 360, s, v))


def palette(world):
    h = CUE[world]
    D.WHITE = rgb(h, .04, 1)
    D.YELLOW = rgb(h, .16, 1)
    D.GLOW = rgb(h, .7, .84)
    D.PINK = rgb(h, .69, 1)
    D.ARC = rgb(h + 20, .5, 1)
    D.ARC_DIM = rgb(h, .6, .6)
    D.SLATE = rgb(h, .3, .32)
    D.SLATE_LT = rgb(h, .25, .58)


def unit(key):
    side = Image.open(D.IDLE / f"{key}.png").height
    if "_rock_" in key: return (side, 15, 35, 4, 1, 12, "shatter")
    if key.endswith("_big"): return (side, 18, 28, 7, 4, 12, "vent")
    if key.endswith("_alien"): return (side, 10, 60, 7, 1, 8, "burst")
    if key.endswith("_chaser"): return (side, 12, 90, 6, 1, 8, "rotor")
    if key.endswith(("_fighter_3", "_fighter_5", "_fighter_6")): return (side, 12, 45, 6, 2, 10, "drum")
    if key.endswith("_fighter_4"): return (side, 14, 40, 8, 3, 12, "coil")
    return (side, 12, 50, 6, 2, 8, "snap")


if __name__ == "__main__":
    for k in sys.argv[1:]:
        palette(k.split("_")[0])
        D.UNITS[k] = unit(k)
        b, o = D.build(k)
        # nothing within 7 px of a death cell's edge (Verdant / Ember framing tests check death strips' margins)
        import numpy as np
        f = D.RES / f"{k}.png"; a = np.array(Image.open(f).convert("RGBA")); h = a.shape[0]
        for i in range(a.shape[1] // h):
            c = a[:, i * h:(i + 1) * h]; c[:7] = 0; c[-7:] = 0; c[:, :7] = 0; c[:, -7:] = 0
        Image.fromarray(a, "RGBA").save(f, optimize=True)
        print("built", k, o.size)
