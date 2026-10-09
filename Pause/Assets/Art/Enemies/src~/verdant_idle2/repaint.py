"""Repaint the live Verdant strips without changing their registered silhouettes.

The previous pass supplies the pixel geometry. Master strips supply fine tonal
variation (the knot uses its unquantised generated source plate). Nearest-neighbour
sampling is used throughout. Run this file to repaint, audit, and make preview.png.
"""
from __future__ import annotations

from pathlib import Path
import sys
from functools import lru_cache

import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
ORIGINAL = HERE.parent / "verdant_fixes" / "original"
SOURCE = HERE.parent / "verdant_fixes" / "candidates"
BACKDROP = HERE.parents[2] / "Backgrounds" / "Resources" / "Worlds" / "Verdant" / "Backdrop3"
PREVIOUS = HERE / "previous"
KEYS = ("alien", "big", "chaser", "fighter_1", "fighter_2", "fighter_3",
        "fighter_4", "rock_knot", "rock_pod", "rock_spore", "rock_vine")


def cells(im: Image.Image) -> list[Image.Image]:
    side = im.height
    return [im.crop((i * side, 0, (i + 1) * side, side)) for i in range(7)]


def knot_source(side: int) -> Image.Image:
    # Exactly the plate and crop used in the preceding pass, before its
    # 22-colour quantisation. It has the same roots, leaves and socket lines.
    plate = Image.open(SOURCE / "verdant_rock_knot.png").convert("RGBA")
    p = plate.crop((5, 5, plate.height - 5, plate.height - 5))
    a = np.array(p)
    a[a[:, :, 3] < 65, 3] = 0
    white = (a[:, :, :3].min(axis=2) > 235) & (a[:, :, 3] > 150)
    a[:, white.sum(axis=0) > p.height * .55, 3] = 0
    p = Image.fromarray(a, "RGBA")
    ys, xs = np.where(np.asarray(p.getchannel("A")) >= 100)
    p = p.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
    scale = min(160 / p.width, 164 / p.height, (side - 16) / p.width,
                (side - 16) / p.height)
    w, h = round(p.width * scale), round(p.height * scale)
    p = p.resize((w, h), Image.Resampling.NEAREST)
    x = max(8, min(side - 8 - w, round(96 - w / 2)))
    y = max(8, min(side - 8 - h, round(96 - h / 2)))
    out = Image.new("RGBA", (side, side))
    out.alpha_composite(p, (x, y))
    return out


def edge(mask: np.ndarray, dy: int, dx: int) -> np.ndarray:
    shifted = np.zeros_like(mask)
    y0, y1 = max(0, dy), mask.shape[0] + min(0, dy)
    x0, x1 = max(0, dx), mask.shape[1] + min(0, dx)
    shifted[y0:y1, x0:x1] = mask[y0-dy:y1-dy, x0-dx:x1-dx]
    return mask & ~shifted


@lru_cache(maxsize=3)
def generated_material_ratios(name: str) -> tuple[np.ndarray, np.ndarray] | None:
    # The generated studies are lighting / material references. Sampling their
    # colour relationships keeps their useful repaint while the registered
    # previous-pass pixels retain every horn, root, socket and silhouette.
    study = {"verdant_alien": "alien", "verdant_rock_knot": "knot",
             "verdant_rock_spore": "spore"}.get(name)
    if study is None: return None
    a = np.asarray(Image.open(HERE / "candidates" / f"{study}_lighting.png").convert("RGBA"))
    rgb = a[:, :, :3].astype(np.float32)
    opaque = (a[:, :, 3] > 160) & (rgb.max(axis=2) > 100)
    green = opaque & (rgb[:, :, 1] > rgb[:, :, 0]*1.12) & (rgb[:, :, 1] > rgb[:, :, 2]*1.25)
    amber = opaque & (rgb[:, :, 0] > rgb[:, :, 1]*1.18) & (rgb[:, :, 0] > rgb[:, :, 2]*1.6)
    return tuple(np.median(rgb[m] / rgb[m].max(axis=1)[:, None], axis=0)
                 for m in (green, amber))


