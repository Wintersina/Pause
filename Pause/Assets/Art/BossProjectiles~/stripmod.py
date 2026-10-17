"""Removes the dark keyline round a beam-kit cell and gives it a soft alpha falloff (no border)."""
import numpy as np

DARK = .34          # luma below this on the silhouette edge is the keyline
PEELS = 6           # outline thickness removed at most (px at 128 px cells)
RAMP = (.30, .60, .85)   # alpha of the outermost 3 pixel layers


def _luma(a):
    return (.299 * a[..., 0] + .587 * a[..., 1] + .114 * a[..., 2]) / 255.


def _edge(m, wrap_v):
    pad = np.pad(m, 1, constant_values=False)
    if wrap_v:   # tiling body: the top / bottom continue into the next tile
        pad[0, 1:-1] = m[0]; pad[-1, 1:-1] = m[-1]
    nb = pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:]
    return m & ~nb


def strip_cell(a, wrap_v=False):
    if wrap_v:   # a tiling body: strip three stacked copies so the seam is treated like any other row
        h = a.shape[0]
        return strip_cell(np.tile(a, (3, 1, 1)), False)[h:2 * h]
    a = a.copy()
    m = a[..., 3] > 20
    for _ in range(PEELS):
        e = _edge(m, wrap_v) & (_luma(a) < DARK)
        if not e.any():
            break
        a[e] = 0
        m &= ~e
    # dark flecks left near the silhouette behind a lighter outermost pixel (hairy keylines between spikes): within 3 px of clear space
    from PIL import Image, ImageFilter
    for _ in range(3):
        clear = ~m
        if wrap_v:
            pass
        near = np.array(Image.fromarray((clear * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(7))) > 0
        e = m & near & (_luma(a) < DARK)
        if not e.any():
            break
        a[e] = 0
        m &= ~e
    cur = m.copy()
    for k, f in enumerate(RAMP):
        e = _edge(cur, wrap_v)
        a[e, 3] *= f
        cur &= ~e
    a[a[..., 3] < 12] = 0
    return a


def strip_sheet(a, cw, ch, wrap_v=False):
    out = a.copy()
    for y in range(0, a.shape[0] - ch + 1, ch):
        for x in range(0, a.shape[1] - cw + 1, cw):
            out[y:y + ch, x:x + cw] = strip_cell(a[y:y + ch, x:x + cw], wrap_v)
    return out
