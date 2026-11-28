"""Key one approved elite drawing into a 384 px hull cell (no flames), find its engine mouths and its glow mask.

The drawing is flipped nose-up (the elite convention), hard-keyed at alpha 128, its cut edges closed, its drawn flames cut off, then
scaled with NEAREST so the bare hull's longest edge equals the old strip's frame-0 longest edge, and its bbox centred where the old
frame 0's bbox was centred. Keying helpers are paragon_standard/build.py's (close_cut, hard, trim)."""
import colorsys, sys
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
import importlib.util
_s = importlib.util.spec_from_file_location("paragon_build", HERE.parent / "build.py")
P = importlib.util.module_from_spec(_s); _s.loader.exec_module(P)   # paragon_standard/build.py: close_cut, hard, trim, CUE
sys.path.insert(0, str(HERE))
from spec import SPEC                  # noqa: E402

CELL = 384
CUE = P.CUE
OLD = HERE / "old"


def hsv_arrays(a):
    rgb = a[..., :3].astype(np.float32) / 255
    mx, mn = rgb.max(-1), rgb.min(-1)
    v = mx; s = np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    d = np.maximum(mx - mn, 1e-6)
    h = np.where(mx == r, ((g - b) / d) % 6, np.where(mx == g, (b - r) / d + 2, (r - g) / d + 4)) * 60
    return h, s, v


def hue_gap(h, c):
    return np.abs((h - c + 180) % 360 - 180)


def flame_like(a):
    h, s, v = hsv_arrays(a)
    return (a[..., 3] > 128) & (((v > .38) & (s > .35)) | (v > .72))


def cut_flames(src, key):
    """-> (drawing without its drawn flames, [(cx, root_y, hw)] per engine in drawing px). The flame under each engine is the bright
    component that reaches the window's lowest opaque row; everything in the widened window from its root down goes."""
    a = np.array(src).copy()
    sp = SPEC[key]
    op = a[..., 3] > 128
    F = flame_like(a)
    H, W = op.shape
    roots = []
    for (x, hw) in sp["engines"]:
        x0, x1 = max(0, x - hw - 1), min(W, x + hw + 2)
        cols = op[:, x0:x1]
        ys = np.nonzero(cols.any(1))[0]
        bottom = ys.max()
        if not sp["flames"]:
            roots.append((x, bottom + 1, hw)); continue
        # the flame: the flame-like component (4-connected) holding the window's lowest opaque pixels; its top row is the root
        from scipy import ndimage
        win = np.zeros_like(op); win[:, x0:x1] = True
        lab, n = ndimage.label(F & win)
        bot = [lab[bottom, xx] for xx in range(x0, x1) if lab[bottom, xx] > 0]
        if "root" in sp:
            root = sp["root"]
            wx0, wx1 = max(0, x - hw - 4), min(W, x + hw + 5)
            a[root:, wx0:wx1] = 0
            roots.append((x, root, hw)); continue
        if not bot:
            roots.append((x, bottom + 1, hw)); continue
        comp = np.isin(lab, bot)
        ys_c = np.nonzero(comp.any(1))[0]
        root = int(ys_c.min())
        wx0, wx1 = max(0, x - hw - 4), min(W, x + hw + 5)
        a[root:, wx0:wx1] = 0
        roots.append((x, root, hw))
    if sp["flames"]:                                     # stray spark pixels left below the hull: drop small detached specks
        op2 = a[..., 3] > 128
        from scipy import ndimage
        lab, n = ndimage.label(op2)
        sizes = ndimage.sum(op2, lab, range(1, n + 1))
        big = int(np.argmax(sizes)) + 1
        for i in range(1, n + 1):
            if i != big and sizes[i - 1] < 40:
                a[lab == i] = 0
    return Image.fromarray(a, "RGBA"), roots


