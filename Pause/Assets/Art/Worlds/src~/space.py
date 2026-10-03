"""Space: indigo night with nebula glows, glinting stars, cel-shaded ringed
planets turning past, an orbital megastructure with searchlights, spiral
galaxies, comets with long light trails and shooting stars."""
import math
import random

from bgkit import (TAU, World, doc, blur, lin, wrap_y, path_smooth, pts, ink_attr, hard_glow, grain_defs, grain)
from palette import SPACE as P, BG_INK as INK, BONE

W, H = 512, 1024


def sky():
    rnd = random.Random(11)
    s = P["sky"]
    defs = (lin("bg", [(0, s[0], 1), (0.25, s[1], 1), (0.5, s[2], 1), (0.75, s[3], 1), (1, s[4], 1)])
            + lin("vig", [(0, P["vignette"], 0.8), (0.22, P["vignette"], 0), (0.78, P["vignette"], 0),
                          (1, P["vignette"], 0.8)], 0, 0, 1, 0)
            + blur("b60", 60) + blur("b30", 30) + grain_defs())
    body = [f'<rect width="{W}" height="{H}" fill="url(#bg)"/>']
    neb = []
    for i in range(14):
        x, y = rnd.uniform(-40, W + 40), rnd.uniform(0, H)
        c = rnd.choice(P["nebula"])
        neb.append((y, lambda dy, x=x, y=y, c=c, rx=rnd.uniform(90, 200), ry=rnd.uniform(140, 280),
                    a=rnd.uniform(0.25, 0.4), rot=rnd.uniform(-35, 35):
                    f'<ellipse cx="{x:.0f}" cy="{y + dy:.0f}" rx="{rx:.0f}" ry="{ry:.0f}" fill="{c}" '
                    f'opacity="{a:.2f}" transform="rotate({rot:.0f} {x:.0f} {y + dy:.0f})" filter="url(#b60)"/>'))
    for i in range(6):
        x, y = rnd.uniform(80, W - 80), rnd.uniform(0, H)
        c = rnd.choice(P["nebula"])
        neb.append((y, lambda dy, x=x, y=y, c=c, r=rnd.uniform(30, 60):
                    f'<ellipse cx="{x:.0f}" cy="{y + dy:.0f}" rx="{r:.0f}" ry="{r * 1.6:.0f}" fill="{c}" '
                    f'opacity="0.28" filter="url(#b30)"/>'))
    body.append(wrap_y(neb, H))
    stars = []
    for i in range(380):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        r = rnd.choice([0.5, 0.7, 0.9, 1.1])
        c = rnd.choice(P["stars"])
        a = rnd.uniform(0.2, 0.6)
        stars.append((y, lambda dy, x=x, y=y, r=r, c=c, a=a:
                      f'<rect x="{x - r:.1f}" y="{y + dy - r:.1f}" width="{2 * r:.1f}" height="{2 * r:.1f}" '
                      f'fill="{c}" opacity="{a:.2f}"/>'))
    for i in range(16):
        x, y = rnd.uniform(0, W), rnd.uniform(0, H)
        c = rnd.choice(P["stars"])
        stars.append((y, lambda dy, x=x, y=y, c=c: sparkle(x, y + dy, 5, c, 0.6)))
    body.append(wrap_y(stars, H))
    body.append(f'<rect width="{W}" height="{H}" fill="url(#vig)"/>')
    body.append(grain(W, H))
    return doc(W, H, "".join(body), defs)


def sparkle(x, y, r, c, a):
    """Four-point anime star glint, hard edged."""
    k = r * 0.18
    p = [(x, y - r), (x + k, y - k), (x + r, y), (x + k, y + k), (x, y + r), (x - k, y + k), (x - r, y), (x - k, y - k)]
    return f'<polygon points="{pts(p)}" fill="{c}" opacity="{a:.2f}"/>'


# ------------------------------------------------------------- planets ---

