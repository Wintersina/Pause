#!/usr/bin/env python3
"""Assemble Desert cover sprites from image-generated paintings. Output is a new directory."""
from pathlib import Path
import argparse
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).parent
CELL = 768
SELECTIONS = (("mesa_a.png", "mesa_burst.png"),
              ("arch_b.png", "arch_burst.png"),
              ("butte_b.png", "butte_burst.png"))


def source_cell(filename, width=650, ycenter=372):
    source = Image.open(HERE / filename).convert("RGBA")
    a = np.asarray(source)[..., 3]
    yy, xx = np.nonzero(a > 245)
    # Every selected source is larger than its destination. Only downsample.
    box = (int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1)
    source = source.crop(box)
    height = round(source.height * width / source.width)
    assert source.width >= width and source.height >= height
    source = source.resize((width, height), Image.Resampling.LANCZOS)
    raw = np.array(source).astype(np.float32)
    # Atmospheric khaki in the reference mid layer and dark landmark atlas.
    rgb = raw[..., :3] * .50 + np.array([106, 94, 70]) * .12
    if filename.startswith("butte"):
        gy, gx = np.mgrid[0:height, 0:width]
        shade = 1.15 - .45 * (.5*gx/max(1,width-1) + .5*gy/max(1,height-1))
        rgb *= shade[..., None]
    rgb = np.clip(rgb, 0, 255).astype(np.uint8)
    al = raw[..., 3]
    # Pixel-clean the generation's semi-transparent cutout. Keep a short
    # natural-material edge, not a light stroke around the silhouette.
    alpha = np.where(al >= 250, 255,
                     np.where(al <= 205, 0, ((al - 205) / 45 * 245))).astype(np.uint8)
    arr = np.dstack((rgb, alpha))
    im = Image.fromarray(arr, "RGBA")
    x = (CELL - width) // 2
    y = ycenter - height // 2
    canvas = Image.new("RGBA", (CELL, CELL))
    canvas.alpha_composite(im, (x, y))
    return canvas


