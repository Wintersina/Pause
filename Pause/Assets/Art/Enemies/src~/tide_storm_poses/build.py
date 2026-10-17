#!/usr/bin/env python3
"""Assemble Tide / Storm regular-enemy strips with REAL painted tell and hit poses.

Cells 0-3 (idle) are kept from orig/<name>.png. Cells 4, 5 (tell) and 6 (hit recoil) come from the Codex
pose sheet out/<name>.png (left, middle, right third), registered onto the idle body anchor (scale + shift
search maximising silhouette IoU against idle cell 0). The hit cell also gets the world's impact flash.

  build.py [--apply] [--fake] [names...]
    --apply  write the strips into Resources/Enemies (default: out/build/)
    --fake   placeholder dry run: synthesise pose sheets from idle cell 0 (rotations/squashes) into out_fake/
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from spec import SHIPS

RES = HERE.parents[2] / "Resources" / "Enemies"
MARGIN = 6
ANCHOR = {"storm_chaser": "mass", "tide_chaser": "mass", "tide_big": "mass"}

TIDE_FLASH = dict(core=(247, 255, 249, 255), ring=(124, 242, 192, 255), pale=(220, 255, 237, 255))
STORM_FLASH = dict(core=(255, 250, 225, 255), ring=(173, 216, 241, 255), pale=(242, 246, 255, 255))


def alpha(im):
    return np.array(im.getchannel("A"))


def key_background(im):
    """Codex normally returns transparency; if it returned an opaque canvas, key the corner colour out."""
    a = np.array(im.convert("RGBA"))
    if (a[..., 3] < 250).mean() > 0.05:
        return Image.fromarray(a, "RGBA")
    corners = np.array([a[0, 0, :3], a[0, -1, :3], a[-1, 0, :3], a[-1, -1, :3]]).astype(int)
    bg = np.median(corners, axis=0)
    d = np.abs(a[..., :3].astype(int) - bg).max(-1)
    a[..., 3] = np.where(d < 28, 0, 255)
    return Image.fromarray(a, "RGBA")


def split_sheet(path):
    im = key_background(Image.open(path).convert("RGBA"))
    w, h = im.size
    cw = w / 3
    # Poses can overhang the nominal thirds (wing tips): cut at the emptiest column near each boundary instead.
    col = np.array(im.getchannel("A")).astype(np.float32).sum(0)
    cuts = [0]
    for k in (1, 2):
        lo, hi = int(k * cw - cw * .18), int(k * cw + cw * .18)
        cuts.append(lo + int(np.argmin(col[lo:hi])))
    cuts.append(w)
    out = []
    for i in range(3):
        region = im.crop((cuts[i], 0, cuts[i + 1], h))
        bb = region.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
        ccx = (bb[0] + bb[2]) / 2
        side = max(h, int(bb[2] - bb[0]) + 24)
        tile = Image.new("RGBA", (side, h))
        tile.alpha_composite(region, (int(side / 2 - ccx), 0))   # content centred on its own bbox, not the nominal third
        out.append(tile)
    return out


def resize_rgba(im, size):
    """Premultiplied box downsample, then a hard alpha edge (pixel-art look, no soft fringe)."""
    a = np.array(im).astype(np.float32)
    al = a[..., 3:4] / 255.0
    pm = Image.fromarray(np.concatenate([a[..., :3] * al, a[..., 3:4]], -1).clip(0, 255).astype(np.uint8), "RGBA")
    r = np.array(pm.resize(size, Image.Resampling.BOX)).astype(np.float32)
    ra = r[..., 3:4] / 255.0
    rgb = np.where(ra > 0.01, r[..., :3] / np.maximum(ra, 0.01), 0)
    out = np.concatenate([rgb.clip(0, 255), np.where(r[..., 3:4] > 127, 255, 0)], -1).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def xcorr(ref, mov):
    """Overlap count of binary masks for every shift (FFT cross-correlation); both HxW same shape."""
    n = ref.shape[0] * 2
    f = np.fft.rfft2(ref.astype(np.float32), (n, n))
    g = np.fft.rfft2(mov.astype(np.float32), (n, n))
    c = np.fft.irfft2(f * np.conj(g), (n, n))
    return c  # c[dy,dx] (mod n): ref(y) overlaps mov(y - d) ... shift mov by +d


def register(pose, ref_alpha, cell, maxshift=26, mrange=(0.60, 1.30), anchor=None):
    """Return a cell x cell RGBA of the pose scaled+shifted onto the idle anchor, plus (iou, scale, dx, dy)."""
    pose = pose.copy()
    bbox = pose.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
    base = cell / pose.height
    ref = ref_alpha > 40
    best = (-1, None)
    for m in np.arange(mrange[0], mrange[1], 0.02):
        s = base * m
        size = (max(1, round(pose.width * s)), max(1, round(pose.height * s)))
        scaled = resize_rgba(pose, size)
        # centre the scaled third on the cell, then search shifts
        canvas = Image.new("RGBA", (cell, cell))
        canvas.alpha_composite(scaled, ((cell - size[0]) // 2, (cell - size[1]) // 2)) if size[0] <= cell and size[1] <= cell \
            else canvas.paste(scaled.crop(((size[0] - cell) // 2, (size[1] - cell) // 2,
                                           (size[0] - cell) // 2 + cell, (size[1] - cell) // 2 + cell)), (0, 0))
        mov = alpha(canvas) > 40
        c = xcorr(ref, mov)
        n = c.shape[0]
        A, B = ref.sum(), mov.sum()
        for dy in range(-maxshift, maxshift + 1):
            for dx in range(-maxshift, maxshift + 1):
                inter = c[dy % n, dx % n]
                iou = inter / (A + B - inter + 1e-6)
                if iou > best[0]:
                    best = (iou, (m, dx, dy, canvas))
    iou, (m, dx, dy, canvas) = best
    if anchor:
        # silhouettes that change a lot (folded blades, whipped tail): pin the body part that does not move instead
        def pt(mask):
            ys, xs = np.nonzero(mask)
            if anchor == "low":
                keep = ys >= np.percentile(ys, 65)
                ys, xs = ys[keep], xs[keep]
            return xs.mean(), ys.mean()
        rx, ry = pt(ref)
        px_, py_ = pt(alpha(canvas) > 40)
        dx, dy = int(round(rx - px_)), int(round(ry - py_))
    arr = np.zeros((cell, cell, 4), np.uint8)
    src = np.array(canvas)
    ys, xs = slice(max(0, dy), min(cell, cell + dy)), slice(max(0, dx), min(cell, cell + dx))
    sy, sx = slice(max(0, -dy), min(cell, cell - dy)), slice(max(0, -dx), min(cell, cell - dx))
    arr[ys, xs] = src[sy, sx]
    return Image.fromarray(arr, "RGBA"), (iou, m, dx, dy)


def margin_ok(im):
    bb = im.getchannel("A").point(lambda v: 255 if v > 40 else 0).getbbox()
    w, h = im.size
    return bb is not None and bb[0] >= MARGIN and bb[1] >= MARGIN and bb[2] <= w - MARGIN and bb[3] <= h - MARGIN, bb


def fit_margin(im):
    """Shrink about the centre if a painted pose touches the 6 px safe border (rare)."""
    ok, bb = margin_ok(im)
    if ok:
        return im
    w = im.width
    for s in np.arange(0.98, 0.7, -0.02):
        size = round(w * s)
        small = resize_rgba(im, (size, size))
        c = Image.new("RGBA", im.size)
        c.alpha_composite(small, ((w - size) // 2, (w - size) // 2))
        if margin_ok(c)[0]:
            return c
    return im


def impact_flash(im, world):
    pal = TIDE_FLASH if world == "tide" else STORM_FLASH
    out = im.copy()
    a = alpha(out) > 40
    ys, xs = np.nonzero(a)
    cx, cy = int(xs.mean()), int(ys.mean())
    cell = out.width
    k = cell / 192
    d = ImageDraw.Draw(out)
    r = int(8 * k)
    for ang in range(0, 360, 45):
        import math
        L = r + 6 * k if ang % 90 == 0 else r + 2 * k
        d.line((cx, cy, cx + L * math.cos(math.radians(ang)), cy + L * math.sin(math.radians(ang))), fill=pal["pale"], width=1)
    d.ellipse((cx - r, cy - r, cx + r, cy + r), fill=pal["ring"])
    d.ellipse((cx - r * .6, cy - r * .6, cx + r * .6, cy + r * .6), fill=pal["core"])
    return out


def fake_sheet(name, cell):
    """Placeholder poses for a dry run: rotate / squash idle cell 0."""
    src = Image.open(HERE / "orig" / f"{name}.png").convert("RGBA").crop((0, 0, cell, cell)).resize((cell * 3, cell * 3), Image.NEAREST)
    poses = [src.resize((int(cell * 2.6), int(cell * 3.2)), Image.NEAREST), src.resize((int(cell * 3.3), int(cell * 2.8)), Image.NEAREST),
             src.rotate(14, resample=Image.NEAREST)]
    sheet = Image.new("RGBA", (cell * 3 * 3, cell * 3))
    for i, p in enumerate(poses):
        sheet.alpha_composite(p, (i * cell * 3 + (cell * 3 - p.width) // 2, (cell * 3 - p.height) // 2))
    return sheet


def build(name, cell, src_dir, dest_dir):
    world = name.split("_")[0]
    orig = Image.open(HERE / "orig" / f"{name}.png").convert("RGBA")
    ref = alpha(orig.crop((0, 0, cell, cell)))
    thirds = split_sheet(src_dir / f"{name}.png")
    cells, info = [], []
    # The three poses share one painted scale: take it from the best-matching pose, then only fine-tune it
    # (a retracted or folded pose would otherwise be blown up to fill the idle silhouette).
    first = [register(t, ref, cell, anchor=ANCHOR.get(name))[1] for t in thirds]
    mstar = max(first, key=lambda r: r[0])[1]
    for i, t in enumerate(thirds):
        reg, meta = register(t, ref, cell, mrange=(mstar - .04, mstar + .045), anchor=ANCHOR.get(name))
        reg = fit_margin(reg)
        if i == 2:
            reg = impact_flash(reg, world)
        cells.append(reg)
        info.append(meta)
    strip = Image.new("RGBA", (cell * 7, cell))
    strip.alpha_composite(orig.crop((0, 0, cell * 4, cell)), (0, 0))
    for i, c in enumerate(cells):
        strip.alpha_composite(c, ((4 + i) * cell, 0))
    dest_dir.mkdir(parents=True, exist_ok=True)
    strip.save(dest_dir / f"{name}.png", optimize=True)
    return info


def main():
    args = sys.argv[1:]
    apply, fake = "--apply" in args, "--fake" in args
    names = [a for a in args if not a.startswith("--")]
    src_dir = HERE / ("out_fake" if fake else "out")
    dest_dir = RES if apply else HERE / ("build_fake" if fake else "build")
    if fake:
        src_dir.mkdir(exist_ok=True)
    for name, cell, *_ in SHIPS:
        if names and name not in names:
            continue
        if fake:
            fake_sheet(name, cell).save(src_dir / f"{name}.png")
        if not (src_dir / f"{name}.png").exists():
            print("skip (no sheet)", name)
            continue
        info = build(name, cell, src_dir, dest_dir)
        print(name, " ".join(f"iou={i:.2f} m={m:.2f} d=({dx},{dy})" for i, m, dx, dy in info))


if __name__ == "__main__":
    main()