def terminator(cx, cy, R, lx=-0.42, ly=-0.36, k=1.12):
    """Hard cel shadow: the disc minus a lit circle offset toward the light."""
    return (f'<path fill-rule="evenodd" d="M{cx - 2 * R},{cy - 2 * R} h{4 * R} v{4 * R} h{-4 * R}Z '
            f'M{cx + lx * R + k * R},{cy + ly * R} a{k * R},{k * R} 0 1,0 {-2 * k * R},0 '
            f'a{k * R},{k * R} 0 1,0 {2 * k * R},0Z"/>')


def gas_giant(phase, size=252, seed=5):
    """Flat banded giant. Bands and storms sit at longitudes and are
    projected onto the disc; the pattern repeats every 180 deg, so `phase`
    over [0, 1) is half a turn and the flipbook loops."""
    rnd = random.Random(seed)
    R = size * 0.44
    cx = cy = size / 2
    rot = phase * math.pi

    def proj(lam, phi):
        return cx + R * math.cos(phi) * math.sin(lam - rot), cy - R * math.sin(phi)

    lats = [-1.45, -0.95, -0.62, -0.3, 0.05, 0.32, 0.7, 1.02, 1.45]
    waves = [(rnd.choice([2, 4]), rnd.uniform(0.03, 0.07), rnd.uniform(0, TAU)) for _ in lats]

    def boundary(i):
        f, a, p = waves[i]
        out = []
        for k in range(33):
            lam = rot - math.pi / 2 + math.pi * k / 32
            out.append(proj(lam, lats[i] + a * math.sin(f * lam + p)))
        return out

    defs = f'<clipPath id="disc"><circle cx="{cx}" cy="{cy}" r="{R}"/></clipPath>'
    g = [f'<circle cx="{cx}" cy="{cy}" r="{R}" fill="{P["planet_base"]}"/>']
    bounds = [boundary(i) for i in range(len(lats))]
    for i in range(0, len(lats) - 1, 2):
        shape = bounds[i] + list(reversed(bounds[i + 1]))
        g.append(f'<polygon points="{pts(shape)}" fill="{P["planet_band"]}"/>')
    # Thin hard streaks travel with the bands.
    for i in range(10):
        lat = rnd.uniform(-1.1, 1.1)
        lam0 = rnd.uniform(0, math.pi)
        for copy in (0, math.pi):
            ps = [proj(lam0 + copy + 0.6 * k / 7, lat) for k in range(8)
                  if math.cos(lam0 + copy + 0.6 * k / 7 - rot) > 0.05]
            if len(ps) > 1:
                g.append(f'<polyline points="{pts(ps)}" fill="none" stroke="{P["planet_hi"]}" stroke-width="2.4" '
                         f'stroke-linecap="butt" opacity="0.8"/>')
    for lam0, phi, sw, sh in [(0.6, -0.42, 0.30, 0.13), (2.0, 0.5, 0.12, 0.06)]:
        for copy in (0, math.pi):
            c = math.cos(lam0 + copy - rot)
            if c <= 0.08:
                continue
            x, y = proj(lam0 + copy, phi)
            g.append(f'<ellipse cx="{x:.1f}" cy="{y:.1f}" rx="{R * sw * c:.1f}" ry="{R * sh:.1f}" '
                     f'fill="{P["planet_spot"]}" {ink_attr(2)}/>')
            g.append(f'<ellipse cx="{x - R * sw * c * 0.25:.1f}" cy="{y - R * sh * 0.3:.1f}" rx="{R * sw * c * 0.4:.1f}" '
                     f'ry="{R * sh * 0.3:.1f}" fill="{P["planet_hi"]}"/>')
    g.append(f'<g fill="{P["planet_shadow"]}" opacity="0.92">{terminator(cx, cy, R)}</g>')
    # Rim light on the dark side, then a highlight crescent on the lit side.
    g.append(f'<circle cx="{cx}" cy="{cy}" r="{R - 4}" fill="none" stroke="{P["planet_rim"]}" stroke-width="3" '
             f'stroke-dasharray="{R * 1.2:.0f} {R * 5:.0f}" stroke-dashoffset="{-R * 0.2:.0f}" opacity="0.9"/>')
    g.append(f'<path d="M{cx - R * 0.78:.1f},{cy - R * 0.18:.1f} A{R * 0.82:.1f},{R * 0.82:.1f} 0 0,1 '
             f'{cx - R * 0.1:.1f},{cy - R * 0.8:.1f}" stroke="{P["planet_hi"]}" stroke-width="5" fill="none" '
             f'stroke-linecap="round"/>')
    body = (f'<g clip-path="url(#disc)">{"".join(g)}</g>'
            f'<circle cx="{cx}" cy="{cy}" r="{R}" fill="none" {ink_attr(5)}/>')
    return doc(size, size, body, defs)


