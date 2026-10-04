#!/usr/bin/env python3
"""The pause-teleport kill: an enemy the ship blinks onto is "erased by the pause".

Neon pixel art (PixelKit, the rail-mine atlas look): instead of the fiery cel
explosion a weapon kill makes, the target freezes inside a time-stop viewfinder,
the PAUSE bars flash over it, a glitch tears it sideways, it implodes to a
pinpoint and then shatters outward as ice-cyan time crystals with violet glitch
pixels. Cyan / violet / bone only -- the player's red never appears on enemy FX.

    ../teleport_kill_atlas.png   8 frames, 64 game px each (1 world unit),
                                 exported x4 -> 256 px cells, 4 x 2 sheet,
                                 read left to right, top to bottom
    preview: --preview DIR       atlas on night + GIF at the game's timing

TeleportKillArt.cs slices the sheet (PPU 256) and plays it with TICKS below
(24 fps). Run:  python3 teleport_kill.py [--preview DIR]
"""
from __future__ import annotations

import math
import os
import random
import sys

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(HERE, "..", "..", "..", "PixelKit", "src~"))
import pixelkit as pk  # noqa: E402

N = 64                      # native frame size (game pixels)
C = N // 2                  # centre
COLS, ROWS = 4, 2
OUT = os.path.join(HERE, "..", "teleport_kill_atlas.png")
# Hold per frame in 1/24 s ticks -- TeleportKillArt.Ticks must match.
TICKS = [1, 2, 2, 1, 2, 2, 2, 3]

# Pause-blink palette: the teleport's ice cyan, a violet glitch, BONE light.
ICE = pk.ramp("#0b2f5a", "#1569c8", "#46ddfd", "#aaf2fb", "#f5fdfd")
VIOLET = pk.ramp("#2a1060", "#5a2ac0", "#9a5cff", "#d2b2ff")
BONE = "#f4ead4"
WHITE = "#ffffff"
GLOW = "#46c8fd"
GLOW_V = "#8a4cff"


def frame() -> np.ndarray:
    return pk.blank(N, N)


def neon(img: np.ndarray, color=GLOW, radius=2.4, strength=1.0) -> np.ndarray:
    """Stepped pixel halo behind every bright pixel (additive), sprite on top."""
    halo = pk.glow(pk.emissive_of(img, 150), color, radius=radius, strength=strength, steps=3)
    return pk.over(halo, img)


def brackets(img, r, length, color, thick=1):
    """Four corner brackets of a viewfinder square of half-size r."""
    for sx in (-1, 1):
        for sy in (-1, 1):
            x0, y0 = C + sx * r, C + sy * r
            for t in range(thick):
                h = pk.line(N, N, [(x0, y0 - sy * t), (x0 - sx * length, y0 - sy * t)])
                v = pk.line(N, N, [(x0 - sx * t, y0), (x0 - sx * t, y0 - sy * length)])
                pk.fill(img, h | v, color)
    return img


