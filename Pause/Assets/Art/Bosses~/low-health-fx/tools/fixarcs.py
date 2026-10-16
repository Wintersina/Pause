import sys
from fxlib import *
W = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w/'

def strip_boxy(cell, maxarea=300, minrun=7):
    """drop small components that carry a flat axis-aligned clipped edge (cut-off bolt slivers)"""
    a = cell[:, :, 3]
    lab, n = components(a > 0)
    out = cell.copy()
    removed = 0
    for i in range(1, n + 1):
        m = lab == i
        area = int(m.sum())
        if area > maxarea:
            continue
        ys, xs = np.nonzero(m)
        sub = np.zeros((ys.max() - ys.min() + 3, xs.max() - xs.min() + 3), np.uint8)
        sub[ys - ys.min() + 1, xs - xs.min() + 1] = 255
        if longest_straight(sub) >= minrun:
            out[m] = 0
            removed += 1
    return out, removed

if __name__ == '__main__':
    k = sys.argv[1]
    fx = load(W + 'orig/%s_damage_fx.png' % k)
    out = fx.copy()
    for c in range(6):
        cell, r = strip_boxy(cell_of(fx, 1, c).copy())
        out[C:2 * C, c * C:(c + 1) * C] = cell
        print(k, c, 'removed', r, 'straight', longest_straight(cell[:, :, 3]))
    save(out, W + 'out/%s_arcs.png' % k)