def rocky(phase, size=128, seed=9):
    rnd = random.Random(seed)
    R = size * 0.42
    cx = cy = size / 2
    rot = phase * math.pi
    defs = f'<clipPath id="d"><circle cx="{cx}" cy="{cy}" r="{R}"/></clipPath>'
    g = [f'<circle cx="{cx}" cy="{cy}" r="{R}" fill="{P["planet_base"]}"/>']
    for i in range(12):
        lam0, phi, s = rnd.uniform(0, math.pi), rnd.uniform(-1.1, 1.1), rnd.uniform(0.1, 0.24)
        for copy in (0, math.pi):
            c = math.cos(lam0 + copy - rot)
            if c <= 0.1:
                continue
            x = cx + R * math.cos(phi) * math.sin(lam0 + copy - rot)
            y = cy - R * math.sin(phi)
            rx, ry = R * s * c, R * s * math.cos(phi) * 0.9 + 1
            g.append(f'<ellipse cx="{x:.1f}" cy="{y:.1f}" rx="{rx:.1f}" ry="{ry:.1f}" fill="{P["planet_band"]}" '
                     f'{ink_attr(1.4)}/>')
            g.append(f'<ellipse cx="{x + rx * 0.2:.1f}" cy="{y + ry * 0.25:.1f}" rx="{rx * 0.55:.1f}" ry="{ry * 0.45:.1f}" '
                     f'fill="{P["planet_shadow"]}"/>')
    g.append(f'<g fill="{P["planet_shadow"]}" opacity="0.92">{terminator(cx, cy, R)}</g>')
    g.append(f'<path d="M{cx - R * 0.75:.1f},{cy - R * 0.2:.1f} A{R * 0.8:.1f},{R * 0.8:.1f} 0 0,1 '
             f'{cx - R * 0.15:.1f},{cy - R * 0.76:.1f}" stroke="{P["planet_hi"]}" stroke-width="3" fill="none" '
             f'stroke-linecap="round"/>')
    body = f'<g clip-path="url(#d)">{"".join(g)}</g><circle cx="{cx}" cy="{cy}" r="{R}" fill="none" {ink_attr(3.5)}/>'
    return doc(size, size, body, defs)


