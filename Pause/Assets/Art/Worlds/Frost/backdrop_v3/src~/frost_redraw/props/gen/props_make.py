import sys, os, json
from props_build import *
R = lambda n, f: f'{OUT}/bd_frost_prop_{n}/raw/{f}'
os.makedirs(f'{PV}', exist_ok=True)
# surfacing one-shot: (body vis, spout scale, spout alpha, foam scale, foam alpha); frame 0 = idle (held while the creature is down)
SURF = [(.12, 0, 0, .5, 0), (.45, 0, 0, .6, .7), (1, .3, .9, .86, .8), (1, .75, 1, .93, 1), (1, 1, 1, 1, 1), (1, 1.1, .6, 1.12, .75), (.55, 1.15, .25, 1.25, .45), (.2, 0, 0, 1.38, .2)]
SURF_MS = [1400, 110, 110, 130, 160, 150, 130, 150]
POD = [(.12, 0, 0, .5, 0), (.5, 0, 0, .6, .7), (1, 0, 0, .8, 1), (1, 0, 0, .92, 1), (1, 0, 0, 1, 1), (1, 0, 0, 1.08, .8), (.5, 0, 0, 1.2, .45), (.18, 0, 0, 1.32, .2)]
LIT_MS = [125] * 8

def P(name, i, frames, durs, var, cls, size, label, nth=0, meta=None):
    os.makedirs(f'{OUT}/bd_frost_prop_{name}', exist_ok=True)
    strip(frames, f'{OUT}/bd_frost_prop_{name}/r1_{i}.png')
    g, score = preview(name, frames, durs, var, cls, size, f'{PV}/{name}_r1_{i}.gif', nth, label)
    pk = max(pinkcheck(f) for f in frames)
    M[f'{name}_r1_{i}'] = dict(frames=len(frames), cell=list(frames[0].shape[1::-1]), ms=durs, mask=cls, variant=var, size_px=size,
                               spot_fit=round(float(score), 2), pink_px=pk, **(meta or {}))
    return g

M = {}
G = {}
which = sys.argv[1:]
def want(k): return not which or k in which
if want('whale'):
    a = load(R('whale', 'r1_3.png'))     # top-down humpback, spout above the head
    G['whale_0'] = P('whale', 0, creature(a, (0, 45, 40, 90), SURF), SURF_MS, 'v2', 'W', 54, 'humpback surfacing + blow')
    a = load(R('whale', 'r1_0.png'))     # diagonal humpback, spout top-left
    G['whale_1'] = P('whale', 1, creature(a, (0, 40, 20, 70), SURF), SURF_MS, 'v2', 'W', 58, 'humpback (3/4) surfacing + blow', nth=1)
    # AI blow loop (animate-with-text-v3) for the surfaced phase, then a procedural dive
    fr = [load(R('whale', f'a3/f_{i}.png')) for i in range(8)]
    k = grade_factor(fr[0], np.zeros(fr[0].shape[:2], bool))
    g = [grade(f, k, np.zeros(f.shape[:2], bool)) for f in fr]
    body, sp, foam = split_water(fr[4], (0, 45, 40, 90))
    B = layer(g[4], body | foam)
    frames = [fade(submerge(layer(g[4], body), .12), 1), fade(submerge(B, .45), .8), g[4], g[5], g[6], g[0], g[2], fade(submerge(B, .4), .6)]
    G['whale_2'] = P('whale', 2, frames, SURF_MS, 'v2', 'W', 54, 'humpback AI blow loop', nth=2)
if want('orca'):
    a = load(R('orca', 'r1_3.png'))
    G['orca_0'] = P('orca', 0, creature(a, None, POD), [1200, 110, 120, 140, 160, 140, 120, 140], 'v2', 'W', 46, 'orca pod surfacing', nth=3)
    a = load(R('orca', 'r1_1.png'))
    G['orca_1'] = P('orca', 1, creature(a, None, POD), [1200, 110, 120, 140, 160, 140, 120, 140], 'v2', 'W', 48, 'orca pod (line) surfacing', nth=4)
if want('station'):
    for i, (f, seed, var, nth, kw) in enumerate((('r1_0.png', 1, 'v1', 0, {}), ('r1_3.png', 2, 'v4', 0, {'add_beacon': (20, 119)}))):
        fr, L = lit(load(R('station', f)), seed=seed, **kw)
        G[f'station_{i}'] = P('station', i, fr, LIT_MS, var, 'O', 62, f'research station {f}', nth=nth, meta={k: len(v) for k, v in L.items()})
if want('lab'):   # raw r1_2 rejected: the generator filled the cell with a snow drift up to hard square edges
    for i, (f, seed, var, nth) in enumerate((('r1_3.png', 5, 'v1', 1), ('r1_0.png', 4, 'v3', 0))):
        fr, L = lit(load(R('lab', f)), seed=seed)
        G[f'lab_{i}'] = P('lab', i, fr, LIT_MS, var, 'O', 58, f'abandoned lab {f}', nth=nth, meta={k: len(v) for k, v in L.items()})
if want('plane'):
    for i, (f, var, nth) in enumerate((('r1_3.png', 'v4', 1), ('r1_0.png', 'v3', 1))):
        fr, L = lit(load(R('plane', f)), seed=i, blink=(0, 1, 4))
        G[f'plane_{i}'] = P('plane', i, fr, LIT_MS, var, 'O', 56, f'crashed plane {f}', nth=nth, meta={k: len(v) for k, v in L.items()})
if want('icebreaker'):
    fr = [load(R('icebreaker', f'a1/f_{i}.png')) for i in range(8)]
    lm0 = lights_mask(fr[0]); k = grade_factor(fr[0], lm0)
    g = [grade(f, k, lights_mask(f)) for f in fr]
    G['icebreaker_0'] = P('icebreaker', 0, g, [120] * 8, 'v2', 'W', 58, 'icebreaker in a lead (AI wake loop)', nth=0)
    fr2, L = lit(load(R('icebreaker', 'r1_3.png')), seed=1)
    G['icebreaker_1'] = P('icebreaker', 1, fr2, LIT_MS, 'v2', 'W', 58, 'icebreaker r1_3 (lights only)', nth=1)
if want('seals'):
    tint = (.86, .94, 1.0)
    fr = [load(R('seals', f'a1/f_{i}.png')) for i in range(8)]
    k = grade_factor(fr[0], np.zeros(fr[0].shape[:2], bool))
    g = [grade(f, k, np.zeros(f.shape[:2], bool), tint) for f in fr]
    G['seals_0'] = P('seals', 0, g, [180] * 8, 'v2', 'W', 48, 'seal colony on a floe (AI idle)', nth=7)
mp = f'{PV}/meta.json'; old = json.load(open(mp)) if os.path.exists(mp) else {}
old.update(M); json.dump(old, open(mp, 'w'), indent=1)
for k, v in M.items(): print(k, v)
