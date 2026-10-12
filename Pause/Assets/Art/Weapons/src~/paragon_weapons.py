#!/usr/bin/env python3
"""Paragon's ultimate art: weapon atlas, attack/power fx row, secret meter row.

    python3 paragon_weapons.py

Paragon is the premium ship (see docs / add-ship skill). Its ultimate, the
AEGIS BARRAGE, is a fan of cyan homing lances, so everything here is neon
blue / cyan (no pink: pink is Space's colour alone). Same flat cel style as
weapons.py: hard shapes, one ink outline, one shade tone, one highlight,
stepped halos, no gradients. Drawn at 4x and Lanczos-downsampled.

Writes
  Resources/Weapons/Paragon.png       2048 x 384, 128 px cells, the layout in
                                      WeaponArt.cs (charge / ready / shot /
                                      impact / muzzle / release / trail)
  Resources/Weapons/AttackFx.png      row 16 (index 15) appended or replaced:
                                      attack loop+burst, power loop+burst
  Resources/Weapons/SecretMeter.png   row 16 appended or replaced (64 px cells)
Run it any time; it is idempotent.
"""
import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.normpath(os.path.join(HERE, "..", "..", "Resources", "Weapons"))
S = 4
INK = (20, 12, 20, 255)
CYAN = (110, 242, 238, 255)
TEAL = (31, 181, 185, 255)
BLUE = (46, 155, 230, 255)
DEEP = (23, 76, 140, 255)
NIGHT = (26, 31, 69, 255)
BONE = (244, 234, 212, 255)
WHITE = (255, 255, 255, 255)
ROW = 15          # ship id 16 -> row index 15


def canvas(n=128):
    return Image.new("RGBA", (n * S, n * S), (0, 0, 0, 0)), n


def done(img, n):
    return img.resize((n, n), Image.LANCZOS)


def P(pts):
    return [(x * S, y * S) for x, y in pts]


def star(cx, cy, r_out, r_in, n=8, rot=0.0):
    pts = []
    for i in range(n * 2):
        r = r_out if i % 2 == 0 else r_in
        a = rot + math.pi * i / n
        pts.append((cx + math.cos(a) * r, cy + math.sin(a) * r))
    return pts


def poly(d, pts, fill, outline=INK, w=2):
    d.polygon(P(pts), fill=fill)
    d.line(P(pts + [pts[0]]), fill=outline, width=w * S, joint="curve")


def halo(d, cx, cy, r, steps=3, col=(110, 242, 238)):
    for i in range(steps):
        rr = r * (1 + .55 * (steps - i) / steps)
        d.ellipse([(cx - rr) * S, (cy - rr) * S, (cx + rr) * S, (cy + rr) * S],
                  fill=(*col, 34 + 20 * i))


# ---------------------------------------------------------------- shot ----


def lance(cx, cy, flame, smear=False, phase=0):
    """A cyan homing lance pointing up, centred on (cx, cy)."""
    img, n = canvas()
    d = ImageDraw.Draw(img)
    L, W = (46 if smear else 38), (6 if smear else 8)
    top, bot = cy - L / 2, cy + L / 2
    # exhaust
    fl = flame
    poly(d, [(cx - W * .6, bot - 2), (cx, bot + fl), (cx + W * .6, bot - 2)], BLUE, INK, 1)
    poly(d, [(cx - W * .3, bot - 2), (cx, bot + fl * .65), (cx + W * .3, bot - 2)], CYAN, CYAN, 1)
    # fins
    poly(d, [(cx - W, bot - 10), (cx - W - 7, bot + 2), (cx - W * .5, bot - 1)], DEEP)
    poly(d, [(cx + W, bot - 10), (cx + W + 7, bot + 2), (cx + W * .5, bot - 1)], DEEP)
    # body: needle with a swept nose
    body = [(cx, top - 6), (cx + W, top + 12), (cx + W * .8, bot), (cx - W * .8, bot), (cx - W, top + 12)]
    poly(d, body, TEAL, INK, 2)
    poly(d, [(cx, top - 4), (cx + W * .55, top + 12), (cx + W * .4, bot - 2), (cx, bot - 2)], BLUE, TEAL, 1)
    d.line(P([(cx - W * .45, top + 14 + phase), (cx - W * .45, bot - 6)]), fill=CYAN, width=2 * S)
    d.ellipse(P([(cx - 2, top + 4), (cx + 2, top + 8)]), fill=BONE)
    return done(img, n)


