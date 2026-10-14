"""Compose the generated Storm paintings into 768 px cover-boulder atlases."""

from pathlib import Path
import argparse
import math
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
CELL = 768
SOURCES = ("thunderhead_source.png", "hail_source.png", "island_source.png")
BAYER = np.array([[0, 8, 2, 10], [12, 4, 14, 6], [3, 11, 1, 9], [15, 7, 13, 5]], dtype=np.float32) / 16


def paint_source(path, variant):
    im = Image.open(path).convert("RGBA")
    aa = np.asarray(im)[:, :, 3]
    yy, xx = np.where(aa > 75)
    im = im.crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))
    w, h = im.size
    size = (int(round(w * min(670 / w, 682 / h))), int(round(h * min(670 / w, 682 / h))))
    im = im.resize(size, Image.Resampling.LANCZOS)
    a = np.asarray(im).copy()
    h, w = a.shape[:2]
    # A muted blue-grey ramp is sampled from the Storm reference; luminance retains
    # the source painting's structure, while ordered dither retains its fine grain.
    lum = a[:, :, :3].astype(np.float32) @ np.array([.25, .60, .15])
    lum = np.clip(lum * .77 + 7, 18, 139)
    gy, gx = np.mgrid[:h, :w]
    tone = np.clip(np.floor((lum + (BAYER[gy % 4, gx % 4] - .5) * 5) / 6) * 6, 18, 138)
    a[:, :, 0] = np.clip(tone * .92, 0, 255).astype(np.uint8)
    a[:, :, 1] = np.clip(tone * .96, 0, 255).astype(np.uint8)
    a[:, :, 2] = tone.astype(np.uint8)
    # Set the stone fully opaque. The lower 82 px of generated cloud break up
    # into hard pixels, leaving a textured vapour skirt and no soft edge.
    solid = a[:, :, 3] >= 120
    a[:, :, 3] = np.where(solid, 255, 0).astype(np.uint8)
    edge_start = h - 82
    fade = np.clip((h - 1 - gy) / 82, 0, 1)
    cloud_zone = gy >= edge_start
    noise = ((gx * 73856093) ^ (gy * 19349663) ^ (variant * 83492791)) & 255
    threshold = noise / 255
    a[:, :, 3] = np.where(cloud_zone & (threshold > fade), 0, a[:, :, 3])
    # Slightly darken exposed edges in local colour, never a light outline.
    mask = a[:, :, 3] > 240
    inside = mask.copy()
    inside[1:] &= mask[:-1]
    inside[:-1] &= mask[1:]
    inside[:, 1:] &= mask[:, :-1]
    inside[:, :-1] &= mask[:, 1:]
    edge = mask & ~inside
    a[edge, :3] = (a[edge, :3].astype(np.float32) * .92).astype(np.uint8)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.alpha_composite(Image.fromarray(a), ((CELL - w) // 2, (CELL - h) // 2))
    return cell


def fractures(variant, frame):
    layer = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(layer)
    rng = np.random.default_rng(454 + variant)
    # Branching, angular seams; lit only in the destruction sequence.
    origins = [(360, 370), (407, 375), (380, 420)]
    origin = origins[variant]
    branches = 5 if frame >= 2 else (3 if frame == 1 else 2)
    for j in range(branches):
        angle = -math.pi * .82 + j * 2 * math.pi / branches + variant * .2
        length = (125 if frame == 0 else 215 if frame == 1 else 305)
        points = [origin]
        for k in range(1, 12):
            radius = k * length / 11
            kink = rng.integers(-10, 11)
            points.append((int(origin[0] + radius * math.cos(angle) + kink),
                           int(origin[1] + radius * math.sin(angle) - kink)))
        width = 3 if frame == 0 else 6 if frame == 1 else 12
        d.line(points, fill=(105, 117, 141, 210), width=width + 4, joint="curve")
        d.line(points, fill=(221, 229, 245, 235), width=width, joint="curve")
        if frame >= 1:
            d.line(points[1:7], fill=(255, 243, 160, 250), width=max(2, width // 3), joint="curve")
    return layer


def generated_flash():
    im = Image.open(ROOT / "burst_source.png").convert("RGBA")
    a = np.asarray(im).copy()
    lum = a[:, :, :3].astype(np.float32) @ np.array([.22, .62, .16])
    # Retain the painted lightning and its brightest few cloud and chip details;
    # discard the generated broad translucent grey background.
    bright = (lum > 151) & (a[:, :, 3] > 100)
    a[:, :, 3] = np.where(bright, 255, 0).astype(np.uint8)
    yy, xx = np.where(bright)
    crop = Image.fromarray(a).crop((int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1))
    return crop.resize((510, 510), Image.Resampling.LANCZOS)


def partition(base, variant):
    yy, xx = np.mgrid[:CELL, :CELL]
    seeds = np.array([(272, 254), (460, 242), (585, 360), (468, 492),
                      (290, 532), (178, 382), (384, 365)], dtype=np.float32)
    seeds += np.random.default_rng(91 + variant).integers(-26, 27, seeds.shape)
    wobble = 35 * np.sin(xx / 14 + yy / 19) + 28 * np.cos(yy / 11 - xx / 18)
    dist = np.stack([((xx - sx) ** 2 + (yy - sy) ** 2) + wobble * (k - 3) * 16
                     for k, (sx, sy) in enumerate(seeds)])
    return np.argmin(dist, axis=0), seeds


def chunks(base, group, seeds, frame, variant):
    a = np.asarray(base)
    out = Image.new("RGBA", (CELL, CELL))
    rng = np.random.default_rng(2016 + variant * 17 + frame)
    for k in range(7):
        if frame == 4 and k == (variant + 1) % 7:
            continue
        keep = (group == k) & (a[:, :, 3] > 0)
        if not np.any(keep):
            continue
        y, x = np.where(keep)
        x0, x1, y0, y1 = int(x.min()), int(x.max()) + 1, int(y.min()), int(y.max()) + 1
        piece = a[y0:y1, x0:x1].copy()
        piece[:, :, 3] = np.where(keep[y0:y1, x0:x1], piece[:, :, 3], 0)
        image = Image.fromarray(piece)
        scale = [1, 1, 1, .70, .34][frame]
        if frame == 2:
            scale = .94
        if scale != 1:
            image = image.resize((max(1, int(image.width * scale)),
                                  max(1, int(image.height * scale))), Image.Resampling.NEAREST)
        angle = math.atan2(seeds[k, 1] - 384, seeds[k, 0] - 384)
        motion = [0, 2, 22, 77, 150][frame]
        dx = int(math.cos(angle) * motion + rng.integers(-5, 6))
        dy = int(math.sin(angle) * motion + rng.integers(-5, 6))
        cx = (x0 + x1) // 2 + dx
        cy = (y0 + y1) // 2 + dy
        out.alpha_composite(image, (cx - image.width // 2, cy - image.height // 2))
    return out


def debris(base, variant, frame):
    out = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(out)
    rng = np.random.default_rng(5100 + variant * 13 + frame)
    source = np.asarray(base)
    valid = np.argwhere(source[:, :, 3] == 255)
    count = [0, 3, 13, 34, 41][frame]
    extent = [0, 20, 170, 245, 300][frame]
    for _ in range(count):
        sy, sx = valid[rng.integers(len(valid))]
        theta = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(min(8 if frame == 1 else 75, extent), extent)
        x = int(np.clip(384 + math.cos(theta) * r, 7, 758))
        y = int(np.clip(384 + math.sin(theta) * r, 7, 758))
        radius = int(rng.integers(2, 7 if frame >= 3 else 10))
        color = tuple(int(c) for c in source[sy, sx, :3]) + (255,)
        d.polygon([(x - radius, y), (x + radius // 2, y - radius),
                   (x + radius, y + radius // 2), (x, y + radius)], fill=color)
        if rng.random() < .18:
            d.point((x + radius + 2, y - 2), fill=(242, 246, 255, 255))
    return out


def seam_light(base, opened, frame, variant):
    original = np.asarray(base.getchannel("A")) > 128
    now = np.asarray(opened.getchannel("A")) > 128
    gap = original & ~now
    near = now.copy()
    for _ in range(4 if frame == 2 else 2):
        spread = near.copy()
        spread[1:] |= near[:-1]
        spread[:-1] |= near[1:]
        spread[:, 1:] |= near[:, :-1]
        spread[:, :-1] |= near[:, 1:]
        near = spread
    edge = gap & near
    yy, xx = np.mgrid[:CELL, :CELL]
    fleck = ((xx * 13 + yy * 29 + variant * 11) % 19) < (8 if frame == 2 else 4)
    glow = edge & fleck
    a = np.zeros((CELL, CELL, 4), dtype=np.uint8)
    a[glow] = (184, 197, 223, 210 if frame == 2 else 130)
    core = glow & (((xx * 11 + yy * 7) % 17) < 3)
    a[core] = (242, 246, 255, 255 if frame == 2 else 180)
    return Image.fromarray(a)


def make_frames(base, variant, flash):
    group, seeds = partition(base, variant)
    frames = []
    intact_alpha = base.getchannel("A")
    for f in range(5):
        if f == 0:
            out = base.copy()
            crack = fractures(variant, f)
            crack.putalpha(Image.fromarray(np.minimum(np.asarray(crack.getchannel("A")),
                                                      np.asarray(intact_alpha))))
            out.alpha_composite(crack)
        else:
            out = Image.new("RGBA", (CELL, CELL))
            if f in (1, 2, 3):
                if f == 2:
                    out.alpha_composite(flash, ((CELL - flash.width) // 2, (CELL - flash.height) // 2))
                elif f == 3:
                    small = flash.resize((340, 340), Image.Resampling.NEAREST)
                    sa = np.asarray(small).copy()
                    sa[:, :, 3] = (sa[:, :, 3].astype(np.float32) * .45).astype(np.uint8)
                    out.alpha_composite(Image.fromarray(sa), ((CELL - 340) // 2, (CELL - 340) // 2))
                else:
                    out.alpha_composite(fractures(variant, f))
            out.alpha_composite(chunks(base, group, seeds, f, variant))
            if f in (2, 3):
                out.alpha_composite(seam_light(base, out, f, variant))
            if f == 1:
                crack = fractures(variant, f)
                crack.putalpha(Image.fromarray(np.minimum(np.asarray(crack.getchannel("A")),
                                                          np.asarray(out.getchannel("A")))))
                out.alpha_composite(crack)
            out.alpha_composite(debris(base, variant, f))
        arr = np.asarray(out).copy()
        arr[:4, :, 3] = arr[-4:, :, 3] = 0
        arr[:, :4, 3] = arr[:, -4:, 3] = 0
        frames.append(Image.fromarray(arr))
    return frames


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("output", type=Path)
    args = ap.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    for name in ("boulder.png", "break.png"):
        if (args.output / name).exists():
            raise FileExistsError(args.output / name)
    bases = [paint_source(ROOT / name, i) for i, name in enumerate(SOURCES)]
    intact = Image.new("RGBA", (CELL * 3, CELL))
    for i, im in enumerate(bases):
        intact.alpha_composite(im, (i * CELL, 0))
    intact.save(args.output / "boulder.png")
    flash = generated_flash()
    strip = Image.new("RGBA", (CELL * 5, CELL * 3))
    for i, base in enumerate(bases):
        for f, im in enumerate(make_frames(base, i, flash)):
            strip.alpha_composite(im, (f * CELL, i * CELL))
    strip.save(args.output / "break.png")


if __name__ == "__main__":
    main()
