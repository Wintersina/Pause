#!/usr/bin/env python3
"""Checks (and with --fix, re-measures) the pipe ENDS in each planet world's points.json against
the drawings: a pipe run (the directors' PipeRun) lays a horizontal pipe so its two flange faces
(points.json "ends" of the piece) land on the two structures it joins; an end recorded where the
drawing has no pipe leaves a visible gap between the pipe and the structure (Desert's ends were
mostly the 20 / 236 defaults while e.g. half_buried_pipe's pipe ends at x 38 / 227).

For every horizontal end (left / right of the cell centre, within 40 px of its middle row) of a
pipes-atlas piece, the drawing's main body gives the measured face: its outermost opaque column on
that side (only x is corrected: the recorded y, the pipe axis, is kept). Vertical ends are left
alone. Run it per world and look at what it reports before --fix: a piece whose outermost column is
not its flange (a pool base, leaves) must be measured by hand.

    python3 measure_pipe_ends.py <Resources/Worlds> [--fix] [--worlds Desert,Ember]
"""
import json, os, re, sys
import numpy as np
from collections import deque
from PIL import Image

TOL = 6

def label(m):
    H, W = m.shape
    lab = np.zeros((H, W), np.int32)
    n = 0
    for y0, x0 in zip(*np.nonzero(m)):
        if lab[y0, x0]: continue
        n += 1
        lab[y0, x0] = n
        q = deque([(y0, x0)])
        while q:
            y, x = q.popleft()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = y + dy, x + dx
                    if 0 <= yy < H and 0 <= xx < W and m[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = n
                        q.append((yy, xx))
    return lab, n

def main():
    base = sys.argv[1]
    fix = '--fix' in sys.argv
    worlds = sys.argv[sys.argv.index('--worlds') + 1].split(',') if '--worlds' in sys.argv else ['Verdant', 'Ember', 'Tide', 'Desert']
    for w in worlds:
        pj = os.path.join(base, w, 'Backdrop3', 'points.json')
        if not os.path.exists(pj): continue
        text = open(pj).read()
        im = np.asarray(Image.open(os.path.join(base, w, 'Backdrop3', 'pipes.png')).convert('RGBA'))
        cells = {c['n']: c for c in json.load(open(os.path.join(base, w, 'Backdrop3', 'pipes.json')))['sprites']}
        H = im.shape[0]
        changed = 0
        for line in text.split('\n'):
            m = re.search(r'"name": "([^"]+)", "atlas": "pipes"', line)
            if not m: continue
            name = m.group(1)
            entry = json.loads(line.strip().rstrip(','))
            ends = entry.get('ends', [])
            horiz = [e for e in ends if abs(e['y'] - 128) <= 40 and abs(e['x'] - 128) > 60]
            if len(horiz) < 2 or name not in cells: continue
            c = cells[name]
            top = H - c['y'] - c['h']
            a = im[top:top + c['h'], c['x']:c['x'] + c['w'], 3] > 8
            # the drawing's main body (largest 8-connected component): its extreme columns on each
            # side are the flange faces; the face's y is the middle of the opaque rows there
            lab, n = label(a)
            if n == 0: continue
            sizes = np.bincount(lab.ravel())[1:]
            body = lab == (sizes.argmax() + 1)
            cols = np.nonzero(body.any(0))[0]
            faces = {}
            for side, x in (('L', cols.min()), ('R', cols.max())):
                xs = slice(x, x + 4) if side == 'L' else slice(max(0, x - 3), x + 1)
                rows = np.nonzero(body[:, xs].any(1))[0]
                faces[side] = (float(x), float(np.median(rows)))
            new = []
            for e in ends:
                if e not in horiz:
                    new.append(e); continue
                fx, fy = faces['L' if e['x'] < 128 else 'R']
                if abs(fx - e['x']) > TOL:
                    # x only: the outermost column is the flange face (its row middle can be a base or
                    # a leg, not the pipe axis, so the recorded y is kept)
                    print('%s %s end (%.0f, %.0f) -> drawn face x %.0f' % (w, name, e['x'], e['y'], fx))
                    changed += 1
                    new.append({'x': fx, 'y': e['y']})
                else:
                    new.append(e)
            if fix and new != ends:
                entry['ends'] = new
                newline = '  ' + json.dumps(entry) + (',' if line.rstrip().endswith(',') else '')
                text = text.replace(line, newline)
        if fix and changed:
            open(pj, 'w').write(text)

if __name__ == '__main__':
    main()