def shot_frames():
    out = []
    for i, fl in enumerate((14, 18, 12, 17)):
        out.append(lance(64, 58, fl, phase=i))
    out.append(lance(64, 56, 26, smear=True))
    out.append(lance(64, 56, 32, smear=True, phase=2))
    return out


def impact_frames():
    out = []
    spec = [(36, 12, WHITE, CYAN), (44, 18, BONE, CYAN), (40, 20, CYAN, BLUE), (34, 16, CYAN, DEEP),
            (26, 10, BLUE, DEEP), (16, 6, DEEP, NIGHT)]
    for i, (ro, ri, a, b) in enumerate(spec):
        img, n = canvas()
        d = ImageDraw.Draw(img)
        if i < 5:
            poly(d, star(64, 64, ro, ri, 7, .3), b, INK, 2)
            poly(d, star(64, 64, ro * .62, ri * .6, 7, .3), a, a, 1)
        for k in range(8):
            ang = k * math.pi / 4 + i * .2
            r0, r1 = ro * (.8 + i * .1), ro * (1.05 + i * .13)
            d.line(P([(64 + math.cos(ang) * r0, 64 + math.sin(ang) * r0),
                      (64 + math.cos(ang) * r1, 64 + math.sin(ang) * r1)]), fill=CYAN, width=2 * S)
        out.append(done(img, n))
    return out


def muzzle_frames():
    out = []
    for i, r in enumerate((10, 22, 30, 26)):
        img, n = canvas()
        d = ImageDraw.Draw(img)
        poly(d, star(64, 64, r, r * .35, 4, math.pi / 4 * (i % 2)), CYAN if i else BONE, INK, 1)
        if i >= 2:
            for k in range(8):
                a = k * math.pi / 4 + .4
                d.line(P([(64 + math.cos(a) * r, 64 + math.sin(a) * r),
                          (64 + math.cos(a) * (r + 12), 64 + math.sin(a) * (r + 12))]), fill=BONE, width=S)
        out.append(done(img, n))
    return out


def release_frames():
    out = []
    for i, (r, w) in enumerate(((24, 5), (38, 5), (50, 4), (58, 2))):
        img, n = canvas()
        d = ImageDraw.Draw(img)
        ring = [(64 + math.cos(a * math.pi / 4 + .39) * r, 64 + math.sin(a * math.pi / 4 + .39) * r) for a in range(8)]
        d.polygon(P(ring), outline=INK, width=(w + 2) * S)
        d.line(P(ring + [ring[0]]), fill=CYAN if i < 3 else BLUE, width=w * S)
        if i < 3:
            poly(d, star(64, 64, 14 - i * 3, 5, 4), BONE, INK, 1)
        out.append(done(img, n))
    return out


def trail_frames():
    out = []
    for dx in (0, 6):
        img, n = canvas()
        d = ImageDraw.Draw(img)
        d.line(P([(64 - dx / 2, 40), (64 + dx / 2, 88)]), fill=CYAN, width=5 * S)
        d.line(P([(64 - dx / 2, 46), (64 + dx / 2, 82)]), fill=BONE, width=2 * S)
        out.append(done(img, n))
    return out


# ------------------------------------------------------ charge indicator --


