#!/usr/bin/env python3
"""Slices Codex's raw chroma-keyed sheets (Art/BossProjectiles~/<w>/<piece>_raw.png) into the game formats.
usage: assemble.py <world> [piece ...]   pieces: body flare impact tell shots lash (default: all that exist)
Outputs: Resources/Attacks/<World>/<w>_attack_laser{,body,tell}.png, Resources/Bosses/<World>_attackshots.png,
Resources/Attacks/Verdant/verdant_attack_lash.png.  Prints the measured constants (bodyOpaquePx, impactBasePx)."""
import sys, os, json
import numpy as np
from PIL import Image
from collections import deque

ROOT = '/Users/sina/Developer/Pause/.claude/worktrees/agent-a5b9f3f319d40b387/Pause/Assets/'
RAW = ROOT + 'Art/BossProjectiles~/'
RES = ROOT + 'Art/Resources/'

CFG = {
    'desert': dict(World='Desert', key=(255, 0, 255), hue=42, fy=.30, pinkfix=True),
    'storm':  dict(World='Storm',  key=(255, 0, 255), hue=262, fy=.5, pinkfix=True),
    'verdant': dict(World='Verdant', key=(255, 0, 255), hue=90, fy=.5, pinkfix=True),
    'ember':  dict(World='Ember',  key=(0, 255, 0), hue=30, fy=.5, pinkfix=False),
    'frost':  dict(World='Frost',  key=(255, 0, 255), hue=205, fy=.5, pinkfix=True),
    'space':  dict(World='Space',  key=(0, 255, 0), hue=195, fy=.5, pinkfix=False),
    'tide':   dict(World='Tide',   key=(255, 0, 255), hue=185, fy=.5, pinkfix=True, cutnozzle=True),
}


def rgb2hsv(a):
    a = a.astype(np.float32) / 255.
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(-1); mn = a.min(-1); d = mx - mn
    h = np.zeros_like(mx)
    m = d > 1e-6
    rc = (mx == r) & m; gc = (mx == g) & m & ~rc; bc = m & ~rc & ~gc
    h[rc] = ((g - b)[rc] / d[rc]) % 6
    h[gc] = (b - r)[gc] / d[gc] + 2
    h[bc] = (r - g)[bc] / d[bc] + 4
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    return h * 60., s, mx


def hsv2rgb(h, s, v):
    h = (h % 360.) / 60.
    c = v * s
    x = c * (1 - np.abs(h % 2 - 1))
    z = np.zeros_like(c)
    conds = [(h < 1), (h < 2), (h < 3), (h < 4), (h < 5), (h >= 5)]
    rs = [c, x, z, z, x, c]; gs = [x, c, c, x, z, z]; bs = [z, z, x, c, c, x]
    r = np.select(conds, rs); g = np.select(conds, gs); b = np.select(conds, bs)
    m = v - c
    return np.stack([r + m, g + m, b + m], -1) * 255.


def key_image(path, cfg):
    """RGBA float32 array (premultiplied later), key removed, key-spill hues pulled into the world's hue."""
    im = np.array(Image.open(path).convert('RGB')).astype(np.float32)
    key = np.array(cfg['key'], np.float32)
    d = np.sqrt(((im - key) ** 2).sum(-1))
    alpha = np.clip((d - 60.) / 70., 0, 1)
    h, s, v = rgb2hsv(im)
    if cfg['key'][0] == 255:   # magenta key: pinkish pixels are spill (not for Space, which keeps its pink)
        if cfg['pinkfix']:
            bad = (h >= 278) & (h < 356) & (s > .03) & (v > .2)
            h2 = np.where(bad, cfg['hue'], h)
            if cfg['hue'] == 42:   # sand: also keep it warm, not saturated
                s = np.where(bad, np.minimum(s, .55), s)
            im = np.where(bad[..., None], hsv2rgb(h2, s, v), im)
    else:                      # green key: green-ish spill
        bad = (h >= 70) & (h < 170) & (s > .2)
        h2 = np.where(bad, cfg['hue'], h)
        im = np.where(bad[..., None], hsv2rgb(h2, s, v), im)
    return np.dstack([im, alpha * 255.]).astype(np.float32)


