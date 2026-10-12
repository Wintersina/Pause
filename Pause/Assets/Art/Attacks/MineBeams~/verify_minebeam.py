"""Audit a <w>_attack_minebeam.png (768x768, same cell map as ember_attack_mineflame.png).

usage: python3 verify_minebeam.py <file.png> [forbidden_hue_deg ...]
Exit 1 on failure.
"""
import sys
from pathlib import Path
import numpy as np
from PIL import Image

path = Path(sys.argv[1])
forbid = [float(v) for v in sys.argv[2:]]
im = Image.open(path)
a = np.array(im.convert("RGBA"))
fails = []

def hsv(c):
    rgb = c[:, :, :3].astype(np.float32) / 255
    mx = rgb.max(2); mn = rgb.min(2); d = mx - mn
    h = np.zeros(mx.shape, np.float32)
    for ch, off in ((0, 0), (1, 2), (2, 4)):
        ix = (mx == rgb[:, :, ch]) & (d > 0)
        nxt = {0: (1, 2), 1: (2, 0), 2: (0, 1)}[ch]
        h[ix] = ((rgb[:, :, nxt[0]][ix] - rgb[:, :, nxt[1]][ix]) / d[ix] + off) % 6 if ch == 0 else (rgb[:, :, nxt[0]][ix] - rgb[:, :, nxt[1]][ix]) / d[ix] + off
    return (h * 60) % 360, np.where(mx > 0, d / np.maximum(mx, 1e-4), 0), mx

def gap(h, t): return np.abs((h - t + 180) % 360 - 180)

if im.size != (768, 768) or im.mode != "RGBA": fails.append("size/mode %s %s" % (im.size, im.mode))
n = np.array(im.resize((384, 384), Image.NEAREST).resize((768, 768), Image.NEAREST))
if not np.array_equal(a, n): fails.append("not a x2 nearest upscale of a 64 px original")

# (name, x, y, w, h)
cells = []
for i in range(6): cells.append(("beam%d" % i, i * 128, 0, 128, 384))
for i in range(4): cells.append(("pilot%d" % i, i * 128, 384, 128, 128))
for i in range(2): cells.append(("aim%d" % i, (4 + i) * 128, 384, 128, 128))
for i in range(4): cells.append(("burst%d" % i, i * 128, 512, 128, 128))
for i in range(2): cells.append(("impact%d" % i, (4 + i) * 128, 512, 128, 128))
cells.append(("impact2", 0, 640, 128, 128))
for i in range(2): cells.append(("haze%d" % i, (1 + i) * 128, 640, 128, 128))
reserved = [(3 * 128, 640, 384, 128)]
for x, y, w, h in reserved:
    if a[y:y + h, x:x + w, 3].max() > 0: fails.append("reserved cells not empty")
print("%-8s %6s %6s %6s %5s" % ("cell", "opaque", "width", "pink%", "edge"))
beam_w = 0; pinks = []
for name, x, y, w, h in cells:
    c = a[y:y + h, x:x + w]; op = c[:, :, 3] >= (8 if name.startswith('haze') else 128)
    if not op.any(): fails.append(name + " empty"); continue
    hh, ss, vv = hsv(c)
    pink = (((hh >= 300) & (hh <= 340) & (ss > .2)) | ((vv > .89) & (ss < .25))) & op
    ps = 100 * pink.sum() / op.sum()
    xs = np.where(op)[1]; width = xs.max() - xs.min() + 1
    isbeam = name.startswith("beam")
    clear = bool((c[:, :6, 3] <= 24).all() and (c[:, -6:, 3] <= 24).all())
    if not (name.startswith("aim") or name.startswith("haze")):
        clear = clear and bool((c[:6, :, 3] <= 24).all() and (c[-6:, :, 3] <= 24).all())
    if name.startswith("aim"):  # tiles along its length
        clear = bool((c[:, :6, 3] <= 24).all() and (c[:, -6:, 3] <= 24).all())
    if not clear: fails.append(name + " touches the 6 px border")
    if isbeam:
        beam_w = max(beam_w, width); pinks.append(ps)
        if ps < 3: fails.append(name + " pink cue %.1f%% < 3%%" % ps)
    print("%-8s %6d %6d %6.1f %5s" % (name, op.sum(), width, ps, clear))
if beam_w > 100: fails.append("beam wider than 100 px (%d)" % beam_w)
hh, ss, vv = hsv(a); opa = a[:, :, 3] >= 128
# the game's HostileGlow.IsPlayerRed: s > .35, v > .25, hue < 28 or > 332 (the pink cue must stay at 312-330)
red = 100 * ((((hh >= 332) | (hh < 28)) & (ss > .35) & (vv > .25)) & opa).sum() / max(1, opa.sum())
if red > 0: fails.append("player-red band %.2f%%" % red)
print("red band %.3f%%" % red)
for t in [178, 82, 259, 37] + forbid:
    sh = 100 * (((gap(hh, t) <= 15) & (ss > .65) & (vv > .35)) & opa).sum() / max(1, opa.sum())
    lim = 8.0
    print("saturated within 15deg of hue %g: %.2f%% (<%g)" % (t, sh, lim))
    if sh >= lim and (t in forbid or t in (178, 82, 259, 37)): fails.append("hue %g share %.1f%%" % (t, sh))
def diff(p, q):
    u = (p[:, :, 3] >= 128) | (q[:, :, 3] >= 128)
    return 100 * (np.any(p != q, axis=2) & u).sum() / max(1, u.sum())
def loop(label, coords, mn=3):
    cs = [a[y:y + h, x:x + w] for x, y, w, h in coords]
    d = [diff(cs[i], cs[(i + 1) % len(cs)]) for i in range(len(cs))]
    print("loop %s steps %s" % (label, ",".join("%.1f" % v for v in d)))
    if min(d) < mn: fails.append("loop " + label + " too static")
loop("beam", [(i * 128, 0, 128, 384) for i in range(6)], 8)
loop("pilot", [(i * 128, 384, 128, 128) for i in range(4)])
loop("burst", [(i * 128, 512, 128, 128) for i in range(4)])
loop("aim", [(4 * 128, 384, 128, 128), (5 * 128, 384, 128, 128)])
hz = a[640:768, 128:384, 3].astype(float) / 255
print("haze max alpha %.2f" % (hz.max()))
if hz.max() > .30: fails.append("haze too opaque")
print("RESULT", "PASS" if not fails else "FAIL " + "; ".join(fails))
sys.exit(bool(fails))