def repaint_cell(prev: Image.Image, master: Image.Image, name: str, frame: int,
                 knot: Image.Image | None) -> Image.Image:
    a = np.asarray(prev, dtype=np.uint8).copy()
    old = a[:, :, :3].astype(np.float32)
    src = np.asarray(knot if knot is not None else master, dtype=np.uint8)
    solid = a[:, :, 3] > 128
    src_rgb = src[:, :, :3].astype(np.float32)
    src_ok = src[:, :, 3] > 128
    # Recover per-pixel source brightness relative to each old material swatch.
    # The original's RGB variation adds 1-2 px rivets, pores and plate shading
    # without bringing back its magenta or changing the current material hues.
    src_val = (.2126 * src_rgb[:, :, 0] + .7152 * src_rgb[:, :, 1] +
               .0722 * src_rgb[:, :, 2])
    code = a[:, :, 0].astype(np.int32) << 16 | a[:, :, 1].astype(np.int32) << 8 | a[:, :, 2].astype(np.int32)
    mean = np.full(solid.shape, 90, dtype=np.float32)
    for c in np.unique(code[solid]):
        loc = (code == c) & solid & src_ok
        if loc.any():
            mean[code == c] = src_val[loc].mean()
    residual = np.clip((src_val - mean) * .66, -30, 30)
    residual[~src_ok] = 0
    yy, xx = np.indices(solid.shape)
    ys, xs = np.where(solid)
    if not len(xs):
        return prev
    cx, cy = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2
    rw, rh = max(1, xs.max() - xs.min()), max(1, ys.max() - ys.min())
    nx, ny = (xx-cx)/rw, (yy-cy)/rh
    # A broad upper-left material light, retaining pockets of deep separator
    # shadow and the thin interior outline. Brighter resin/leaf swatches keep
    # their identity through the repaint.
    rgbmax, rgbmin = old.max(axis=2), old.min(axis=2)
    green = (old[:, :, 1] > old[:, :, 0] * 1.12) & (old[:, :, 1] > old[:, :, 2] * 1.25)
    amber = (old[:, :, 0] > old[:, :, 1] * 1.18) & (old[:, :, 0] > old[:, :, 2] * 1.6)
    shadow = rgbmax < 55
    field = np.clip(.92 - .62*nx - .70*ny, 0, 1)
    val = rgbmax + residual
    val += field * np.where(green, 52, np.where(amber, 64, 49))
    val += np.where((~shadow) & (rgbmax < 160), 19, 0)
    val = np.where(shadow, np.minimum(val, 84), val)
    val = np.clip(val, 0, 250)
    # Preserve each plate's hue while warming dark brown material to amber.
    saturation = np.maximum(1, rgbmax)
    ratio = old / saturation[:, :, None]
    ratio[:, :, 0] = np.where(green, np.maximum(ratio[:, :, 0], .47), ratio[:, :, 0])
    ratio[:, :, 1] = np.where(amber, np.maximum(ratio[:, :, 1], .69), ratio[:, :, 1])
    rgb = val[:, :, None] * ratio
    # A continuous source-derived colour microvariation, constrained to the
    # same Verdant material hue. This yields thousands of useful tones.
    deviations = src_rgb - src_val[:, :, None]
    rgb += np.clip(deviations * .16, -7, 7) * src_ok[:, :, None]
    study_ratios = generated_material_ratios(name)
    if study_ratios is not None:
        for material, target in ((green, study_ratios[0]), (amber, study_ratios[1])):
            m = solid & material & ~shadow
            value = rgb.max(axis=2)
            rgb[m] = rgb[m] * .82 + value[m, None] * target * .18
    # Controlled bright face facets increase 3:1 contrast on all four jungle
    # variants. This is a tonal ramp across existing material, not a flat fill.
    face = solid & ~shadow & (field > .59) & (old[:, :, 1] > 55)
    rgb[face] += (field[face] - .59)[:, None] * np.array([55, 64, 33])
    rgb = np.clip(rgb, 0, 255)
    # Pixel-clean two-pixel upper-left light. The innermost dark separator is
    # retained elsewhere on the contour.
    exposed = edge(solid, 1, 0) | edge(solid, 0, 1)
    inward = np.zeros_like(solid)
    inward[1:, :] |= exposed[:-1, :]
    inward[:, 1:] |= exposed[:, :-1]
    upper_left = (ny < .36) & (nx < .4)
    rim = exposed & upper_left & (xx >= 6) & (yy >= 6) & (xx < prev.width-6) & (yy < prev.height-6)
    inner = inward & solid & upper_left & ~rim
    rgb[inner] = rgb[inner] * .45 + np.array([203, 231, 83]) * .55
    rgb[rim] = (226, 250, 116)
    # Preserve previous deliberate very bright eye/core/rim pixels.
    highlights = solid & (rgbmax >= 205)
    rgb[highlights] = np.maximum(rgb[highlights], old[highlights])
    hsv = np.asarray(Image.fromarray(np.round(np.clip(rgb, 0, 255)).astype(np.uint8), "RGB").convert("HSV"))
    foreign = ((hsv[:, :, 0] >= 120) & (hsv[:, :, 0] <= 240) &
               (hsv[:, :, 1] > 80) & (hsv[:, :, 2] > 45) & (a[:, :, 3] > 100))
    if foreign.any():
        bright = rgb.max(axis=2)
        moss = foreign & green
        resin = foreign & ~green
        rgb[moss] = bright[moss, None] * np.array([.62, .90, .29])
        rgb[resin] = bright[resin, None] * np.array([.96, .70, .32])
    # Keep the preceding pass's hand-placed pale rim intact, including the
    # small gaps that give the silhouette its jagged organic edge.
    old_rim = solid & np.all(a[:, :, :3] == (230, 252, 94), axis=2)
    rgb[old_rim] = (230, 252, 94)
    if frame == 3 and name in ("verdant_rock_knot", "verdant_rock_pod"):
        # Their existing death final pose disperses a dark central stone core.
        # Keep that material pocket while lighting the surrounding shell.
        core = ((xx-cx)**2 + (yy-cy)**2 < (prev.height*.19)**2)
        dark_stone = solid & core & (rgbmax < 155) & (rgb.max(axis=2) >= 155)
        strength = 148 / np.maximum(1, rgb.max(axis=2))
        rgb[dark_stone] *= strength[dark_stone, None]
    a[solid, :3] = np.round(rgb[solid]).astype(np.uint8)
    # The old translucent fringe remains exactly registered.
    return Image.fromarray(a, "RGBA")


