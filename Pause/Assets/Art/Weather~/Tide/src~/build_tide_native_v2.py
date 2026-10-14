#!/usr/bin/env python3
"""Rebuild Tide weather at the delivered cell resolution from imagegen paintings.

All resizes reduce the painted originals with nearest-neighbour sampling.  The
only rotated pixels are sampled at the 512-pixel working resolution.  No sheet
is enlarged, blurred, or resampled after its cells have been composed.
"""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
RAMP = np.array([
    (14, 42, 48), (29, 72, 78), (55, 112, 112),
    (76, 151, 143), (143, 208, 196), (191, 239, 228),
    (240, 255, 250),
], dtype=np.uint8)


def crop_alpha(im, cutoff=35):
    a = np.asarray(im.getchannel("A"))
    yy, xx = np.where(a > cutoff)
    return im.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))


def quantize(a, alpha_cap, alpha_floor=0):
    a = a.copy()
    rgb = a[..., :3].astype(np.float32)
    lum = rgb @ np.array([.2126, .7152, .0722], dtype=np.float32)
    ix = np.digitize(lum, [43, 72, 105, 140, 184, 229])
    a[..., :3] = RAMP[ix]
    raw_alpha = a[..., 3].astype(np.float32)
    if alpha_floor:
        raw_alpha = np.maximum(raw_alpha - alpha_floor, 0) * 255 / (255 - alpha_floor)
    a[..., 3] = np.clip(raw_alpha * alpha_cap / 255, 0, 255).astype(np.uint8)
    a[a[..., 3] == 0, :3] = 0
    return a


