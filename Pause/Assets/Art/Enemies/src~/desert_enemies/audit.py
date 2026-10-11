#!/usr/bin/env python3
"""Print Desert strip measurements and make an art-only contact preview."""
from pathlib import Path
import math
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
STRIPS = HERE / "strips"
BACKDROP = (HERE.parents[5].parent / "codex-desert-backdrop-a" /
            "Pause/Assets/Art/Worlds/Desert/backdrop_v3~")


def tiles():
    paths = [BACKDROP / f"v{i}/mid.png" for i in range(1, 5)]
    return [Image.open(p).convert("RGB") for p in paths] if all(p.exists() for p in paths) else []


def luminance(rgb):
    x = np.asarray(rgb, dtype=np.float32) / 255
    x = np.where(x <= .04045, x / 12.92, ((x + .055) / 1.055) ** 2.4)
    return x @ np.array([.2126, .7152, .0722], dtype=np.float32)


def longest_cut(cell):
    op = cell[:, :, 3] > 128
    worst = 0
    for side in (-1, 1):
        for x in range(1, op.shape[1] - 1):
            run = 0
            for bit in op[:, x] & ~op[:, x + side]:
                run = run + 1 if bit else 0
                worst = max(worst, run)
    return worst


def motion(a, b):
    aa, bb = a[:, :, 3] > 128, b[:, :, 3] > 128
    union = aa | bb
    delta = np.abs(a[:, :, :3].astype(np.int16) - b[:, :, :3].astype(np.int16)).sum(2) > 24
    return 100 * ((aa ^ bb) | (aa & bb & delta)).sum() / union.sum()


def measure(path, tile_images=None):
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)
    n = im.height
    cells = [a[:, i*n:(i+1)*n] for i in range(7)]
    key = cells[0]
    op = key[:, :, 3] > 128
    rgb = (key[:, :, 0].astype(np.int32) << 16) | (key[:, :, 1].astype(np.int32) << 8) | key[:, :, 2]
    boundaries = np.zeros_like(op)
    boundaries[:, :-1] |= (rgb[:, :-1] != rgb[:, 1:]) & op[:, :-1] & op[:, 1:]
    boundaries[:-1, :] |= (rgb[:-1, :] != rgb[1:, :]) & op[:-1, :] & op[1:, :]
    centres, areas, margins = [], [], []
    for c in cells:
        yy, xx = np.nonzero(c[:, :, 3] > 128)
        centres.append((xx.mean(), yy.mean()))
        areas.append(len(xx))
        yy, xx = np.nonzero(c[:, :, 3] > 16)
        margins.append(min(xx.min(), yy.min(), n-1-xx.max(), n-1-yy.max()))
    hsv = np.asarray(im.convert("RGB").convert("HSV")).astype(np.float32)
    h, s, v = hsv[:, :, 0] * (360/255), hsv[:, :, 1] / 255, hsv[:, :, 2] / 255
    all_op = a[:, :, 3] > 128
    share = lambda m: 100 * np.count_nonzero(m & all_op) / np.count_nonzero(all_op)
    sand = all_op & (h >= 40) & (h <= 48) & (s > .08)
    copper = all_op & (h >= 22) & (h <= 30) & (s > .30)
    tile_images = tile_images or []
    key_lum = luminance(key[:, :, :3][op])
    standout = []
    for tile in tile_images:
        mean = float(luminance(np.asarray(tile).reshape(-1, 3)).mean())
        ratio = (np.maximum(key_lum, mean) + .05) / (np.minimum(key_lum, mean) + .05)
        standout.append(100 * float(np.mean(ratio >= 3)))
    return {
        "key": path.stem, "cell": n, "tones": len(np.unique(rgb[op])),
        "boundaries": int(boundaries.sum()),
        "side_columns": sum(int((c[:, 0, 3] > 128).sum() + (c[:, -1, 3] > 128).sum()) for c in cells),
        "cut": max(longest_cut(c) for c in cells),
        "drift": max(math.dist(centres[0], c) for c in centres[1:4]),
        "area_lo": min(areas[1:4]) / areas[0],
        "area_hi": max(areas[1:4]) / areas[0],
        "margins": margins,
        "motion": [motion(cells[i], cells[(i+1)%4]) for i in range(4)],
        "amber": share((h >= 30) & (h <= 48) & (s > .45)),
        "red": share(((h >= 345) | (h <= 15)) & (s > .5)),
        "pink": share((h >= 312) & (h <= 326) & (s > .1)),
        "violet": share((h >= 250) & (h <= 300) & (s > .1)),
        "sand_p95": float(np.percentile(s[sand], 95)) if sand.any() else 0,
        "copper_bad": int(np.count_nonzero(copper & ((s > .62) | (v > .60)))),
        "standout": standout,
    }


def rows():
    tile_images = tiles()
    return [measure(p, tile_images) for p in sorted(STRIPS.glob("*.png"))]


def print_table(results):
    print("key                    tones  bounds side cut drift area lo-hi  margins (0..6)       motion min sand95 CuBad amber red pink violet  standout v1..v4")
    for r in results:
        stands = "/".join(f"{x:.1f}" for x in r["standout"]) if r["standout"] else "tiles unavailable"
        print(f'{r["key"]:22s} {r["tones"]:5d} {r["boundaries"]:7d} {r["side_columns"]:4d} '
              f'{r["cut"]:3d} {r["drift"]:5.1f} {r["area_lo"]:.3f}-{r["area_hi"]:.3f} '
              f'{"/".join(map(str,r["margins"])):22s} {min(r["motion"]):6.1f}% '
              f'{r["sand_p95"]:.3f} {r["copper_bad"]:5d} '
              f'{r["amber"]:.3f} {r["red"]:.3f} {r["pink"]:.3f} {r["violet"]:.3f}  {stands}')


def preview():
    """Show every complete strip at 2x, then every key pose over each tile."""
    paths = sorted(STRIPS.glob("*.png"))
    bg = (11, 11, 26)
    width = 256 * 7 * 2
    strips_height = sum(Image.open(p).height * 2 + 20 for p in paths)
    tile_images = tiles()
    tile_height = (len(paths) * (512 + 16) + 24) if tile_images else 0
    canvas = Image.new("RGB", (width, strips_height + tile_height), bg)
    y = 0
    for path in paths:
        im = Image.open(path).convert("RGBA")
        row = Image.new("RGBA", im.size, bg + (255,))
        row.alpha_composite(im)
        row = row.resize((im.width*2, im.height*2), Image.Resampling.NEAREST).convert("RGB")
        canvas.paste(row, ((width-row.width)//2, y))
        y += row.height + 20
    if tile_images:
        y += 24
        for path in paths:
            strip = Image.open(path).convert("RGBA")
            key = strip.crop((0, 0, strip.height, strip.height))
            for i, tile in enumerate(tile_images):
                tw, th = tile.size
                ground = tile.crop(((tw-256)//2, (th-256)//2, (tw+256)//2, (th+256)//2)).convert("RGBA")
                ground.alpha_composite(key, ((256-key.width)//2, (256-key.height)//2))
                ground = ground.resize((512,512), Image.Resampling.NEAREST).convert("RGB")
                x = (width - 2048)//2 + i*512
                canvas.paste(ground, (x,y))
            y += 512 + 16
    canvas.save(HERE / "preview.png", optimize=True)
    print(f"preview.png {canvas.width}x{canvas.height}; tiles={'4' if tile_images else 'unavailable'}")


if __name__ == "__main__":
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", action="store_true")
    args = ap.parse_args()
    print_table(rows())
    if args.preview:
        preview()
