"""Shared helpers of the HD elite rebuilds (build_*_hd.py in each world's src~/).

The old world builders scaled a ~1250 px painted master down to a ~110-150 px hull with NEAREST sampling (about 7 of 8 source
texels dropped, a noisy dithered look) on a 192 px cell. The HD builders keep every recipe step (hue clean-up, rim, bank warp,
exhaust, damage, death frames, hardpoints) but run it on a Z x bigger cell (Z = 2: 384 px) with a premultiplied area-averaging
(Lanczos) downsample. Their drawing code is the old code written in 192-space and scaled by Z through ZDraw; image warps use
zgrid (192-space float coordinates sampled back at HD). Nothing is block-doubled and no master is ever upscaled
(fit_to_master asserts it).
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

Z = 2
BASE = 192
CELL = BASE * Z          # 384


def premult_resize(img: Image.Image, size: tuple[int, int], resample=Image.Resampling.LANCZOS) -> Image.Image:
    """Resize an RGBA image through premultiplied alpha (no dark fringes)."""
    arr = np.asarray(img.convert("RGBA")).astype(np.float32)
    pre = arr.copy()
    pre[:, :, :3] *= arr[:, :, 3:4] / 255.0
    small = Image.fromarray(np.clip(pre, 0, 255).astype(np.uint8), "RGBA").resize(size, resample)
    p = np.asarray(small).astype(np.float32)
    a = p[:, :, 3:4]
    rgb = np.where(a > 0, p[:, :, :3] * 255.0 / np.maximum(a, 1), 0)
    return Image.fromarray(np.concatenate([np.clip(rgb, 0, 255), a], axis=2).astype(np.uint8), "RGBA")


def fit_to_master(master: Image.Image, alpha_threshold: int, size_192: tuple[int, int], name: str = "") -> Image.Image:
    """Crop the master to its silhouette and area-average it to the old hull size x Z. Asserts it is not an upscale."""
    bbox = master.getchannel("A").point(lambda a: 255 if a >= alpha_threshold else 0).getbbox()
    assert bbox, name
    crop = master.crop(bbox)
    w, h = size_192[0] * Z, size_192[1] * Z
    assert crop.width >= w and crop.height >= h, f"{name}: master crop {crop.size} would be upscaled to {(w, h)}"
    return premult_resize(crop, (w, h))


def zgrid(size: int = CELL):
    """(yy, xx) of an HD cell in 192-space (float): the old index maths runs on these, then `zsample` reads the HD pixels."""
    yy, xx = np.indices((size, size))
    return (yy + .5) / Z, (xx + .5) / Z


def zsample(arr: np.ndarray, sy, sx) -> np.ndarray:
    """arr[sy, sx] where sy / sx are 192-space coordinates (nearest HD texel: the warp amounts are tiny sub-pixel shifts of a
    painted surface, a pure translation per texel, so this is lossless, not a resample)."""
    h, w = arr.shape[:2]
    iy = np.clip(np.floor(sy * Z).astype(int), 0, h - 1)
    ix = np.clip(np.floor(sx * Z).astype(int), 0, w - 1)
    return arr[iy, ix]


def rotate(img: Image.Image, angle: float, center: tuple[float, float]) -> Image.Image:
    """Bicubic rotation about a 192-space centre through premultiplied alpha."""
    arr = np.asarray(img).astype(np.float32)
    pre = arr.copy()
    pre[:, :, :3] *= arr[:, :, 3:4] / 255.0
    im = Image.fromarray(np.clip(pre, 0, 255).astype(np.uint8), "RGBA").rotate(angle, Image.Resampling.BICUBIC, center=(center[0] * Z, center[1] * Z))
    p = np.asarray(im).astype(np.float32)
    a = p[:, :, 3:4]
    rgb = np.where(a > 0, p[:, :, :3] * 255.0 / np.maximum(a, 1), 0)
    return Image.fromarray(np.concatenate([np.clip(rgb, 0, 255), a], axis=2).astype(np.uint8), "RGBA")


def shift_mask(a: np.ndarray, dx: int, dy: int) -> np.ndarray:
    out = np.zeros_like(a)
    h, w = a.shape[:2]
    ys = slice(max(0, dy), min(h, h + dy)); xs = slice(max(0, dx), min(w, w + dx))
    fy = slice(max(0, -dy), min(h, h - dy)); fx = slice(max(0, -dx), min(w, w - dx))
    out[ys, xs] = a[fy, fx]
    return out


class ZDraw:
    """ImageDraw in 192-space: every coordinate and width is multiplied by Z; a `point` becomes a Z x Z block (a 1 px mark of the
    old art is Z px wide here, i.e. the same size on screen)."""

    def __init__(self, im: Image.Image):
        self.d = ImageDraw.Draw(im)

    @staticmethod
    def _pts(pts):
        if len(pts) == 4 and not isinstance(pts[0], (tuple, list)):
            pts = [(pts[0], pts[1]), (pts[2], pts[3])]
        return [(x * Z, y * Z) for x, y in pts]

    @staticmethod
    def _box(b):
        if not isinstance(b[0], (tuple, list)):
            b = [(b[0], b[1]), (b[2], b[3])]
        (x0, y0), (x1, y1) = b
        return (x0 * Z, y0 * Z, x1 * Z + Z - 1, y1 * Z + Z - 1)    # inclusive pixel boxes

    def point(self, xy, fill):
        x, y = xy
        self.d.rectangle((x * Z, y * Z, x * Z + Z - 1, y * Z + Z - 1), fill=fill)

    def line(self, pts, fill=None, width=1, joint=None):
        self.d.line(self._pts(pts), fill=fill, width=max(1, int(round(width * Z))), joint=joint)

    def polygon(self, pts, fill=None, outline=None):
        self.d.polygon(self._pts(pts), fill=fill, outline=outline)

    def ellipse(self, box, fill=None, outline=None, width=1):
        self.d.ellipse(self._box(box), fill=fill, outline=outline, width=max(1, width * Z))

    def rectangle(self, box, fill=None, outline=None, width=1):
        self.d.rectangle(self._box(box), fill=fill, outline=outline, width=max(1, width * Z))

    def arc(self, box, start, end, fill=None, width=1):
        self.d.arc(self._box(box), start, end, fill=fill, width=max(1, int(round(width * Z))))


def hsv_arrays(rgb: np.ndarray):
    r, g, b = (rgb[..., i].astype(np.float32) / 255 for i in range(3))
    mx = np.maximum(np.maximum(r, g), b); mn = np.minimum(np.minimum(r, g), b); d = mx - mn
    dd = np.where(d > 1e-9, d, 1)
    h = np.where(mx == r, ((g - b) / dd) % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) * 60
    h = np.where(d > 1e-9, h, 0)
    s = np.where(mx > 0, d / np.where(mx > 0, mx, 1), 0)
    return h, s, mx


def rgb_from_hsv(h, s, v) -> np.ndarray:
    h = (h % 360) / 60.0
    i = np.floor(h).astype(int) % 6; f = h - np.floor(h)
    p = v * (1 - s); q = v * (1 - s * f); t = v * (1 - s * (1 - f))
    r = np.choose(i, [v, q, p, p, t, v]); g = np.choose(i, [t, v, v, q, p, p]); b = np.choose(i, [p, p, t, v, v, q])
    return np.clip(np.rint(np.stack([r, g, b], -1) * 255), 0, 255).astype(np.uint8)


def paste_strip(frames, cell: int = CELL) -> Image.Image:
    strip = Image.new("RGBA", (cell * len(frames), cell))
    for i, f in enumerate(frames):
        strip.alpha_composite(f, (i * cell, 0))
    return strip


def before_after(old_strip: Path, new_strip: Image.Image, world_cell_size: float, out: Path, label: str, crop_box_192=None, cell_index: int = 3):
    """One image: OLD vs HD hover cell at (1) 1x native, (2) real on-screen size on a 1080 px wide phone (1 u = 145 px), (3) a zoomed
    crop of a key detail (same region of the cell, both at 4x screen px per old texel)."""
    old = Image.open(old_strip).convert("RGBA")
    oc = old.height
    px = int(round(world_cell_size * 145))
    bg = (56, 52, 96, 255)
    pad = 12
    zoom_src = crop_box_192 or (60, 40, 132, 112)
    zs = 288
    cols = [("OLD %d px cell" % oc, old.crop((cell_index * oc, 0, (cell_index + 1) * oc, oc)), oc), ("HD %d px cell" % new_strip.height, new_strip.crop((cell_index * new_strip.height, 0, (cell_index + 1) * new_strip.height, new_strip.height)), new_strip.height)]
    W = pad + 2 * (CELL + px + zs + 3 * pad) + pad
    H = CELL + 2 * pad + 28
    sheet = Image.new("RGBA", (W, H), bg)
    dr = ImageDraw.Draw(sheet)
    dr.text((pad, 4), label + " (cell, on-screen at 1080 w, detail crop)", fill=(255, 255, 255, 255))
    x = pad
    for title, cell, size in cols:
        dr.text((x, 16), title, fill=(255, 255, 255, 255))
        sheet.alpha_composite(cell, (x, 28)) if size == CELL else sheet.alpha_composite(cell, (x + (CELL - size) // 2, 28 + (CELL - size) // 2))
        x += CELL + pad
        small = cell.resize((px, px), Image.Resampling.LANCZOS)
        sheet.alpha_composite(small, (x, 28 + (CELL - px) // 2))
        x += px + pad
        f = size / 192
        box = tuple(int(round(v * f)) for v in zoom_src)
        det = cell.crop(box).resize((zs, zs), Image.Resampling.NEAREST)
        sheet.alpha_composite(det, (x, 28))
        x += zs + pad
    sheet.convert("RGB").save(out, optimize=True)


def emit(out_dir: Path, key: str, strip: Image.Image, death: Image.Image, defs_dir: Path, old_strip: Path, detail_crop=None, palette=None):
    """Write <key>.png / <key>_death.png / <key>.json (def: cellPixels, muzzles + nozzles x Z) / <key>_before_after.png to out_dir."""
    import json
    out_dir.mkdir(exist_ok=True)
    if palette:
        strip = Image.fromarray(palette(np.asarray(strip)), "RGBA")
        death = Image.fromarray(palette(np.asarray(death)), "RGBA")
    strip.save(out_dir / f"{key}.png", optimize=True)
    death.save(out_dir / f"{key}_death.png", optimize=True)
    d = json.load(open(defs_dir / f"{key}.json"))
    if d.get("cellPixels", 192) == BASE:
        d["cellPixels"] = CELL
        for k in ("muzzles", "nozzles"):
            for p in d[k]:
                p["x"] = round(p["x"] * Z); p["y"] = round(p["y"] * Z)
    with open(out_dir / f"{key}.json", "w") as f:
        json.dump(d, f, indent=2); f.write("\n")
    before_after(old_strip, strip, d["cellWorldSize"], out_dir / f"{key}_before_after.png", key, detail_crop)
    print("ok", key, strip.size, death.size)
    return strip
