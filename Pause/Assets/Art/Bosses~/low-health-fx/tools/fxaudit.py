import sys
import numpy as np
from fxlib import components, longest_straight, load
R = '/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/Resources/Bosses/'
W = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w/'
src = sys.argv[1]
for k in sys.argv[2:]:
    fx = load((R if src == 'new' else W + 'orig/') + '%s_damage_fx.png' % k)
    cs = fx.shape[1] // 6
    for r in range(2):
        res = []
        for c in range(6):
            a = fx[r * cs:(r + 1) * cs, c * cs:(c + 1) * cs, 3]
            m = 12
            border = int((a[:m] > 0).sum() + (a[-m:] > 0).sum() + (a[:, :m] > 0).sum() + (a[:, -m:] > 0).sum())
            op = a > 0
            faint = int(((a > 0) & (a < 48)).sum()) / max(1, op.sum())
            mean = a[op].mean() if op.any() else 255
            st = longest_straight(a) * 384 / cs
            lab, n = components(op)
            boxy = 0
            for i in range(1, n + 1):
                mm = lab == i
                if mm.sum() > 300 * (cs / 384) ** 2:
                    continue
                ys, xs = np.nonzero(mm)
                sub = np.zeros((ys.max() - ys.min() + 3, xs.max() - xs.min() + 3), np.uint8)
                sub[ys - ys.min() + 1, xs - xs.min() + 1] = 255
                if longest_straight(sub) * 384 / cs >= 7:
                    boxy += 1
            res.append((border, round(faint, 3), int(mean), int(st), boxy))
        print(k, 'smoke' if r == 0 else 'arcs ', res)
