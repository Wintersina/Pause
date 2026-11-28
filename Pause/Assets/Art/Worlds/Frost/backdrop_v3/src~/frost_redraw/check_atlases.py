#!/usr/bin/env python3
"""The backdrop tests' rules for the Frost atlases, ported to Python (no Unity).

    python3 check_atlases.py [<Resources/Worlds/Frost/Backdrop3>]

Ported (each from Pause/Assets/Editor/Tests/):
  WorldBackdropTest        every frame name the director relies on (FrostAtlases) exists; frame sequences have no gap
                           (BackdropAtlas.Frames stops at the first one) and keep their counts; atlas brightness as drawn
                           (FrostAtlasAsDrawn: landmarks / sites at the ground lift 1.95, aurora .3 share, smoke / steam the
                           plume share .6, the weather at the ceiling's lift, thickening x7 and saturation x.7): luminance
                           x min(1, max alpha x the loops' draw alpha) <= .66 (weather .80)
  BackdropResolutionTest   every 256 cell of the 1024 sheets / every whole other sheet: block score < .97, pairs <= .85
  BackdropLoopGuardTest    smoke / steam / fire_lights / beacons frames: art >= 2 px from the top, left and right of the
                           cell (6 px reported); no detached flat sliver
  BackdropCellBleedTest    landmarks / sites: no detached thin sliver near a cell edge; the causeway span's ends inside
                           its drawing (FrostTuning.Span ends, read from FrostBackdrop.cs)
  FrostDockTest            each launch strip (rig, silo, cave, icebreaker): 6 equal cells, launch order (idle vs hold > 500
                           px, idle vs closing < idle vs hold), the pad point (FrostTuning.SitePads) inside and opaque with
                           its 9 x 9 neighbourhood (>= 90% alpha >= 200) in all six cells, no cell brighter than idle + .02
  FrostBackdropTest        blizzard art max alpha x BlizzardMaxAlpha (read) <= .30 (the gust); loops animate (consecutive
                           frames differ, last to first too); the ceiling (cloud_bank thickened as drawn) vs the planetfall
                           deck's mean value (estimate: the bank's own drawn mean value, as the upper view under it)
  WeatherArtTest           wx_hazard 3072 x 1536, rows 0-1 x 6 cells and row 2 x 4 cells of 512 with art (>= 300 px) and
                           no block upscale; wx_ambient 2048 x 512, 16 cells of 256 with art (>= 100 px)
  WorldHazardsTest         wx_ambient.json holds 16 tight cell rects (>= 16, else the sheet is ignored), each tight
Exit code = number of failures.
"""
import json
import re
import sys
from pathlib import Path

import numpy as np
from PIL import Image

sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import check_tiles as ct  # noqa: E402

DEFAULT = ct.DEFAULT
SCRIPTS = HERE.parents[5] / "Scripts/Worlds/Backdrop"
fails = 0


def check(what, ok):
    global fails
    print(("PASS  " if ok else "FAIL  ") + what)
    if not ok:
        fails += 1


FROST_ATLASES = {
    "landmarks": ["rig_00", "rig_01", "platform_00", "platform_01", "platform_02", "refinery_00", "refinery_01", "relay_00", "relay_01",
                  "causeway_00", "icebreaker_00", "icebreaker_01", "cliff_00", "cliff_01", "derrick_00", "convoy_00"],
    "weather": ["cloud_bank_00", "cloud_bank_03", "cloud_wisp_00", "mist_00", "blizzard_00", "blizzard_02", "snow_00", "snow_01"],
    "sites": ["hangar_closed", "hangar_open", "rigbay_closed", "rigbay_open", "padring_idle", "padring_active", "crawlerbay_closed",
              "crawlerbay_open", "hatch_closed", "hatch_open", "lights_off", "lights_on"],
    "smoke": ["smoke_a_00", "smoke_a_07", "smoke_b_00"],
    "steam": ["steam_vent_00", "geyser_07"],
    "fire_lights": ["flare_00", "searchlight_07"],
    "beacons": ["beacon_magenta_03", "beacon_cyan_00", "strobe_white_00", "window_lights_03"],
    "aurora": ["aurora_00", "aurora_15"],
}
# sequences and their counts (the director / ambient catalog read them with Frames(name))
SEQUENCES = {
    "smoke": {"smoke_a": 8, "smoke_b": 8}, "steam": {"steam_vent": 8, "geyser": 8},
    "fire_lights": {"flare": 8, "searchlight": 8},
    "beacons": {"beacon_magenta": 4, "beacon_cyan": 4, "strobe_white": 4, "window_lights": 4},
    "aurora": {"aurora": 16},
    "weather": {"cloud_bank": 4, "cloud_wisp": 4, "mist": 3, "blizzard": 3, "snow": 2},
    "docks": {"rig": 6, "silo": 6, "cave": 6, "icebreaker": 6},
}
LOOP_ALPHA = {"smoke": .72, "steam": .75, "fire_lights": .78, "beacons": .70}     # FrostAmbientCatalog: highest loop alpha per atlas
AURORA_ALPHA = 1.0                                                                  # max FrostAmbientCatalog.AuroraAlpha
BRIGHTNESS, SAT = ct.BRIGHTNESS, ct.SATURATION
CEILING_THICKEN, CEILING_SAT, PLUME_SHARE = 7.0, .7, .6
OLD_BANK_CORE = .94     # check_atlases.py on old/ (the shipped banks, which pass FrostBackdropTest's rendered ceiling vs deck)