def ring(phase, half):
    """Flat ring bands, 448 x 64 per half (back half behind the planet,
    front half over it). A highlight dash travels round the ring."""
    w, h = 448, 128
    cx, cy = w / 2, h / 2
    g = [f'<ellipse cx="{cx}" cy="{cy}" rx="210" ry="48" fill="{P["ring"]}"/>',
         f'<ellipse cx="{cx}" cy="{cy}" rx="186" ry="41" fill="{P["ring_shadow"]}"/>',
         f'<ellipse cx="{cx}" cy="{cy}" rx="170" ry="37" fill="{P["ring"]}"/>',
         f'<ellipse cx="{cx}" cy="{cy}" rx="132" ry="28" fill="#000"/>']
    a = phase * TAU
    for k, (r, ry) in enumerate([(198, 45), (152, 33)]):
        aa = a * (1.0 if k == 0 else 1.6)
        x0, y0 = cx + r * math.cos(aa), cy + ry * math.sin(aa)
        x1, y1 = cx + r * math.cos(aa + 0.5), cy + ry * math.sin(aa + 0.5)
        g.append(f'<path d="M{x0:.1f},{y0:.1f} A{r},{ry} 0 0,1 {x1:.1f},{y1:.1f}" stroke="{P["ring_hi"]}" '
                 f'stroke-width="4" fill="none"/>')
    # The hole is cut with a mask so the planet shows through.
    defs = (f'<mask id="hole" maskUnits="userSpaceOnUse" x="0" y="0" width="{w}" height="{h}">'
            f'<rect width="{w}" height="{h}" fill="#fff"/>'
            f'<ellipse cx="{cx}" cy="{cy}" rx="132" ry="28" fill="#000"/></mask>')
    ink = (f'<ellipse cx="{cx}" cy="{cy}" rx="210" ry="48" fill="none" {ink_attr(3)}/>'
           f'<ellipse cx="{cx}" cy="{cy}" rx="132" ry="28" fill="none" {ink_attr(3)}/>')
    clip_y = 0 if half == "back" else cy
    defs += f'<clipPath id="hh"><rect x="0" y="{clip_y}" width="{w}" height="{h / 2}"/></clipPath>'
    body = (f'<g transform="translate(0 {-clip_y})"><g clip-path="url(#hh)">'
            f'<g mask="url(#hole)">{"".join(g)}</g>{ink}</g></g>')
    return doc(w, h // 2, body, defs)


def galaxy(seed):
    rnd = random.Random(seed)
    s = 256
    c = s / 2
    defs = blur("b14", 14)
    g = [f'<circle cx="{c}" cy="{c}" r="{c * 0.75}" fill="{P["galaxy_arm"]}" opacity="0.25" filter="url(#b14)"/>']
    for arm in range(2):
        ps = []
        for k in range(40):
            t = k / 39
            a = arm * math.pi + t * 3.6
            r = 8 + t * c * 0.82
            ps.append((c + r * math.cos(a), c + r * math.sin(a)))
        for wdt, op in [(18, 0.25), (9, 0.45), (3, 0.9)]:
            g.append(f'<polyline points="{pts(ps)}" fill="none" stroke="{P["galaxy_arm"] if wdt > 3 else P["galaxy_core"]}" '
                     f'stroke-width="{wdt}" opacity="{op}" stroke-linecap="round" stroke-linejoin="round"/>')
        for k in range(18):
            t = rnd.uniform(0.1, 1)
            a = arm * math.pi + t * 3.6 + rnd.uniform(-0.2, 0.2)
            r = 8 + t * c * 0.82 + rnd.uniform(-8, 8)
            g.append(f'<rect x="{c + r * math.cos(a):.1f}" y="{c + r * math.sin(a):.1f}" width="2" height="2" '
                     f'fill="{P["galaxy_core"]}" opacity="0.85"/>')
    g.append(hard_glow(c, c, 36, P["galaxy_core"], 0.9, 3))
    return doc(s, s, "".join(g), defs)


def wisp(seed):
    """Nebula glow (lights may stay soft); tinted and spun at runtime."""
    rnd = random.Random(seed)
    s = 256
    defs = blur("b20", 20) + blur("b8", 8)
    g = []
    for i in range(9):
        g.append(f'<ellipse cx="{rnd.uniform(70, 186):.0f}" cy="{rnd.uniform(70, 186):.0f}" rx="{rnd.uniform(30, 60):.0f}" '
                 f'ry="{rnd.uniform(20, 50):.0f}" fill="#ffffff" opacity="{rnd.uniform(0.14, 0.26):.2f}" filter="url(#b20)"/>')
    for i in range(5):
        p = [(128 + rnd.uniform(-70, 70), 128 + rnd.uniform(-70, 70)) for _ in range(4)]
        g.append(f'<path d="{path_smooth(p, False)}" fill="none" stroke="#ffffff" stroke-width="{rnd.uniform(5, 10):.0f}" '
                 f'stroke-opacity="0.2" filter="url(#b8)"/>')
    return doc(s, s, "".join(g), defs)


def comet(phase):
    """Long light trail with a hard-bloom head; the trail flickers."""
    w, h = 320, 64
    f = math.sin(phase * TAU)
    defs = lin("t", [(0, P["comet_trail"], 0.85), (1, P["comet_trail"], 0)], 0, 0, 1, 0) + \
        lin("t2", [(0, P["comet_core"], 0.95), (1, P["comet_core"], 0)], 0, 0, 1, 0)
    g = [f'<polygon points="22,32 316,{10 + 4 * f:.0f} 316,{54 - 4 * f:.0f}" fill="url(#t)" opacity="0.6"/>',
         f'<polygon points="22,32 300,{24 + 3 * f:.0f} 300,{40 - 3 * f:.0f}" fill="url(#t2)"/>',
         f'<polygon points="30,30 250,{12 - 4 * f:.0f} 250,{16 - 4 * f:.0f}" fill="url(#t2)" opacity="0.5"/>',
         hard_glow(24, 32, 15, P["comet_trail"], 0.8, 3),
         f'<circle cx="24" cy="32" r="5" fill="{P["comet_core"]}"/>']
    return doc(w, h, "".join(g), defs)


def station(phase):
    """Orbital megastructure: angular hull, amber window rows that blink in
    sequence, red warning beacons, and two teal searchlights sweeping."""
    w, h = 320, 200
    rnd = random.Random(42)
    k = int(phase * 4)
    g = []
    # Searchlight cones first (behind the hull).
    for i, (bx, base_ang) in enumerate([(92, 120), (232, 60)]):
        ang = math.radians(base_ang + 18 * math.sin(phase * TAU + i * 2))
        L = 190
        x1 = bx + L * math.cos(ang - 0.09)
        y1 = 120 + L * math.sin(ang - 0.09)
        x2 = bx + L * math.cos(ang + 0.09)
        y2 = 120 + L * math.sin(ang + 0.09)
        g.append(f'<polygon points="{bx},120 {x1:.1f},{y1:.1f} {x2:.1f},{y2:.1f}" fill="{P["station_light"]}" opacity="0.16"/>')
        g.append(f'<polygon points="{bx},120 {(bx + x1) / 2 + (x2 - x1) * 0.2:.1f},{(120 + y1) / 2 + (y2 - y1) * 0.2:.1f} '
                 f'{(bx + x2) / 2 - (x2 - x1) * 0.2:.1f},{(120 + y2) / 2 - (y2 - y1) * 0.2:.1f}" fill="{P["station_light"]}" opacity="0.22"/>')
    hull = P["station_hull"]
    shade = P["station_shadow"]
    # Main spine and angular modules.
    parts = [
        [(20, 96), (300, 96), (310, 108), (300, 120), (20, 120), (10, 108)],
        [(70, 60), (130, 60), (140, 96), (60, 96)],
        [(180, 70), (250, 70), (262, 96), (170, 96)],
        [(110, 120), (210, 120), (196, 150), (124, 150)],
        [(140, 24), (156, 24), (160, 60), (136, 60)],
        [(40, 120), (64, 120), (58, 168), (46, 168)],
        [(256, 120), (280, 120), (274, 160), (262, 160)],
    ]
    for p in parts:
        g.append(f'<polygon points="{pts(p)}" fill="{hull}" {ink_attr(2.5)}/>')
        xs = [q[0] for q in p]
        ys = [q[1] for q in p]
        y_mid = (min(ys) + max(ys)) / 2
        sh = [(x, max(y, y_mid)) for x, y in p]
        g.append(f'<polygon points="{pts(sh)}" fill="{shade}"/>')
        g.append(f'<polygon points="{pts(p)}" fill="none" {ink_attr(2.5)}/>')
        g.append(f'<line x1="{min(xs) + 4}" y1="{min(ys) + 2.5}" x2="{max(xs) - 4}" y2="{min(ys) + 2.5}" '
                 f'stroke="{P["station_hi"]}" stroke-width="2"/>')
    # Solar wings: flat panels with ink grid.
    for x0 in (0, 248):
        g.append(f'<polygon points="{x0 + 8},40 {x0 + 64},40 {x0 + 64},88 {x0 + 8},88" fill="{shade}" {ink_attr(2)}/>')
        for gx in range(1, 4):
            g.append(f'<line x1="{x0 + 8 + gx * 14}" y1="40" x2="{x0 + 8 + gx * 14}" y2="88" stroke="{INK}" stroke-width="1"/>')
        g.append(f'<line x1="{x0 + 36}" y1="88" x2="{x0 + 36}" y2="96" stroke="{INK}" stroke-width="3"/>')
    # Window rows: amber, blinking in a travelling pattern.
    for row_y, x0, x1 in [(104, 30, 290), (110, 30, 290), (76, 72, 128), (82, 186, 248), (130, 126, 196)]:
        for i, x in enumerate(range(x0, x1, 7)):
            on = ((i + k * 3) % 11) < 8 and rnd.random() > 0.25
            if on:
                g.append(f'<rect x="{x}" y="{row_y}" width="4" height="3" fill="{P["station_window"]}"/>')
    for i, (x, y) in enumerate([(148, 22), (18, 108), (302, 108)]):
        if (k + i) % 2 == 0:
            g.append(hard_glow(x, y, 9, P["station_beacon"], 0.9, 3))
            g.append(f'<rect x="{x - 2}" y="{y - 2}" width="4" height="4" fill="{BONE}"/>')
    for bx in (92, 232):
        g.append(hard_glow(bx, 120, 8, P["station_light"], 0.9, 2))
    return doc(w, h, "".join(g))


def moon():
    s = 64
    R = 26
    g = [f'<circle cx="32" cy="32" r="{R}" fill="{P["planet_base"]}"/>',
         f'<ellipse cx="24" cy="38" rx="6" ry="5" fill="{P["planet_band"]}" {ink_attr(1.2)}/>',
         f'<ellipse cx="38" cy="24" rx="4" ry="3.5" fill="{P["planet_band"]}" {ink_attr(1.2)}/>',
         f'<g fill="{P["planet_shadow"]}" opacity="0.92">{terminator(32, 32, R)}</g>']
    defs = f'<clipPath id="c"><circle cx="32" cy="32" r="{R}"/></clipPath>'
    return doc(s, s, f'<g clip-path="url(#c)">{"".join(g)}</g><circle cx="32" cy="32" r="{R}" fill="none" {ink_attr(3)}/>', defs)


def star_sprite():
    s = 32
    body = hard_glow(16, 16, 9, BONE, 0.5, 2) + sparkle(16, 16, 15, BONE, 1.0)
    return doc(s, s, body)


def soft_dot():
    """A crisp diamond flake/spark with a small hard halo (particles)."""
    s = 32
    body = (hard_glow(16, 16, 14, BONE, 0.45, 2)
            + f'<polygon points="16,6 26,16 16,26 6,16" fill="{BONE}"/>')
    return doc(s, s, body)


def streak(w=128, h=8):
    """Speed line: hard-edged, tapering."""
    return doc(w, h, f'<polygon points="0,{h / 2} {w - 6},{h / 2 - 2} {w},{h / 2} {w - 6},{h / 2 + 2}" fill="{BONE}"/>')


def build():
    w = World("Space")
    w.tile("sky", sky())
    w.flipbook("anim", "giant", 12, lambda p: gas_giant(p))
    w.flipbook("anim", "rocky", 8, lambda p: rocky(p))
    w.flipbook("fx", "ringback", 4, lambda p: ring(p, "back"))
    w.flipbook("fx", "ringfront", 4, lambda p: ring(p, "front"))
    w.flipbook("fx", "comet", 4, comet)
    w.flipbook("fx", "station", 4, station, size=(288, 180))
    w.sprite("fx", "galaxy0", galaxy(1), size=(192, 192))
    w.sprite("fx", "galaxy1", galaxy(4), size=(192, 192))
    w.sprite("fx", "wisp0", wisp(7), size=(192, 192))
    w.sprite("fx", "wisp1", wisp(13), size=(192, 192))
    w.sprite("fx", "moon", moon())
    w.sprite("fx", "star", star_sprite())
    w.sprite("fx", "dot", soft_dot())
    w.sprite("fx", "streak", streak())
    w.pack()


def preview(t):
    """Approximates WorldBackdrop's Space set at a mid-run speed."""
    v = 30 * (0.15 + 0.25)
    fr = lambda n, fps: int(t * fps) % n
    c = [("tile", "sky", t * v * 0.010, 1, (1, 1, 1, 1)),
         ("sprite", "fx", "wisp0", -0.8, 2.0 - t * v * 0.018, 4.8, t * 3, (0.45, 0.22, 0.5, 0.28), False),
         ("sprite", "fx", "wisp1", 1.2, -2.5 - t * v * 0.018, 5.2, -t * 2.5, (0.2, 0.42, 0.55, 0.26), False),
         ("sprite", "fx", "galaxy0", -1.6, 4.6 - t * v * 0.026, 1.5, 20 + t * 4, (0.65, 0.6, 0.8, 0.55), False)]
    for i in range(26):
        x = ((i * 0.618) % 1) * 5.0 - 2.5
        y = (((i * 0.377) % 1) * 13 - t * v * 0.04) % 13 - 6.5
        tw = math.sin(t * (1.5 + i % 3 * 0.5) + i * 1.7)
        c.append(("sprite", "fx", "star", x, y, 0.12 + 0.05 * (i % 3), 0, (0.85, 0.92, 1, 0.75 if tw > 0.75 else 0.4), False))
    c.append(("sprite", "fx", f"station_{fr(4, 3):02d}", -1.3, 1.0 - t * v * 0.055 + 3, 2.6, 6, (0.78, 0.78, 0.86, 0.9), False))
    py = 3.0 - t * v * 0.07
    tint = (0.55, 0.72, 0.80, 1)
    rt = (0.66, 0.74, 0.84, 0.9)
    a = math.radians(-14)
    ox, oy = -math.sin(a) * 0.32 * 3.4 / 252 * 448 / 4.48, math.cos(a) * 0.32 * 3.4 / 252 * 448 / 4.48
    c.append(("sprite", "fx", f"ringback_{fr(4, 6):02d}", 1.0 + ox, py + oy, 3.4 * 448 / 252, -14, rt, False))
    c.append(("sprite", "anim", f"giant_{fr(12, 5):02d}", 1.0, py, 3.4, -14, tint, False))
    c.append(("sprite", "fx", f"ringfront_{fr(4, 6):02d}", 1.0 - ox, py - oy, 3.4 * 448 / 252, -14, rt, False))
    ma = t * 0.6
    c.append(("sprite", "fx", "moon", 1.0 + 2.6 * math.cos(ma), py + 0.8 * math.sin(ma), 0.45, 0,
              (0.72, 0.72, 0.82, 1), False))
    c.append(("sprite", "anim", f"rocky_{fr(8, 5):02d}", -1.7, -3.5 - t * v * 0.095 + 4, 1.0, 0,
              (0.82, 0.66, 0.52, 1), False))
    cx = 3.5 - (t % 8) * 1.2
    c.append(("sprite", "fx", f"comet_{fr(4, 10):02d}", cx, 5.0 - (t % 8) * 0.5, 2.4, -22,
              (0.75, 0.9, 1, 0.8), False))
    for i in range(12):
        x = ((i * 0.41) % 1) * 5 - 2.5
        y = (((i * 0.73) % 1) * 13 - t * v * 0.6) % 13 - 6.5
        c.append(("sprite", "fx", "streak", x, y, 0.5, 90, (0.6, 0.85, 1, 0.22), False))
    return c


if __name__ == "__main__":
    build()