def whirl_base():
    src = crop_alpha(Image.open(SRC / "whirlpool_native_source_v2.png").convert("RGBA"), 28)
    # 1254 px painting to a 466 px motif, a strict reduction.
    side = 466
    motif = src.resize((side, side), Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (512, 512))
    canvas.alpha_composite(motif, ((512 - side) // 2, (512 - side) // 2))
    original = np.asarray(canvas).astype(np.float32)
    # Four equivalent quarter turns make the 90-degree motion a true loop.
    alpha = np.zeros((512, 512), dtype=np.float32)
    premul = np.zeros((512, 512, 3), dtype=np.float32)
    for k in range(4):
        turn = np.rot90(original, k)
        aa = turn[..., 3] / 255
        alpha += aa / 4
        premul += turn[..., :3] * aa[..., None] / 4
    sym = np.zeros_like(original, dtype=np.uint8)
    sym[..., :3] = np.clip(premul / np.maximum(alpha[..., None], .001), 0, 255).astype(np.uint8)
    sym[..., 3] = np.clip(alpha * 255, 0, 255).astype(np.uint8)
    out = quantize(sym, 148, 18)
    yy, xx = np.indices((512, 512))
    radius = np.hypot(xx - 255.5, yy - 255.5)
    # Calm centre; foam and spray live on the rims.
    air = np.clip((radius - 22) / 66, .14, 1)
    out[..., 3] = (out[..., 3] * air).astype(np.uint8)
    out[out[..., 3] == 0, :3] = 0
    return Image.fromarray(out, "RGBA")


def periodic_seam(tile, width=72):
    # Bring both ends toward the same painted values at the join, then fade
    # back to the unmodified painting.  Keep the middle of the band intact.
    tile = tile.astype(np.float32)
    for j in range(width):
        f = j / width
        left = tile[:, j].copy()
        right = tile[:, -1 - j].copy()
        common = (left + right) * .5
        tile[:, j] = common * (1 - f) + left * f
        tile[:, -1 - j] = common * (1 - f) + right * f
    tile[:, -1] = tile[:, 0]
    return np.clip(tile, 0, 255).astype(np.uint8)


def surge_base():
    src = Image.open(SRC / "surge_native_source_v2.png").convert("RGBA")
    # Keep the source's continuous band, including its transparent margins.
    reduced = src.resize((1024, 341), Image.Resampling.NEAREST)
    strip = np.asarray(reduced)[:, 200:712].copy()
    strip = periodic_seam(strip)
    canvas = np.zeros((512, 512, 4), dtype=np.uint8)
    canvas[85:426] = strip
    out = quantize(canvas, 139, 30)
    yy = np.arange(512, dtype=np.float32)[:, None]
    taper = np.minimum(np.clip((yy - 91) / 25, 0, 1), np.clip((421 - yy) / 25, 0, 1))
    out[..., 3] = (out[..., 3] * taper).astype(np.uint8)
    out[:, -1] = out[:, 0]
    out[out[..., 3] == 0, :3] = 0
    return Image.fromarray(out, "RGBA")


def warning(body, front, phase):
    a = np.asarray(body).copy()
    yy, xx = np.indices((512, 512))
    lum = a[..., :3].astype(np.float32) @ np.array([.2126, .7152, .0722], dtype=np.float32)
    # Thin broken water marks rather than a solid reduced-alpha silhouette.
    sample = ((xx * 37 + yy * 71 + xx * yy * 3 + phase * 23) % 101)
    keep = (sample < 33) | ((lum > 185) & (sample < 69))
    if not front:
        keep &= np.hypot(xx - 255.5, yy - 255.5) > 103
    a[..., 3] = np.where(keep, (a[..., 3].astype(float) * .76).astype(np.uint8), 0)
    if front:
        a[:, :10, 3] = 0
        a[:, -10:, 3] = 0
    a[a[..., 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def ambient_particle(source, kind, variant):
    # The generated 1536x1024 sheet has one painted motif in each quadrant.
    boxes = [(72, 38, 697, 493), (795, 36, 1478, 505),
             (53, 526, 738, 993), (795, 523, 1490, 991)]
    motif = crop_alpha(source.crop(boxes[kind]), 125)
    sizes = [(54, 64, 75, 69), (65, 79, 88, 73),
             (70, 82, 90, 77), (56, 67, 77, 63)][kind]
    longest = sizes[variant]
    factor = longest / max(motif.size)
    w, h = max(1, round(motif.width * factor)), max(1, round(motif.height * factor))
    small = np.asarray(motif.resize((w, h), Image.Resampling.NEAREST)).copy()
    # Strip the source's faint painted ambience; keep the pixel marks.
    small[..., 3] = np.where(small[..., 3] > 103, small[..., 3], 0)
    if kind == 0:
        yy, xx = np.indices((h, w))
        hole = ((xx - w * .49) / (w * .32))**2 + ((yy - h * .50) / (h * .32))**2 < 1
        small[hole, 3] = 0
    small = quantize(small, (175, 190, 180, 195)[variant], 40)
    cell = Image.new("RGBA", (256, 256))
    cell.alpha_composite(Image.fromarray(small, "RGBA"), ((256 - w) // 2, (256 - h) // 2))
    return cell


def report(name, a):
    alpha = np.asarray(a)[..., 3]
    active = alpha > 8
    print(f"{name}: mean active alpha {alpha[active].mean()/255:.3f}; "
          f"mean cell alpha {alpha.mean()/255:.3f}; max {alpha.max()}; "
          f"alpha mass {alpha.sum()}")


def main():
    whirl = whirl_base()
    surge = surge_base()
    hazard = Image.new("RGBA", (3072, 1536))
    for i in range(6):
        # 15-degree increments make a 90-degree cycle in six frames.
        spin = whirl.rotate(-15 * i, Image.Resampling.NEAREST)
        moving = np.roll(np.asarray(surge), round(i * 512 / 6), axis=1).copy()
        moving[:, -1] = moving[:, 0]
        hazard.alpha_composite(spin, (i * 512, 0))
        hazard.alpha_composite(Image.fromarray(moving, "RGBA"), (i * 512, 512))
    for i in range(2):
        spin = whirl.rotate(-15 * i, Image.Resampling.NEAREST)
        hazard.alpha_composite(warning(spin, False, i), (i * 512, 1024))
        moving = np.roll(np.asarray(surge), i * 85, axis=1).copy()
        hazard.alpha_composite(warning(Image.fromarray(moving, "RGBA"), True, i), ((i + 2) * 512, 1024))
    hazard.save(ROOT / "hazard.png")

    ambient = Image.new("RGBA", (2048, 512))
    source = Image.open(SRC / "ambient_native_source_v2.png").convert("RGBA")
    for kind in range(4):
        for variant in range(4):
            index = kind * 4 + variant
            ambient.alpha_composite(ambient_particle(source, kind, variant),
                                    ((index % 8) * 256, (index // 8) * 256))
    ambient.save(ROOT / "ambient.png")
    report("whirlpool body", whirl)
    report("surge body", surge)
    report("whirlpool warning", warning(whirl, False, 0))
    report("surge warning", warning(surge, True, 0))


if __name__ == "__main__":
    main()
