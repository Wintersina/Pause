import sys
import numpy as np
from PIL import Image
C = 384

def premul_resize(im, size):
    a = np.asarray(im.convert('RGBA')).astype(np.float32)
    al = a[:, :, 3:4] / 255.0
    pm = np.concatenate([a[:, :, :3] * al, a[:, :, 3:4]], axis=2)
    chans = [Image.fromarray(pm[:, :, i]).resize(size, Image.LANCZOS) for i in range(4)]
    r = np.stack([np.asarray(c) for c in chans], axis=2).astype(np.float32)
    al2 = np.clip(r[:, :, 3:4] / 255.0, 1e-4, 1)
    rgb = np.clip(r[:, :, :3] / al2, 0, 255)
    out = np.concatenate([rgb, r[:, :, 3:4]], axis=2)
    return Image.fromarray(np.clip(out, 0, 255).astype(np.uint8), 'RGBA')

def paste_at(canvas_size, im, dx, dy):
    out = Image.new('RGBA', canvas_size, (0, 0, 0, 0))
    out.paste(im, (dx, dy))
    return out

def iou(a, b):
    i = (a & b).sum(); u = (a | b).sum()
    return i / max(1, u)

def register(raw_path, clean_cell, search=True):
    raw = Image.open(raw_path).convert('RGBA')
    T = np.asarray(clean_cell)[:, :, 3] > 100
    tys, txs = np.nonzero(T)
    best = (-1, None)
    base = C / raw.width
    for f in np.linspace(base * .75, base * 1.35, 25):
        sz = (int(round(raw.width * f)), int(round(raw.height * f)))
        m = np.asarray(raw.resize(sz, Image.BILINEAR))[:, :, 3] > 100
        ys, xs = np.nonzero(m)
        if len(xs) == 0:
            continue
        # align bbox centres (the vapor puffs are part of both)
        dx0 = int(round((txs.min() + txs.max()) / 2 - (xs.min() + xs.max()) / 2))
        dy0 = int(round((tys.min() + tys.max()) / 2 - (ys.min() + ys.max()) / 2))
        for dx in range(dx0 - 8, dx0 + 9, 2):
            for dy in range(dy0 - 8, dy0 + 9, 2):
                canvas = np.zeros((C, C), bool)
                x0, y0 = max(0, dx), max(0, dy)
                x1, y1 = min(C, dx + sz[0]), min(C, dy + sz[1])
                if x1 <= x0 or y1 <= y0:
                    continue
                canvas[y0:y1, x0:x1] = m[y0 - dy:y1 - dy, x0 - dx:x1 - dx]
                s = iou(canvas, T)
                if s > best[0]:
                    best = (s, (f, sz, dx, dy))
    s, (f, sz, dx, dy) = best
    # refine +-1
    m_small = raw.resize(sz, Image.BILINEAR)
    m = np.asarray(m_small)[:, :, 3] > 100
    bb = (s, dx, dy)
    for ddx in range(-2, 3):
        for ddy in range(-2, 3):
            canvas = np.zeros((C, C), bool)
            x0, y0 = max(0, dx + ddx), max(0, dy + ddy)
            x1, y1 = min(C, dx + ddx + sz[0]), min(C, dy + ddy + sz[1])
            canvas[y0:y1, x0:x1] = m[y0 - dy - ddy:y1 - dy - ddy, x0 - dx - ddx:x1 - dx - ddx]
            sc = iou(canvas, T)
            if sc > bb[0]:
                bb = (sc, dx + ddx, dy + ddy)
    s, dx, dy = bb
    scaled = premul_resize(raw, sz)
    cell = paste_at((C, C), scaled, dx, dy)
    return cell, dict(iou=round(float(s), 3), scale=round(f, 4), dx=dx, dy=dy)

if __name__ == '__main__':
    raw_path, clean_path, out_path = sys.argv[1:4]
    clean = Image.open(clean_path).convert('RGBA')
    cell, info = register(raw_path, clean)
    print(info)
    cell.save(out_path)
