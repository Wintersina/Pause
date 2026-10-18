#!/usr/bin/env python3
"""Post-pass on docks.png (run after build_docks_atlas.py): no black box, no baked glow ball.

Codex's launch cells paint the closed door as a FLAT opaque dark rectangle (cells 0 idle, 1, 2, 5 closing) that spills past the
hull's silhouette on the carrier and plugs the open sky between the gantry's pillars, so after a launch the dock closes back
into a black box. And cells 3 / 4 carry a baked soft glow ball at the pad (the "flare" halo).

  1. cell 4 loses its glow ball (inpainted row by row from the pixels round it) -> the clean open interior; cell 3 (the skipped
     flare cell) becomes the same.
  2. in cells 0, 1, 2, 5 every flat door-coloured pixel is replaced with that clean interior darkened (DOOR_SHADE), keeping its
     alpha: a shadowed bay where there is hull behind it, nothing where the opening is sky. The doors' shrinking animation and
     the little light dots in cells 1 / 2 stay.

    python3 clean_dock_doors.py   (from Pause/Assets)
"""
import json, os
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
DIR = os.path.join(ASSETS, 'Art/Backgrounds/Resources/Worlds/Space/Backdrop')
PADS = {'gantry': (512, 593, 1024, 936), 'carrier': (577, 360, 1024, 512), 'gate': (511, 397, 1024, 950), 'derelict': (512, 516, 1024, 836)}
DOOR_SHADE, BLOB_R, TOL = .35, 20, 14

im = Image.open(os.path.join(DIR, 'docks.png')).convert('RGBA')
A = np.array(im).astype(np.float32)
man = json.load(open(os.path.join(DIR, 'docks.json')))
sp = {s['n']: s for s in man['sprites']}
H = A.shape[0]

def box(n):
    s = sp[n]                       # (manifest y is bottom-up)
    y0 = H - s['y'] - s['h']
    return y0, y0 + s['h'], s['x'], s['x'] + s['w']

def inpaint_ball(c, cx, cy):
    h, w, _ = c.shape
    yy, xx = np.mgrid[0:h, 0:w]
    d = np.hypot(xx - cx, yy - cy)
    ring = (d > BLOB_R) & (d < BLOB_R + 10)
    ref = np.median(c[ring], axis=0)
    inside = d <= BLOB_R
    mask = inside & (np.abs(c - ref).sum(axis=2) > 70)
    mask = inside & (mask | (np.hypot(xx - cx, yy - cy) < 11))    # the ball's core, always
    out = c.copy()
    if ref[3] < 10:                 # round it is open sky (the gantry): the ball just goes
        out[mask] = 0
        return out
    for y in range(h):
        xs = np.where(mask[y])[0]
        if len(xs) == 0: continue
        l, r = xs.min() - 1, xs.max() + 1
        l, r = max(l, 0), min(r, w - 1)
        for x in xs:
            t = (x - l) / max(1, r - l)
            out[y, x] = c[y, l] * (1 - t) + c[y, r] * t
    return out

for tag, (px, py, sw, sh) in PADS.items():
    y0, y1, x0, x1 = box(tag + '_04')
    w, h = x1 - x0, y1 - y0
    cx, cy = px / sw * w, py / sh * h
    clean = inpaint_ball(A[y0:y1, x0:x1], cx, cy)
    for n in (3, 4):
        a0, a1, b0, b1 = box('%s_%02d' % (tag, n))
        A[a0:a1, b0:b1] = clean
    door = A[box(tag + '_00')[0]:box(tag + '_00')[1], box(tag + '_00')[2]:box(tag + '_00')[3]]
    col = door[int(cy), int(cx)]
    for n in (0, 1, 2, 5):
        a0, a1, b0, b1 = box('%s_%02d' % (tag, n))
        c = A[a0:a1, b0:b1]
        box_ = c[int(cy) - 14:int(cy) + 15, int(cx) - 14:int(cx) + 15].reshape(-1, 4)
        box_ = box_[box_[:, 3] > 150]
        vals, counts = np.unique(box_[:, :3], axis=0, return_counts=True)
        col = np.append(vals[counts.argmax()], 255)           # this cell's own door colour (the commonest round the pad)
        flat = (c[..., 3] > 25) & (np.abs(c[..., :3] - col[:3]).max(axis=2) < TOL)
        shaded = clean.copy()
        shaded[..., :3] *= DOOR_SHADE
        c[flat] = shaded[flat]
    print(tag, 'door colour', col[:3])
Image.fromarray(np.clip(A, 0, 255).astype(np.uint8)).save(os.path.join(DIR, 'docks.png'))