def dust_skirt(im, seed):
    """Sand and talus around only the lower foot, tapering over about 70 px."""
    rng = np.random.default_rng(seed)
    body = np.array(im)
    yy, xx = np.nonzero(body[..., 3] >= 250)
    bottom = int(np.quantile(yy, .99))
    low = (body[..., 3] >= 250) & (np.indices((CELL, CELL))[0] > bottom - 90)
    sy, sx = np.nonzero(low)
    dust = Image.new("RGBA", (CELL, CELL))
    d = ImageDraw.Draw(dust)
    for i in range(3400):
        j = int(rng.integers(0, len(sx)))
        dx = int(rng.normal(0, 23))
        dy = int(rng.uniform(5, 72))
        x, y = int(sx[j] + dx), int(sy[j] + dy)
        if x < 22 or x >= CELL - 22 or y < 18 or y >= CELL - 22:
            continue
        fade = max(0, 1 - (dy / 82) ** 1.4)
        if rng.random() > fade:
            continue
        radius = int(rng.choice([1, 1, 2, 2, 3, 4, 6]))
        color = [(86, 76, 57), (99, 88, 64), (115, 101, 75), (69, 57, 41)][i % 4]
        alpha = int((90 if radius < 4 else 145) * fade)
        d.ellipse((x-radius, y-radius//2, x+radius, y+radius//2+1), fill=(*color, alpha))
    # Composite behind the solid body, so its alpha remains fully opaque.
    dust.alpha_composite(im)
    return dust


def fitted_burst(filename, variant):
    return source_cell(filename, width=650, ycenter=372)


def crack_mask(solid, variant):
    # Hand-set fault lines are narrow and local, never a hazard marker.
    paths = [
        [[(315, 318), (321, 328), (317, 339), (326, 352), (322, 365)],
         [(463, 317), (457, 329), (460, 341), (449, 353)]],
        [[(335, 288), (341, 299), (338, 309), (346, 322)],
         [(483, 314), (475, 321), (479, 334), (469, 342)]],
        [[(313, 305), (319, 316), (317, 329), (326, 340)],
         [(466, 309), (458, 320), (463, 329), (453, 341)]]
    ][variant]
    mask = Image.new("L", (CELL, CELL))
    d = ImageDraw.Draw(mask)
    for line in paths:
        d.line(line, fill=255, width=2)
    return np.minimum(np.array(mask), (solid > 245).astype(np.uint8) * 255)


def add_lit_cracks(im, variant, strength):
    a = np.array(im)
    solid = a[..., 3]
    crack = crack_mask(solid, variant)
    thick = np.array(Image.fromarray(crack).filter(ImageFilter.MaxFilter(3)))
    edge = (thick > 0) & (solid > 245)
    core = (crack > 0) & (solid > 245)
    a[edge, :3] = np.array([min(255, int(v*strength)) for v in (163, 136, 92)])
    a[core, :3] = np.array([min(255, int(v*strength)) for v in (255, 233, 168)])
    return Image.fromarray(a, "RGBA")


def regions(variant):
    # Seven broad slabs, with slight variants in the placement of their faults.
    seeds = np.array([
        [(240, 250), (390, 230), (540, 295), (220, 405), (385, 390), (548, 425), (380, 555)],
        [(245, 290), (375, 255), (515, 285), (220, 410), (360, 390), (535, 420), (390, 530)],
        [(245, 265), (380, 230), (515, 285), (235, 410), (395, 375), (540, 405), (405, 520)],
    ][variant], dtype=np.float32)
    yy, xx = np.mgrid[0:CELL, 0:CELL]
    distance = ((xx[..., None] - seeds[:, 0])**2 +
                (yy[..., None] - seeds[:, 1])**2 * 1.1)
    # Uneven material fractures instead of a straight Voronoi diagram.
    rng = np.random.default_rng(840 + variant)
    for k in range(7):
        coarse = (rng.random((33, 33)) * 255).astype(np.uint8)
        fine = (rng.random((145, 145)) * 255).astype(np.uint8)
        n1 = np.array(Image.fromarray(coarse).resize((CELL, CELL), Image.Resampling.BILINEAR), dtype=np.float32)
        n2 = np.array(Image.fromarray(fine).resize((CELL, CELL), Image.Resampling.BILINEAR), dtype=np.float32)
        distance[..., k] += (n1 - 127) * 14 + (n2 - 127) * 2.5
    return distance.argmin(axis=-1).astype(np.uint8), seeds


def shift_chunks(intact, variant, travel, scale=1.0):
    original = np.array(intact)
    ids, seeds = regions(variant)
    canvas = Image.new("RGBA", (CELL, CELL))
    center = np.array([CELL/2, CELL/2])
    for k, seed in enumerate(seeds):
        cut = original.copy()
        cut[ids != k, 3] = 0
        cut_im = Image.fromarray(cut, "RGBA")
        bb = cut_im.getbbox()
        if bb is None:
            continue
        cut_im = cut_im.crop(bb)
        if scale != 1:
            nw = max(1, round(cut_im.width * scale))
            nh = max(1, round(cut_im.height * scale))
            cut_im = cut_im.resize((nw, nh), Image.Resampling.LANCZOS)
        direction = seed - center
        direction /= max(1., np.linalg.norm(direction))
        dx, dy = np.round(direction * travel).astype(int)
        px = round((bb[0] + bb[2]) / 2 - cut_im.width / 2 + dx)
        py = round((bb[1] + bb[3]) / 2 - cut_im.height / 2 + dy)
        px = max(4, min(CELL-4-cut_im.width, px))
        py = max(4, min(CELL-4-cut_im.height, py))
        canvas.alpha_composite(cut_im, (px, py))
    return canvas


def generated_seams(burst):
    a = np.array(burst)
    rgb = a[..., :3].astype(float)
    l = rgb[..., 0]*.299 + rgb[..., 1]*.587 + rgb[..., 2]*.114
    gold = (l > 112) & (rgb[..., 0] > rgb[..., 2]*1.25) & (a[..., 3] > 150)
    return gold


def seam_underlay(intact, moved, burst, variant, power):
    a = np.array(intact)
    m = np.array(moved)
    ids, _ = regions(variant)
    boundary = np.zeros((CELL, CELL), bool)
    boundary[1:] |= ids[1:] != ids[:-1]
    boundary[:, 1:] |= ids[:, 1:] != ids[:, :-1]
    line = Image.fromarray((boundary*255).astype(np.uint8))
    line = line.filter(ImageFilter.MaxFilter(7 if power >= 2 else 3))
    gap = (np.array(line) > 0) & (a[..., 3] > 220) & (m[..., 3] < 200)
    # The exposed edges of the separated slabs carry the brilliant seam.
    # Constrain it to the original mass so it cannot become an outer halo.
    edge_reach = np.array(Image.fromarray(m[..., 3]).filter(ImageFilter.MaxFilter(17)))
    face_light = ((edge_reach > 220) & (m[..., 3] < 190) &
                  (a[..., 3] > 220) & (power >= 2))
    # The generated burst painting modulates the seams: this retains its
    # painterly, irregular hot spots while the slabs remain cut from intact art.
    gen = generated_seams(burst)
    hot = gap & gen
    base = np.zeros_like(a)
    base[gap, :3] = (137, 102, 60)
    base[gap, 3] = 150 if power == 1 else 190
    base[face_light, :3] = (238, 202, 133)
    base[face_light, 3] = 215 if power == 2 else 165
    base[hot, :3] = (255, 233, 168)
    base[hot, 3] = 245
    core = (hot & boundary) | (face_light & gen & boundary)
    base[core, :3] = (255, 251, 234)
    base[core, 3] = 255
    under = Image.fromarray(base, "RGBA")
    under.alpha_composite(moved)
    return under


def debris_frame(intact, burst, variant, phase):
    rng = np.random.default_rng(1731 + variant*37 + phase)
    base = np.array(intact)
    painted = np.array(burst)
    canvas = Image.new("RGBA", (CELL, CELL))
    # Small slabs inherit their actual texture from the chosen intact painting.
    for i in range(125 if phase == 3 else 46):
        px = int(rng.integers(100, 665))
        py = int(rng.integers(215, 570))
        if base[py, px, 3] < 240:
            continue
        side = int(rng.integers(4, 17 if phase == 3 else 12))
        crop = Image.fromarray(base, "RGBA").crop((px-side, py-side, px+side, py+side))
        irregular = Image.new("L", crop.size)
        md = ImageDraw.Draw(irregular)
        md.polygon([(1,side//2),(side//2,1),(side+2,0),(2*side-2,side//2),
                    (2*side-3,2*side-3),(side//2,2*side-1)],fill=255)
        crop.putalpha(Image.fromarray(np.minimum(np.array(crop.getchannel("A")),
                                                   np.array(irregular))))
        destx = int(np.clip(px + rng.normal(0, 55 if phase == 3 else 95), 5, CELL-2*side-5))
        desty = int(np.clip(py + rng.normal(0, 55 if phase == 3 else 83), 5, CELL-2*side-5))
        canvas.alpha_composite(crop, (destx, desty))
    # Fine dust gets its colours from the image-generated burst painting.
    d = ImageDraw.Draw(canvas)
    count = 850 if phase == 3 else 620
    for i in range(count):
        x = int(np.clip(rng.normal(384, 150 if phase == 3 else 155), 16, 751))
        y = int(np.clip(rng.normal(420, 110 if phase == 3 else 125), 16, 751))
        sx, sy = int(np.clip(x, 0, 767)), int(np.clip(y, 0, 767))
        src = painted[sy, sx, :3]
        if painted[sy, sx, 3] < 100:
            src = np.array([100, 82, 58])
        color = tuple(int(v) for v in (src*.50 + np.array([106, 94, 70])*.12))
        r = int(rng.choice([1, 1, 2, 2, 3, 4, 6]))
        opacity = int(rng.integers(135, 215)) if phase == 4 else int(rng.integers(140, 235))
        d.ellipse((x-r,y-r//2,x+r,y+r//2+1), fill=(*color,opacity))
    return canvas


def compose(out):
    out.mkdir(parents=True, exist_ok=False)
    boulder = Image.new("RGBA", (CELL*3, CELL))
    breaking = Image.new("RGBA", (CELL*5, CELL*3))
    for v, (source, generated_burst) in enumerate(SELECTIONS):
        intact = dust_skirt(source_cell(source), 400+v)
        burst = fitted_burst(generated_burst, v)
        boulder.alpha_composite(intact, (v*CELL, 0))
        frames = [add_lit_cracks(intact, v, 1.0)]
        for phase, travel, scale in [(1, 7, 1), (2, 33, .98), (3, 64, .62)]:
            moved = shift_chunks(intact, v, travel, scale)
            frame = seam_underlay(intact, moved, burst, v, phase)
            if phase == 3:
                frame.alpha_composite(debris_frame(intact, burst, v, 3))
            frames.append(frame)
        frames.append(debris_frame(intact, burst, v, 4))
        for f, frame in enumerate(frames):
            breaking.alpha_composite(frame, (f*CELL, v*CELL))
    boulder.save(out / "boulder.png")
    breaking.save(out / "break.png")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    compose(args.output)
