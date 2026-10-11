#!/usr/bin/env python3
"""Elite heat pass: a stepped hot engine glow BEHIND the hull at every nozzle of each recoloured elite strip (the refreshed hulls
only had a few orange pixels of flame). Hull pixels are never touched: the glow is composited under them. Nozzle points come from
the elite defs (Resources/Elites/Defs/ember_elite_*.json). Per cell the flame breathes (idle 1.0/.8/1.1/.9, tell 1.3, action 1.5, hit .6).
Reads ./out/elites, writes ./out/elites_glow."""
import json, math
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
DEFS = HERE.parents[2] / "Resources" / "Elites" / "Defs"
SCALE = [1.0, .8, 1.1, .9, 1.3, 1.5, .6]
RAMP = [((255, 120, 24), 110), ((255, 150, 40), 170), ((255, 190, 70), 235), ((255, 232, 170), 255)]   # outer .. core


def glow_layer(nozzles, k, S=192):
    lay = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(lay)
    for (x, y) in nozzles:
        for i, (col, al) in enumerate(RAMP):
            r = (7.0 - i * 1.6) * k
            L = (13.0 - i * 3.2) * k                  # flame streaming down the screen from the nozzle
            d.ellipse((x - r, y - r * .8, x + r, y + L), fill=(*col, al))
    return lay


def main():
    (HERE / "out" / "elites_glow").mkdir(parents=True, exist_ok=True)
    for f in sorted((HERE / "out" / "elites").glob("ember_elite_*.png")):
        key = f.stem
        noz = [(n["x"], n["y"]) for n in json.load(open(DEFS / f"{key}.json"))["nozzles"]]
        strip = Image.open(f).convert("RGBA")
        out = Image.new("RGBA", strip.size)
        for c in range(7):
            cell = strip.crop((c * 192, 0, c * 192 + 192, 192))
            base = glow_layer(noz, SCALE[c])
            # keep >= 12 px clear margin: crop anything the glow adds beyond it
            a = np.asarray(base).copy(); a[-12:, :, 3] = 0; a[:, :12, 3] = 0; a[:, -12:, 3] = 0; a[:12, :, 3] = 0
            base = Image.fromarray(a, "RGBA")
            base.alpha_composite(cell)
            out.alpha_composite(base, (c * 192, 0))
        out.save(HERE / "out" / "elites_glow" / f.name)
        print(f.name, noz)


if __name__ == "__main__":
    main()