def pods(fill_n, pulse=0, ready=False):
    """Six missile pods round a core; `fill_n` of them lit."""
    img, n = canvas()
    d = ImageDraw.Draw(img)
    cx = cy = 64
    R = 34
    d.ellipse(P([(cx - R - 6, cy - R - 6), (cx + R + 6, cy + R + 6)]), outline=INK, width=3 * S)
    d.ellipse(P([(cx - R - 4, cy - R - 4), (cx + R + 4, cy + R + 4)]), outline=DEEP, width=2 * S)
    if ready:
        halo(d, cx, cy, 20 + pulse, 3)
    for k in range(6):
        a = -math.pi / 2 + k * math.pi / 3
        px, py = cx + math.cos(a) * R, cy + math.sin(a) * R
        lit = k < fill_n
        body = CYAN if lit else NIGHT
        edge = BONE if lit else DEEP
        pts = [(px, py - 11), (px + 5, py - 3), (px + 5, py + 9), (px - 5, py + 9), (px - 5, py - 3)]
        # rotate the pod to point outward
        rot = a + math.pi / 2
        c, s_ = math.cos(rot), math.sin(rot)
        pts = [(px + (x - px) * c - (y - py) * s_, py + (x - px) * s_ + (y - py) * c) for x, y in pts]
        poly(d, pts, body, INK, 2)
        d.line(P([pts[0], ((pts[2][0] + pts[3][0]) / 2, (pts[2][1] + pts[3][1]) / 2)]), fill=edge, width=S)
    core_r = 7 + (4 if fill_n >= 6 else 0) + pulse * .4
    col = BONE if fill_n >= 6 else (CYAN if fill_n >= 3 else BLUE)
    poly(d, star(cx, cy, core_r + 4, core_r - 1, 6), col, INK, 2)
    return done(img, n)


def charge_frames():
    return [pods(min(6, int(i / 15 * 6 + .0001) if i < 15 else 6)) for i in range(16)]


def ready_frames():
    return [pods(6, p, True) for p in (0, 3, 6, 3)]


def build_weapon_atlas():
    cols, rows = 16, 3
    atlas = Image.new("RGBA", (cols * 128, rows * 128), (0, 0, 0, 0))

    def put(frames, row, col):
        for i, f in enumerate(frames):
            atlas.alpha_composite(f, ((col + i) * 128, row * 128))

    put(charge_frames(), 0, 0)
    put(ready_frames(), 1, 0)
    put(shot_frames(), 1, 4)
    put(impact_frames(), 1, 10)
    put(muzzle_frames(), 2, 0)
    put(release_frames(), 2, 4)
    put(trail_frames(), 2, 8)
    atlas.save(os.path.join(OUT, "Paragon.png"), optimize=True)


# ------------------------------------------------- shared fx / meter rows --


def reticle(i):
    img, n = canvas()
    d = ImageDraw.Draw(img)
    r = 46 - i * 2
    for k in range(4):
        a = i * .25 + k * math.pi / 2
        d.arc(P([(64 - r, 64 - r), (64 + r, 64 + r)]), math.degrees(a) - 24, math.degrees(a) + 24,
              fill=CYAN, width=4 * S)
    poly(d, star(64, 64, 8, 3, 4), BONE, INK, 1)
    return done(img, n)


def burst(i):
    img, n = canvas()
    d = ImageDraw.Draw(img)
    r = 18 + i * 8
    poly(d, star(64, 64, r, r * .45, 8, i * .2), CYAN if i < 3 else BLUE, INK, 2)
    if i < 3:
        poly(d, star(64, 64, r * .55, r * .25, 8, i * .2), BONE, BONE, 1)
    return done(img, n)


def shield_loop(i):
    img, n = canvas()
    d = ImageDraw.Draw(img)
    r = 44
    hexa = [(64 + math.cos(k * math.pi / 3 + i * .08) * r, 64 + math.sin(k * math.pi / 3 + i * .08) * r) for k in range(6)]
    d.polygon(P(hexa), fill=(46, 155, 230, 46))
    d.line(P(hexa + [hexa[0]]), fill=INK, width=6 * S, joint="curve")
    d.line(P(hexa + [hexa[0]]), fill=CYAN, width=3 * S, joint="curve")
    k = (i % 4) * 1
    a, b = hexa[k], hexa[(k + 1) % 6]
    d.line(P([a, b]), fill=BONE, width=3 * S)
    return done(img, n)