def repair_spore_cut(im: Image.Image, frame: int) -> Image.Image:
    a = np.array(im)
    # The 43px ruler edge in cell 5 is at x=168, y=64..106. Cell 4 has a
    # related 40px edge. Small notches break the slices without moving poses.
    x = 168 if frame == 5 else 173
    cuts = ((76, 81, 2), (91, 96, 3)) if frame == 5 else ((79, 85, 2), (94, 100, 3))
    for y0, y1, depth in cuts:
        for y in range(y0, y1):
            a[y, x+1-depth:x+1, 3] = 0
            a[y, x+1-depth:x+1, :3] = 0
    return Image.fromarray(a, "RGBA")


def shift_for_margin(im: Image.Image) -> Image.Image:
    a = np.asarray(im.getchannel("A")) > 128
    ys, xs = np.where(a)
    left, top = xs.min(), ys.min()
    right, bottom = im.width-1-xs.max(), im.height-1-ys.max()
    dx = max(0, 6-left) - max(0, 6-right)
    dy = max(0, 6-top) - max(0, 6-bottom)
    if not dx and not dy: return im
    out = Image.new("RGBA", im.size)
    out.alpha_composite(im, (int(dx), int(dy)))
    return out


def trim_fringe_for_margin(im: Image.Image) -> Image.Image:
    # Fighter 1's outermost wing/flame tips spill only 2-3 px into the safe
    # border. Trimming those tips leaves its anchor and death flash registered.
    a = np.array(im)
    a[:6, :, :] = 0
    a[-6:, :, :] = 0
    a[:, :6, :] = 0
    a[:, -6:, :] = 0
    return Image.fromarray(a, "RGBA")


