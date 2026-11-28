"""Tide r1 props atlas (2026-11-28, user-approved kinds): Resources/Worlds/Tide/Backdrop3/props.png + props.json.

  python3 build_atlas.py     inputs: ./src/out2/bd_tide_prop_<kind>/(r1|r2)_<variant>.png (8 / 16 frame strips of square 256 cells), ./src/props_meta.json

One atlas for all four ground sets (Tide's layouts differ only in the ground; the night set is graded by the director's tint).
Each strip is <kind>_<variant>_<frame>, every cell resized to its drawn px on a 1080 px screen at the .49 altitude, cut to the kind's union
box, the cell's centre the sprite pivot (py measured from the bottom like landmarks.json). Whales and mantas are the r2 strips (16 frames:
surface / spout / dive; wing-beat glide). Identical cells share one rect. Pink guard: WorldCue.IsPink (hue 285-352, s > .25, v > .3) -> grey.
"""
import os, re, json, glob, numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
D = os.path.normpath(os.path.join(HERE, '../../../Backgrounds/Resources/Worlds/Tide/Backdrop3'))
OUT = f'{HERE}/src/out2'
META = json.load(open(f'{HERE}/src/props_meta.json'))
# kind: (drawn px at the .49 altitude on a 1080 px screen, strip tag)  -- TidePropsTuning.Kinds mirrors this table
KINDS = dict(lighthouse=(160, 'r1'), platform=(170, 'r1'), village=(200, 'r1'), freighter=(260, 'r1'), wreck=(180, 'r1'), atoll=(270, 'r1'),
             buoys=(180, 'r1'), farm=(210, 'r1'), dock=(220, 'r1'), whales=(240, 'r2'), mantas=(230, 'r2'))
PAD = 2
def is_pink(rgb):
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]; mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn; h = np.zeros_like(mx); m = d > 1e-6
    rr = m & (mx == r); gg = m & (mx == g) & ~rr; bb = m & ~rr & ~gg
    h[rr] = (60 * ((g - b)[rr] / d[rr])) % 360; h[gg] = 60 * ((b - r)[gg] / d[gg]) + 120; h[bb] = 60 * ((r - g)[bb] / d[bb]) + 240
    s = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0); return (h >= 285) & (h <= 352) & (s > .25) & (mx > .3)
def resize_premul(a, w, h):
    pm = a.copy(); pm[..., :3] *= pm[..., 3:4]; out = np.zeros((h, w, 4))
    for c in range(4): out[..., c] = np.asarray(Image.fromarray(pm[..., c].astype(np.float32), 'F').resize((w, h), Image.LANCZOS))
    out = np.clip(out, 0, 1); al = out[..., 3:4]; out[..., :3] = np.where(al > 1e-4, out[..., :3] / np.maximum(al, 1e-4), 0); return np.clip(out, 0, 1)
def strips(kind, tag):
    fs = sorted(glob.glob(f'{OUT}/bd_tide_prop_{kind}/{tag}_*.png'), key=lambda p: int(re.findall(rf'{tag}_(\d+)', p)[0])); out = []
    for p in fs:
        a = np.asarray(Image.open(p).convert('RGBA')).astype(float) / 255; N = a.shape[0]; out.append([a[:, i * N:(i + 1) * N] for i in range(a.shape[1] // N)])
    return out
def main():
    uniq = {}; names = []; frame_ms = {}
    for kind, (px, tag) in KINDS.items():
        S = strips(kind, tag); scaled = [[resize_premul(f, px, px) for f in fr] for fr in S]
        al = np.zeros((px, px), bool)
        for fr in scaled:
            for f in fr: al |= f[..., 3] > 2 / 255
        ys, xs = np.nonzero(al); x0, x1 = max(0, xs.min() - 1), min(px, xs.max() + 2); y0, y1 = max(0, ys.min() - 1), min(px, ys.max() + 2)
        pivot = ((px / 2 - x0) / (x1 - x0), (y1 - px / 2) / (y1 - y0))
        frame_ms[kind] = META[f'{kind}_{tag}_0']['ms'][0]
        for vi, fr in enumerate(scaled):
            for fi, f in enumerate(fr):
                f = f[y0:y1, x0:x1].copy(); pk = is_pink(f[..., :3]) & (f[..., 3] > 0)
                if pk.any(): g = f[..., :3].mean(-1, keepdims=True); f[..., :3] = np.where(pk[..., None], g, f[..., :3])
                key = f.tobytes(); uniq.setdefault(key, f); names.append((f'{kind}_{vi}_{fi:02d}', key, pivot))
    items = sorted(uniq.items(), key=lambda t: -t[1].shape[0])
    for width in (2048, 4096):
        x = y = PAD; row = 0; place = []
        for key, f in items:
            h, w = f.shape[:2]
            if x + w + PAD > width: x = PAD; y += row + PAD; row = 0
            place.append((key, x, y)); x += w + PAD; row = max(row, h)
        H = (y + row + PAD + 3) // 4 * 4
        if H <= 4096: break
    at = {key: (x, y) for key, x, y in place}; sheet = np.zeros((H, width, 4))
    for key, f in items:
        x, y = at[key]; h, w = f.shape[:2]; sheet[y:y + h, x:x + w] = f
    sprites = []
    for n, key, pv in names:
        x, y = at[key]; h, w = uniq[key].shape[:2]; sprites.append(dict(n=n, x=x, y=H - y - h, w=w, h=h, px=round(pv[0], 5), py=round(pv[1], 5)))
    q = (sheet * 255 + .5).astype(np.uint8)
    for _ in range(4):    # 8-bit rounding can tip a faint edge texel back into the pink band: scrub on the stored values
        pk = is_pink(q[..., :3].astype(float) / 255) & (q[..., 3] > 0)
        if not pk.any(): break
        g = q[..., :3].astype(float).mean(-1, keepdims=True); q[..., :3] = np.where(pk[..., None], g.astype(np.uint8), q[..., :3])
    pink = int((is_pink(q[..., :3].astype(float) / 255) & (q[..., 3] > 0)).sum())
    Image.fromarray(q, 'RGBA').save(f'{D}/props.png', optimize=True)
    json.dump(dict(sheetW=width, sheetH=H, sprites=sprites, frameMs=frame_ms), open(f'{D}/props.json', 'w'), indent=0)
    print(f'props: {len(sprites)} sprites, {len(uniq)} unique cells, {width} x {H}, ASTC 4x4 {width * H / 1048576:.2f} MB, pink {pink}, frameMs {frame_ms}')
if __name__ == '__main__': main()