def shield_pop(i):
    img, n = canvas()
    d = ImageDraw.Draw(img)
    r = 28 + i * 10
    hexa = [(64 + math.cos(k * math.pi / 3) * r, 64 + math.sin(k * math.pi / 3) * r) for k in range(6)]
    d.line(P(hexa + [hexa[0]]), fill=INK, width=6 * S, joint="curve")
    d.line(P(hexa + [hexa[0]]), fill=CYAN if i < 3 else BLUE, width=3 * S, joint="curve")
    return done(img, n)


def write_row(path, cell, cols, rows, frames_by_col):
    im = Image.open(path).convert("RGBA")
    if im.height < rows * cell:
        grown = Image.new("RGBA", (im.width, rows * cell), (0, 0, 0, 0))
        grown.alpha_composite(im, (0, 0))
        im = grown
    # clear then paint our row
    im.paste(Image.new("RGBA", (cols * cell, cell), (0, 0, 0, 0)), (0, ROW * cell))
    for col, f in frames_by_col.items():
        im.alpha_composite(f, (col * cell, ROW * cell))
    im.save(path, optimize=True)


def build_attackfx():
    frames = {}
    for i in range(4):
        frames[i] = reticle(i)
        frames[4 + i] = burst(i)
        frames[8 + i] = shield_loop(i)
        frames[12 + i] = shield_pop(i)
    write_row(os.path.join(OUT, "AttackFx.png"), 128, 16, 16, frames)


def meter_cell(fill_steps, ready=False, flash=False, icon=False, pulse=False):
    img, n = canvas(64)
    d = ImageDraw.Draw(img)
    c = 32
    if icon:
        hexa = [(c + math.cos(k * math.pi / 3) * 20, c + math.sin(k * math.pi / 3) * 20) for k in range(6)]
        poly(d, hexa, CYAN, INK, 2)
        poly(d, [(c, c - 10), (c + 8, c - 4), (c + 6, c + 8), (c, c + 12), (c - 6, c + 8), (c - 8, c - 4)], BONE, INK, 1)
        return done(img, n)
    if flash:
        poly(d, star(c, c, 31, 12, 6), CYAN, WHITE, 2)
    d.ellipse(P([(c - 24, c - 24), (c + 24, c + 24)]), fill=NIGHT, outline=INK, width=2 * S)
    arc_col = CYAN if ready else BLUE
    sweep = 360 * (fill_steps / 9.0)
    if sweep > 0:
        d.arc(P([(c - 20, c - 20), (c + 20, c + 20)]), -90, -90 + sweep, fill=arc_col, width=4 * S)
    # shield glyph
    gl = BONE if (ready and pulse) else (CYAN if ready else DEEP)
    shield = [(c, c - 11), (c + 9, c - 6), (c + 7, c + 7), (c, c + 12), (c - 7, c + 7), (c - 9, c - 6)]
    poly(d, shield, gl, INK, 1)
    return done(img, n)


def build_meter():
    frames = {}
    for i in range(10):
        frames[i] = meter_cell(i, ready=(i == 9))
    for i, p in enumerate((False, True, False, True)):
        frames[10 + i] = meter_cell(9, ready=True, pulse=p)
    frames[14] = meter_cell(9, ready=True, flash=True)
    frames[15] = meter_cell(0, icon=True)
    write_row(os.path.join(OUT, "SecretMeter.png"), 64, 16, 16, frames)


if __name__ == "__main__":
    build_weapon_atlas()
    build_attackfx()
    build_meter()
    print("wrote Paragon.png, AttackFx row 16, SecretMeter row 16")
