import subprocess, sys
from fxlib import *
import fixsmoke, fixarcs, verdantarcs
W = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w/'
DST = '/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/Resources/Bosses/'
ART = '/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/Bosses/'

def arcs_for(k, fx):
    if k == 'Frost':
        out = fx.copy()
        for c in range(6):
            cell, _ = fixarcs.strip_boxy(cell_of(fx, 1, c).copy())
            out[C:2 * C, c * C:(c + 1) * C] = cell
        return out
    if k == 'Verdant':
        return verdantarcs.build()
    if k == 'Storm':
        out = fx.copy()
        for c in range(6):
            cell, _ = fixarcs.strip_boxy(cell_of(fx, 1, c).copy())
            out[C:2 * C, c * C:(c + 1) * C] = cell
        return out
    return fx

for k, fn in (('Frost', fixsmoke.run_frost), ('Verdant', fixsmoke.run_verdant), ('Storm', fixsmoke.run_storm), ('Desert', fixsmoke.run_desert)):
    smoke = fn()
    orig = load(W + 'orig/%s_damage_fx.png' % k)
    arcs = arcs_for(k, orig)
    out = orig.copy()
    out[0:C] = smoke[0:C]
    for c in range(6):
        out[0:C, c * C:(c + 1) * C] = border_fade(out[0:C, c * C:(c + 1) * C].copy())
    out[C:2 * C] = arcs[C:2 * C]
    save(out, DST + '%s_damage_fx.png' % k)
    save(out, W + 'out/%s_fx_final.png' % k)
    print(k, 'written')