def to_pil(a):
    return Image.fromarray(np.clip(a + .5, 0, 255).astype(np.uint8), 'RGBA')


def resize_rgba(a, w, h):
    im = to_pil(a).convert('RGBa').resize((w, h), Image.BOX)
    out = np.array(im.convert('RGBA')).astype(np.float32)
    out[..., 3] = np.where(out[..., 3] < 22, 0, out[..., 3])
    out[out[..., 3] == 0] = 0
    return out


def blank(w, h):
    return np.zeros((h, w, 4), np.float32)


def paste(dst, src, x, y):
    """alpha-over src (RGBA, straight) onto dst at (x,y), clipping."""
    h, w = src.shape[:2]
    x0, y0 = max(0, x), max(0, y)
    x1, y1 = min(dst.shape[1], x + w), min(dst.shape[0], y + h)
    if x1 <= x0 or y1 <= y0:
        return
    s = src[y0 - y:y1 - y, x0 - x:x1 - x]
    dst[y0:y1, x0:x1] = np.where(s[..., 3:4] > 0, s, dst[y0:y1, x0:x1])


def bbox(a, thr=40):
    ys, xs = np.where(a[..., 3] > thr)
    if len(xs) == 0:
        return None
    return xs.min(), xs.max() + 1, ys.min(), ys.max() + 1


def place_cell(region, anchor, scale, out=128):
    """scale region, put its anchor point (x,y in region px) at the output cell centre."""
    h, w = region.shape[:2]
    nw, nh = max(1, int(round(w * scale))), max(1, int(round(h * scale)))
    r = resize_rgba(region, nw, nh)
    cell = blank(out, out)
    paste(cell, r, int(round(out / 2 - anchor[0] * scale)), int(round(out / 2 - anchor[1] * scale)))
    return cell