def lift(share):
    return ct.lift(share)


def apply(px, k, sat=1.0, alpha_lift=1.0):
    """BackdropGrade.Apply on float RGBA."""
    if abs(k - 1) < 1e-6 and abs(sat - 1) < 1e-6 and alpha_lift <= 1:
        return px
    rgb = px[..., :3]
    v = rgb.max(-1)
    kk = np.where(v > 1e-4, ct.curve(v, k) / np.maximum(v, 1e-4), 0)
    rgb = rgb * kk[..., None]
    l = rgb.mean(-1, keepdims=True)
    rgb = np.clip(l + (rgb - l) * sat, 0, 1)
    return np.concatenate([rgb, ct.curve(px[..., 3:], alpha_lift)], -1)


def as_drawn(atlas, px):
    k, al, s = 1.0, 1.0, 1.0
    if atlas in ("landmarks", "sites"):
        k = lift(1.0)
    elif atlas == "aurora":
        k = lift(.3)
    elif atlas in ("smoke", "steam"):
        k = lift(PLUME_SHARE)
    elif atlas == "weather":
        k = lift(1.0)
        al = CEILING_THICKEN
        s = CEILING_SAT
        return apply(px, k, ct.saturation(k) * s, al)
    return apply(px, k, ct.saturation(k) * s, al)


def draw_alpha(atlas):
    if atlas == "aurora":
        return AURORA_ALPHA
    return LOOP_ALPHA.get(atlas, 1.0)


def read_json(path):
    d = json.loads(Path(path).read_text())
    return d


def cell_img(a, s, H):
    """The cell of sprite s (Unity rect, y from the bottom) as top-down rows."""
    y0 = H - s["y"] - s["h"]
    return a[y0:y0 + s["h"], s["x"]:s["x"] + s["w"]]


def components(mask):
    from scipy import ndimage
    lab, n = ndimage.label(mask, structure=np.ones((3, 3)))
    return lab, n


def loop_guard(name, a, sprites, H):
    hard = soft = slivers = 0
    for s in sprites:
        c = cell_img(a, s, H)
        m = c[..., 3] > 8
        if not m.any():
            continue
        ys, xs = np.nonzero(m)
        top, left, right = ys.min(), xs.min(), s["w"] - 1 - xs.max()
        if min(top, left, right) < 2:
            hard += 1
            print(f"      {name} {s['n']}: top {top} left {left} right {right}")
        elif min(top, left, right) < 6:
            soft += 1
        lab, n = components(m)
        if n > 1:
            sizes = np.bincount(lab.ravel())[1:]
            main = sizes.argmax() + 1
            for i in range(1, n + 1):
                if i == main or sizes[i - 1] < 30:
                    continue
                yy, xx = np.nonzero(lab == i)
                bw, bh = xx.max() - xx.min() + 1, yy.max() - yy.min() + 1
                if bw >= 24 and bh <= 12 and bw >= 3 * bh:
                    slivers += 1
    check(f"{name}: no frame's art touches the top or a side of its cell ({hard} do, {soft} within 6 px)", hard == 0)
    check(f"{name}: no detached flat sliver ({slivers})", slivers == 0)


