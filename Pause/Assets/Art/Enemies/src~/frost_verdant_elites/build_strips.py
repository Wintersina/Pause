#!/usr/bin/env python3
"""The three old 192 px elites rebuilt at 384 px from REAL painted frames (Codex, frames/<ship>_<frame>.png on flat #00FF40):
Cryo Siren, Rimebreaker (Frost) and the redesigned Resin Warden (Verdant). Ten square 384 px cells:

  0 parked (engines off)          painted "parked" frame
  1 parked idle (pilot lights)    the parked frame with each bell's mouth lit
  2 lift-off ignition             painted "liftoff" frame + stub flames
  3 4 5 6  flight cycle           painted master / flightB / master / flightB, each with its own drawn engine-burn (Tide-style plumes)
  7 bank left   8 bank right     painted bank frames, long burn
  9 damaged                       painted "damaged" frame, short ragged plumes

Every frame is keyed, then registered onto the master on ONE fixed anchor (scale + shift maximising the overlap of the silhouettes) so
the hull does not jitter between cells; nothing comes within the 6 px margin. A missing frame falls back (dimmed master, mirrored bank)
and the fallback is listed in the report. Flames are the Tide / Space recipe (space_elites_engine_burn: hard-stepped column, white core,
ragged tail, faint halo, no lips, discs or dither).

  python3 build_strips.py [--out <dir>] [--frames <dir>] [ship ...]     default out: Resources/Elites/<World>
"""
import sys, json, random
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ART = HERE.parents[2]                    # Pause/Assets/Art
sys.path.insert(0, str(HERE.parent / "space_elites_engine_burn"))
import build_strips as sp                # flame(), halo(), pilot_lights(), engine_cell(), find_openings(), erode(), _component()

CELL = 384
K = 2
SAFE = 8                                  # keep every pixel at least this far from the cell border (the tests ask for 6)
ENGINE_ROOM = 70                          # px kept under the hull for the burn
# fit box (w, h) of the master hull in the 384 cell, bell width hint, flame colours (outer, mid, core), number of engines
SHIPS = {
    "cryo_siren":   dict(key="frost_elite_cryo_siren",   world="Frost",   w=170, h=282, engines=2, min_rx=7, flame=((60, 150, 255), (140, 230, 255), (255, 255, 255))),
    "rimebreaker":  dict(key="frost_elite_rimebreaker",  world="Frost",   w=250, h=270, engines=2, flame=((50, 120, 255), (120, 215, 255), (255, 255, 255))),
    "resin_warden": dict(key="verdant_elite_resin_warden", world="Verdant", w=300, h=270, engines=2, flame=((255, 130, 30), (255, 205, 100), (255, 252, 235))),
}
LENGTHS = (30, 38, 44, 35)               # burn length per flight drawing (192-cell px)
WIDTHS = (1.0, .92, .84, .92)


def key_out(path):
    im = np.array(Image.open(path).convert("RGB")).astype(float)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    gn = g - np.maximum(r, b)
    a = np.clip(1 - (gn - 35) / 80, 0, 1)
    gs = np.where(a < 1, np.minimum(g, np.maximum(r, b)), g)
    return Image.fromarray(np.dstack([r, gs, b, a * 255]).astype(np.uint8), "RGBA")


def mask(im, size=256):
    a = np.asarray(im.getchannel("A").resize((size, size), Image.Resampling.BILINEAR)) > 128
    return a


