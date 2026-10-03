"""World background vignettes: portrait phone frame (540 x 1170 u, rendered 2x),
built from parallax layers, with gameplay sprites composited at true game scale.

Scale: the camera shows ~5.7 world units across, so 1 world unit = 94.7 u here.
Ship 0.58 u, Kenney-size enemy 0.83 u, alien 0.64 u, asteroid 0.58 u, pickup
0.28 u, rail wall 0.64 u.
"""
import random
from akira import *
import sprites as S

W, H = 540, 1170
U = W / 5.7
BG_INK = "#0A0C1C"   # background ink: thinner and closer to the sky than INK

GRAIN = """<filter id="grain" x="0" y="0" width="100%" height="100%">
<feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="7"/>
<feColorMatrix type="matrix" values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 0.9 -0.35"/>
</filter>"""


def sky(id_, stops):
    st = "".join(f'<stop offset="{o}" stop-color="{c}"/>' for o, c in stops)
    return (f'<linearGradient id="{id_}" x1="0" y1="0" x2="0" y2="1">{st}</linearGradient>'
            f'<rect width="{W}" height="{H}" fill="url(#{id_})"/>')


def starfield(seed, n, colors):
    r = random.Random(seed)
    out = ""
    for _ in range(n):
        x, y = r.uniform(0, W), r.uniform(0, H)
        c = r.choice(colors)
        if r.random() < 0.12:
            s = r.uniform(3, 6)
            out += poly(star(x, y, s, s * 0.22, 4), c, f'opacity="{r.uniform(.5, .85):.2f}"')
        else:
            s = r.uniform(0.8, 1.8)
            out += f'<rect x="{f(x)}" y="{f(y)}" width="{f(s)}" height="{f(s)}" fill="{c}" opacity="{r.uniform(.3, .7):.2f}"/>'
    return out


def walls(fill, sh, panel, lamp, edge, ice=False):
    out = ""
    ww = 0.64 * U
    for side in (0, 1):
        x0 = 0 if side == 0 else W - ww
        inner = ww if side == 0 else W - ww
        out += f'<rect x="{f(x0)}" y="0" width="{f(ww)}" height="{H}" fill="{fill}"/>'
        # one hard shadow band on the inner face
        sx = inner - 12 if side == 0 else inner
        out += f'<rect x="{f(sx)}" y="0" width="12" height="{H}" fill="{sh}"/>'
        for k in range(0, H, 90):
            out += line([(x0 + 4, k), (x0 + ww - 4, k)], 2, BG_INK)
            out += line([(x0 + 10, k + 18), (x0 + ww - 18, k + 18)], 1.2, panel)
            ly = k + 50
            lx = inner - 20 if side == 0 else inner + 20
            out += f'<rect x="{f(lx - 4)}" y="{ly}" width="8" height="14" fill="{lamp}"/>'
            out += f'<rect x="{f(lx - 7)}" y="{ly - 4}" width="14" height="22" fill="{lamp}" opacity="0.45" filter="url(#glowS)"/>'
        if ice:
            r = random.Random(side + 3)
            for k in range(0, H, 46):
                d = r.uniform(10, 26)
                p = [(inner, k), (inner + (d if side == 0 else -d), k + 18), (inner, k + 40)]
                out += poly(p, "#2B6E86") + poly([p[0], p[1], (inner, k + 20)], "#9FE8F0", 'opacity="0.55"')
                out += inkpoly(p, 1.5, BG_INK)
        out += line([(inner, 0), (inner, H)], 3, BG_INK)
        out += line([(inner + (-2 if side == 0 else 2), 0), (inner + (-2 if side == 0 else 2), H)], 1.2, edge)
    return out


def speed_lines(seed, color, op):
    r = random.Random(seed)
    out = ""
    for _ in range(26):
        x = r.uniform(70, W - 70)
        y = r.uniform(-100, H)
        L = r.uniform(80, 260)
        out += line([(x, y), (x, y + L)], r.uniform(0.8, 1.6), color, f'opacity="{op * r.uniform(.5, 1):.2f}"')
    return out


