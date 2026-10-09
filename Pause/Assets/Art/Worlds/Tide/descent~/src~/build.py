"""Build the Tide planetfall set from the retained image-generated sources.

All resampling is nearest-neighbor. No spatial blur or global palette reduction.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
NN = Image.Resampling.NEAREST


def source(name: str) -> Image.Image:
    return Image.open(SRC / name).convert("RGBA")


def palette(im: Image.Image, alpha_floor: int = 0) -> Image.Image:
    """Steer chromatic pixels into Tide bands, retaining the source's tonal variety."""
    rgba = np.asarray(im.convert("RGBA")).copy()
    hsv = np.asarray(im.convert("RGB").convert("HSV")).copy()
    h = hsv[:, :, 0].astype(np.float32) * (360.0 / 255)
    s = hsv[:, :, 1]
    v = hsv[:, :, 2]
    chromatic = (s > 32) & (v > 24)
    # Original painted material remains. Only hues belonging to other worlds
    # are steered to mint, ocean teal, restrained brown, or shadow blue.
    warm = chromatic & ((h < 42) | (h >= 330))
    hsv[:, :, 0][warm] = 18  # 25 degrees: desaturated rust, no red.
    hsv[:, :, 1][warm] = np.minimum(s[warm], 105)
    alien_green = chromatic & (h >= 42) & (h < 147)
    hsv[:, :, 0][alien_green] = 110  # 155 degrees, mint.
    shield = chromatic & (h >= 168) & (h < 193)
    hsv[:, :, 0][shield] = 116  # 164 degrees, safely away from shield cyan.
    violet = chromatic & (h >= 246) & (h < 336)
    hsv[:, :, 0][violet] = 160  # 226 degrees, muted marine blue.
    # Neutral pale white stays white; saturated glow belongs to mint.
    out = Image.fromarray(hsv, "HSV").convert("RGB").convert("RGBA")
    a = rgba[:, :, 3]
    if alpha_floor:
        a = np.where(a >= alpha_floor, a, 0).astype(np.uint8)
    out.putalpha(Image.fromarray(a, "L"))
    return out


def fit_planet() -> None:
    raw = palette(source("planet_candidate_a.png"), 32)
    # The 4096 master retains the generated source's full geometry before the
    # 1024 delivery is reduced with nearest-neighbor sampling.
    master = Image.new("RGBA", (4096, 4096))
    large = raw.resize((3712, 3712), NN)
    master.alpha_composite(large, (192, 172))
    master.save(ROOT / "masters" / "tide_planet_master_4096.png")
    planet = master.resize((1024, 1024), NN)
    planet.save(ROOT / "tide_planet.png")