def register(master, frame):
    """(scale, dx, dy) in canvas px so that frame scaled about the canvas centre then shifted overlays the master best (IoU of the silhouettes)."""
    W = master.size[0]
    sz = 256
    f = sz / W
    m = mask(master, sz)
    base = frame.getchannel("A").resize((sz, sz), Image.Resampling.BILINEAR)
    best = (-1, 1.0, 0, 0)
    for s in np.arange(.76, 1.271, .02):
        n = max(1, int(round(sz * s)))
        sc = base.resize((n, n), Image.Resampling.BILINEAR)
        canvas = Image.new("L", (sz, sz), 0)
        canvas.paste(sc, ((sz - n) // 2, (sz - n) // 2))
        a = np.asarray(canvas) > 128
        if not a.any():
            continue
        # coarse: align the centroids, then a +-6 search
        ys, xs = np.nonzero(a); ym, xm = np.nonzero(m)
        cx = int(round(xm.mean() - xs.mean())); cy = int(round(ym.mean() - ys.mean()))
        for dy in range(cy - 6, cy + 7):
            for dx in range(cx - 6, cx + 7):
                r = np.roll(np.roll(a, dy, 0), dx, 1)
                iou = (r & m).sum() / max(1, (r | m).sum())
                if iou > best[0]:
                    best = (iou, s, dx, dy)
    iou, s, dx, dy = best
    return s, dx / f, dy / f, iou


def placed(frame, reg, s_m, box, ox, oy):
    """The frame as a 384 cell: registered onto the master's canvas, then the master's crop box -> (ox, oy) at the master's fit scale."""
    s, dx, dy = reg
    W = frame.size[0]
    c = W / 2
    canvas = frame.transform((W, W), Image.Transform.AFFINE, (1 / s, 0, c - (c + dx) / s, 0, 1 / s, c - (c + dy) / s), Image.Resampling.BICUBIC)
    bx0, by0 = box[0], box[1]
    big = canvas.resize((round(W * s_m), round(W * s_m)), Image.Resampling.LANCZOS)
    cell = Image.new("RGBA", (CELL, CELL))
    cell.paste(big, (ox - round(bx0 * s_m), oy - round(by0 * s_m)))
    a = cell.getchannel("A").point(lambda v: 255 if v >= 128 else 0)   # crisp edge
    cell.putalpha(a)
    return cell


def mouths_of(hull_cell, n, hint=None):
    """The dark bell mouths: the n largest dark components in the lower part of the hull, left to right. Returns (cx, cy, rx, ry)."""
    arr = np.array(hull_cell).astype(int)
    lum = arr[..., 0] * .3 + arr[..., 1] * .59 + arr[..., 2] * .11
    alpha = arr[..., 3] > 200
    ys0, ys1 = np.nonzero(alpha)[0].min(), np.nonzero(alpha)[0].max()
    low = np.zeros_like(alpha); low[int(ys0 + (ys1 - ys0) * .6):, :] = True
    e = sp.erode((lum < 46) & alpha & low, 2)
    seen = np.zeros_like(e)
    comps = []
    ys, xs = np.nonzero(e)
    for y, x in zip(ys, xs):
        if seen[y, x]:
            continue
        c = sp._component(e, (y, x)); seen |= c.astype(seen.dtype)
        yy, xx = np.nonzero(c)
        comps.append((len(yy), xx.min() - 2, xx.max() + 2, yy.min() - 2, yy.max() + 2, xx.mean(), yy.mean()))
    comps.sort(reverse=True)
    out = [(mx, my, (x1 - x0) / 2, (y1 - y0) / 2) for _, x0, x1, y0, y1, mx, my in comps[:n]]
    out.sort()
    return out


def dim(im, f):
    a = np.asarray(im).astype(np.float32).copy()
    s = a[..., 3] > 0
    a[s, :3] *= f
    return Image.fromarray(np.clip(a, 0, 255).astype(np.uint8), "RGBA")


def shear(cell, sign, top, h):
    a = np.asarray(cell)
    yy, xx = np.indices((CELL, CELL))
    mid = top + h * .55
    shift = np.rint(sign * 4 * (mid - yy) / (h * .5)).astype(int)
    src = np.clip(np.rint(CELL / 2 + (xx - shift - CELL / 2) / .97).astype(int), 0, CELL - 1)
    return Image.fromarray(a[yy, src], "RGBA")


def build(name, out_dir, frames_dir):
    cfg = SHIPS[name]
    fr = {}
    for f in ("master", "parked", "liftoff", "flightB", "bankL", "bankR", "damaged"):
        p = frames_dir / f"{name}_{f}.png"
        fr[f] = key_out(p) if p.exists() else None
    master = fr["master"]
    for f in fr:                       # Codex returns 1024 or 1254 px canvases: put every frame on the master's canvas first
        if fr[f] is not None and fr[f].size != master.size:
            fr[f] = fr[f].resize(master.size, Image.Resampling.LANCZOS)
    box = master.getchannel("A").point(lambda v: 255 if v > 128 else 0).getbbox()
    mw, mh = box[2] - box[0], box[3] - box[1]
    s_m = min(cfg["w"] / mw, cfg["h"] / mh)
    w, h = round(mw * s_m), round(mh * s_m)
    ox, oy = (CELL - w) // 2, (CELL - h - ENGINE_ROOM + 20) // 2
    report = []
    cells = {}
    for f in ("master", "parked", "liftoff", "flightB", "bankL", "bankR", "damaged"):
        if fr[f] is None:
            continue
        reg = (1.0, 0, 0)
        if f != "master":
            *reg, iou = register(master, fr[f])
            report.append(f"{f}: registered scale {reg[0]:.2f} shift ({reg[1]:.0f},{reg[2]:.0f}) iou {iou:.3f}")
        cells[f] = placed(fr[f], reg, s_m, box, ox, oy)
        if f != "master":                 # one nose line: slide the cell vertically so its top meets the master's
            t0 = cells["master"].getchannel("A").getbbox()[1]; t1 = cells[f].getchannel("A").getbbox()[1]
            if abs(t1 - t0) > 1:
                sh = Image.new("RGBA", (CELL, CELL)); sh.paste(cells[f], (0, t0 - t1)); cells[f] = sh
                report.append(f"{f}: nose line moved {t0 - t1:+d} px")
    base = cells["master"]
    for f, alt in (("parked", None), ("liftoff", None), ("flightB", None), ("damaged", None)):
        if f not in cells:
            cells[f] = dim(base, .8 if f in ("parked", "damaged") else 1.0)
            report.append(f"{f}: FALLBACK (master{' dimmed' if f in ('parked', 'damaged') else ''})")
    if "bankL" not in cells:
        cells["bankL"] = shear(base, -1, oy, h); report.append("bankL: FALLBACK (sheared master)")
    if "bankR" not in cells:
        cells["bankR"] = cells["bankL"].transpose(Image.Transpose.FLIP_LEFT_RIGHT) if "bankL" in fr and fr["bankL"] is not None else shear(base, 1, oy, h)
        report.append("bankR: FALLBACK (mirrored bankL)" if fr["bankL"] is not None else "bankR: FALLBACK (sheared master)")

    n = cfg["engines"]
    mouths = {f: mouths_of(cells[f], n) for f in cells}
    mm = mouths["master"]
    for f in mouths:
        bad = len(mouths[f]) != n or any(abs(a[0] - b[0]) > 14 or abs(a[1] - b[1]) > 16 for a, b in zip(mouths[f], mm))
        if bad:
            mouths[f] = mm; report.append(f"{f}: bell search off the master's bells, used the master's mouths")
    for f in mouths:                   # a bell too small to carry a plume (Cryo Siren): floor the plume width
        mouths[f] = [(cx, cy, max(rx, cfg.get("min_rx", 0)), max(ry, 3)) for cx, cy, rx, ry in mouths[f]]
    cols = cfg["flame"]
    lowest = max(m[1] for f in mouths for m in mouths[f])                       # keep every plume (and its sparks) 14 px inside the cell
    fk = min(1.0, (CELL - 14 - lowest) / (max(LENGTHS) * K))
    LEN = [round(v * fk) for v in LENGTHS]
    report.append(f"flame length factor {fk:.2f} (lowest mouth y {lowest:.0f})")
    out = []
    out.append(cells["parked"])
    c1 = cells["parked"].copy(); c1.alpha_composite(sp.pilot_lights(mouths["parked"], cols[1], .85)); out.append(c1)
    out.append(sp.engine_cell(cells["liftoff"], mouths["liftoff"], cols, [round(12 * fk)] * n, [.75] * n, 1))
    for i in range(4):
        src = "master" if i % 2 == 0 else "flightB"
        gs = [(i + 2 * e) % 4 for e in range(n)]
        out.append(sp.engine_cell(cells[src], mouths[src], cols, [LEN[g] for g in gs], [WIDTHS[g] for g in gs], 10 + i))
    out.append(sp.engine_cell(cells["bankL"], mouths["bankL"], cols, [LEN[2]] * n, [.9] * n, 20))
    out.append(sp.engine_cell(cells["bankR"], mouths["bankR"], cols, [LEN[2]] * n, [.9] * n, 21))
    out.append(sp.engine_cell(cells["damaged"], mouths["damaged"], cols, [round((22 + 6 * (e % 2)) * fk) for e in range(n)], [.8] * n, 99, ragged=.5))
    strip = Image.new("RGBA", (CELL * len(out), CELL))
    for i, c in enumerate(out):
        strip.alpha_composite(c, (i * CELL, 0))
    out_dir.mkdir(parents=True, exist_ok=True)
    strip.save(out_dir / f"{cfg['key']}.png")
    info = dict(hull_w=w, hull_h=h, ox=ox, oy=oy, mouths=[tuple(round(v, 1) for v in m) for m in mm], s_m=round(s_m, 4))
    print(name, json.dumps(info))
    bi = frames_dir / "build_info.json"
    allinfo = json.loads(bi.read_text()) if bi.exists() else {}
    allinfo[name] = info
    if out_dir.resolve() == (ART / "Resources" / "Elites" / cfg["world"]).resolve():
        bi.write_text(json.dumps(allinfo, indent=1))
    for r in report:
        print("   ", r)
    return info


if __name__ == "__main__":
    args = sys.argv[1:]
    out = None
    frames = HERE / "frames"
    if "--out" in args:
        i = args.index("--out"); out = Path(args[i + 1]); del args[i:i + 2]
    if "--frames" in args:
        i = args.index("--frames"); frames = Path(args[i + 1]); del args[i:i + 2]
    for nme in (args or [n for n in SHIPS if (frames / f"{n}_master.png").exists()]):
        build(nme, out or ART / "Resources" / "Elites" / SHIPS[nme]["world"], frames)