def pause_bars(img, h, w, gap, color_body=ICE[3], color_core=BONE, ink=True, slices=None):
    """The PAUSE glyph (two upright bars) centred, optionally glitch-sliced:
    slices = list of (row, dx) shifting single rows sideways."""
    m = pk.rect(N, N, C - gap // 2 - w, C - h // 2, C - gap // 2 - 1, C + h // 2) | \
        pk.rect(N, N, C + (gap + 1) // 2, C - h // 2, C + (gap + 1) // 2 + w - 1, C + h // 2)
    if slices:
        for row, dx in slices:
            line_ = np.zeros_like(m)
            line_[row] = m[row]
            m = (m & ~line_) | pk.shift(line_, dx, 0)
    if ink:
        pk.fill(img, pk.dilate(m) & ~m, pk.INK)
    pk.fill(img, m, tuple(color_body))
    inner = pk.erode(m)
    pk.fill(img, inner & ~pk.shift(inner, 1, 0), color_core)      # lit left edge of each bar
    pk.fill(img, inner & ~pk.shift(inner, 0, 1) & ~pk.shift(inner, 1, 0), WHITE)
    return img


def shard_mask(cx, cy, ang, length, width):
    """A thin crystal splinter pointing along `ang` (radians)."""
    tip = (cx + math.cos(ang) * length * .6, cy + math.sin(ang) * length * .6)
    tail = (cx - math.cos(ang) * length * .4, cy - math.sin(ang) * length * .4)
    nx, ny = -math.sin(ang) * width, math.cos(ang) * width
    mid = (cx + math.cos(ang) * length * .05, cy + math.sin(ang) * length * .05)
    return pk.poly(N, N, [tip, (mid[0] + nx, mid[1] + ny), tail, (mid[0] - nx, mid[1] - ny)])


def shards(img, radius, size, seed, count=9, fade=0):
    """Crystal splinters thrown out radially; `fade` steps them down the ramp."""
    rng = random.Random(seed)
    for i in range(count):
        a = (i / count) * 2 * math.pi + rng.uniform(-.25, .25)
        r = radius * rng.uniform(.75, 1.15)
        cx, cy = C + math.cos(a) * r, C + math.sin(a) * r
        ln = size * rng.uniform(.8, 1.25)
        m = shard_mask(cx, cy, a + rng.uniform(-.4, .4), ln, max(1.0, ln * .28))
        if not m.any():
            continue
        top = max(1, 4 - fade)
        pk.fill(img, pk.dilate(m) & ~m & (img[..., 3] == 0), pk.INK)
        pk.fill(img, m, tuple(ICE[max(0, top - 2)]))
        lit = m & ~pk.shift(m, 1, 1)          # facet catching the light (top-left)
        pk.fill(img, lit, tuple(ICE[top]))
        if fade == 0:
            core = pk.erode(m) if pk.erode(m).any() else m & ~pk.shift(m, -1, -1)
            pk.fill(img, core & lit, WHITE)
    return img


def glitch_px(img, n, spread, seed, colors=(VIOLET[2], VIOLET[3], ICE[2])):
    """Loose square 'dead pixels' and short scan-line dashes."""
    rng = random.Random(seed)
    for _ in range(n):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(spread * .45, spread)
        x, y = int(C + math.cos(a) * r), int(C + math.sin(a) * r)
        col = tuple(rng.choice(colors))
        if rng.random() < .4:
            ln = rng.randint(2, 5)
            pk.fill(img, pk.rect(N, N, x, y, x + ln, y), col)
        else:
            s = rng.choice((1, 1, 2))
            pk.fill(img, pk.rect(N, N, x, y, x + s - 1, y + s - 1), col)
    return img


def ring_mask(r, w=1):
    return pk.ring(N, N, C, C, r, r - w)


def dashed(mask, period=6, on=4, phase=0):
    yy, xx = np.mgrid[0:N, 0:N]
    a = (np.degrees(np.arctan2(yy - C, xx - C)) + 360 + phase) % 360
    return mask & ((a // (360 / (period * 6))) % period < on)


# --------------------------------------------------------------------------- frames

def f0_freeze():
    """Time stops: a hard white-cyan diamond pop inside a closing viewfinder."""
    img = frame()
    d = pk.star(N, N, C, C, 15, 6, points=4, rot=-math.pi / 2)
    pk.fill(img, pk.dilate(d) & ~d, pk.INK)
    pk.fill(img, d, tuple(ICE[3]))
    pk.fill(img, pk.erode(d, 2), WHITE)
    brackets(img, 25, 7, tuple(ICE[2]), thick=2)
    return neon(img, GLOW, 1.8, 1.0)


def f1_pause():
    """The PAUSE bars stamped over the target, ring and viewfinder locking on."""
    img = frame()
    pk.fill(img, ring_mask(21, 2), tuple(ICE[2]))
    pk.fill(img, ring_mask(21, 1) & ~pk.shift(ring_mask(21, 1), 1, 1), tuple(ICE[4]))
    pause_bars(img, 20, 6, 5)
    brackets(img, 21, 6, tuple(ICE[3]), thick=2)
    glitch_px(img, 6, 26, seed=11)
    return neon(img, GLOW, 2.6)


def f2_glitch():
    """The frozen target tears sideways: sliced bars, violet scan lines, ring closing."""
    img = frame()
    pk.fill(img, dashed(ring_mask(15, 2), 6, 4, 10), tuple(VIOLET[2]))
    slices = [(C - 8, 3), (C - 7, 3), (C - 2, -4), (C + 3, 2), (C + 4, 2), (C + 8, -3)]
    pause_bars(img, 18, 5, 4, color_body=ICE[2], slices=slices)
    for row, dx in ((C - 12, -10), (C + 1, 7), (C + 11, -6)):
        pk.fill(img, pk.rect(N, N, C + dx - 6, row, C + dx + 6, row), tuple(VIOLET[3]))
    brackets(img, 16, 5, tuple(VIOLET[3]))
    glitch_px(img, 12, 22, seed=21)
    return neon(img, GLOW_V, 2.2)


def f3_pinch():
    """Implosion: everything sucked into one hot point, streaks pulling inward."""
    img = frame()
    for i in range(8):
        a = i * math.pi / 4 + math.pi / 8
        x0, y0 = C + math.cos(a) * 22, C + math.sin(a) * 22
        x1, y1 = C + math.cos(a) * 8, C + math.sin(a) * 8
        pk.fill(img, pk.line(N, N, [(x0, y0), (x1, y1)]), tuple(ICE[2] if i % 2 else VIOLET[2]))
    core = pk.disc(N, N, C, C, 4)
    pk.fill(img, pk.dilate(core) & ~core, tuple(ICE[2]))
    pk.fill(img, core, WHITE)
    pk.twinkle(img, C, C, 9, tuple(ICE[3]), WHITE)
    return neon(img, GLOW, 3.2, 1.4)


def burst_frame(ring_r, ring_w, shard_r, shard_size, fade, glitch_n, seed, ring_dash=False,
                core_r=0, sparkles=0):
    img = frame()
    rm = ring_mask(ring_r, ring_w)
    if ring_dash:
        rm = dashed(rm, 5, 3, seed * 7)
    if ring_r > 0:
        pk.fill(img, rm, tuple(ICE[max(1, 3 - fade)]))
        pk.fill(img, rm & ~pk.shift(rm, 1, 1), tuple(ICE[max(2, 4 - fade)]))
    if core_r:
        core = pk.disc(N, N, C, C, core_r)
        pk.fill(img, core, BONE)
        pk.fill(img, pk.erode(core), WHITE)
    shards(img, shard_r, shard_size, seed, fade=fade)
    glitch_px(img, glitch_n, min(30, shard_r + 8), seed + 100)
    rng = random.Random(seed + 5)
    for _ in range(sparkles):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(shard_r * .6, min(29, shard_r + 6))
        pk.twinkle(img, C + math.cos(a) * r, C + math.sin(a) * r, rng.choice((1, 2)), tuple(ICE[3]), WHITE)
    return neon(img, GLOW if fade < 2 else GLOW_V, 2.0 if fade < 2 else 1.6, 1.0 - fade * .2)


def f4_shatter():
    return burst_frame(18, 2, 11, 9, 0, 6, seed=4, core_r=3, sparkles=3)


def f5_spread():
    return burst_frame(24, 2, 17, 8, 1, 9, seed=5, ring_dash=True, sparkles=3)


def f6_drift():
    return burst_frame(28, 1, 22, 6, 2, 10, seed=6, ring_dash=True, sparkles=2)


def f7_gone():
    img = frame()
    glitch_px(img, 9, 30, seed=77, colors=(VIOLET[1], VIOLET[2], ICE[1]))
    rng = random.Random(7)
    for _ in range(4):
        a = rng.uniform(0, 2 * math.pi)
        r = rng.uniform(20, 29)
        pk.twinkle(img, C + math.cos(a) * r, C + math.sin(a) * r, 1, tuple(ICE[2]), tuple(ICE[3]))
    return img


FRAMES = [f0_freeze, f1_pause, f2_glitch, f3_pinch, f4_shatter, f5_spread, f6_drift, f7_gone]


def render():
    frames = [f() for f in FRAMES]
    for f in frames:   # never the player's red anywhere
        rgb = f[f[..., 3] > 0][:, :3].astype(int)
        red = (rgb[:, 0] > 180) & (rgb[:, 1] < 90) & (rgb[:, 2] < 90)
        assert not red.any(), "player red in teleport-kill FX"
    return frames


def main():
    frames = render()
    atlas = pk.upscale(pk.sheet(frames, COLS))
    pk.save(atlas, OUT)
    print("wrote", os.path.normpath(OUT), atlas.shape[1], "x", atlas.shape[0])
    if "--preview" in sys.argv:
        out = sys.argv[sys.argv.index("--preview") + 1]
        os.makedirs(out, exist_ok=True)
        pk.save(pk.upscale(pk.on_bg(pk.sheet(frames, COLS), "#070a16"), 4),
                os.path.join(out, "teleport_kill_atlas_preview.png"))
        pk.save(atlas, os.path.join(out, "teleport_kill_atlas.png"))
        # loop with a beat of empty night between plays
        seq = frames + [frame()]
        pk.save_gif(seq, os.path.join(out, "teleport_kill.gif"), TICKS + [8], scale=4, bg="#070a16")
        print("preview in", out)


if __name__ == "__main__":
    main()
