#!/usr/bin/env python3
"""Assemble the Verdant cover boulder from image-generated paintings.

The three source paintings are larger than the final cell. All cuts and debris
sample their pixels; only the jade fracture light and tiny spores are drawn.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np

HERE = Path(__file__).resolve().parent
DEST = HERE / "v2"
CELL = 768
SOURCES = ("stump_candidate_d.png", "root_candidate_c.png", "arch_candidate_c.png")
WIDTHS = (674, 670, 672)
JADE = (184, 255, 196)
CORE = (239, 255, 240)


def erosion(m, n):
    for _ in range(n):
        old = m
        m = old.copy()
        m[1:] &= old[:-1]
        m[:-1] &= old[1:]
        m[:, 1:] &= old[:, :-1]
        m[:, :-1] &= old[:, 1:]
    return m


def bounding(m):
    yy, xx = np.where(m)
    return int(xx.min()), int(yy.min()), int(xx.max()) + 1, int(yy.max()) + 1


def place_source(name, width):
    raw = Image.open(HERE / name).convert("RGBA")
    alpha = np.asarray(raw)[..., 3]
    x0, y0, x1, y1 = bounding(alpha >= 220)
    pad = 12
    raw = raw.crop((max(0, x0-pad), max(0, y0-pad), min(raw.width, x1+pad), min(raw.height, y1+pad)))
    h = round(raw.height * width / raw.width)
    assert raw.width >= width and raw.height >= h
    raw = raw.resize((width, h), Image.Resampling.LANCZOS)
    a = np.array(raw)
    rgb = a[..., :3].astype(np.float32)
    # Atmosphere and the backdrop's leafy median; no pale highlight or saturated rim.
    rgb[..., 0] *= .82
    rgb[..., 1] *= .86
    rgb[..., 2] *= .84
    lum = rgb @ np.array([.299, .587, .114], np.float32)
    rgb *= np.minimum(1, 144 / np.maximum(lum, 1))[..., None]
    mx = rgb.max(2)
    mn = rgb.min(2)
    excess = (mx-mn) / np.maximum(mx, 1) > .55
    grey = (rgb @ np.array([.299, .587, .114], np.float32))[..., None]
    rgb[excess] = rgb[excess] * .80 + grey[excess] * .20
    # Bark may be warm brown, but reject red and pink pixels from the model.
    red = (rgb[..., 0] > rgb[..., 1] * 1.28) & (rgb[..., 0] > rgb[..., 2] * 1.20)
    rgb[..., 1][red] = np.maximum(rgb[..., 1][red], rgb[..., 0][red] * .79)
    pink = (rgb[..., 0] > rgb[..., 1] * 1.16) & (rgb[..., 2] > rgb[..., 1] * 1.14)
    rgb[..., 1][pink] = np.maximum(rgb[..., 1][pink], np.minimum(rgb[..., 0][pink], rgb[..., 2][pink]))
    a[..., :3] = np.clip(rgb, 0, 255).astype(np.uint8)
    al = a[..., 3].astype(np.float32)
    al = np.where(al >= 180, 255, np.clip((al-70)*2.3, 0, 220))
    # The already painted dangling fronds dissolve through 70 px of dithered alpha.
    yy, xx = np.indices((h, width))
    fade = np.clip((h - yy) / 72, 0, 1)
    lower = yy >= h-72
    al[lower] = np.minimum(al[lower], np.floor(fade[lower] * 8) * (255/8))
    al[(lower) & (((xx*13 + yy*17) % 11) == 0)] *= .55
    a[..., 3] = np.clip(al, 0, 255).astype(np.uint8)
    a[a[..., 3] == 0, :3] = 0
    cell = np.zeros((CELL, CELL, 4), np.uint8)
    xx0 = (CELL-width)//2
    yy0 = (CELL-h)//2
    cell[yy0:yy0+h, xx0:xx0+width] = a
    return cell


PATHS = (
    (((274, 307), (331, 328), (377, 359), (430, 399), (487, 438)),
     ((360, 285), (371, 331), (377, 359), (340, 419), (306, 459)),
     ((467, 301), (433, 344), (430, 399), (456, 445))),
    (((246, 318), (313, 356), (377, 392), (447, 434), (504, 483)),
     ((383, 268), (392, 329), (377, 392), (341, 460), (312, 512)),
     ((523, 316), (456, 360), (447, 434), (476, 490))),
    (((263, 325), (329, 344), (388, 353), (451, 366), (505, 401)),
     ((369, 264), (377, 321), (388, 353), (339, 391)),
     ((486, 306), (451, 366), (480, 411), (522, 445))),
)


def crack_mask(v, width):
    image = Image.new("L", (CELL, CELL))
    draw = ImageDraw.Draw(image)
    for path in PATHS[v]:
        draw.line(path, fill=255, width=width, joint="curve")
    return np.array(image) > 0


def light(base, v, deep=False):
    result = base.copy()
    solid = result[..., 3] >= 245
    inside = erosion(solid, 5)
    seam = crack_mask(v, 6 if deep else 3) & inside
    core = crack_mask(v, 2 if deep else 1) & inside
    result[seam, :3] = (116, 200, 135) if deep else (83, 153, 98)
    result[core, :3] = CORE if deep else JADE
    return result


SEEDS = np.array(((196, 323), (294, 278), (415, 280), (535, 346),
                  (485, 463), (371, 496), (255, 462)), dtype=float)


def fragments(base, v):
    yy, xx = np.indices((CELL, CELL))
    wx = xx + 19*np.sin(yy*.031+v) + 7*np.sin(xx*.066+yy*.015)
    wy = yy + 15*np.sin(xx*.027-v) + 8*np.sin(yy*.079-xx*.012)
    ds = np.stack([((wx-sx)*.92)**2 + ((wy-sy)*1.08)**2 for sx, sy in SEEDS])
    labels = np.argmin(ds, axis=0)
    solid = base[..., 3] > 128
    seam = np.zeros_like(solid)
    seam[1:] |= (labels[1:] != labels[:-1]) & solid[1:] & solid[:-1]
    seam[:-1] |= (labels[1:] != labels[:-1]) & solid[1:] & solid[:-1]
    seam[:, 1:] |= (labels[:, 1:] != labels[:, :-1]) & solid[:, 1:] & solid[:, :-1]
    seam[:, :-1] |= (labels[:, 1:] != labels[:, :-1]) & solid[:, 1:] & solid[:, :-1]
    return [solid & (labels == i) for i in range(7)], seam


def paste_shift(dst, source, mask, dx, dy, lit=None, strength=1):
    x0, y0, x1, y1 = bounding(mask)
    dx = int(np.clip(dx, 4-x0, CELL-4-x1))
    dy = int(np.clip(dy, 4-y0, CELL-4-y1))
    sx0, sx1 = max(0, -dx), min(CELL, CELL-dx)
    sy0, sy1 = max(0, -dy), min(CELL, CELL-dy)
    tx0, tx1 = sx0+dx, sx1+dx
    ty0, ty1 = sy0+dy, sy1+dy
    block = source[sy0:sy1, sx0:sx1].copy()
    m = mask[sy0:sy1, sx0:sx1]
    if lit is not None:
        edge = lit[sy0:sy1, sx0:sx1] & m
        block[edge, :3] = (np.array(JADE)*strength).astype(np.uint8)
        core = edge & (((np.indices(edge.shape).sum(0)) % 5) == 0)
        block[core, :3] = (np.array(CORE)*strength).astype(np.uint8)
    target = dst[ty0:ty1, tx0:tx1]
    target[m] = block[m]


def chips(dst, source, seed, count, size, spread, fading=False):
    rng = np.random.default_rng(seed)
    interior = erosion(source[..., 3] > 245, 9)
    ys, xs = np.where(interior)
    for _ in range(count):
        pick = rng.integers(0, len(xs))
        cx, cy = int(xs[pick]), int(ys[pick])
        w = int(rng.integers(size//2, size+1))
        h = int(rng.integers(max(4,size//4), max(5,size//2)+1))
        x0, y0 = max(0,cx-w//2), max(0,cy-h//2)
        piece = source[y0:y0+h, x0:x0+w].copy()
        hh, ww = piece.shape[:2]
        py, px = np.indices((hh, ww))
        taper = np.abs(py-hh/2)/(hh/2+1) + .34*np.abs(px-ww/2)/(ww/2+1)
        piece[taper > 1, 3] = 0
        if fading:
            piece[..., 3] = (piece[..., 3].astype(np.float32)*rng.uniform(.45,.80)).astype(np.uint8)
        tx = int(np.clip(384+rng.integers(-spread,spread+1)-ww//2, 5, CELL-ww-5))
        ty = int(np.clip(384+rng.integers(-spread,spread+1)-hh//2, 5, CELL-hh-5))
        patch = dst[ty:ty+hh, tx:tx+ww]
        m = piece[..., 3] > 0
        patch[m] = piece[m]


def leaves_spores(dst, seed, nleaves, nspores, spread, fading=False):
    rng = np.random.default_rng(seed)
    im = Image.new("RGBA", (CELL,CELL))
    draw = ImageDraw.Draw(im)
    alpha = 150 if fading else 225
    for _ in range(nleaves):
        x = int(np.clip(384+rng.integers(-spread,spread+1), 8,CELL-8))
        y = int(np.clip(384+rng.integers(-spread,spread+1), 8,CELL-8))
        length = rng.integers(8,18)
        dx, dy = rng.uniform(-1,1), rng.uniform(-1,1)
        draw.polygon([(x-int(dx*length),y-int(dy*length)),
                      (x-int(dy*4),y+int(dx*4)),
                      (x+int(dx*length),y+int(dy*length)),
                      (x+int(dy*4),y-int(dx*4))], fill=(50,92,54,alpha))
    for _ in range(nspores):
        x = int(np.clip(384+rng.integers(-spread,spread+1), 6,CELL-6))
        y = int(np.clip(384+rng.integers(-spread,spread+1), 6,CELL-6))
        r = int(rng.integers(1,4))
        draw.ellipse((x-r,y-r,x+r,y+r), fill=(108,165,121,alpha//2))
    a = np.array(im)
    present = a[...,3] > 0
    dst[present] = a[present]


def break_frames(base, v):
    frames = [light(base, v)]
    parts, seams = fragments(base, v)
    for stage in (1,2,3):
        out = np.zeros_like(base)
        for i, part in enumerate(parts):
            vector = SEEDS[i]-np.array((384,384))
            vector /= max(1,np.linalg.norm(vector))
            distance = (4,43,82)[stage-1] + (i%3-1)*(2,8,10)[stage-1]
            dx, dy = np.rint(vector*distance).astype(int)
            if stage == 3:
                yy,xx = np.indices(part.shape)
                cut = xx+.45*yy+13*np.sin(yy*.037+i)
                mid = SEEDS[i,0]+.45*SEEDS[i,1]
                subparts = ((part & (cut < mid-3),-8,-7),
                            (part & (cut > mid+3),8,7))
            else:
                subparts = ((part,0,0),)
            for sub, ex, ey in subparts:
                if sub.any():
                    paste_shift(out,base,sub,dx+ex,dy+ey,
                                seams if stage>=1 else None, 1 if stage<=2 else .65)
        if stage == 1:
            out = light(out,v,True)
            chips(out,base,100+v,12,16,18)
        elif stage == 2:
            chips(out,base,200+v,37,22,135)
            leaves_spores(out,240+v,13,12,200)
        else:
            chips(out,base,300+v,72,27,190)
            leaves_spores(out,340+v,29,25,245)
        frames.append(out)
    last = np.zeros_like(base)
    chips(last,base,400+v,165,32,250,True)
    leaves_spores(last,440+v,60,48,270,True)
    frames.append(last)
    return frames


def main():
    DEST.mkdir(exist_ok=False)
    intact = Image.new("RGBA",(CELL*3,CELL))
    breaking = Image.new("RGBA",(CELL*5,CELL*3))
    for v,(name,width) in enumerate(zip(SOURCES,WIDTHS)):
        base = place_source(name,width)
        intact.paste(Image.fromarray(base),(v*CELL,0))
        for f,frame in enumerate(break_frames(base,v)):
            breaking.paste(Image.fromarray(frame),(f*CELL,v*CELL))
    intact.save(DEST/"boulder.png")
    breaking.save(DEST/"break.png")
    print(DEST)


if __name__ == "__main__":
    main()
