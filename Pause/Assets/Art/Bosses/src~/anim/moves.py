"""Move builders shared by every boss rig: the idle loop, the turns, the hit flinch and the
three-act attack (tell -> fire -> recoil). A pose is {part: (rot, dx, dy, sx, sy)}."""
import math

# Frame counts (BossAnim.cs reads the same numbers; keep in step).
IDLE, TURN, HIT, TELL, FIRE, RECOIL = 12, 6, 3, 8, 4, 4
# Hold ticks at 24 fps for the fixed-length sequences.
IDLE_TICKS = [3] * IDLE
TURN_TICKS = [2, 2, 3, 3, 3, 3]
HIT_TICKS = [1, 2, 2]
FIRE_TICKS = [1, 1, 2, 2]
RECOIL_TICKS = [2, 2, 3, 3]

REST = (0., 0., 0., 1., 1.)


def P(rot=0., dx=0., dy=0., sx=1., sy=1.):
    return (rot, dx, dy, sx, sy)


def lerp_t(a, b, t):
    return tuple(x + (y - x) * t for x, y in zip(a, b))


def mix(pa, pb, t):
    keys = set(pa) | set(pb)
    return {k: lerp_t(pa.get(k, REST), pb.get(k, REST), t) for k in keys}


def add(pa, pb):
    keys = set(pa) | set(pb)
    out = {}
    for k in keys:
        a = pa.get(k, REST); b = pb.get(k, REST)
        out[k] = (a[0] + b[0], a[1] + b[1], a[2] + b[2], a[3] * b[3], a[4] * b[4])
    return out


def scale(p, s):
    return {k: (v[0] * s, v[1] * s, v[2] * s, 1 + (v[3] - 1) * s, 1 + (v[4] - 1) * s) for k, v in p.items()}


def ease_in_out(t):
    return t * t * (3 - 2 * t)


def ease_in(t):
    return t * t


def ease_out(t):
    return 1 - (1 - t) ** 2


def idle_loop(osc, n=IDLE):
    """osc: {part: [(component index, amplitude, phase[, cycles per loop]), ...]} -> n poses of a seamless loop."""
    frames = []
    for i in range(n):
        t = i / n
        pose = {}
        for part, terms in osc.items():
            v = list(REST)
            for term in terms:
                comp, amp, ph = term[:3]
                fq = term[3] if len(term) > 3 else 1
                w = math.sin(2 * math.pi * fq * t + ph)
                v[comp] += amp * w
            pose[part] = tuple(v)
        frames.append((pose, t))
    return frames


def turn(lean, n=TURN):
    """lean: pose at full lean (the trailing limbs). The lean swings in, overshoots a touch and settles to rest."""
    out = []
    for i in range(n):
        t = (i + 1) / n
        f = math.sin(math.pi * t) * (1.05 - .3 * t) if t < 1 else 0.
        out.append((scale(lean, f), t))
    return out


def hit(flinch, n=HIT):
    ks = [1., .65, .25]
    return [(scale(flinch, ks[i]), i / n) for i in range(n)]


def attack(wind, strike, tremble=None):
    """tell: rest -> wind (eased, the last frames trembling); fire: strike (beyond the wind) decaying toward a
    hold between strike and wind; recoil: hold -> rest with a small overshoot past rest."""
    tell = []
    for i in range(TELL):
        t = (i + 1) / TELL
        p = scale(wind, ease_in_out(t))
        if tremble and i >= TELL - 3:
            p = add(p, scale(tremble, 1 if i % 2 == 0 else -1))
        tell.append((p, t))
    fire = []
    hold = mix(strike, wind, .45)
    for i in range(FIRE):
        t = i / (FIRE - 1)
        fire.append((mix(strike, hold, ease_out(t)), t))
    recoil = []
    for i in range(RECOIL):
        t = (i + 1) / RECOIL
        # hold -> slightly past rest (-12% of hold) -> rest
        k = 1 - ease_in_out(min(1, t * 1.25))
        k = k - .12 * math.sin(math.pi * t) * (t > .5)
        recoil.append((scale(hold, k), t))
    return tell, fire, recoil