def depink(im, lo=275, hi=356, to=266):
    """Turn the drawing's pink-violet glow (hue lo..hi: the band FrameElitesDamagedTest / NoPink count as pink, 280-346 deg, with a
    margin) to violet (hue `to`), keeping saturation and value: pink is Space's cue only."""
    a = np.array(im)
    hh, s, v = hsv_arrays(a)
    m = (a[..., 3] > 0) & (s > .2) & (hh >= lo) & (hh <= hi)
    rgb = a[..., :3].astype(np.float32) / 255
    mx = rgb.max(-1); mn = rgb.min(-1)
    # rebuild the pixel at hue `to` with the same max / min (same S and V)
    t = to / 60.0; sec = int(t); fr = t - sec
    x = mn + (mx - mn) * (1 - fr) if sec % 2 else mn + (mx - mn) * fr
    chans = {0: (mx, x, mn), 1: (x, mx, mn), 2: (mn, mx, x), 3: (mn, x, mx), 4: (x, mn, mx), 5: (mx, mn, x)}[sec]
    new = np.stack(chans, -1)
    a[..., :3] = np.where(m[..., None], np.round(new * 255).astype(np.uint8), a[..., :3])
    return Image.fromarray(a, "RGBA")


def old_frame0(key):
    w = key.split("_")[0].capitalize()
    im = Image.open(OLD / f"{key}.png").convert("RGBA")
    return im.crop((0, 0, im.height, im.height))


def bbox(im):
    a = np.asarray(im)[..., 3] > 128
    ys, xs = np.nonzero(a)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


def place(key, src_path):
    src0 = Image.open(src_path).convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM)
    src = P.hard(P.close_cut(src0))
    if SPEC[key].get("depink"): src = depink(src)
    pad = (src.width - src0.width) // 2
    sp = dict(SPEC[key]); sp["engines"] = [(x + pad, hw) for x, hw in sp["engines"]]
    if "root" in sp: sp["root"] = sp["root"] + pad
    SPEC[key + "#pad"] = sp
    bare, roots = cut_flames(src, key + "#pad")
    if SPEC[key].get("cut"):
        ba = np.array(bare)
        for x0, y0, x1, y1 in SPEC[key]["cut"]:
            ba[y0 + pad:y1 + pad, x0 + pad:x1 + pad] = 0
        bare = Image.fromarray(ba, "RGBA")
    old0 = old_frame0(key)
    ox0, oy0, ox1, oy1 = bbox(old0)
    x0, y0, x1, y1 = bbox(bare)
    f = max(ox1 - ox0, oy1 - oy0) / max(x1 - x0, y1 - y0) * SPEC[key].get("edge", 1.0)
    big = bare.resize((max(1, round(bare.width * f)), max(1, round(bare.height * f))), Image.NEAREST)
    ox = round((ox0 + ox1) / 2 - (x0 + x1) / 2 * f); oy = round((oy0 + oy1) / 2 - (y0 + y1) / 2 * f)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.paste(big, (ox, oy), big)
    cell = P.hard(cell)

    def to_cell(x, y):                                   # unpadded drawing px (pixel centre) -> cell px
        return ox + (x + pad + .5) * f, oy + (y + pad + .5) * f

    mouths = []
    for (x, root, hw) in roots:                          # padded coords from cut_flames
        cx = ox + (x + .5) * f
        cy = oy + root * f                               # the bell's lower edge
        rx = max(4.0, hw * f * .8)
        mouths.append((round(cx, 1), round(cy - 3, 1), round(rx, 1), round(max(3.0, rx * .4), 1)))
    return dict(hull=cell, f=f, ox=ox, oy=oy, pad=pad, mouths=mouths, to_cell=to_cell, src=src, bare=bare, old0=old0)


def glow_mask(cell, world, key=None):
    """Bright, saturated pixels in the world's cue hue (+-30 deg) on the hull: the parts the lights overlay sits on."""
    a = np.asarray(cell)
    h, s, v = hsv_arrays(a)
    sp = SPEC.get(key, {}) if key else {}
    if "glow" in sp:
        lo, hi, smin, vmin = sp["glow"]
        band = ((h - lo) % 360) <= ((hi - lo) % 360)
    else:
        band = hue_gap(h, CUE[world]) <= 30
        smin, vmin = .45, .6
    return (a[..., 3] > 128) & band & (s > smin) & (v > vmin)
