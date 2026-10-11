#!/usr/bin/env python3
"""Desert boss / elite readability: the same thin pale bone border add_border.py gives the Scarab Drone, for the Dune-Eater atlas, its damage hull and death strip (384 cells)
and the three darkest Desert elites (192 cells). Written only into (almost) transparent pixels touching the silhouette, once per cell, so interior
art, grids, registration and anchors are untouched; no pink; NEVER a repaint. Reads the pre-border files from original/ next to each target
(kept in Art/Enemies/src~/desert_enemies/original_big/), writes the live files. Re-runnable:  python3 add_border_big.py [--check]
Why: on the Desert mid tiles (sand, V .30-.50) these bodies measure only ~1.1-1.4:1 median luminance contrast (Hauler 1.05-1.44, Stalker 1.0-1.37,
Scorpion 1.0-1.37, boss 1.1-1.24); ReadabilitySweep cannot render Desert yet (its backdrop is Tide's stand-in), so this is the offline estimate.
"""
import sys, shutil
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
ASSETS = HERE.parents[2]          # .../Assets/Art
BORDER = (230, 220, 192, 255)     # #E6DCC0 bone
SOLID = 96
ORIG = HERE / "original_big"
TARGETS = {   # name -> (live path, cell px)
    "Desert": (ASSETS / "Resources/Bosses/Desert.png", 384),
    "Desert_damage": (ASSETS / "Resources/Bosses/Desert_damage.png", 384),
    "Desert_death": (ASSETS / "Resources/Bosses/Desert_death.png", 384),
    "desert_elite_dune_stalker": (ASSETS / "Resources/Elites/Desert/desert_elite_dune_stalker.png", 192),
    "desert_elite_caravan_hauler": (ASSETS / "Resources/Elites/Desert/desert_elite_caravan_hauler.png", 192),
    "desert_elite_scorpion_walker": (ASSETS / "Resources/Elites/Desert/desert_elite_scorpion_walker.png", 192),
}
SOURCES = {n: ASSETS / "Enemies/Elite/Desert" / (n + ".png") for n in TARGETS if n != "Desert"}
SOURCES["Desert"] = ASSETS / "Bosses/Desert~/Desert.png"
SOURCES["Desert_damage"] = ASSETS / "Bosses/DesertDamage~/Desert_damage.png"
SOURCES["Desert_death"] = ASSETS / "Bosses/DesertDamage~/Desert_death.png"


def dilate(m):
    out = m.copy(); p = np.pad(m, 1); h, w = m.shape
    for dy in (-1, 0, 1):
        for dx in (-1, 0, 1):
            if dx or dy:
                out |= p[1 + dy:1 + dy + h, 1 + dx:1 + dx + w]
    return out


def border(img, cell):
    out = img.copy(); a = img[:, :, 3]; H, W = a.shape
    for r in range(H // cell):
        for c in range(W // cell):
            sl = (slice(r * cell, (r + 1) * cell), slice(c * cell, (c + 1) * cell))
            solid = a[sl] >= SOLID
            ring = dilate(solid) & ~solid & (a[sl] < SOLID)
            out[sl][ring] = BORDER
    return out


def main():
    ORIG.mkdir(exist_ok=True)
    for n, (live, cell) in TARGETS.items():
        keep = ORIG / (n + ".png")
        if not keep.exists():
            shutil.copyfile(SOURCES[n], keep)
        src = np.array(Image.open(keep).convert("RGBA"))
        res = border(src, cell)
        assert (res[src[:, :, 3] >= SOLID] == src[src[:, :, 3] >= SOLID]).all(), n
        print(f"{n}: {int((res != src).any(axis=2).sum())} border px")
        if "--check" not in sys.argv:
            Image.fromarray(res).save(live)
            if n in SOURCES and n.startswith("desert_elite"):
                Image.fromarray(res).save(SOURCES[n])


if __name__ == "__main__":
    main()
