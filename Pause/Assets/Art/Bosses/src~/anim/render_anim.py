#!/usr/bin/env python3
"""Render the many-frame boss animation from each boss's painted idle cell.

    python3 render_anim.py --boss frost [--moves idle,tell0] --review OUT   strips + GIFs for approval
    python3 render_anim.py --boss frost --atlas                             pack Resources/Bosses/<Key>_anim*.png

Frame order in the atlas (BossAnim.cs slices the same order; keep in step):
    idle 0..11 | turnL 0..5 | turnR 0..5 | hit 0..2 | for each tell pose p = 0..2: tell 0..7, fire 0..3, recoil 0..3
packed 7 cells across into <Key>_anim0.png, <Key>_anim1.png (no sheet past 4096 px).
"""
import argparse, os, sys
import numpy as np
from PIL import Image
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import rig, rigs, moves

RES = os.path.normpath(os.path.join(HERE, '..', '..', '..', 'Resources', 'Bosses'))
ORDER = ['idle', 'turnL', 'turnR', 'hit'] + [f'{m}{p}' for p in range(3) for m in ('tell', 'fire', 'recoil')]
MAX_TEX = 4096
COLUMNS = 7


def source_cell(key, cell, frame):
    im = Image.open(os.path.join(RES, key + '.png')).convert('RGBA')
    r, c = divmod(frame, 5)
    return im.crop((c * cell, r * cell, c * cell + cell, r * cell + cell))


def build(spec):
    """-> {move: [(pose, fx, maw)]}"""
    out = {}
    idle_maw = spec.get('idle_maw', 0.)
    mawf = spec.get('maw')
    idle_fx = spec.get('idle_fx')
    out['idle'] = [(p, idle_fx(t) if idle_fx else None, mawf(idle_maw) if mawf else None) for p, t in spec['idle']]
    out['turnL'] = [(p, None, mawf(idle_maw) if mawf else None) for p, t in spec['turnL']]
    out['turnR'] = [(p, None, mawf(idle_maw) if mawf else None) for p, t in spec['turnR']]
    out['hit'] = [(p, None, mawf(idle_maw) if mawf else None) for p, t in spec['hit']]
    for i, (tell, fire, rec) in enumerate(spec['attacks']):
        ft, ff, fr, jaw = spec['fx'][i]
        m = (lambda g: mawf(g)) if (jaw and mawf) else (lambda g: mawf(idle_maw) if mawf else None)
        out[f'tell{i}'] = [(p, ft(t), m(t)) for p, t in tell]
        out[f'fire{i}'] = [(p, ff(t), m(1 - .4 * t)) for p, t in fire]
        out[f'recoil{i}'] = [(p, fr(t), m(.6 * (1 - t))) for p, t in rec]
    return out


def render_moves(spec, names):
    src = source_cell(spec['key'], spec['cell'], spec['src_frame'])
    r = rig.Rig(src, spec['parts'], base=spec.get('base', 384), maw_dark=spec.get('maw_dark', (18, 10, 12)))
    allm = build(spec)
    res = {}
    for n in names:
        fr = [r.render(pose, fx=fx, maw=maw) for pose, fx, maw in allm[n]]
        if spec['key'] != 'Space':
            fr = [rig.clamp_pink(f) for f in fr]
        res[n] = [Image.fromarray(f) for f in fr]
    return res, src


def ticks_for(spec, move, nframes):
    if move == 'idle': return moves.IDLE_TICKS
    if move.startswith('turn'): return moves.TURN_TICKS
    if move == 'hit': return moves.HIT_TICKS
    if move.startswith('fire'): return moves.FIRE_TICKS
    if move.startswith('recoil'): return moves.RECOIL_TICKS
    if move.startswith('tell'):
        secs = spec.get('tell_seconds', [.8, .8, .8])[int(move[-1])]
        return [max(1, round(secs * 24 / nframes))] * nframes
    return [3] * nframes


def gif(frames, ticks, path, bg=(34, 32, 52)):
    out = []
    for f in frames:
        b = Image.new('RGBA', f.size, bg + (255,)); b.alpha_composite(f)
        out.append(b.convert('RGB').convert('P', palette=Image.ADAPTIVE, colors=255))
    out[0].save(path, save_all=True, append_images=out[1:], duration=[int(t * 1000 / 24) for t in ticks], loop=0, disposal=2)


