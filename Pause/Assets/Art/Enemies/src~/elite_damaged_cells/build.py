"""Turn Codex's damaged-hull images into registered damaged cells with sputtering Tide-style flames.
usage: build.py [--install] [key ...]     default: preview only into SCR/build/"""
import sys, os, random, importlib.util
sys.path.insert(0, '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/dmg')
from common import *
from PIL import ImageDraw
TARGETS['space'] = ['eventide_bastion', 'orbit_reaver', 'rift_lancer', 'singularity_hauler']
spec = importlib.util.spec_from_file_location('sb', ART + 'Enemies/src~/space_elites_engine_burn/build_strips.py')
sb = importlib.util.module_from_spec(spec); spec.loader.exec_module(sb)
CELL = 384
OUT = SCR + 'build/'
os.makedirs(OUT, exist_ok=True)


def src_path(w, key):
    return ART + f'Enemies/Elite/Frost/{key}.png' if w == 'frost' else respath(w, key)


def hull_from(path):
    art = sb.key_out(path)
    art = art.crop(art.getchannel('A').point(lambda v: 255 if v > 128 else 0).getbbox())
    return art


def iou_at(ref, cand, dx, dy, ymax):
    c = np.zeros_like(ref)
    h, w = cand.shape
    ys0, xs0 = max(0, dy), max(0, dx)
    ys1, xs1 = min(CELL, dy + h), min(CELL, dx + w)
    if ys1 <= ys0 or xs1 <= xs0: return 0
    c[ys0:ys1, xs0:xs1] = cand[ys0 - dy:ys1 - dy, xs0 - dx:xs1 - dx]
    r = ref[:ymax]; c = c[:ymax]
    return (r & c).sum() / max(1, (r | c).sum())


def register(art, flight, ymax):
    ref = mask(flight)
    fb = flight.getbbox()
    best = (-1, None)
    for s in np.arange(0.0, 1.0, 1):   # placeholder to keep structure simple
        pass
    w0 = fb[2] - fb[0]
    for k in np.arange(0.88, 1.13, 0.01):
        tw = w0 * k
        s = tw / art.width
        im = art.resize((max(1, round(art.width * s)), max(1, round(art.height * s))), Image.LANCZOS)
        a = np.asarray(im)[..., 3] >= 128
        # coarse: align centre x with the flight bbox centre, top with the flight top
        bx = (fb[0] + fb[2]) // 2 - im.width // 2
        by = fb[1]
        for dy in range(-10, 11, 2):
            for dx in range(-8, 9, 2):
                v = iou_at(ref, a, bx + dx, by + dy, ymax)
                if v > best[0]: best = (v, (s, bx + dx, by + dy))
    v0, (s, bx, by) = best
    # refine at 1 px
    im = art.resize((max(1, round(art.width * s)), max(1, round(art.height * s))), Image.LANCZOS)
    a = np.asarray(im)[..., 3] >= 128
    for dy in range(-2, 3):
        for dx in range(-2, 3):
            v = iou_at(ref, a, bx + dx, by + dy, ymax)
            if v > best[0]: best = (v, (s, bx + dx, by + dy))
    return best


