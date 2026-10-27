#!/usr/bin/env python3
"""Draws the shared quick-action glyphs (run from the repo root):
Resources/HudSkins/hud_home.png and hud_replay.png, white hollow outlines on a 256 px tile,
tinted per world in code (HudSkin.IconTint). Drawn at 4x and downsampled."""
import math
from PIL import Image, ImageDraw

OUT = "Pause/Assets/Art/Resources/HudSkins/"
SS = 4
TILE = 256 * SS
STROKE = 15 * SS      # 15 px of the 256 tile (was 10: ~1.5x)

def canvas(): return Image.new("L", (TILE, TILE), 0)

def stroke(d, pts, closed=False):
    pts = list(pts) + ([pts[0]] if closed else [])
    d.line(pts, fill=255, width=STROKE)
    r = STROKE / 2
    for x, y in pts: d.ellipse((x - r, y - r, x + r, y + r), fill=255)

def finish(mask, name):
    small = mask.resize((256, 256), Image.LANCZOS)
    out = Image.new("RGBA", (256, 256), (255, 255, 255, 0))
    out.putalpha(small)
    out.save(OUT + name)

def home():
    m = canvas(); d = ImageDraw.Draw(m)
    s = SS
    stroke(d, [(128*s, 36*s), (222*s, 124*s), (194*s, 124*s), (194*s, 220*s), (62*s, 220*s), (62*s, 124*s), (34*s, 124*s)], closed=True)
    stroke(d, [(108*s, 220*s), (108*s, 156*s), (148*s, 156*s), (148*s, 220*s)])
    finish(m, "hud_home.png")

def replay():
    m = canvas(); d = ImageDraw.Draw(m)
    s = SS; cx = cy = 128 * s; r = 76 * s
    a0, a1 = -40, 250      # degrees, clockwise from 3 o'clock, gap at the upper right
    pts = [(cx + r*math.cos(math.radians(a)), cy + r*math.sin(math.radians(a))) for a in [a0 + (a1 - a0) * i / 120 for i in range(121)]]
    stroke(d, pts)
    ex, ey = pts[-1]; th = math.radians(a1)
    tx, ty = -math.sin(th), math.cos(th)
    tip = (ex + tx * 18 * s, ey + ty * 18 * s)
    for sign in (1, -1):
        ang = math.atan2(ty, tx) + math.pi + sign * math.radians(38)
        stroke(d, [tip, (tip[0] + math.cos(ang) * 50 * s, tip[1] + math.sin(ang) * 50 * s)])
    finish(m, "hud_replay.png")

if __name__ == "__main__":
    home(); replay()
