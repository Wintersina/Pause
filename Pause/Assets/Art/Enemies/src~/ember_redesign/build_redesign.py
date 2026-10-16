#!/usr/bin/env python3
"""Turn the vetted Codex redesign sprites (src/<name>.png) into seven-cell strips: 0-3 idle, 4 tell, 5 action, 6 hit.

Enemies / hazards reuse ../ember_refresh/build.py (motion warp, tell rings, hit cracks); elites reuse ../ember_elites_refresh/build.py
(move_parts, add_marks) with its rim and hue_clean steps bypassed (the painted colours and the no-outline rule are kept as generated).
Nearest-neighbour resizing only. Output: ./strips/<name>.png

    python3 build_redesign.py [name ...]      # default: every name in briefs.BRIEFS that has a src png
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import briefs
import importlib.util


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path); m = importlib.util.module_from_spec(spec); sys.modules[name] = m; spec.loader.exec_module(m); return m


B = _load("enemy_build", HERE.parent / "ember_refresh" / "build.py")   # enemies
E = _load("elite_build", HERE.parent / "ember_elites_refresh" / "build.py")   # elites
OUT = HERE / "strips"


def crop_main(name):
    im = Image.open(HERE / "src" / f"{name}.png").convert("RGBA")
    a = np.array(im)
    a[:, :, 3] = np.where(a[:, :, 3] > 110, 255, 0)                       # hard pixel edges, no soft halo
    keep = B.largest(np.asarray(Image.fromarray(a[:, :, 3]).filter(__import__("PIL.ImageFilter", fromlist=["x"]).MaxFilter(9))) > 0)
    a[~keep, 3] = 0
    im = Image.fromarray(a, "RGBA")
    ys, xs = np.where(a[:, :, 3] > 0)
    return im.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))


def build_enemy(name):
    spec = briefs.BRIEFS[name]
    im = crop_main(name)
    f = spec["size"] / max(im.size)
    im = im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))), Image.Resampling.NEAREST)
    side = spec["cell"]
    key = Image.new("RGBA", (side, side)); key.alpha_composite(im, ((side - im.width) // 2, (side - im.height) // 2))
    frames = [B.cycle(B.motion(key, name, p, i), i, name) for i, p in enumerate(B.PHASE)]
    frames.extend((B.tell(key, name, 4), B.tell(key, name, 5), B.hit(key, name)))
    strip = Image.new("RGBA", (side * 7, side))
    for i, fr in enumerate(frames):
        strip.alpha_composite(B.nick_flat_edges(fr), (i * side, 0))
    return strip


def build_elite(name):
    ship = next(s for s in E.SHIPS if "ember_elite_" + s.name == name)
    im = crop_main(name)
    # longest side = briefs["long"] px: sized so the drawn silhouette clears the elite tier floor (ShipTierTest) at the def's cellWorldSize
    f = briefs.BRIEFS[name]["long"] / max(im.size)
    im = im.resize((max(1, round(im.width * f)), max(1, round(im.height * f))), Image.Resampling.NEAREST)
    hull = Image.new("RGBA", (E.CELL, E.CELL)); hull.alpha_composite(im, ((E.CELL - im.width) // 2, (E.CELL - im.height) // 2))
    cells = [E.move_parts(hull, ship, k) for k in range(4)]
    cells += [E.add_marks(hull, ship, k) for k in (4, 5, 6)]              # tell / action / hit marks only (idle flames are painted)
    strip = Image.new("RGBA", (E.CELL * 7, E.CELL))
    for k, fr in enumerate(cells):
        strip.alpha_composite(fr, (k * E.CELL, 0))
    return strip


MARGIN = 6          # clear px between any alpha and the cell edge (EmberFramingTest asserts >= 4 on the installed strips)


def check_framing(name, strip, tumbling):
    """Guard against clipped / shifted frames (same limits as Editor/Tests/EmberFramingTest.cs). Raises on a violation."""
    h = strip.height
    a = np.asarray(strip.getchannel("A")) > 8
    boxes = []
    for c in range(strip.width // h):
        ys, xs = np.where(a[:, c * h:(c + 1) * h])
        assert len(xs), f"{name}: cell {c} is empty"
        box = (xs.min(), ys.min(), xs.max(), ys.max()); boxes.append(box)
        m = min(box[0], box[1], h - 1 - box[2], h - 1 - box[3])
        assert m >= MARGIN, f"{name}: cell {c} art comes within {m} px of the cell edge (bbox {box}); lower briefs size or the motion"
    if tumbling:
        return
    b = np.array(boxes)
    cx = (b[:, 0] + b[:, 2]) / 2; cy = (b[:, 1] + b[:, 3]) / 2; w = b[:, 2] - b[:, 0]; hh = b[:, 3] - b[:, 1]
    ok = np.ptp(cx[:4]) <= 3 and np.ptp(cy[:4]) <= 5 and np.ptp(w[:4]) <= 14 and np.ptp(hh[:4]) <= 8 and np.ptp(cx) <= 6 and np.ptp(cy) <= 9
    assert ok, f"{name}: idle frames drift (pivot dx {np.ptp(cx[:4])}, dy {np.ptp(cy[:4])}, size {np.ptp(w[:4])}x{np.ptp(hh[:4])})"


def main():
    OUT.mkdir(exist_ok=True)
    names = sys.argv[1:] or [n for n in briefs.BRIEFS if (HERE / "src" / f"{n}.png").exists()]
    for n in names:
        s = build_elite(n) if briefs.BRIEFS[n]["kind"] == "elite" else build_enemy(n)
        check_framing(n, s, "_rock_" in n)
        s.save(OUT / f"{n}.png", optimize=True)
        print(n, s.size)


if __name__ == "__main__":
    main()