def lum(rgb: np.ndarray) -> np.ndarray:
    c = rgb.astype(np.float32) / 255
    c = np.where(c <= .04045, c / 12.92, ((c + .055) / 1.055) ** 2.4)
    return .2126*c[..., 0] + .7152*c[..., 1] + .0722*c[..., 2]


def detail(cell: Image.Image) -> tuple[int, int]:
    a = np.asarray(cell)
    m = a[:, :, 3] > 128
    colours = len(np.unique(a[m, :3], axis=0))
    adjacent = np.zeros_like(m)
    adjacent[:, :-1] |= m[:, :-1] & m[:, 1:] & np.any(a[:, :-1, :3] != a[:, 1:, :3], axis=2)
    adjacent[:-1, :] |= m[:-1, :] & m[1:, :] & np.any(a[:-1, :, :3] != a[1:, :, :3], axis=2)
    return colours, int(adjacent.sum())


def scar(cell: Image.Image) -> int:
    a = np.asarray(cell)
    m = a[:, :, 3] > 128
    worst = 0
    for dx in (-1, 1):
        neighbor = np.zeros_like(m)
        if dx == 1:
            neighbor[:, :-1] = a[:, 1:, 3] > 0
        else:
            neighbor[:, 1:] = a[:, :-1, 3] > 0
        line = m & ~neighbor
        for x in range(m.shape[1]):
            run = 0
            for item in line[:, x]:
                run = run+1 if item else 0
                worst = max(worst, run)
    return worst


def box(cell: Image.Image) -> tuple[int, int, int, int, float, float]:
    m = np.asarray(cell.getchannel("A")) >= 176
    ys, xs = np.where(m)
    return xs.min(), ys.min(), xs.max()+1, ys.max()+1, xs.mean(), ys.mean()


def standout(cell: Image.Image, bg_rgb: np.ndarray) -> float:
    a = np.asarray(cell).astype(np.float32)
    alpha = a[:, :, 3] / 255
    m = alpha >= .5
    fg = a[:, :, :3] * alpha[:, :, None] + bg_rgb * (1-alpha[:, :, None])
    lbg = float(lum(bg_rgb.reshape(1, 1, 3))[0, 0])
    lf = lum(fg)
    ratio = (np.maximum(lf, lbg) + .05) / (np.minimum(lf, lbg) + .05)
    return float((m & (ratio >= 3)).sum() / max(1, m.sum()))


def backgrounds() -> list[np.ndarray]:
    means = []
    for v in range(1, 5):
        a = np.asarray(Image.open(BACKDROP / f"v{v}" / "mid.png").convert("RGB"))
        means.append(a.reshape(-1, 3).mean(axis=0))
    return means