def gameplay(layout):
    """Composite the sample sprites at true game scale."""
    out = ""
    for kind, x, y in layout:
        if kind == "player":
            w = 0.58 * U * 128 / 116
            out += embed(S.ship_with_exhaust(1), 128, 256, x - w / 2, y - w / 2, w, w * 2)
        elif kind == "fighter":
            w = 0.83 * U * 128 / 120
            out += embed(S.fighter(0), 128, 104, x - w / 2, y - w * 0.4, w, w * 104 / 128)
        elif kind == "alien":
            w = 0.64 * U * 128 / 104
            out += embed(S.alien(0), 128, 128, x - w / 2, y - w / 2, w, w)
        elif kind == "asteroid":
            w = 0.62 * U
            out += embed(S.asteroid(), 128, 128, x - w / 2, y - w / 2, w, w)
        elif kind == "dust":
            w = 0.34 * U
            out += embed(S.stardust(0), 64, 64, x - w / 2, y - w / 2, w, w)
        elif kind == "heal":
            w = 0.32 * U
            out += embed(S.heal(0), 64, 64, x - w / 2, y - w / 2, w, w)
        elif kind == "mine":
            w = 0.8 * U
            out += embed(S.mine(0), 128, 128, x - w / 2, y - w / 2, w, w)
    return out


def space():
    L = []
    L.append(("L0-sky", sky("skyS", [(0, NIGHT_0), (0.55, NIGHT_1), (0.85, INDIGO_0), (1, "#2A1E48")])))
    L.append(("L1-far-stars", starfield(1, 170, [BONE, CYAN, BONE, AMBER])))
    # L2: a flat-cel planet with Neo-Tokyo sodium lights on its night side
    pl = (f'<clipPath id="plc"><circle cx="440" cy="300" r="230"/></clipPath>'
          f'<circle cx="440" cy="300" r="236" fill="{TEAL}" opacity="0.18" filter="url(#glowM)"/>'
          f'<circle cx="440" cy="300" r="230" fill="{INDIGO_1}"/>'
          f'<g clip-path="url(#plc)"><circle cx="520" cy="380" r="250" fill="#141838"/>'
          f'<polygon points="210,250 680,180 680,214 210,288" fill="#22265A"/>')
    r = random.Random(4)
    for _ in range(130):
        a, d = r.uniform(0, 6.283), r.uniform(0, 230)
        x, y = 520 + d * math.cos(a) * 0.9, 400 + d * math.sin(a) * 0.9
        if (x - 440) ** 2 + (y - 300) ** 2 < 222 ** 2 and (x - 520) ** 2 + (y - 380) ** 2 < 240 ** 2:
            s = r.uniform(1.2, 3)
            pl += f'<rect x="{f(x)}" y="{f(y)}" width="{f(s)}" height="{f(s)}" fill="{r.choice([SODIUM, AMBER, SODIUM])}"/>'
    pl += (f'</g><circle cx="440" cy="300" r="230" fill="none" stroke="{BG_INK}" stroke-width="3"/>'
           f'<path d="M 270 160 A 230 230 0 0 1 560 104" fill="none" stroke="{CYAN}" stroke-width="2" opacity="0.5"/>')
    L.append(("L2-planet", pl))
    # L3: distant orbital wreck (low contrast, thin bg ink)
    st = [(60, 760), (230, 700), (250, 712), (300, 690), (320, 704), (180, 790), (60, 800)]
    mid = poly(st, "#1A1C3A") + inkpoly(st, 2, BG_INK)
    mid += poly([(120, 700), (132, 600), (140, 600), (138, 742)], "#1A1C3A") + inkpoly([(120, 700), (132, 600), (140, 600), (138, 742)], 2, BG_INK)
    for k in range(9):
        mid += f'<rect x="{90 + k * 22}" y="{760 - k * 7}" width="6" height="3" fill="{SODIUM}" opacity="0.8"/>'
    L.append(("L3-mid-structures", mid))
    # L4: near debris + speed lines (fastest parallax)
    near = ""
    for x, y, rr, rot in [(110, 420, 16, 10), (430, 880, 22, 40), (360, 1080, 12, 0)]:
        p = ngon(x, y, rr, 5, rot, 1, 0.7)
        near += poly(p, "#191B33") + inkpoly(p, 2, BG_INK)
    near += speed_lines(2, CYAN, 0.16)
    L.append(("L4-near-debris", near))
    L.append(("L5-walls", walls("#1B1C2E", "#111120", "#2C2E48", SODIUM, RED)))
    L.append(("L6-grain", f'<rect width="{W}" height="{H}" filter="url(#grain)" opacity="0.06"/>'))
    L.append(("gameplay", gameplay([("fighter", 210, 230), ("alien", 360, 420), ("mine", W - 0.64 * U - 30, 590),
                                    ("asteroid", 160, 640), ("dust", 280, 760), ("dust", 306, 800),
                                    ("heal", 390, 700), ("player", 270, 960)])))
    return "".join(f'<g id="{n}">{b}</g>\n' for n, b in L)


