#!/usr/bin/env python3
"""Compose Storm's native-resolution weather sheets from generated paintings.

All source images are larger than the exported cells. Every resize uses nearest
neighbour; the script never enlarges a source texel.
"""
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
N = Image.Resampling.NEAREST

# Restrained storm ramp. The alpha plane carries the cloud's soft structure.
RAMP = np.array([
    (20, 21, 46),
    (38, 38, 72),
    (58, 56, 102),
    (84, 82, 132),
    (122, 120, 184),
    (170, 170, 210),
    (244, 246, 255),
], dtype=np.uint8)


def clean(im, strength=0.48, max_alpha=145, remove_warm=False):
    a = np.array(im.convert("RGBA"), dtype=np.uint8)
    rgb = a[..., :3].astype(np.float32)
    old_alpha = a[..., 3].astype(np.float32)
    # Transparent blue in the generated source is matte residue, not paint.
    old_alpha[old_alpha <= 24] = 0
    if remove_warm:
        warm = (rgb[..., 0] > rgb[..., 1] + 12) & (rgb[..., 1] > rgb[..., 2] + 13)
        old_alpha[warm] = 0
    lum = rgb[..., 0] * .27 + rgb[..., 1] * .55 + rgb[..., 2] * .18
    idx = np.digitize(lum, [27, 47, 75, 106, 157, 218]).astype(np.uint8)
    a[..., :3] = RAMP[idx]
    # Light values are intentionally quieter than hostile ship rim lights.
    light_factor = np.where(idx >= 6, .54, np.where(idx == 5, .82, 1.0))
    a[..., 3] = np.clip(old_alpha * strength * light_factor, 0, max_alpha).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def attenuate_eye(im):
    a = np.array(im)
    yy, xx = np.mgrid[:512, :512]
    rr = np.hypot(xx - 255.5, yy - 255.5)
    f = np.clip((rr - 76) / 97, .30, 1)
    a[..., 3] = (a[..., 3].astype(float) * f).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


def tornado():
    # Candidate 1 has four broad arms, so a quarter turn nearly rejoins frame 0.
    source = Image.open(SRC / "tornado_candidate_1.png").convert("RGBA")
    source = source.resize((450, 450), N)
    source = clean(source, .49, 142)
    cell = Image.new("RGBA", (512, 512))
    cell.alpha_composite(source, (31, 31))
    cell = attenuate_eye(cell)
    return [cell.rotate(-angle, resample=N) for angle in (0, 18, 36, 54, 72, 90)]


def make_seamless(im, taper=40):
    a = np.array(im).astype(np.float32)
    # Matched folded edge; a thin gradual join is quieter than a hard tile cut.
    for i in range(taper):
        t = .5 * (1 - i / taper)
        left = a[:, i].copy()
        right = a[:, 511 - i].copy()
        mid = (left + right) * .5
        a[:, i] = left * (1 - 2*t) + mid * (2*t)
        a[:, 511 - i] = right * (1 - 2*t) + mid * (2*t)
    a[:, -1] = a[:, 0]
    return Image.fromarray(np.uint8(np.clip(a, 0, 255)), "RGBA")


def gust():
    source = Image.open(SRC / "gust_candidate_1.png").convert("RGBA")
    # Use the painted roll, leading lip, rain and streaks; remove blank canvas.
    source = source.crop((0, 345, source.width, 960)).resize((512, 282), N)
    source = clean(source, .46, 138, remove_warm=True)
    band = Image.new("RGBA", (512, 512))
    band.alpha_composite(source, (0, 115))
    band = make_seamless(band)
    # The lateral flow eases back to zero at the loop seam.
    frames = []
    for shift in (0, 18, 36, 54, 36, 18):
        a = np.roll(np.array(band), shift, axis=1)
        a[:, -1] = a[:, 0]
        frames.append(Image.fromarray(a, "RGBA"))
    return frames


def warning(im, seed, density=.42):
    a = np.array(im)
    yy, xx = np.mgrid[:512, :512]
    # A broken fine preview of the painted weather, with no UI silhouette.
    keep = ((xx * 37 + yy * 73 + seed * 53) % 101) < 71
    a[..., 3] = (a[..., 3].astype(float) * density * keep).astype(np.uint8)
    a[:, :8, 3] = 0
    a[:, -8:, 3] = 0
    return Image.fromarray(a, "RGBA")


def build_hazard():
    out = Image.new("RGBA", (3072, 1536))
    aa, bb = tornado(), gust()
    for i, frame in enumerate(aa):
        out.paste(frame, (i * 512, 0))
    for i, frame in enumerate(bb):
        out.paste(frame, (i * 512, 512))
    for i, frame in enumerate((aa[0], aa[3], bb[0], bb[3])):
        out.paste(warning(frame, i), (i * 512, 1024))
    out.save(ROOT / "hazard.png")


PARTICLES = [
    # Rain: long light streaks.
    ((106, 139), (280, 100), (400, 102), (555, 143)),
    # Heavy rain: shorter with a painted droplet end.
    ((750, 134), (900, 125), (1040, 154), (1162, 133)),
    # Small wind curls.
    ((120, 733), (273, 729), (423, 733), (541, 735)),
    # Spray clusters.
    ((738, 722), (885, 720), (1035, 735), (1170, 722)),
]


def build_ambient():
    original = Image.open(SRC / "ambient_paint.png").convert("RGBA")
    sheet = Image.new("RGBA", (2048, 512))
    sizes = [(90, 112), (84, 104), (110, 104), (106, 104)]
    for kind in range(4):
        w, h = sizes[kind]
        for variant, (cx, cy) in enumerate(PARTICLES[kind]):
            crop = original.crop((cx-w//2, cy-h//2, cx+w//2, cy+h//2))
            crop = clean(crop, .48 if kind in (0, 2) else .53, 140)
            # Keep each painted crop small, centred and at its source resolution.
            cell = Image.new("RGBA", (256, 256))
            cell.alpha_composite(crop, ((256-w)//2, (256-h)//2))
            k = kind*4 + variant
            sheet.paste(cell, ((k % 8)*256, (k // 8)*256))
    sheet.save(ROOT / "ambient.png")


if __name__ == "__main__":
    build_hazard()
    build_ambient()