def fit_limb() -> None:
    im = palette(source("limb_source.png"), 24)
    src = np.asarray(im)
    sh, sw = src.shape[:2]
    alpha = src[:, :, 3]
    # Determine the painted horizon at every source column from its first
    # sustained opaque pixel; then register it to the required parabolic arc.
    tops = np.zeros(sw, dtype=np.int32)
    for x in range(sw):
        candidates = np.flatnonzero(alpha[: sh // 2, x] > 180)
        tops[x] = int(candidates[0]) if len(candidates) else sh // 2
    dst = np.zeros((1024, 2048, 4), dtype=np.uint8)
    xs = np.minimum((np.arange(2048) * sw / 2048).astype(np.int32), sw - 1)
    for x in range(2048):
        edge = abs((x - 1023.5) / 1023.5)
        horizon = round(355 + 365 * edge ** 1.75)
        sy = tops[xs[x]] + (np.arange(1024 - horizon) * (sh - tops[xs[x]] - 1) / max(1, 1023 - horizon)).astype(np.int32)
        dst[horizon:, x] = src[np.minimum(sy, sh - 1), xs[x]]
        # Preserve a continuous mint glint on the hand-painted atmosphere.
        dst[horizon, x] = (89, 195, 155, 255)
    Image.fromarray(dst, "RGBA").save(ROOT / "tide_planet_limb.png")


def wrap_deck(im: Image.Image) -> Image.Image:
    a = np.asarray(im.resize((2048, 1024), NN)).copy()
    # Source edge materials are close already; register the exact terminal
    # pixel rows without changing the image's cloud forms or tonal ramps.
    a[:, -1] = a[:, 0]
    a[-1, :] = a[0, :]
    return Image.fromarray(a, "RGBA")


def clouds() -> None:
    for file, out in [("cloud_source.png", "tide_cloud_deck.png"),
                      ("cloud_dark_source.png", "tide_cloud_deck_dark.png")]:
        im = palette(source(file))
        im.putalpha(255)
        wrap_deck(im).save(ROOT / out)


def entry() -> None:
    a = palette(source("fx_source_a.png"), 38)
    b = palette(source("fx_source_b.png"), 48)
    strip = Image.new("RGBA", (3072, 1024))
    holes = []
    for i in range(6):
        frame = Image.new("RGBA", (512, 1024))
        # Painted shroud changes width and phase, with alternating extra arms.
        width = [472, 480, 462, 478, 480, 480][i]
        height = [780, 800, 765, 795, 783, 792][i]
        phase = [0, 2, -3, 3, -2, 1][i]
        layer = a.resize((width, height), NN)
        frame.alpha_composite(layer, ((512 - width) // 2 + phase, 178 + (i % 3 - 1) * 5))
        if i in (1, 3, 5):
            accent = b.resize((480, 810), NN)
            accent.putalpha(accent.getchannel("A").point(lambda v: v // 4))
            frame.alpha_composite(accent, (16 - phase, 175))
        arr = np.asarray(frame).copy()
        yy, xx = np.ogrid[:1024, :512]
        cx, cy = 256 + [0, 2, -2, 3, -1, 1][i], 338 + [0, 1, -1, 1, 0, -1][i]
        rx, ry = [74, 73, 74, 75, 73, 74][i], [83, 84, 82, 84, 83, 83][i]
        hole = ((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2 <= 1
        # The generated source has a wider horizontal oval. Close only its
        # excess transparent area by extending its own adjacent painted
        # pixels, then cut the clean registered ship opening below.
        for y in range(285, 391):
            row = arr[y]
            left = np.flatnonzero(row[110:cx, 3] > 120) + 110
            right = np.flatnonzero(row[cx:402, 3] > 120) + cx
            if not len(left) or not len(right):
                continue
            lo, hi = int(left[-1]), int(right[0])
            if hi - lo > 270 or hi <= lo:
                continue
            for x in range(lo + 1, hi):
                if row[x, 3] < 40 and not hole[y, x]:
                    row[x] = row[lo if x < cx else hi]
        arr[hole] = 0
        # A compact pixel rim keeps the opening closed for flood-fill checks
        # and ties its contour to the painted white-hot plasma core.
        radius = np.sqrt(((xx - cx) / rx) ** 2 + ((yy - cy) / ry) ** 2)
        inner = (radius > 1.0) & (radius <= 1.045)
        outer = (radius > 1.045) & (radius <= 1.09)
        arr[inner] = (225, 250, 229, 255)
        arr[outer] = (119, 230, 187, 235)
        # All frame borders stay genuinely transparent, including bottom.
        arr[:8] = 0; arr[-8:] = 0; arr[:, :8] = 0; arr[:, -8:] = 0
        frame = Image.fromarray(arr, "RGBA")
        strip.alpha_composite(frame, (i * 512, 0))
        holes.append({"center_px": [cx, cy], "size_px": [2 * rx, 2 * ry]})
    strip.save(ROOT / "tide_entry_fx.png")
    (SRC / "entry_registration.json").write_text(json.dumps(holes, indent=2) + "\n")


def breakthrough() -> None:
    base = palette(source("breakthrough_source.png"), 44)
    strip = Image.new("RGBA", (5120, 1024))
    for i, side in enumerate([270, 440, 625, 800, 930]):
        frame = Image.new("RGBA", (1024, 1024))
        ring = base.resize((side, side), NN)
        if i == 4:
            ring.putalpha(ring.getchannel("A").point(lambda v: round(v * 0.67)))
        frame.alpha_composite(ring, ((1024 - side) // 2, (1024 - side) // 2))
        strip.alpha_composite(frame, (i * 1024, 0))
    strip.save(ROOT / "tide_breakthrough.png")


def streaks() -> None:
    im = palette(source("streaks_source.png"), 46).resize((1024, 2048), NN)
    a = np.asarray(im).copy()
    # The source carries the broad rain ribbons. Keep them translucent so
    # gameplay stays legible, then layer fine 1px speed lines into their gaps.
    a[:, :, 3] = (a[:, :, 3].astype(np.uint16) * 0.48).astype(np.uint8)
    out = Image.fromarray(a, "RGBA")
    d = ImageDraw.Draw(out)
    rng = np.random.default_rng(2155)
    colors = [(147, 225, 198, 75), (213, 248, 229, 106), (81, 175, 156, 80), (89, 116, 165, 52)]
    for _ in range(760):
        x = int(rng.integers(18, 1006))
        y = int(rng.integers(0, 2048))
        length = int(rng.integers(14, 190))
        d.line((x, y, x, min(2047, y + length)), fill=colors[int(rng.integers(4))], width=1)
    arr = np.asarray(out).copy()
    sleeve = 64
    arr[-sleeve:] = arr[:sleeve][::-1]
    arr[-1] = arr[0]
    Image.fromarray(arr, "RGBA").save(ROOT / "tide_entry_streaks.png")


def previews() -> None:
    bg = Image.new("RGB", (2048, 2048), "#0b0b1a")
    d = ImageDraw.Draw(bg)
    layout = [
        ("tide_planet.png", (0, 40, 660, 700)),
        ("tide_planet_limb.png", (680, 40, 2028, 715)),
        ("tide_cloud_deck.png", (20, 750, 1010, 1245)),
        ("tide_cloud_deck_dark.png", (1030, 750, 2020, 1245)),
        ("tide_entry_fx.png", (20, 1300, 990, 1900)),
        ("tide_breakthrough.png", (1030, 1300, 2020, 1690)),
        ("tide_entry_streaks.png", (1030, 1730, 2020, 1990)),
    ]
    for name, box in layout:
        im = Image.open(ROOT / name).convert("RGBA")
        if name == "tide_entry_fx.png":
            im = im.crop((0, 0, 1536, 1024))
        elif name == "tide_breakthrough.png":
            im = im.crop((0, 0, 3072, 1024))
        im.thumbnail((box[2] - box[0], box[3] - box[1] - 24), NN)
        bg.paste(im, (box[0], box[1] + 20), im)
        d.text((box[0] + 5, box[1]), name, fill=(180, 239, 210))
    # Contact sheet is rendered at 2x from the layout above.
    bg.resize((4096, 4096), NN).save(ROOT / "preview.png")
    fx = Image.open(ROOT / "tide_entry_fx.png")
    frames = []
    for i in range(6):
        frame = Image.new("RGBA", (512, 1024), "#0b0b1a")
        frame.alpha_composite(fx.crop((i * 512, 0, (i + 1) * 512, 1024)))
        frames.append(frame.resize((256, 512), NN).convert("P", palette=Image.Palette.ADAPTIVE))
    frames[0].save(ROOT / "preview.gif", save_all=True, append_images=frames[1:], duration=110, loop=0, disposal=2)


def main() -> None:
    for job in (fit_planet, fit_limb, clouds, entry, breakthrough, streaks, previews):
        job()
        print(job.__name__, "done", flush=True)


if __name__ == "__main__":
    main()
