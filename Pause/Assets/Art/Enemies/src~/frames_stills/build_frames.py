"""Register the Codex frames (out/<sprite>_c<k>.png) into the enemy sheets.

  python3 build_frames.py [--src DIR]   # --src: read frames from DIR instead of out/ (dry runs)
                                          [--dry]: write to /tmp-style preview sheets, not the project

For every finished job the frame is: keyed to alpha if the model returned an opaque
background, fit onto the sprite's cell canvas, registered to cell 0 on one fixed anchor
(mask-overlap search over small shifts and scales), pulled in so it is >= 6 px clear of the
cell border, and pasted into idle cell k. Cell 0 (the design cell the death strips are built
from), the tell and the hit cells are never touched. frost_big cell 2 is fixed in code
(it touched the cell border).
"""
import os, sys
import numpy as np
from PIL import Image
from collections import deque

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import jobs

MARGIN = 6


def key_background(im):
    """If the image has no real transparency, flood the border colour out to alpha."""
    a = np.array(im.convert("RGBA"))
    if (a[..., 3] < 250).mean() > 0.02:
        return Image.fromarray(a)
    h, w = a.shape[:2]
    corner = np.median(np.concatenate([a[:4, :4].reshape(-1, 4), a[:4, -4:].reshape(-1, 4),
                                       a[-4:, :4].reshape(-1, 4), a[-4:, -4:].reshape(-1, 4)]), 0)[:3]
    d = np.abs(a[..., :3].astype(int) - corner).max(-1) < 28
    seen = np.zeros((h, w), bool)
    dq = deque([(0, 0), (0, w - 1), (h - 1, 0), (h - 1, w - 1)])
    for y in range(0, h, 4):
        dq.append((y, 0)); dq.append((y, w - 1))
    for x in range(0, w, 4):
        dq.append((0, x)); dq.append((h - 1, x))
    while dq:
        y, x = dq.popleft()
        if y < 0 or x < 0 or y >= h or x >= w or seen[y, x] or not d[y, x]:
            continue
        seen[y, x] = True
        dq.extend(((y + 1, x), (y - 1, x), (y, x + 1), (y, x - 1)))
    a[seen, 3] = 0
    return Image.fromarray(a)


def to_cell(im, cw):
    """Fit onto a square cw x cw canvas (premultiplied resize so edges stay clean)."""
    im = im.convert("RGBA")
    w, h = im.size
    s = max(w, h)
    sq = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    sq.paste(im, ((s - w) // 2, (s - h) // 2))
    arr = np.array(sq).astype(np.float32)
    al = arr[..., 3:4] / 255.0
    arr[..., :3] *= al
    pm = Image.fromarray(arr.astype(np.uint8), "RGBA").resize((cw, cw), Image.LANCZOS)
    p = np.array(pm).astype(np.float32)
    al2 = p[..., 3:4] / 255.0
    p[..., :3] = np.where(al2 > 0.02, p[..., :3] / np.maximum(al2, 0.02), 0)
    return Image.fromarray(np.clip(p, 0, 255).astype(np.uint8), "RGBA")


def transform(im, s, dx, dy):
    cw = im.size[0]
    ns = max(1, round(cw * s))
    r = im.resize((ns, ns), Image.LANCZOS)
    big = Image.new("RGBA", (cw * 3, cw * 3), (0, 0, 0, 0))
    big.alpha_composite(r, (cw + (cw - ns) // 2 + dx, cw + (cw - ns) // 2 + dy))
    return big.crop((cw, cw, 2 * cw, 2 * cw))


def mask(im):
    return np.array(im)[..., 3] > 40


def register(frame, ref):
    """Search scale + shift so the frame's silhouette overlaps the reference cell best."""
    cw = frame.size[0]
    rm = mask(ref)
    best = (-1, 1.0, 0, 0)
    for s in (0.94, 0.97, 1.0, 1.03, 1.06):
        t0 = transform(frame, s, 0, 0)
        m0 = mask(t0)
        for dy in range(-14, 15, 2):
            for dx in range(-14, 15, 2):
                m = np.roll(np.roll(m0, dy, 0), dx, 1)
                iou = (m & rm).sum() / max(1, (m | rm).sum())
                if iou > best[0]:
                    best = (iou, s, dx, dy)
    iou, s, dx, dy = best
    # refine to single pixels
    for ddy in (-1, 0, 1):
        for ddx in (-1, 0, 1):
            t = transform(frame, s, dx + ddx, dy + ddy)
            m = mask(t)
            v = (m & rm).sum() / max(1, (m | rm).sum())
            if v > iou:
                iou, best = v, (v, s, dx + ddx, dy + ddy)
    iou, s, dx, dy = best
    return transform(frame, s, dx, dy), (round(float(iou), 3), s, dx, dy)


def enforce_margin(im, margin=MARGIN):
    cw = im.size[0]
    for _ in range(12):
        ys, xs = np.where(mask(im))
        m = min(xs.min(), ys.min(), cw - 1 - xs.max(), cw - 1 - ys.max())
        if m >= margin:
            return im
        im = transform(im, 0.97, 0, 0)
    return im


def sheet_path(sprite):
    return jobs.RES + sprite + ".png"


def main():
    src = HERE + "/out"
    dry = "--dry" in sys.argv
    if "--src" in sys.argv:
        src = sys.argv[sys.argv.index("--src") + 1]
    outdir = HERE + "/preview" if dry else None
    if dry:
        os.makedirs(outdir, exist_ok=True)
    sheets = {}
    log = []
    for sprite, cell, _, _ in jobs.JOBS:
        f = f"{src}/{sprite}_c{cell}.png"
        if not os.path.exists(f):
            continue
        if sprite not in sheets:
            sheets[sprite] = Image.open(sheet_path(sprite)).convert("RGBA")
        sh = sheets[sprite]
        cw = sh.size[1]
        ref = sh.crop((0, 0, cw, cw))
        fr = to_cell(key_background(Image.open(f)), cw)
        fr, info = register(fr, ref)
        fr = enforce_margin(fr)
        sh.paste(Image.new("RGBA", (cw, cw), (0, 0, 0, 0)), (cell * cw, 0))
        sh.alpha_composite(fr, (cell * cw, 0))
        log.append((sprite, cell, info))
    # frost_big cell 2 touched the cell border: shrink slightly and lift it into the safe area
    if "--no-big" not in sys.argv:
        s = "frost_big"
        sh = sheets.get(s) or Image.open(sheet_path(s)).convert("RGBA")
        cw = sh.size[1]
        c = sh.crop((2 * cw, 0, 3 * cw, cw))
        ys, xs = np.where(mask(c))
        if min(xs.min(), ys.min(), cw - 1 - xs.max(), cw - 1 - ys.max()) < MARGIN:
            t = transform(c, 0.97, 0, -3)
            t = enforce_margin(t)
            sh.paste(Image.new("RGBA", (cw, cw), (0, 0, 0, 0)), (2 * cw, 0))
            sh.alpha_composite(t, (2 * cw, 0))
            sheets[s] = sh
            log.append((s, 2, "code shrink/lift"))
    for sprite, sh in sheets.items():
        sh.save((outdir + "/" + sprite + ".png") if dry else sheet_path(sprite))
    for l in log:
        print(l)


if __name__ == "__main__":
    main()