def cell_bleed(name, a, sprites, H):
    bad = []
    band, thin = 40, 18
    for s in sprites:
        c = cell_img(a, s, H)
        m = c[..., 3] > 8
        lab, n = components(m)
        if n <= 1:
            continue
        sizes = np.bincount(lab.ravel())[1:]
        main = sizes.argmax() + 1
        cw, ch = s["w"], s["h"]
        from scipy import ndimage
        near_main = ndimage.binary_dilation(lab == main, iterations=3)
        for i in range(1, n + 1):
            if i == main or sizes[i - 1] <= 30 or sizes[i - 1] >= sizes[main - 1] * .08:
                continue
            yy, xx = np.nonzero(lab == i)
            x0, x1, y0, y1 = xx.min(), xx.max(), yy.min(), yy.max()
            w, h = x1 - x0 + 1, y1 - y0 + 1
            lr = (x0 <= band and x1 <= band + thin or x1 >= cw - 1 - band and x0 >= cw - 1 - band - thin) and w <= thin and h >= 2 * w
            tb = (y0 <= band and y1 <= band + thin or y1 >= ch - 1 - band and y0 >= ch - 1 - band - thin) and h <= thin and w >= 2 * h
            if (lr or tb) and not (near_main & (lab == i)).any():
                bad.append(f"{s['n']} {x0}..{x1},{y0}..{y1}")
                break
    check(f"{name}: no cell carries a sliver of its neighbour ({'; '.join(bad) or 'none'})", not bad)


def resolution(name, a):
    H, W = a.shape[:2]
    cells = W >= 1024 and H >= 1024 and W % 256 == 0 and H % 256 == 0
    cw, chh = (256, 256) if cells else (W, H)
    au = a[::-1]
    worst = 0
    for y in range(0, H, chh):
        for x in range(0, W, cw):
            c = au[y:y + chh, x:x + cw]
            for k in (2, 3, 4):
                worst = max(worst, ct.block_score(c, k))
    pd = ct.pair_dup(au)
    check(f"{name}: block score {worst:.3f} < .97 (no nearest-neighbour upscale), pairs {pd:.2f} <= .85", worst < .97 and pd <= .85)


def brightness(name, a):
    px = a.astype(np.float64) / 255
    px = as_drawn(name, px)
    f = px.reshape(-1, 4)[::3]
    f = f[f[:, 3] > .01]
    if len(f) == 0:
        return
    wsum = f[:, 3].sum()
    lum = float((f[:, 3] * (f[:, :3] @ np.array([.2126, .7152, .0722]))).sum() / wsum)
    max_a = float(px[..., 3].max())
    drawn = lum * min(1.0, max_a * draw_alpha(name))
    cap = .80 if name == "weather" else .66
    check(f"{name}: atlas brightness as drawn (lum {lum:.3f} x opacity {min(1, max_a * draw_alpha(name)):.2f} = {drawn:.3f} <= {cap})", drawn <= cap)
    return lum


def frames_of(sprites, seq):
    names = {s["n"]: s for s in sprites}
    out = []
    i = 0
    while f"{seq}_{i:02d}" in names:
        out.append(names[f"{seq}_{i:02d}"])
        i += 1
    return out


def motion(name, a, sprites, H):
    for seq, n in SEQUENCES.get(name, {}).items():
        fr = frames_of(sprites, seq)
        check(f"{name}/{seq}: {len(fr)} frames (want {n}, no gap)", len(fr) == n)
        if name in ("weather", "docks") or len(fr) < 2:
            continue
        diffs = []
        for i in range(len(fr)):
            p = cell_img(a, fr[i], H).astype(np.int32)
            q = cell_img(a, fr[(i + 1) % len(fr)], H).astype(np.int32)
            vis = (p[..., 3] > 8) | (q[..., 3] > 8)
            ch = (np.abs(p - q).sum(-1) > 40) & vis
            diffs.append(ch.sum() / max(vis.sum(), 1))
        check(f"{name}/{seq}: every frame moves (changed share min {min(diffs):.3f} >= .03, last to first {diffs[-1]:.3f})", min(diffs) >= .03)