def frost():
    L = []
    L.append(("L0-sky", sky("skyF", [(0, "#04080F"), (0.5, "#0A1A2A"), (1, "#123248")])))
    aur = ""
    for k, (y, col, op) in enumerate([(120, TEAL, 0.16), (190, CYAN, 0.10)]):
        p = [(0, y)] + [(x, y + (14 if (x // 45) % 2 else -10) + k * 6) for x in range(0, W + 45, 45)] + [(W, y + 110), (0, y + 90)]
        aur += poly(p, col, f'opacity="{op}"')
    L.append(("L1-aurora", aur + starfield(5, 90, [BONE, CYAN])))
    # L2: frozen Neo-Tokyo skyline, sodium windows mostly out
    city = ""
    r = random.Random(9)
    x = 60
    while x < W - 60:
        w_ = r.uniform(26, 56)
        h_ = r.uniform(160, 420)
        top = 760 - h_
        p = [(x, 760), (x, top + 12), (x + 12, top), (x + w_, top), (x + w_, 760)]
        city += poly(p, "#0F2134") + inkpoly(p, 1.6, BG_INK)
        city += poly([(x, top + 12), (x + 12, top), (x + w_, top), (x + w_, top + 6), (x + 6, top + 14)], "#9FE8F0", 'opacity="0.35"')
        for _ in range(int(h_ / 30)):
            if r.random() < 0.45:
                city += f'<rect x="{f(x + r.uniform(4, w_ - 8))}" y="{f(r.uniform(top + 20, 740))}" width="3" height="4" fill="{SODIUM}" opacity="0.75"/>'
        x += w_ + r.uniform(4, 18)
    city += f'<rect x="0" y="760" width="{W}" height="{H - 760}" fill="#0C1A2A"/>'
    L.append(("L2-frozen-city", city))
    # L3: ice crags (mid), flat with a pale rim kick
    crag = ""
    for pts_ in ([(60, 1170), (60, 820), (130, 900), (170, 840), (240, 1000), (260, 1170)],
                 [(480, 1170), (480, 700), (420, 860), (380, 800), (330, 980), (300, 1170)]):
        crag += poly(pts_, "#16324A") + inkpoly(pts_, 2, BG_INK)
        crag += poly([pts_[1], pts_[2], (pts_[2][0], pts_[2][1] + 14), (pts_[1][0], pts_[1][1] + 18)], "#9FE8F0", 'opacity="0.4"')
    L.append(("L3-ice-crags", crag))
    flecks = ""
    r = random.Random(11)
    for _ in range(70):
        x, y, s = r.uniform(70, W - 70), r.uniform(0, H), r.uniform(1.5, 3.5)
        flecks += poly([(x, y - s), (x + s * 0.5, y), (x, y + s * 2.2), (x - s * 0.5, y)], CYAN, f'opacity="{r.uniform(.25, .6):.2f}"')
    L.append(("L4-ice-flecks", flecks + speed_lines(3, "#9FE8F0", 0.12)))
    L.append(("L5-walls", walls("#10283A", "#0A1A28", "#24506A", AMBER, CYAN, ice=True)))
    L.append(("L6-grain", f'<rect width="{W}" height="{H}" filter="url(#grain)" opacity="0.06"/>'))
    L.append(("gameplay", gameplay([("fighter", 330, 260), ("fighter", 180, 380), ("asteroid", 380, 560),
                                    ("dust", 210, 640), ("dust", 236, 676), ("alien", 260, 760), ("player", 300, 980)])))
    return "".join(f'<g id="{n}">{b}</g>\n' for n, b in L)


def all_scenes():
    return [
        ("world_space", f"<defs>{GRAIN}</defs>" + space(), "World vignette: Space. 540x1170u portrait, layers L0-L5 + gameplay at true scale."),
        ("world_frost", f"<defs>{GRAIN}</defs>" + frost(), "World vignette: Frost. 540x1170u portrait, layers L0-L5 + gameplay at true scale."),
    ]