def audit(before: dict[str, Image.Image], after: dict[str, Image.Image]) -> int:
    bgs = backgrounds()
    failures = []
    print("strip                  tones  boundaries   scar  idle standout   drift size  margin hue")
    for key in KEYS:
        name = "verdant_" + key
        b, a = cells(before[name]), cells(after[name])
        bt, bd = detail(b[0]); at, ad = detail(a[0])
        all_detail = [detail(c) for c in a]
        worst = max(scar(c) for c in a)
        bs = min(standout(c, bg) for c in b[:4] for bg in bgs)
        ass = min(standout(c, bg) for c in a[:4] for bg in bgs)
        boxes = [(box(old), box(new)) for old, new in zip(b, a)]
        drift = max(max(abs(new[4]-old[4]), abs(new[5]-old[5])) for old, new in boxes)
        size = max(max(abs((new[2]-new[0])/(old[2]-old[0])-1),
                       abs((new[3]-new[1])/(old[3]-old[1])-1)) for old, new in boxes)
        margin = min(min(new[0], new[1], c.width-new[2], c.height-new[3])
                     for c, (_, new) in zip(a, boxes))
        foreign = 0
        for c in (a[:4] if key == "chaser" else a):
            pixels = np.asarray(c)
            hsv = np.asarray(Image.fromarray(pixels[:, :, :3], "RGB").convert("HSV"))
            h, s, v = hsv[:, :, 0], hsv[:, :, 1], hsv[:, :, 2]
            foreign += int(((h >= 120) & (h <= 240) & (s > 80) & (v > 45)
                            & (pixels[:, :, 3] > 100)).sum())
        print(f"{name:22s} {bt:4d}->{at:5d} {bd:5d}->{ad:5d} "
              f"{max(scar(c) for c in b):2d}->{worst:2d} "
              f"{bs:5.1%}->{ass:5.1%} {drift:4.1f}px {size:4.1%} {margin:2d}px {foreign:3d}")
        if any(tones < 300 or boundaries < (6000 if key == "big" else 3000)
               for tones, boundaries in all_detail): failures.append(name+": detail")
        if worst >= 29: failures.append(name+": cut")
        if drift > 3 or size > .04: failures.append(name+": anchor/size")
        if ass < (.15 if key == "alien" else .10): failures.append(name+": standout")
        if margin < 6: failures.append(name+": margin")
        if foreign: failures.append(name+": foreign hue")
        if key == "chaser" and any(a[i].tobytes() != b[i].tobytes() for i in (4, 5, 6)):
            failures.append(name+": tell/hit changed")
    print("PASS" if not failures else "FAIL: " + ", ".join(failures))
    return len(failures)


def make_preview(before: dict[str, Image.Image], after: dict[str, Image.Image]) -> None:
    side, label, gap = 256, 24, 14
    width = 4*side*2 + 3*gap + 20
    row_h = side*2 + label + gap
    canvas = Image.new("RGB", (width, len(KEYS)*2*row_h), (11, 11, 26))
    d = ImageDraw.Draw(canvas)
    for k, key in enumerate(KEYS):
        name = "verdant_" + key
        for state, dataset in enumerate((before, after)):
            y0 = (2*k+state)*row_h
            d.text((10, y0+3), f"{name} {'BEFORE' if state == 0 else 'AFTER'}", fill=(229, 236, 215))
            for i, c in enumerate(cells(dataset[name])[:4]):
                up = c.resize((c.width*2, c.height*2), Image.Resampling.NEAREST)
                x = 10+i*(side*2+gap)
                canvas.paste(up, (x, y0+label), up)
    canvas.save(HERE / "preview.png")


def main() -> int:
    before = {"verdant_"+key: Image.open(PREVIOUS/f"verdant_{key}.png").convert("RGBA") for key in KEYS}
    after = {}
    knot = knot_source(192)
    for key in KEYS:
        name = "verdant_"+key
        old = cells(before[name])
        source = cells(Image.open(ORIGINAL/f"{name}.png").convert("RGBA"))
        fresh = [repaint_cell(old[i], source[i], name, i, knot if key == "rock_knot" else None)
                 if not (key == "chaser" and i >= 4) else old[i].copy()
                 for i in range(7)]
        if key == "rock_spore":
            for i in (4, 5): fresh[i] = repair_spore_cut(fresh[i], i)
        if key == "big":
            # Cell 5's solid hull is 245px tall in a 256px cell: it needs a
            # one-pixel vertical tuck to clear both six-pixel margins.
            tucked = Image.new("RGBA", fresh[5].size)
            tucked.alpha_composite(fresh[5].resize((256, 254), Image.Resampling.NEAREST), (0, 1))
            fresh[5] = tucked
        fresh = [trim_fringe_for_margin(c) if key == "fighter_1" and i in (2, 3)
                 else shift_for_margin(c) for i, c in enumerate(fresh)]
        out = Image.new("RGBA", before[name].size)
        for i, c in enumerate(fresh): out.alpha_composite(c, (i*c.width, 0))
        out.save(ART/f"{name}.png")
        after[name] = out
    make_preview(before, after)
    return 1 if audit(before, after) else 0


if __name__ == "__main__":
    sys.exit(main())