def span_ends(a, sprites, H):
    src = (SCRIPTS / "FrostBackdrop.cs").read_text()
    m = re.search(r"Span = new PieceRule\([^;]*WithEnds\(new Vector2\((-?[\d.]+)f, (-?[\d.]+)f\), new Vector2\((-?[\d.]+)f, (-?[\d.]+)f\)", src)
    ea = (float(m.group(1)), float(m.group(2)))
    eb = (float(m.group(3)), float(m.group(4)))
    s = {x["n"]: x for x in sprites}["causeway_00"]
    c = cell_img(a, s, H)
    # the sprite's drawn bounds are its full cell (FullRect, pivot centre): width 256, y from -128..128 (px), as a share of width
    w = s["w"]
    inside = all(abs(e[0]) <= .5 and -s["h"] / 2 - .02 * w <= e[1] * w <= s["h"] / 2 + .02 * w for e in (ea, eb)) and ea[0] < 0 < eb[0]
    def on_art(e):
        x = int(round(w / 2 + e[0] * w))
        y = int(round(s["h"] / 2 - e[1] * w))
        r = 8
        patch = c[max(0, y - r):y + r, max(0, x - r):x + r, 3]
        return patch.size and (patch > 128).mean() > .5
    check(f"landmarks causeway_00: span ends {ea} / {eb} inside the cell and on the drawing ({on_art(ea)}, {on_art(eb)})", inside and on_art(ea) and on_art(eb))


def docks(a, sprites, H):
    src = (SCRIPTS / "FrostBackdrop.cs").read_text()
    pads = re.search(r"SitePads =\s*\{(.*?)\};", src, re.S).group(1)
    pads = [(float(x), float(y)) for x, y in re.findall(r"new Vector2\((-?[\d.]+)f, (-?[\d.]+)f\)", pads)]
    names = ["rig", "silo", "cave", "icebreaker"]
    for k, tag in enumerate(names):
        fr = frames_of(sprites, tag)
        if len(fr) != 6:
            continue
        cells = [cell_img(a, s, H).astype(np.int32) for s in fr]
        same = all(c.shape == cells[0].shape for c in cells)
        check(f"docks/{tag}: every cell the same size {cells[0].shape[1]}x{cells[0].shape[0]}", same)
        diff = lambda i, j: int((np.abs(cells[i] - cells[j]).sum(-1) > 40).sum())
        d4, d5 = diff(0, 4), diff(0, 5)
        check(f"docks/{tag}: launch order (idle vs hold {d4} > 500, idle vs closing {d5} < it)", d4 > 500 and d5 < d4)
        px, py = pads[5 + k]
        check(f"docks/{tag}: pad point ({px:.3f}, {py:.3f}) well inside", abs(px) < .38 and abs(py) < .38)
        ok = True
        bad = []
        for i, c in enumerate(cells):
            h, w = c.shape[:2]
            x = int(round((px + .5) * w))
            y = h - 1 - int(round((py + .5) * h))            # Unity rows are bottom-up
            win = c[y - 4:y + 5, x - 4:x + 5, 3]
            good = c[y, x, 3] >= 200 and (win >= 200).sum() >= 81 * .9
            ok &= bool(good)
            if not good:
                bad.append(i)
        check(f"docks/{tag}: pad pixel and its neighbourhood opaque in all six cells {bad}", ok)
        def peak(c):
            m = c[..., 3] >= 200
            l = (c[..., 0] * .299 + c[..., 1] * .587 + c[..., 2] * .114) / 255
            return float(l[m].max()) if m.any() else 0
        pk = max(peak(c) for c in cells)
        check(f"docks/{tag}: no flash (peak {pk:.2f} <= idle {peak(cells[0]):.2f} + .02)", pk <= peak(cells[0]) + .02)


