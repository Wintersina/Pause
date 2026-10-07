"""Shared helpers for the strip cell tools (audit_cells.py, recell.py).

A flipbook strip is N square cells butted left to right (EnemyArt slices at
width / FrameCount). Nothing here needs more than Pillow + numpy.
"""
import numpy as np
from PIL import Image, ImageDraw

FRAMES = 7          # EnemyRoster.FrameCount: 0-3 idle, 4-5 tell, 6 hit
SOLID = 128         # the alpha the roster tests call "drawn" (a > 128)
FAINT = 16          # anything above this is visible art, not matte residue


def load_rgba(path):
    return np.array(Image.open(path).convert("RGBA"))


def label(mask):
    """8-connected components of a bool mask.

    Returns (labels int32 array, 0 = background, and a list of dicts with
    id / area / bbox (x0, y0, x1, y1 inclusive)). Two-pass run labelling with
    union-find, so a 2000 px sheet takes well under a second.
    """
    h, w = mask.shape
    labels = np.zeros((h, w), np.int32)
    parent = [0]

    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]
            a = parent[a]
        return a

    prev = []                                   # runs on the row above: (x0, x1, id)
    all_runs = []
    for y in range(h):
        row = mask[y]
        if not row.any():
            prev = []
            continue
        d = np.diff(np.concatenate(([0], row.view(np.int8), [0])))
        starts, ends = np.where(d == 1)[0], np.where(d == -1)[0] - 1
        cur = []
        for x0, x1 in zip(starts.tolist(), ends.tolist()):
            lid = 0
            for px0, px1, pid in prev:
                if px1 < x0 - 1:
                    continue
                if px0 > x1 + 1:
                    break
                r = find(pid)
                if lid == 0:
                    lid = r
                elif r != lid:
                    parent[r] = lid
            if lid == 0:
                lid = len(parent)
                parent.append(lid)
            cur.append((x0, x1, lid))
            all_runs.append((y, x0, x1, lid))
        prev = cur

    remap, comps = {}, []
    for y, x0, x1, lid in all_runs:
        r = find(lid)
        i = remap.get(r)
        if i is None:
            i = len(comps) + 1
            remap[r] = i
            comps.append({"id": i, "area": 0, "bbox": [x0, y, x1, y]})
        c = comps[i - 1]
        labels[y, x0:x1 + 1] = i
        c["area"] += x1 - x0 + 1
        b = c["bbox"]
        if x0 < b[0]: b[0] = x0
        if y < b[1]: b[1] = y
        if x1 > b[2]: b[2] = x1
        if y > b[3]: b[3] = y
    return labels, comps


def bbox_of(mask):
    """(x0, y0, x1, y1) inclusive, or None when the mask is empty."""
    ys, xs = np.where(mask)
    if xs.size == 0:
        return None
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def checker(w, h, a=(120, 120, 120), b=(150, 150, 150), step=16):
    im = Image.new("RGBA", (w, h), a + (255,))
    d = ImageDraw.Draw(im)
    for y in range(0, h, step):
        for x in range(0, w, step):
            if (x // step + y // step) % 2:
                d.rectangle([x, y, x + step - 1, y + step - 1], fill=b + (255,))
    return im


def contact_sheet(strip, dst, frames=FRAMES, scale=2):
    """The strip on a checker, upscaled, every cell outlined."""
    im = strip if isinstance(strip, Image.Image) else Image.open(strip)
    im = im.convert("RGBA")
    w, h = im.size
    bg = checker(w, h)
    bg.alpha_composite(im)
    bg = bg.resize((w * scale, h * scale), Image.NEAREST)
    d = ImageDraw.Draw(bg)
    cw = w / frames
    for i in range(frames + 1):
        x = min(int(round(i * cw * scale)), w * scale - 1)
        d.line([x, 0, x, h * scale - 1], fill=(255, 0, 255, 255))
    d.rectangle([0, 0, w * scale - 1, h * scale - 1], outline=(255, 0, 255, 255))
    bg.convert("RGB").save(dst)
