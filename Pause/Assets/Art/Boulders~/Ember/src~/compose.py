"""Compose Ember cover boulders from image-generated 1254 px paintings.

Source paintings: candidate_B0 (spire), candidate_B1 (slab), candidate_A2
(columns). All destructive poses cut pixels from those paintings at source
resolution, then downsample into 768 px cells. Never upsample a painting.
"""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
S = 1254
C = 768
NAMES = ("candidate_B0.png", "candidate_B1.png", "candidate_A2.png")
SEEDS = (
    ((358, 325), (720, 230), (983, 410), (969, 795), (656, 950), (310, 811), (539, 581)),
    ((326, 331), (653, 257), (960, 359), (993, 711), (692, 903), (338, 811), (585, 585)),
    ((335, 322), (654, 255), (978, 339), (998, 727), (688, 941), (333, 830), (616, 590)),
)


def paint_source(path):
    im = Image.open(path).convert("RGBA")
    a = np.asarray(im)
    ys, xs = np.nonzero(a[..., 3] > 16)
    crop = im.crop((int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1))
    # The painting is 1254 px wide. Fit it to about 642 px in the final cell.
    # Both reductions use Lanczos; no texture is enlarged.
    width = 1048
    height = round(crop.height * width / crop.width)
    small = crop.resize((width, height), Image.Resampling.LANCZOS)
    canvas = Image.new("RGBA", (S, S))
    canvas.alpha_composite(small, ((S - width) // 2, round(578 - height / 2)))
    arr = np.asarray(canvas).copy()
    # Warm, muted palette sampled against Ember's landmark atlas. The cool
    # generated highlights are pushed into ash brown; old lava stays amber.
    rgb = arr[..., :3].astype(np.float32)
    rgb *= (0.89, 0.80, 0.55)
    rgb[..., 0] = np.minimum(rgb[..., 0], 225)
    rgb[..., 1] = np.minimum(rgb[..., 1], 174)
    rgb[..., 2] = np.minimum(rgb[..., 2], 92)
    arr[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    return Image.fromarray(arr)


def skirt(im, rng):
    """Scatter source-coloured cooled slag into a 60-90 final-pixel fade."""
    a = np.asarray(im)
    solid = a[..., 3] > 235
    ys, xs = np.nonzero(solid)
    foot = int(np.percentile(ys, 99.9))
    left, right = int(np.percentile(xs, 3)), int(np.percentile(xs, 97))
    skirt_im = Image.new("RGBA", (S, S))
    d = ImageDraw.Draw(skirt_im)
    for _ in range(7000):
        # 75 final pixels = about 122 source pixels. Sparse toward the edge.
        dist = rng.random() ** 1.6
        y = foot - 46 + int(140 * dist)
        x = rng.randrange(left - 70, right + 71)
        lateral = min(x - (left - 70), (right + 70) - x)
        if lateral < 40 and rng.random() > max(0.1, lateral / 40):
            continue
        if not (20 < x < S - 20 and 20 < y < S - 20):
            continue
        if solid[y, x] or rng.random() > (1 - dist) ** 0.55:
            continue
        r = rng.choice((1, 1, 2, 2, 3, 4, 6))
        alpha = int((18 + 110 * rng.random()) * (1 - dist) ** 1.5)
        col = rng.choice(((34, 26, 16), (47, 34, 19), (69, 48, 24), (92, 63, 26)))
        d.polygon(((x-r,y), (x,y-r//2), (x+r,y+1), (x,y+r//2+1)), fill=(*col,alpha))
    skirt_im.alpha_composite(im)
    return skirt_im


def labels_for(a, variant):
    yy, xx = np.mgrid[:S, :S]
    vals = []
    for sx, sy in SEEDS[variant]:
        dist = (xx - sx) ** 2 + (yy - sy) ** 2
        # Broad irregular fracture paths, no repeated square tiling.
        warp = (11000 * np.sin(xx / 67 + sy / 107)
                + 7000 * np.sin(yy / 51 + sx / 94)
                + 2300 * np.sin((xx + yy) / 13 + sx / 47))
        vals.append(dist + warp)
    label = np.argmin(np.stack(vals), axis=0).astype(np.uint8)
    body = a[..., 3] > 220
    edge = np.zeros((S, S), bool)
    for dy, dx in ((1,0),(-1,0),(0,1),(0,-1)):
        edge |= (label != np.roll(label, (dy, dx), (0, 1))) & body & np.roll(body, (dy, dx), (0, 1))
    return label, edge


def light_cracks(im, edge, phase):
    arr = np.asarray(im).copy()
    body = arr[..., 3] > 220
    def grow(mask, width):
        return np.asarray(Image.fromarray((mask * 255).astype('uint8')).filter(ImageFilter.MaxFilter(width))) > 0
    if phase == 0:
        # Sparse first fracture. Pixels outside the first seam remain exact.
        yy, xx = np.indices(edge.shape)
        on = edge & ((((xx // 89) + (yy // 71)) % 4) == 0)
        core = grow(on, 5)
        halo = grow(on, 13)
        mix = .48
    else:
        on = edge
        core = grow(on, 7 if phase == 1 else 9)
        halo = grow(on, 19 if phase == 1 else 25)
        mix = .67 if phase == 1 else 1.0
    core &= body
    halo &= body
    rgb = arr[..., :3].astype(np.float32)
    # A short stepped amber spill stays in the stone, never on the outer rim.
    rgb[halo] = rgb[halo] * (1 - mix * .33) + np.array((190, 101, 27)) * (mix * .33)
    rgb[core] = rgb[core] * (1 - mix) + np.array((255, 180, 90)) * mix
    if phase == 2:
        rgb[on] = np.array((255, 240, 200))
    arr[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    return Image.fromarray(arr)


def chunks(im, labels, edge, phase, rng):
    if phase == 1:
        lit = light_cracks(im, edge, 1)
    else:
        lit = light_cracks(im, edge, 2)
    base = np.asarray(lit)
    canvas = Image.new("RGBA", (S, S))
    if phase in (2, 3):
        # Light remains behind the lifted fragments, visible only through the
        # actual opened cuts. It follows their original fracture network.
        glow = np.zeros((S, S, 4), np.uint8)
        halo = np.asarray(Image.fromarray((edge * 255).astype('uint8')).filter(ImageFilter.MaxFilter(33 if phase == 2 else 23))) > 0
        core = np.asarray(Image.fromarray((edge * 255).astype('uint8')).filter(ImageFilter.MaxFilter(9))) > 0
        glow[halo] = (155, 79, 19, 75 if phase == 2 else 36)
        glow[core] = (255, 154, 60, 175 if phase == 2 else 74)
        glow[edge] = (255, 240, 200, 230 if phase == 2 else 90)
        canvas = Image.fromarray(glow)
    count = 7
    for k in range(count):
        if phase == 4 and k not in (0, 2, 3, 5):
            continue
        mask = labels == k
        alpha = np.where(mask, base[..., 3], 0).astype(np.uint8)
        yy, xx = np.nonzero(alpha > 8)
        if not len(xx):
            continue
        box = (int(xx.min()), int(yy.min()), int(xx.max())+1, int(yy.max())+1)
        piece = Image.fromarray(np.dstack((base[..., :3], alpha))).crop(box)
        scale = {1:1.0, 2:1.0, 3:.76, 4:.50}[phase]
        if scale < 1:
            piece = piece.resize((max(1, round(piece.width*scale)), max(1,round(piece.height*scale))), Image.Resampling.LANCZOS)
        if phase == 4:
            faded = np.asarray(piece).copy()
            faded[..., 3] = (faded[..., 3].astype(np.uint16) * .82).astype(np.uint8)
            piece = Image.fromarray(faded)
        cx, cy = (box[0]+box[2])/2, (box[1]+box[3])/2
        vx, vy = cx - S/2, cy - 550
        norm = max(1, math.hypot(vx, vy))
        amount = {1:9, 2:55, 3:135, 4:188}[phase]
        dx, dy = vx / norm * amount, vy / norm * amount
        if phase >= 2:
            dx += rng.uniform(-8, 8)
            dy += rng.uniform(-8, 8)
        x = round(cx + dx - piece.width/2)
        y = round(cy + dy - piece.height/2)
        x = max(12, min(S - piece.width - 12, x))
        y = max(12, min(S - piece.height - 12, y))
        canvas.alpha_composite(piece, (x, y))
    return canvas


def debris(im, phase, rng):
    if phase < 2:
        return im
    d = ImageDraw.Draw(im)
    n = {2:90, 3:360, 4:1600}[phase]
    for _ in range(n):
        angle = rng.random()*math.tau
        rad = rng.uniform(70, 485 if phase == 4 else 410)
        x = int(S/2 + math.cos(angle)*rad)
        y = int(580 + math.sin(angle)*rad*.77)
        if not (16 < x < S-16 and 16 < y < S-16):
            continue
        size = rng.choice((1,2,2,3,4,6,8))
        if phase == 4:
            col = rng.choice(((40,31,19,48),(52,37,18,70),(88,59,20,90),(116,77,24,62)))
        else:
            col = rng.choice(((40,31,19,140),(54,38,20,160),(98,64,26,170),(210,130,39,165)))
        d.polygon(((x-size,y), (x+size//2,y-size), (x+size,y+size//2), (x,y+size)), fill=col)
    return im


def final_cell(im):
    arr = np.asarray(im.resize((C, C), Image.Resampling.LANCZOS)).copy()
    # Remove a few reddish/magenta subpixels introduced by the source model
    # and Lanczos interpolation. Every coloured texel remains in the warm
    # black-to-amber wedge, with orange starting at hue 20 degrees.
    rgb = arr[..., :3].astype(np.uint16)
    r = np.maximum(rgb[..., 0], rgb[..., 1])
    b = rgb[..., 2]
    g = np.maximum(rgb[..., 1], b)
    g = np.maximum(g, (r + 2*b + 2) // 3)
    active = arr[..., 3] > 0
    arr[..., 0][active] = r[active].astype(np.uint8)
    arr[..., 1][active] = g[active].astype(np.uint8)
    return Image.fromarray(arr)


def main():
    boulder = Image.new("RGBA", (3*C, C))
    breaks = Image.new("RGBA", (5*C, 3*C))
    for v, name in enumerate(NAMES):
        rng = np.random.default_rng(9901 + v)
        # A Python RNG is used for particle choices, seeded alongside numpy.
        import random
        prng = random.Random(9901 + v)
        src = skirt(paint_source(SRC/name), prng)
        base = final_cell(src)
        boulder.alpha_composite(base, (v*C,0))
        lab, edge = labels_for(np.asarray(src), v)
        for f in range(5):
            if f == 0:
                high = light_cracks(src, edge, 0)
            else:
                high = chunks(src, lab, edge, f, prng)
            high = debris(high, f, prng)
            cell = final_cell(high)
            breaks.alpha_composite(cell, (f*C,v*C))
    boulder.save(ROOT/'boulder.png')
    breaks.save(ROOT/'break.png')


if __name__ == '__main__':
    main()