def review(spec, world, names, outroot):
    res, src = render_moves(spec, names)
    paths = []
    for n in names:
        fr = res[n]
        C = fr[0].width
        d = os.path.join(outroot, f'boss_{world}_anim_{n}')
        os.makedirs(d, exist_ok=True)
        strip = Image.new('RGBA', (C * len(fr), C), (0, 0, 0, 0))
        for i, f in enumerate(fr): strip.alpha_composite(f, (i * C, 0))
        strip.save(os.path.join(d, 'r1_0.png'))
        gif(fr, ticks_for(spec, n, len(fr)), os.path.join(d, 'preview.gif'))
        paths.append(os.path.join(d, 'preview.gif'))
    # the full attacks as one clip each (tell -> fire -> recoil), and idle -> turns
    for p in range(3):
        if all(f'{m}{p}' in res for m in ('tell', 'fire', 'recoil')):
            fr = res[f'tell{p}'] + res[f'fire{p}'] + res[f'recoil{p}'] + (res['idle'][:6] if 'idle' in res else [])
            tk = ticks_for(spec, f'tell{p}', 8) + moves.FIRE_TICKS + moves.RECOIL_TICKS + ([3] * 6 if 'idle' in res else [])
            d = os.path.join(outroot, f'boss_{world}_anim_attack{p}_full.gif')
            gif(fr, tk, d); paths.append(d)
    return paths


def atlas(spec):
    res, src = render_moves(spec, ORDER)
    frames = [f for n in ORDER for f in res[n]]
    C = frames[0].width
    cols = COLUMNS                  # BossAnim.Columns: a cell is always the sheet's width / 7
    rows_max = MAX_TEX // C
    per = cols * rows_max
    sheets = []
    for s in range(0, len(frames), per):
        chunk = frames[s:s + per]
        rows = (len(chunk) + cols - 1) // cols
        sheet = Image.new('RGBA', (cols * C, rows * C), (0, 0, 0, 0))
        for i, f in enumerate(chunk):
            sheet.alpha_composite(f, ((i % cols) * C, (i // cols) * C))
        sheets.append(sheet)
    written = []
    for i, sh in enumerate(sheets):
        p = os.path.join(RES, f"{spec['key']}_anim{i}.png")
        sh.save(p, optimize=True); written.append((p, sh.size))
    return written, len(frames)


def showcase(spec, world, outroot):
    """One clip of the whole set: idle, a turn each way, the three attacks, a hit -- at game timing."""
    res, _ = render_moves(spec, ORDER)
    fr, tk = [], []
    def put(m):
        fr.extend(res[m]); tk.extend(ticks_for(spec, m, len(res[m])))
    put('idle'); put('idle'); put('turnL'); put('idle'); put('turnR'); put('idle')
    for p in range(3):
        put(f'tell{p}'); put(f'fire{p}'); put(f'recoil{p}'); put('idle')
    put('hit'); put('idle')
    small = [f.resize((f.width * 2 // 3, f.height * 2 // 3), Image.LANCZOS) if f.width > 400 else f for f in fr]
    p = os.path.join(outroot, f'boss_{world}_anim_showcase.gif')
    gif(small, tk, p)
    return p


def decals(spec):
    """<Key>_anim_damage.png: the damaged hull's own damage (pixels that differ from the pristine idle) over the
    rig's static core only, laid out like <Key>_damage.png (2 columns: idle loop A / B, 4 rows: stage 1..4)."""
    C = spec['cell']
    src = source_cell(spec['key'], C, spec['src_frame'])
    r = rig.Rig(src, spec['parts'], base=spec.get('base', 384))
    moving = np.zeros((C, C), bool)
    for p in r.parts:
        moving |= p.mask
    from scipy import ndimage
    moving = ndimage.binary_dilation(moving, iterations=2)
    pr = np.array(src).astype(np.int16)
    dmg = np.array(Image.open(os.path.join(RES, spec['key'] + '_damage.png')).convert('RGBA')).astype(np.int16)
    out = np.zeros((4 * C, 2 * C, 4), np.uint8)
    for st in range(4):
        for col in range(2):
            d = dmg[st * C:(st + 1) * C, col * C:(col + 1) * C]
            diff = (np.abs(d[..., :3] - pr[..., :3]).max(-1) > 28) | (np.abs(d[..., 3] - pr[..., 3]) > 60)
            m = diff & ~moving & (pr[..., 3] > 0)
            cell = np.zeros((C, C, 4), np.uint8)
            cell[m] = np.clip(d[m], 0, 255).astype(np.uint8)
            # a chunk blown out of the hull (the damaged drawing transparent where the hull was) shows as a dark hole
            gone = m & (d[..., 3] < 128)
            cell[gone] = (16, 12, 18, 255)
            out[st * C:(st + 1) * C, col * C:(col + 1) * C] = cell
    path = os.path.join(RES, spec['key'] + '_anim_damage.png')
    Image.fromarray(out).save(path, optimize=True)
    return path


if __name__ == '__main__':
    ap = argparse.ArgumentParser()
    ap.add_argument('--boss', required=True)
    ap.add_argument('--moves', default=','.join(ORDER))
    ap.add_argument('--review')
    ap.add_argument('--atlas', action='store_true')
    ap.add_argument('--showcase')
    a = ap.parse_args()
    spec = rigs.BOSSES[a.boss]()
    if a.review:
        for p in review(spec, a.boss, a.moves.split(','), a.review): print(p)
    if a.atlas:
        print(atlas(spec))
        if os.path.exists(os.path.join(RES, spec['key'] + '_damage.png')):
            print(decals(spec))
    if a.showcase:
        print(showcase(spec, a.boss, a.showcase))