def flame_cols(w, key, flight, nozzles):
    a = np.asarray(flight).astype(int)
    ys = max(n['y'] for n in nozzles) + 6
    reg = np.zeros((CELL, CELL), bool)
    for n in nozzles:
        reg[ys:, max(0, n['x'] - 16):n['x'] + 16] = True
    rgb = a[..., :3]
    sat = rgb.max(-1) - rgb.min(-1)
    m = reg & (a[..., 3] > 200) & (sat > 90)
    px = rgb[m]
    if len(px) < 20: return None
    lum = px @ np.array([.3, .59, .11])
    o = np.argsort(lum)
    n = len(px)
    outer = tuple(int(v) for v in np.median(px[o[:max(1, n // 4)]], 0))
    mid = tuple(int(v) for v in np.median(px[o[int(n * .6):int(n * .85)]], 0))
    return (outer, mid, (255, 255, 255))


def wisp(canvas, x, y, rnd, rows=6):
    d = ImageDraw.Draw(canvas)
    px = int(x / sb.U)
    py = int(y / sb.U)
    for r in range(rows):
        wd = 1 + (r > 2)
        px += rnd.choice((-1, 0, 0, 1))
        a = max(40, 150 - r * 22)
        d.rectangle((px, py + r, px + wd - 1, py + r), fill=(70, 72, 84, a))


def build_cell(w, key, dm_img):
    d = defn(key)
    fl = d['cells']['flight'][0]
    path = src_path(w, key)
    im, h, cs = strip(path)
    flight = cs[fl]
    nz = d['nozzles']
    ymax = min(n['y'] for n in nz) - 2
    art = hull_from(dm_img)
    sc, (s, ox, oy) = register(art, flight, ymax)
    a2 = art.resize((max(1, round(art.width * s)), max(1, round(art.height * s))), Image.LANCZOS)
    a2.putalpha(a2.getchannel('A').point(lambda v: 255 if v >= 128 else 0))
    hull = Image.new('RGBA', (CELL, CELL)); hull.alpha_composite(a2, (ox, oy))
    seeds = [(n['x'], n['y']) for n in nz]
    mouths = sb.find_openings(hull, seeds, {})
    fixed = []
    for (cx, cy, rx, ry), n in zip(mouths, nz):
        if not (5 <= rx <= 32 and 3 <= ry <= 16) or abs(cx - n['x']) > 12 or abs(cy - n['y']) > 14:
            cx, cy, rx, ry = n['x'], n['y'], 11, 6
        if w == "space": cx, cy = n["x"], n["y"]   # SpaceEliteTest wants the flame to cover the nozzle point
        fixed.append((cx, cy, rx, ry))
    mouths = fixed
    cols = flame_cols(w, key, flight, nz) or ((255, 150, 40), (255, 215, 110), (255, 255, 255))
    n = len(mouths)
    pat = [(26, .8), (0, 0), (15, .7), (31, .85), (0, 0)]
    order = list(range(n))
    random.Random(sum(map(ord, key))).shuffle(order)
    lens = [0] * n; wks = [.8] * n
    for rank, i in enumerate(order):
        lens[i], wks[i] = pat[rank % 5]
    if w == "space": lens = [l if l else 9 for l in lens]; wks = [k if k else .6 for k in wks]   # every Space engine still burns (EngineFlames): a dead one only sputters
    if n == 1: lens[0] = 22
    if n >= 2 and all(l == 0 for l in lens): lens[0] = 24
    # keep every plume 8 px clear of the cell edge
    bottom_room = CELL - 10
    for i, (cx, cy, rx, ry) in enumerate(mouths):
        lens[i] = min(lens[i], max(0, (bottom_room - cy) / sb.K))
    c = sb.engine_cell(hull, mouths, cols, lens, wks, 99, ragged=.55)
    # smoke wisps from the dead engines
    rnd = random.Random(7)
    sm = Image.new('RGBA', (CELL // sb.U, CELL // sb.U))
    for i, (cx, cy, rx, ry) in enumerate(mouths):
        if lens[i] == 0:
            wisp(sm, cx, cy + ry, rnd, rows=min(6, int((bottom_room - cy) / sb.U) - 2))
    c.alpha_composite(sm.resize((CELL, CELL), Image.NEAREST))
    if w != 'space':   # dark maroon scorch reads pink-ish outside Space: turn it to a warm brown (hue .06), bright lilac plates stay
        import colorsys
        arr = np.asarray(c).copy()
        for y, x in zip(*np.nonzero((arr[..., 3] >= 96))):
            r, g, b = arr[y, x, :3] / 255.0
            hh, ss, vv = colorsys.rgb_to_hsv(r, g, b)
            if ss > .3 and vv < .62 and .76 < hh < .98:
                nr, ng, nb = colorsys.hsv_to_rgb(.06, ss * .8, vv)
                arr[y, x, :3] = (round(nr * 255), round(ng * 255), round(nb * 255))
        c = Image.fromarray(arr, 'RGBA')
    info = dict(iou=round(sc, 3), scale=round(s, 3), pos=(ox, oy), mouths=[tuple(round(v) for v in m) for m in mouths], lens=lens, cols=cols, bbox=c.getbbox())
    return c, info, flight, cs


def main():
    args = sys.argv[1:]
    install = '--install' in args
    only = [a for a in args if not a.startswith('--')]
    for w, n, key in keys():
        if only and key not in only: continue
        p = SCR + f'out/{key}.png'
        if not os.path.exists(p): continue
        cell, info, flight, cs = build_cell(w, key, p)
        print(key, info)
        cell.save(OUT + f'{key}_cell.png')
        row = Image.new('RGBA', (384 * 2, 384), (30, 24, 60, 255))
        row.alpha_composite(flight, (0, 0)); row.alpha_composite(cell, (384, 0))
        row.save(OUT + f'{key}_cmp.png')
        if install:
            do_install(w, key, cell)


def do_install(w, key, cell):
    dm = defn(key)['cells']['damaged']
    targets = []
    if w == 'frost':
        targets = [ART + f'Enemies/Elite/Frost/{key}.png', ART + f'Enemies/src~/ship_scale_border~/original/{key}.png']
    elif w == 'space':
        targets = [respath(w, key), ART + f'Enemies/Elite/Space/{key}.png', ART + f'Enemies/src~/ship_scale_border~/original/{key}.png']
    else:
        targets = [respath(w, key), ART + f'Enemies/Elite/{w.capitalize()}/{key}.png']
    for t in targets:
        if not os.path.exists(t): print('  (skip missing)', t); continue
        s = Image.open(t).convert('RGBA')
        s.paste(Image.new('RGBA', (CELL, CELL)), (dm * CELL, 0))
        s.alpha_composite(cell, (dm * CELL, 0))
        s.save(t)
        print('  wrote', t)


main()
