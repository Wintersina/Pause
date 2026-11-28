#!/usr/bin/env python3
"""Side-by-side preview of the Frost ground as the game stacks it (old | new), scrolled across the mid tile's seam.

    python3 preview.py <out dir> [<Resources/Worlds/Frost/Backdrop3>]     -> <out dir>/frost_v1.png, frost_v2.png

Each column is one 896 px wide view, 1344 px tall: sky, far, mid and flow, each graded as drawn (BackdropGrade, Frost
lift x the layer's share) and alpha-stacked like WorldBackdropTest.Composite, each layer scrolled by its own rate
(sky .006, far .014, mid .024, flow .025 + its strip drift) so that the mid tile's wrap (its last row meeting its
first) sits at the middle of the view (marked by the ticks at the edges). Below: each layer alone, as drawn.
"""
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import check_tiles as ct  # noqa: E402

RATE = {"sky": .006, "far": .014, "mid": .024, "flow": .025}
VIEW_H = 1344


def layer_px(d, layer):
    a = np.asarray(Image.open(d / f"{layer}.png").convert("RGBA")).astype(np.float64) / 255
    return ct.as_drawn(a, layer)          # top-down rows here (as_drawn is per texel)


def window(px, top):
    h = px.shape[0]
    rows = (np.arange(VIEW_H) + top) % h
    return px[rows]


def view(d):
    mid_top = 1792 - VIEW_H // 2          # the mid's last rows above the middle, its first rows below
    out = None
    for layer in ("sky", "far", "mid", "flow"):
        px = layer_px(d, layer)
        top = int(round(mid_top * RATE[layer] / RATE["mid"])) + (311 if layer == "flow" else 0)
        w = window(px, top)
        if out is None:
            out = w[..., :3].copy()
        else:
            out = out + (w[..., :3] - out) * w[..., 3:]
    return out


def strip(d):
    tiles = []
    for layer in ("sky", "far", "mid", "flow"):
        px = layer_px(d, layer)
        bg = np.full(px.shape[:2] + (3,), .5)
        bg[(np.arange(px.shape[0])[:, None] // 32 + np.arange(px.shape[1])[None, :] // 32) % 2 == 0] = .35
        rgb = bg + (px[..., :3] - bg) * px[..., 3:]
        tiles.append(np.asarray(Image.fromarray((rgb * 255).astype(np.uint8)).resize((224, 448), Image.LANCZOS)))
    return np.concatenate([np.pad(t, ((0, 0), (0, 0), (0, 0))) for t in tiles], 1)


def main():
    out = Path(sys.argv[1])
    base = Path(sys.argv[2]) if len(sys.argv) > 2 else ct.DEFAULT
    out.mkdir(parents=True, exist_ok=True)
    for v in (1, 2):
        new_dir = base / f"v{v}"
        old_dir = HERE / "old" / f"v{v}"
        # old/ holds only the replaced layers; the rest is shared with the installed set
        def resolve(layer, use_old):
            if use_old and (old_dir / f"{layer}.png").exists():
                return old_dir
            return new_dir
        cols = []
        for use_old in (True, False):
            tmp = {l: resolve(l, use_old) for l in ("sky", "far", "mid", "flow")}

            class D:                       # a folder-like view picking each layer from its own dir
                def __truediv__(self, name):
                    return tmp[name.split(".")[0]] / name
            cols.append((view(D()), strip(D())))
        gap = 24
        W = 896 * 2 + gap
        H = 40 + VIEW_H + 20 + 448
        canvas = Image.new("RGB", (W, H), (20, 20, 24))
        dr = ImageDraw.Draw(canvas)
        for i, (vw, st) in enumerate(cols):
            x = i * (896 + gap)
            dr.text((x + 8, 12), f"Frost v{v} {'OLD' if i == 0 else 'NEW'}  (sky, far, mid, flow as drawn; mid seam at the ticks)", fill=(230, 230, 230))
            canvas.paste(Image.fromarray((np.clip(vw, 0, 1) * 255).astype(np.uint8)), (x, 40))
            yseam = 40 + VIEW_H // 2
            dr.line([(x, yseam), (x + 20, yseam)], fill=(255, 220, 0), width=3)
            dr.line([(x + 876, yseam), (x + 895, yseam)], fill=(255, 220, 0), width=3)
            canvas.paste(Image.fromarray(st), (x, 40 + VIEW_H + 20))
        canvas.save(out / f"frost_v{v}.png")
        print(out / f"frost_v{v}.png")


if __name__ == "__main__":
    main()