def weather_rules(a, sprites, H):
    src = (SCRIPTS / "FrostBackdrop.cs").read_text()
    m = re.search(r"BlizzardMaxAlpha\s*=\s*([\d.]+)f", src)
    bmax = float(m.group(1)) if m else .3
    peak = 0
    for s in frames_of(sprites, "blizzard"):
        peak = max(peak, cell_img(a, s, H)[..., 3].max() / 255)
    check(f"weather/blizzard: the gust covers at most {peak * bmax * 100:.0f}% of what is under it (art max alpha {peak:.2f} x {bmax}; <= 30%)",
          peak * bmax <= .30 + 1e-6)
    deck = DEFAULT.parent / "Planetfall"
    dp = sorted(deck.glob("*cloud_deck.png"))
    if dp:
        d = np.asarray(Image.open(dp[0]).convert("RGBA")).reshape(-1, 4)[::7]
        dv = float((d[:, :3].max(-1) / 255).mean())
        vals = []
        for s in frames_of(sprites, "cloud_bank"):
            c = as_drawn("weather", cell_img(a, s, H).astype(np.float64) / 255)
            al = c[..., 3]
            core = al > .5
            if core.any():
                vals.append(float(c[..., :3].max(-1)[core].mean()))
        bv = float(np.mean(vals)) if vals else 0
        print(f"      ceiling estimate: cloud_bank core mean value as drawn {bv:.2f} vs deck {dv:.2f} (test: rendered upper view >= deck x .7, <= deck + .05)")
        # the old banks (which pass the rendered test) measure .94 here: hold the new ones near them
        check(f"weather/cloud_bank: drawn core value {bv:.2f} near the old banks' {OLD_BANK_CORE} (-.08 / +.03; estimate of the rendered test)",
              OLD_BANK_CORE - .08 <= bv <= OLD_BANK_CORE + .03)


def wx(base):
    a = np.asarray(Image.open(base / "wx_hazard.png").convert("RGBA"))
    check(f"wx_hazard is 3072 x 1536 ({a.shape[1]} x {a.shape[0]})", a.shape[:2] == (1536, 3072))
    for r in range(3):
        for c in range(6 if r < 2 else 4):
            cell = a[r * 512:(r + 1) * 512, c * 512:(c + 1) * 512]
            op = int((cell[..., 3] > 24).sum())
            bs = max(ct.block_score(cell[::-1], k) for k in (2, 3, 4)) if op >= 300 else 0
            check(f"wx_hazard r{r} c{c}: art ({op} px >= 300), block {bs:.2f} < .97", op >= 300 and bs < .97)
    a = np.asarray(Image.open(base / "wx_ambient.png").convert("RGBA"))
    check(f"wx_ambient is 2048 x 512 ({a.shape[1]} x {a.shape[0]})", a.shape[:2] == (512, 2048))
    j = json.loads((base / "wx_ambient.json").read_text())["cells"]
    check(f"wx_ambient.json holds {len(j)} cells (>= 16)", len(j) >= 16)
    for i, c in enumerate(j[:16]):
        col, row = i % 8, i // 8
        cell = a[row * 256:(row + 1) * 256, col * 256:(col + 1) * 256]
        op = int((cell[..., 3] > 24).sum())
        ys, xs = np.nonzero(cell[..., 3] > 0)
        tight = dict(x=int(col * 256 + xs.min()), y=int(512 - (row * 256 + ys.max() + 1)), w=int(xs.max() - xs.min() + 1), h=int(ys.max() - ys.min() + 1))
        same = all(c[k] == tight[k] for k in ("x", "y", "w", "h"))
        check(f"wx_ambient cell {i} (r{row} c{col}): art {op} px >= 100, json rect tight {same}", op >= 100 and same)


def main():
    base = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT
    for name in ["landmarks", "sites", "docks", "smoke", "steam", "fire_lights", "beacons", "aurora", "weather"]:
        a = np.asarray(Image.open(base / f"{name}.png").convert("RGBA"))
        H = a.shape[0]
        sprites = read_json(base / f"{name}.json")["sprites"]
        names = {s["n"] for s in sprites}
        need = FROST_ATLASES.get(name, [])
        missing = [n for n in need if n not in names]
        check(f"{name}: {a.shape[1]}x{a.shape[0]}, the director's frames all there ({len(need) - len(missing)}/{len(need)}{', missing ' + ' '.join(missing) if missing else ''})", not missing)
        resolution(name, a)
        brightness(name, a)
        motion(name, a, sprites, H)
        if name in ("smoke", "steam", "fire_lights", "beacons"):
            loop_guard(name, a, sprites, H)
        if name in ("landmarks", "sites"):
            cell_bleed(name, a, sprites, H)
        if name == "landmarks":
            span_ends(a, sprites, H)
        if name == "docks":
            docks(a, sprites, H)
        if name == "weather":
            weather_rules(a, sprites, H)
    wx(base)
    print(f"failures: {fails}")
    sys.exit(fails)


if __name__ == "__main__":
    main()