def components_clean(a, minarea=18):
    """drop small stray specks (not the main component) from an RGBA array; works at 1/4 res."""
    m = a[::4, ::4, 3] > 40
    H, W = m.shape
    lab = np.zeros((H, W), np.int32)
    sizes = {}
    n = 0
    for sy in range(H):
        for sx in range(W):
            if m[sy, sx] and lab[sy, sx] == 0:
                n += 1
                q = deque([(sy, sx)]); lab[sy, sx] = n; c = 0
                while q:
                    y, x = q.popleft(); c += 1
                    for dy in (-1, 0, 1):
                        for dx in (-1, 0, 1):
                            yy, xx = y + dy, x + dx
                            if 0 <= yy < H and 0 <= xx < W and m[yy, xx] and lab[yy, xx] == 0:
                                lab[yy, xx] = n; q.append((yy, xx))
                sizes[n] = c
    big = max(sizes, key=sizes.get) if sizes else 0
    keep = np.zeros((H, W), bool)
    for k, c in sizes.items():
        if k == big or c >= minarea:
            keep |= lab == k
    # grow the kept mask a little so the real edge pixels stay
    kk = np.kron(keep, np.ones((4, 4), bool))[:a.shape[0], :a.shape[1]]
    from PIL import ImageFilter
    km = Image.fromarray((kk * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(9))
    kk = np.array(km) > 0
    out = a.copy()
    out[~kk] = 0
    return out


CUR = {}


def final_fix(a):
    """last pass on the finished pixels (resizing can blend a violet edge and a warm fill back into pink): no pink outside Space."""
    if CUR.get('World') == 'Space':
        return a
    h, s, v = rgb2hsv(a[..., :3])
    bad = (a[..., 3] > 20) & (h >= 276) & (h < 355) & (v > .2) & (s * v > .015)
    if not bad.any():
        return a
    h2 = np.where(bad, CUR['hue'], h)
    s2 = np.where(bad & (CUR['hue'] == 42), np.minimum(s, .55), s)
    out = a.copy()
    out[..., :3] = np.where(bad[..., None], hsv2rgb(h2, s2, v), a[..., :3])
    return out


def save(a, path):
    a = final_fix(a)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    to_pil(a).save(path)
    print('wrote', path.replace(ROOT, ''), a.shape[1], 'x', a.shape[0])


def do_body(cfg, w):
    p = RAW + w + '/body_raw.png'
    if not os.path.exists(p):
        return {}
    a = key_image(p, cfg)
    H, W = a.shape[:2]
    fw = W // 4
    out = blank(512, 256)
    lo, hi = 999, -1
    frames = []
    for f in range(4):
        fr = a[:, f * fw:(f + 1) * fw].copy()
        fr = components_clean(fr)
        # centre the body on its alpha-weighted column centroid
        col = fr[..., 3].sum(0)
        cx = (col * np.arange(fw)).sum() / max(col.sum(), 1)
        shift = int(round(fw / 2 - cx))
        fr = np.roll(fr, shift, axis=1)
        if shift > 0: fr[:, :shift] = 0
        elif shift < 0: fr[:, shift:] = 0
        # seamless vertical loop: cross-fade the last N rows into the first N (premultiplied)
        N = 72
        pm = fr.copy(); pm[..., :3] *= pm[..., 3:4] / 255.
        L = H - N
        loop = pm[:L].copy()
        wgt = ((np.arange(N) + .5) / N)[:, None, None]
        loop[:N] = wgt * pm[:N] + (1 - wgt) * pm[L:L + N]
        un = loop.copy()
        al = un[..., 3:4]
        un[..., :3] = np.where(al > 0, un[..., :3] * 255. / np.maximum(al, 1e-3), 0)
        fz = resize_rgba(un, 128, 256)
        # settle the loop point: the last three rows ease (premultiplied) from row 252 into row 0
        pmz = fz.copy(); pmz[..., :3] *= pmz[..., 3:4] / 255.
        for j, t in enumerate((.25, .5, .75)):
            row = (1 - t) * pmz[252] + t * pmz[0]
            a_ = row[:, 3:4]
            fz[253 + j] = np.concatenate([np.where(a_ > 0, row[:, :3] * 255. / np.maximum(a_, 1e-3), 0), a_], 1)
        frames.append(fz)
    for f, fr in enumerate(frames):
        out[:, f * 128:(f + 1) * 128] = fr
        cols = np.where(fr[..., 3].max(0) > 0)[0]
        lo, hi = min(lo, cols.min()), max(hi, cols.max())
    save(out, RES + 'Attacks/%s/%s_attack_laserbody.png' % (cfg['World'], w))
    return {'bodyOpaquePx': int(hi - lo + 1), 'bodyCols': [int(lo), int(hi)]}


def do_flare_impact(cfg, w):
    """laser sheet 1024 x 384: row0 windup 1-8, row1 muzzle a-d + fade 1-4, row2 impact 1-4 + spark a-d"""
    pf, pi = RAW + w + '/flare_raw.png', RAW + w + '/impact_raw.png'
    if not (os.path.exists(pf) and os.path.exists(pi)):
        return {}
    sheet = blank(1024, 384)
    fl = key_image(pf, cfg)
    H, W = fl.shape[:2]
    cw, ch = W // 4, H // 4
    scale = 128. / cw
    for i in range(16):
        r, c = divmod(i, 4)
        reg = fl[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw]
        bb = bbox(reg)
        ax = (bb[0] + bb[1]) / 2. if bb else cw / 2.
        ay = cfg['fy'] * ch
        if cfg.get('cutnozzle'):
            reg = cut_nozzle(reg)
            bb = bbox(reg)
            ay = bb[2] + 2 if bb else ay
        cell = place_cell(reg, (ax, ay), scale)
        # windup 0..7 -> row 0; muzzle 8..11 -> row 1 cols 0..3; fade 12..15 -> row 1 cols 4..7
        if i < 8: x, y = i * 128, 0
        elif i < 12: x, y = (i - 8) * 128, 128
        else: x, y = (4 + i - 12) * 128, 128
        sheet[y:y + 128, x:x + 128] = cell
    im = key_image(pi, cfg)
    H, W = im.shape[:2]
    cw, ch = W // 4, H // 2
    scale = 128. / cw
    base = 0
    for i in range(8):
        r, c = divmod(i, 4)
        reg = im[r * ch:(r + 1) * ch, c * cw:(c + 1) * cw]
        if r == 0:
            # base (lowest opaque row) lands near the bottom of the cell, ring centred
            bb = bbox(reg)
            ax = (bb[0] + bb[1]) / 2. if bb else cw / 2.
            ay = (bb[3] if bb else ch) - 47. / scale   # the lowest opaque row lands at y 111 of 128
            cell = place_cell(reg, (ax, ay), scale)
            # place_cell centres the anchor; shift so the anchor y (= bottom - .37 cell) sits at row 64
            sheet[256:384, c * 128:(c + 1) * 128] = cell
        else:
            bb = bbox(reg)
            ax = (bb[0] + bb[1]) / 2. if bb else cw / 2.
            ay = (bb[2] + bb[3]) / 2. if bb else ch / 2.
            cell = place_cell(reg, (ax, ay), scale)
            sheet[256:384, (4 + c) * 128:(5 + c) * 128] = cell
    low = bbox(sheet[256:384, 0:128])
    base = int(low[3] - 1 - 66) if low else 0
    save(sheet, RES + 'Attacks/%s/%s_attack_laser.png' % (cfg['World'], w))
    return {'impactBasePx': base}


def cut_nozzle(reg):
    """Tide: drop the brass nozzle and tentacles drawn in the flare cells (the boss has its own beak): everything above the nozzle tip."""
    h, hh, hv = rgb2hsv(reg[..., :3])
    brass = (h > 30) & (h < 60) & (hh > .45) & (hv > .35) & (reg[..., 3] > 100)
    teal_dark = (h > 140) & (h < 190) & (hv < .45) & (reg[..., 3] > 100)
    ys = np.where(brass.sum(1) >= 2)[0]
    out = reg.copy()
    if len(ys):
        tip = ys.max() + 1
        out[:tip] = 0
    return out


def do_tell(cfg, w):
    p = RAW + w + '/tell_raw.png'
    if not os.path.exists(p):
        return {}
    a = key_image(p, cfg)
    H, W = a.shape[:2]
    out = blank(512, 128)
    if (W, H) == (512, 128):
        save(a, RES + 'Attacks/%s/%s_attack_lasertell.png' % (cfg['World'], w))
        return {}
    cw = W // 4
    # sight cell: 4 dashes at a 32 px period (the old kit's dotted rhythm), taken from the sheet's own dash pitch
    for s in range(2):
        reg = a[:, s * cw:(s + 1) * cw]
        prof = (reg[..., 3] > 100)[:, :].any(1)
        runs, st = [], None
        for y, v in enumerate(prof):
            if v and st is None: st = y
            if not v and st is not None: runs.append(st); st = None
        # the sight column is the narrow one: restrict to its dashes (rows before the reticle's wide part)
        wid = (reg[..., 3] > 100).sum(1)
        wide = np.where(wid > 70)[0]
        limit = wide.min() if len(wide) else H
        starts = [r for r in runs if r < limit - 20]
        if len(starts) < 3:
            starts = runs
        pitch = float(np.mean(np.diff(starts[:6]))) if len(starts) > 1 else 78.
        y0 = starts[0]
        k = 4
        while k > 1 and y0 + k * pitch > H:
            k = 2 if k == 4 else 1
        dash_cols = np.where((reg[y0:y0 + int(pitch), :, 3] > 100).any(0))[0]
        cx = (dash_cols.min() + dash_cols.max()) / 2.
        sc = 32. / pitch
        win_w = int(round(128 / sc)); win_h = int(round(k * pitch))
        x0 = int(round(cx - win_w / 2))
        crop = blank(win_w, win_h)
        sx0 = max(0, x0); sx1 = min(cw, x0 + win_w)
        crop[:, sx0 - x0:sx1 - x0] = reg[y0:y0 + win_h, sx0:sx1]
        piece = resize_rgba(crop, 128, k * 32)
        cell = blank(128, 128)
        for t in range(128 // (k * 32)):
            cell[t * k * 32:(t + 1) * k * 32] = piece
        out[:, s * 128:(s + 1) * 128] = cell
    # lock reticles: the wide part at the bottom of cells 3, 4
    for s in range(2):
        reg = a[:, (2 + s) * cw:(3 + s) * cw]
        wid = (reg[..., 3] > 100).sum(1)
        rows = np.where(wid > .45 * wid.max())[0]
        y0, y1 = rows.min(), rows.max() + 1
        cols = np.where((reg[y0:y1, :, 3] > 100).any(0))[0]
        x0, x1 = cols.min(), cols.max() + 1
        side = max(x1 - x0, y1 - y0) * 1.25
        cxx, cyy = (x0 + x1) / 2., (y0 + y1) / 2.
        sc = 128. / side
        cell = place_cell(reg, (cxx, cyy), sc)
        out[:, (2 + s) * 128:(3 + s) * 128] = cell
    save(out, RES + 'Attacks/%s/%s_attack_lasertell.png' % (cfg['World'], w))
    return {}


def do_shots(cfg, w, rows_used=None):
    p = RAW + w + '/shots_raw.png'
    if not os.path.exists(p):
        return {}
    a = key_image(p, cfg)
    H, W = a.shape[:2]
    rows = {'desert': 3, 'storm': 3, 'verdant': 3, 'ember': 3, 'frost': 3, 'space': 4, 'tide': 3}[w]
    rh, cw = (H // 4 if w in ('ember', 'space') else H // rows), W // 2
    if w == 'ember':
        rh = H // 4
    out = blank(256, 128 * rows)
    for r in range(rows):
        m = int(rh * .04)
        regs = [a[r * rh + m:(r + 1) * rh - m, c * cw:(c + 1) * cw] for c in range(2)]
        bbs = [bbox(g, 60) for g in regs]
        x0 = min(b[0] for b in bbs); x1 = max(b[1] for b in bbs)
        y0 = min(b[2] for b in bbs); y1 = max(b[3] for b in bbs)
        bw, bh = x1 - x0, y1 - y0
        longest = max(bw, bh)
        target = 104 if max(bw, bh) / max(1, min(bw, bh)) > 1.5 else 90
        sc = target / longest
        for c in range(2):
            cell = place_cell(regs[c], ((x0 + x1) / 2., (y0 + y1) / 2.), sc)
            out[r * 128:(r + 1) * 128, c * 128:(c + 1) * 128] = cell
    save(out, RES + 'Bosses/%s_attackshots.png' % cfg['World'])
    return {}


def do_lash(cfg, w):
    p = RAW + w + '/lash_raw.png'
    if not os.path.exists(p):
        return {}
    a = key_image(p, cfg)
    assert a.shape[:2] == (128, 1024), a.shape
    a[a[..., 3] < 22] = 0
    # the two link cells tile end to end at a 104 px period (rows 12..115, AttackHazardArt.PaintedLinkFill): fit the painted vine to that band
    for c in (0, 1):
        cell = a[:, c * 128:(c + 1) * 128]
        bb = bbox(cell, 20)
        piece = resize_rgba(cell[bb[2]:bb[3]], 128, 104)
        new = blank(128, 128)
        new[12:116] = piece
        a[:, c * 128:(c + 1) * 128] = new
    save(a, RES + 'Attacks/Verdant/verdant_attack_lash.png')
    return {}


if __name__ == '__main__':
    w = sys.argv[1]
    cfg = CFG[w]
    CUR.update(cfg)
    pieces = sys.argv[2:] or ['body', 'flare', 'tell', 'shots', 'lash']
    res = {}
    if 'body' in pieces: res.update(do_body(cfg, w))
    if 'flare' in pieces: res.update(do_flare_impact(cfg, w))
    if 'tell' in pieces: res.update(do_tell(cfg, w))
    if 'shots' in pieces: res.update(do_shots(cfg, w))
    if 'lash' in pieces: res.update(do_lash(cfg, w))
    print(w, json.dumps(res))
