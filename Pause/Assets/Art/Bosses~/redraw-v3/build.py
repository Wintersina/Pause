#!/usr/bin/env python3
"""Boss redraw v3: one approved top-down drawing per boss -> every boss sheet, installed in place.

    python3 build.py [Space Frost ...] [--no-install] [--sheets DIR]

Input: the approved drawing (approved/<world>.png, a copy of the user's pick: ~160 px, transparent, facing DOWN).
Everything else is derived here by script (no repaint, no generation):

  * key      close any edge cut by the canvas, drop stray specks, hard alpha; NEAREST-scaled so its longest edge
             matches the OLD idle cell's (old/<World>.png, alpha > 128), centred in the cell. Every effect below is
             drawn at the drawing's own pixel grid (on a padded source canvas) BEFORE that one scale, so the
             flames, charges, cracks and smoke wear the same chunky pixels as the hull and every cell is registered.
  * body     <World>.png, the old layout (BossArt.cs): row 0 idle 0..3 | hit; row 1 tell0 a,b | tell1 a,b | fire;
             row 2 tell2 a,b | death 0..2; row 3 death 3..4 | retreat 0..1 | portrait; the expanded bosses (Space,
             Tide, Ember) add six idle cells (20..25) and three drawings per tell pose (26..34, the last = fire).
             Idle: engine flames at the drawing's own engines + a glow pulse on its own lights, in the world's cue hue.
             Tell: the firing part's lights ramp and a charge orb grows on its muzzle; fire: a muzzle flash there.
  * damage   <World>_damage.png 2 x 4: stage 1..4 cumulative cracks, scorch, dented / missing plates (dark gaps
             with the hull's hot internals showing) and real cut holes in the silhouette, from the hull's own
             pixels; cols A, B = the idle loop (flames + glow + embers flicker).
             BossImpact/attack/<world>_<pose>_stage<N>.png: every tell / fire pose re-rendered from the damaged hull
             (one drawing, the pose's own silhouette: no transfer, no ghosting).
  * fx       <World>_damage_fx.png 6 x 2: row 0 a heavy smoke loop rising off the breaches, row 1 an arc loop.
  * death    <World>_death.png 6 cells: flash through breaches -> split -> core overload -> fireball peak ->
             shockwave + smoke -> fading embers (no wider than the idle art). Body death cells 12..16 reuse them.
  * points   rig_<World>.json: every attack part's muzzle in every body drawing, 384 reference px (y down), snapped
             onto an interior pixel of the drawing; measure_emitters.py reads it into BossEmitterTable.cs.
  * hearts   the BossHearts.Body ellipse, re-measured from the new idle cells (printed).

Cards (name plates, no boss art), shots and attack shots are kept: they are already in the world's cue colour.
"""
import json, math, shutil, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ART = HERE.parents[1]                                   # .../Assets/Art
LIVE = ART / "Resources/Bosses"
IMPACT = ART / "Resources/BossImpact/attack"
OLD = HERE / "old"; OUT = HERE / "out"; APPROVED = HERE / "approved"
COLS = 5
PAD = 24                                                # source canvas padding (room for flames / blasts)

CUE = {"Space": 318, "Frost": 195, "Verdant": 135, "Ember": 32, "Tide": 188, "Desert": 45}
EXPANDED = {"Space", "Ember", "Tide", "Desert"}

# Per boss, in the approved drawing's own pixels (x right, y down):
#   engines: (x, y, dirx, diry, half width, length)  flames drawn behind the hull
#   lights:  (x, y, r)  where the cue-hued pixels pulse
#   parts:   attack part -> muzzle (snapped to the nearest interior pixel)
#   tells:   tell pose -> the parts that charge in it (BossCatalog / BossThemed / pattern tables)
RIG = {
    "Space": dict(
        core=(80, 74),
        engines=[(60, 3, 0, -1, 4.2, 10), (100, 3, 0, -1, 4.2, 10)],
        lights=[(60, 12, 10), (100, 12, 10), (80, 52, 5), (80, 74, 5), (60, 68, 4), (100, 68, 4), (43, 70, 3), (117, 70, 3),
                (80, 118, 5), (56, 130, 5), (104, 130, 5)],
        parts={"Chin": (80, 146), "Core": (80, 74), "PodL": (56, 131), "PodR": (104, 131)},
        tells={0: ["Chin"], 1: ["Core"], 2: ["PodL", "PodR"]}),
    "Frost": dict(
        core=(80, 73),
        engines=[(49, 14, 0, -1, 2.6, 7), (31, 30, 0, -1, 2.6, 7), (109, 14, 0, -1, 2.6, 7), (129, 30, 0, -1, 2.6, 7)],
        lights=[(80, 73, 13), (54, 96, 3), (106, 96, 3), (70, 127, 4), (90, 127, 4), (52, 115, 5), (108, 115, 5),
                (49, 22, 7), (31, 38, 7), (109, 22, 7), (129, 38, 7)],
        parts={"Jaw": (80, 147), "EyeL": (69, 127), "EyeR": (91, 127), "Crown": (80, 14)},
        tells={0: ["Jaw"], 1: ["EyeL", "EyeR"], 2: ["Crown"]}),
    "Verdant": dict(
        core=(80, 50),
        engines=[(61, 7, 0, -1, 3.4, 6), (98, 5, 0, -1, 3.4, 6)],
        lights=[(61, 15, 10), (98, 14, 10), (80, 50, 14), (70, 113, 5), (89, 113, 5)],
        parts={"Stinger": (80, 122), "PetalUL": (30, 20), "PetalUR": (128, 18), "PetalL": (8, 28), "PetalR": (152, 26),
               "PetalLL": (20, 78), "PetalLR": (140, 78), "CannonL": (33, 117), "CannonR": (127, 117), "Bulb": (80, 50)},
        # (acid cannons: the fore-leg joints, as wide apart as the old Queen's cannons -- the mandible tips sat 49 px apart
        #  and the two swept beams left no way through: ShipReachTest)
        tells={0: ["Stinger"], 1: ["PetalUL", "PetalUR", "PetalL", "PetalR", "PetalLL", "PetalLR"], 2: ["CannonL", "CannonR"]}),
    "Ember": dict(
        core=(80, 50),
        engines=[(68, 20, 0, -1, 3.4, 7), (92, 20, 0, -1, 3.4, 7)],   # (tips stay under the horns: CalmIdleTest holds the idle box within 3 %),
        lights=[(80, 50, 14), (80, 34, 4), (73, 124, 4), (87, 124, 4), (25, 85, 28), (135, 85, 28), (80, 88, 22)],
        parts={"Jaw": (80, 139), "Furnace": (80, 50), "Brow": (80, 117), "CannonL": (45, 52), "CannonR": (115, 52)},
        tells={0: ["Jaw"], 1: ["Furnace", "CannonL", "CannonR"], 2: ["Brow"]}),
    "Tide": dict(
        core=(80, 83),
        engines=[(35, 21, -.6, -.8, 3.2, 5), (53, 7, -.3, -1, 3.2, 5), (107, 7, .3, -1, 3.2, 5), (125, 21, .6, -.8, 3.2, 5)],
        lights=[(38, 27, 10), (55, 15, 10), (105, 15, 10), (123, 27, 10), (80, 83, 11),
                (12, 78, 7), (148, 78, 7), (22, 132, 7), (140, 132, 7), (56, 150, 7), (102, 150, 7)],
        # cannons: the two upper jet arms and the two side spear tips, high on the body like the old Kraken's (the lower
        # spear tips put the clusters ~1.3 u nearer the ship: BossLadderBotTest's dodge bot took 10.7 hits a minute)
        parts={"Beak": (80, 90), "LeftA": (38, 28), "LeftB": (14, 78), "RightA": (122, 28), "RightB": (146, 78)},
        tells={0: ["Beak"], 1: ["LeftA", "LeftB"], 2: ["RightA", "RightB"]}),
    # The Dune-Eater: a sand-bleached skull carrier. The crown furnace's flame is its engine, the brass lamp below it the
    # lens (beam, sandstorm wall), the fanged maw the grit fan, the right claw the crane that drops its mines.
    "Desert": dict(
        core=(80, 50),
        engines=[(80, 9, 0, -1, 3.4, 6)],
        lights=[(80, 20, 12), (80, 50, 9), (62, 82, 5), (98, 82, 5), (58, 99, 3), (102, 99, 3), (71, 108, 4), (89, 108, 4)],
        light_sv=(.55, .72),
        parts={"Maw": (80, 122), "Boom": (135, 130), "Lens": (80, 50)},
        tells={0: ["Maw"], 1: ["Boom"], 2: ["Lens"]}),
}
# Smoke tint per world (steam for Frost, spore haze for Verdant).
SMOKE = {"Space": ((58, 52, 66), (104, 96, 112), (150, 142, 156)), "Frost": ((120, 140, 160), (176, 198, 214), (226, 240, 248)),
         "Verdant": ((52, 64, 46), (92, 108, 80), (140, 156, 122)), "Ember": ((50, 44, 42), (92, 82, 76), (140, 128, 116)),
         "Tide": ((52, 66, 76), (96, 114, 124), (150, 170, 178)), "Desert": ((66, 54, 40), (118, 100, 76), (176, 156, 120))}


# ------------------------------------------------------------------------------------------------ colour helpers

def hsv2rgb(h, s, v):
    h = (h % 360) / 60.0
    c = v * s; x = c * (1 - abs(h % 2 - 1)); m = v - c
    r, g, b = [(c, x, 0), (x, c, 0), (0, c, x), (0, x, c), (x, 0, c), (c, 0, x)][int(h) % 6]
    return (int(round((r + m) * 255)), int(round((g + m) * 255)), int(round((b + m) * 255)))


def palette(world):
    h = CUE[world]
    return dict(dark=hsv2rgb(h, .85, .45), mid=hsv2rgb(h, .8, .85), light=hsv2rgb(h, .45, 1.0), core=hsv2rgb(h, .12, 1.0),
                hot=hsv2rgb(h + (8 if world != "Ember" else 18), .6, 1.0))


def hsv_arrays(a):
    rgb = a[..., :3].astype(np.float32) / 255
    mx = rgb.max(-1); mn = rgb.min(-1); d = mx - mn
    h = np.zeros_like(mx)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    nz = d > 1e-6
    i = nz & (mx == r); h[i] = ((g - b)[i] / d[i]) % 6
    i = nz & (mx == g) & ~(mx == r); h[i] = (b - r)[i] / d[i] + 2
    i = nz & (mx == b) & ~(mx == r) & ~(mx == g); h[i] = (r - g)[i] / d[i] + 4
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    return h * 60, s, mx


# ------------------------------------------------------------------------------------------------ keying

def close_cut(a):
    """A drawing that runs off its canvas has a flat sliced edge: close that edge with the outline colour (paragon build.py)."""
    op = a[:, :, 3] > 128
    ink = a[op][np.argmin(a[op][:, :3].astype(int).sum(1))]
    a = np.pad(a, ((2, 2), (2, 2), (0, 0)))
    op = a[:, :, 3] > 128; H, W = op.shape
    if op[2].any(): a[1][op[2]] = ink
    if op[H - 3].any(): a[H - 2][op[H - 3]] = ink
    if op[:, 2].any(): a[:, 1][op[:, 2]] = ink
    if op[:, W - 3].any(): a[:, W - 2][op[:, W - 3]] = ink
    return a


def components(mask):
    H, W = mask.shape
    lab = np.zeros(mask.shape, np.int32); n = 0; sizes = [0]
    for y, x in zip(*np.nonzero(mask)):
        if lab[y, x]: continue
        n += 1; st = [(y, x)]; lab[y, x] = n; c = 0
        while st:
            cy, cx = st.pop(); c += 1
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    yy, xx = cy + dy, cx + dx
                    if 0 <= yy < H and 0 <= xx < W and mask[yy, xx] and not lab[yy, xx]:
                        lab[yy, xx] = n; st.append((yy, xx))
        sizes.append(c)
    return lab, sizes


def key_source(path, world):
    a = np.array(Image.open(path).convert("RGBA"))
    a[..., 3] = np.where(a[..., 3] > 128, 255, 0)
    a = close_cut(a)
    lab, sizes = components(a[..., 3] > 0)                 # stray specks (a lone grey pixel off the leaves) go
    for i, s in enumerate(sizes):
        if i and s < 8: a[lab == i] = 0
    if world != "Space":                                    # NoPinkOutsideSpace: nudge pinkish reds (Tide's coral) to red
        h, s, v = hsv_arrays(a)
        pink = (a[..., 3] > 24) & (v > .25) & (s * v >= .035) & (h >= 280) & (h < 352)
        if pink.any():
            for y, x in zip(*np.nonzero(pink)):
                r, g, b = [int(c) for c in a[y, x, :3]]
                mx = max(r, g, b)
                a[y, x, :3] = (mx, min(g, b), min(g, b) // 2) if s[y, x] > .3 else (mx, mx - 4, mx - 8)
    a = np.pad(a, ((PAD, PAD), (PAD, PAD), (0, 0)))
    return a


def bbox(mask):
    ys, xs = np.nonzero(mask)
    return xs.min(), ys.min(), xs.max() + 1, ys.max() + 1


# ------------------------------------------------------------------------------------------------ the boss

class Boss:
    def __init__(self, world):
        self.w = world
        self.rig = RIG[world]
        self.pal = palette(world)
        self.base = key_source(APPROVED / f"{world}.png", world)
        self.hull = self.base[..., 3] > 0
        old = Image.open(OLD / f"{world}.png").convert("RGBA")
        self.C = old.width // COLS
        idle = 20 if world in EXPANDED else 0
        r, c = divmod(idle, COLS)
        oa = np.asarray(old.crop((c * self.C, r * self.C, c * self.C + self.C, r * self.C + self.C)))[..., 3] > 128
        ox0, oy0, ox1, oy1 = bbox(oa)
        x0, y0, x1, y1 = bbox(self.hull)
        self.old_edge = max(ox1 - ox0, oy1 - oy0)
        self.f = self.old_edge / max(x1 - x0, y1 - y0)
        self.ox = self.C / 2 - (x0 + x1) / 2 * self.f
        self.oy = self.C / 2 - (y0 + y1) / 2 * self.f
        self.src_off = (2 - PAD, 2 - PAD)        # approved px -> padded px: + PAD - 2 (close_cut pads 2)
        self.rng = np.random.default_rng(sum(map(ord, world)))
        self.lights = self.light_mask()
        self.damage = self.plan_damage()

    # approved-drawing coords -> padded canvas coords
    def P(self, x, y): return (x + PAD + 2, y + PAD + 2)

    # padded canvas px (pixel centre) -> cell px
    def cell_xy(self, x, y): return (self.ox + (x + .5) * self.f, self.oy + (y + .5) * self.f)

    def render(self, a):
        H, W = a.shape[:2]
        im = Image.fromarray(a, "RGBA").resize((round(W * self.f), round(H * self.f)), Image.NEAREST)
        cell = Image.new("RGBA", (self.C, self.C))
        cell.paste(im, (round(self.ox), round(self.oy)), im)
        c = np.array(cell)
        # per-texel grain (+-1 a channel, one fixed field for every cell, so registered drawings stay pixel-equal where they
        # agree): a NEAREST upscale is otherwise all identical neighbours (EnemyResolutionTest: identical pairs <= 0.75)
        if getattr(self, "_grain", None) is None or self._grain.shape[0] != self.C:
            self._grain = np.random.default_rng(77).integers(-1, 2, (self.C, self.C, 3))
        op = c[..., 3] > 0
        g = np.clip(c[..., :3].astype(int) + self._grain, 0, 255)
        c[..., :3] = np.where(op[..., None], g, c[..., :3]).astype(np.uint8)
        m = 6                                     # nothing within 6 px of the cell border
        c[:m] = 0; c[-m:] = 0; c[:, :m] = 0; c[:, -m:] = 0
        return c

    # ---------------------------------------------------------------- lights / flames / charges

    def light_mask(self):
        h, s, v = hsv_arrays(self.base)
        cue = CUE[self.w]
        dh = np.abs((h - cue + 180) % 360 - 180)
        smin, vmin = self.rig.get("light_sv", (.35, .5))
        band = self.hull & (dh <= 32) & (s > smin) & (v > vmin)
        H, W = band.shape
        yy, xx = np.mgrid[:H, :W]
        near = np.zeros_like(band)
        for x, y, r in self.rig["lights"]:
            px, py = self.P(x, y)
            near |= (xx - px) ** 2 + (yy - py) ** 2 <= r * r
        return band & near

    def glow(self, a, k, mask=None):
        a = a.copy()
        m = (self.lights if mask is None else mask) & (a[..., 3] > 0)
        rgb = a[..., :3].astype(np.float32)
        rgb[m] = np.clip(rgb[m] * (1 + k) + 255 * max(0, k) * .22, 0, 255)
        a[..., :3] = rgb.astype(np.uint8)
        return a

    # Idle engine flames (user, 2026-11-27: "bigger and clearly animated"): FLAME_LONG x the rig length, FLAME_WIDE x its
    # width, a length cycle that swings ~2:1 between idle drawings, a forked / spitting tip on alternate drawings, and a
    # white-hot cue-tinted core. Capped so a flame never reaches the 8 px band at the cell's edge.
    FLAME_LONG, FLAME_WIDE = 1.75, 1.3
    FLAME_CYCLE = (1.0, .58, .88, .5, 1.12, .7)

    def flames(self, a, t, scale=1.0):
        """Engine flames behind the hull: a tapered tongue per engine; length, wobble and tip step with t."""
        out = a.copy()
        hull = a[..., 3] > 0
        P = self.pal
        H, W = hull.shape
        for i, (x, y, dx, dy, hw, L) in enumerate(self.rig["engines"]):
            n = math.hypot(dx, dy); dx, dy = dx / n, dy / n
            px, py = self.P(x, y)
            ph = (t + i * 2) % 6
            Lt = L * scale * self.FLAME_LONG * self.FLAME_CYCLE[ph]
            hw = hw * self.FLAME_WIDE * (1.0 if ph % 2 == 0 else .85)
            # the cell's 8 px margin, in source px along the flame
            cap = 1e9
            for c0, d, off in ((px, dx, self.ox), (py, dy, self.oy)):
                o0, lim = (8 - off) / self.f, (self.C - 8 - off) / self.f
                if d < -1e-6: cap = min(cap, (c0 - o0) / -d)
                if d > 1e-6: cap = min(cap, (lim - c0) / d)
            Lt = min(Lt, max(1.0, cap - 3.5))
            wob = (0, .7, 0, -.7, .4, -.4)[ph]
            fork = ph % 2 == 1
            tot = Lt + 3.0
            for s in np.arange(0, tot, .25):
                k = s / tot
                half = hw * (1 - k) ** .6
                cx = px + dx * s - dy * wob * k * 2
                cy = py + dy * s + dx * wob * k * 2
                for o in np.arange(-half, half + .01, .25):
                    if fork and k > .72 and abs(o) < half * .35: continue          # a forked tip
                    X, Y = int(round(cx - dy * o)), int(round(cy + dx * o))
                    if not (0 <= X < W and 0 <= Y < H) or hull[Y, X]: continue
                    edge = abs(o) / max(half, .01)
                    col = (P["core"] if (k < .5 and edge < .55) else P["light"] if (k < .8 and edge < .85)
                           else P["mid"] if k < .94 else P["dark"])
                    out[Y, X] = (*col, 255)
            if fork:                                                             # a spark spat off the tip
                X = int(round(px + dx * (tot + 1.5))); Y = int(round(py + dy * (tot + 1.5)))
                if 0 <= X < W and 0 <= Y < H and not hull[Y, X] and cap > tot + 2: out[Y, X] = (*P["light"], 255)
        return out

    def part_mask(self, parts, r=9):
        H, W = self.hull.shape
        yy, xx = np.mgrid[:H, :W]
        m = np.zeros((H, W), bool)
        for p in parts:
            px, py = self.muzzle_src(p)
            m |= (xx - px) ** 2 + (yy - py) ** 2 <= r * r
        h, s, v = hsv_arrays(self.base)
        return m & self.hull & ((self.lights) | (v > .55))

    def orb(self, a, x, y, r):
        P = self.pal
        H, W = a.shape[:2]
        for Y in range(int(y - r - 2), int(y + r + 3)):
            for X in range(int(x - r - 2), int(x + r + 3)):
                if not (0 <= X < W and 0 <= Y < H): continue
                d = math.hypot(X - x, Y - y)
                if d > r + .5: continue
                col = P["core"] if d < r * .45 else P["light"] if d < r * .8 else P["mid"]
                a[Y, X] = (*col, 255)
        if r >= 2.5:                                       # sparkle cross
            for o in (-int(r) - 2, int(r) + 2):
                for X, Y in ((int(x) + o, int(y)), (int(x), int(y) + o)):
                    if 0 <= X < W and 0 <= Y < H: a[Y, X] = (*P["light"], 255)
        return a

    def flash(self, a, x, y, r):
        P = self.pal
        H, W = a.shape[:2]
        for ang in range(0, 360, 45):
            L = r * (1.0 if ang % 90 == 0 else .6) * (1.25 if ang == 90 else 1)   # the ray toward the player is longest
            for s in np.arange(0, L, .5):
                X = int(round(x + math.cos(math.radians(ang)) * s)); Y = int(round(y + math.sin(math.radians(ang)) * s))
                if 0 <= X < W and 0 <= Y < H:
                    a[Y, X] = (*(P["core"] if s < L * .5 else P["light"]), 255)
        return self.orb(a, x, y, r * .45)

    # ---------------------------------------------------------------- poses

    def muzzle_src(self, part):
        """The part's muzzle on the padded canvas: the nearest interior hull pixel (3 x 3 opaque) to the rig point."""
        if not hasattr(self, "_mz"):
            self._mz = {}
            core = self.hull.copy()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    core &= np.roll(np.roll(self.hull, dy, 0), dx, 1)
            ys, xs = np.nonzero(core)
            for name, (x, y) in self.rig["parts"].items():
                px, py = self.P(x, y)
                i = np.argmin((xs - px) ** 2 + (ys - py) ** 2)
                self._mz[name] = (int(xs[i]), int(ys[i]))
        return self._mz[part]

    def idle(self, a, t, n):
        k = (0, .16, .06, .24, .1, .2)[t % 6] if n == 6 else (0, .18, .06, .26)[t % 4]
        return self.glow(self.flames(a, t), k)

    def tell(self, a, pose, stage, stages):
        """stage 0 .. stages-1 charges; stage == stages is the fire drawing."""
        parts = self.rig["tells"][pose]
        a = self.flames(a, 1, 1.15)
        a = self.glow(a, .2)
        if stage >= stages:
            a = self.glow(a, 1.0, self.part_mask(parts))
            for p in parts:
                x, y = self.muzzle_src(p); self.flash(a, x, y, 10)
            return a
        k = (stage + 1) / stages
        a = self.glow(a, .35 + .6 * k, self.part_mask(parts))
        for p in parts:
            x, y = self.muzzle_src(p); self.orb(a, x, y, 2.0 + 3.2 * k)
        return a

    def hit(self, a):
        a = self.flames(a, 0).copy()
        op = a[..., 3] > 0
        rgb = a[..., :3].astype(np.float32)
        rgb[op] = rgb[op] * .45 + 255 * .55
        a[..., :3] = rgb.astype(np.uint8)
        return a

    def retreat(self, a, n):
        a = self.flames(a, n, 1.4)
        a = self.glow(a, .3)
        rng = np.random.default_rng(40 + n)
        x0, y0, x1, y1 = bbox(self.hull)
        H, W = self.hull.shape
        for _ in range(10 + 8 * n):
            x = int(rng.integers(x0 + 4, x1 - 4)); y = int(rng.integers(y0, y1 - 20)); L = int(rng.integers(14, 34))
            col = self.pal["light"] if rng.random() < .6 else self.pal["core"]
            for Y in range(y, min(y + L, H)):
                a[Y, x] = (*col, 255)
        return a

    # ---------------------------------------------------------------- damage

    def plan_damage(self):
        """Features, each tagged with the stage it appears at (cumulative)."""
        rng = self.rng
        hull = self.hull
        inner = hull.copy()
        for _ in range(3):
            e = inner.copy()
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    e &= np.roll(np.roll(inner, dy, 0), dx, 1)
            inner = e
        ys, xs = np.nonzero(inner)
        keep = [self.muzzle_src(p) for p in self.rig["parts"]]
        def far(x, y, d=9): return all((x - kx) ** 2 + (y - ky) ** 2 > d * d for kx, ky in keep)
        def pick(d=9):
            for _ in range(400):
                i = rng.integers(len(xs))
                if far(xs[i], ys[i], d): return int(xs[i]), int(ys[i])
            return int(xs[0]), int(ys[0])
        feats = []
        for stage, (cracks, scorch, plates, holes) in {1: (4, 4, 1, 0), 2: (5, 5, 2, 1), 3: (8, 6, 3, 2), 4: (11, 7, 4, 3)}.items():
            for _ in range(cracks):
                x, y = pick(4)
                feats.append(dict(kind="crack", stage=stage, x=x, y=y, ang=rng.uniform(0, 2 * math.pi), L=int(rng.integers(14, 30)), seed=int(rng.integers(1 << 30))))
            for _ in range(scorch):
                x, y = pick(4)
                feats.append(dict(kind="scorch", stage=stage, x=x, y=y, r=rng.uniform(4, 8), seed=int(rng.integers(1 << 30))))
            for _ in range(plates):
                x, y = pick(10)
                feats.append(dict(kind="plate", stage=stage, x=x, y=y, w=int(rng.integers(8, 14)), h=int(rng.integers(6, 10)), seed=int(rng.integers(1 << 30))))
            # holes: bites out of the silhouette's edge, clear of the muzzles
            edge = hull & ~inner
            eys, exs = (np.nonzero(edge) if stage % 2 else np.nonzero(inner))
            for _ in range(holes):
                for _ in range(400):
                    i = rng.integers(len(exs))
                    if far(exs[i], eys[i], 14): break
                feats.append(dict(kind="hole", stage=stage, x=int(exs[i]), y=int(eys[i]), r=rng.uniform(4.5, 7.5), seed=int(rng.integers(1 << 30))))
        return feats

    def damaged(self, stage):
        a = self.base.copy()
        if stage <= 0: return a
        H, W = self.hull.shape
        hot = self.pal["hot"]; ember = (255, 170, 60) if self.w != "Frost" else (200, 240, 255)
        for f in self.damage:
            if f["stage"] > stage: continue
            rng = np.random.default_rng(f["seed"])
            if f["kind"] == "scorch":
                r = f["r"] * (1 + .15 * (stage - f["stage"]))
                for Y in range(int(f["y"] - r - 1), int(f["y"] + r + 2)):
                    for X in range(int(f["x"] - r - 1), int(f["x"] + r + 2)):
                        if 0 <= X < W and 0 <= Y < H and a[Y, X, 3]:
                            d = math.hypot(X - f["x"], Y - f["y"]) / r + rng.uniform(-.25, .25)
                            if d < 1:
                                g = int(a[Y, X, :3].astype(int).mean())
                                soot = (np.array([g, g, g]) * (.18 + .45 * d)).astype(int)
                                if .55 < d < .85 and (X + Y) % 2: soot = np.array([96, 62, 44]) if self.w != "Frost" else np.array([120, 150, 170])
                                a[Y, X, :3] = np.clip(soot + 6, 0, 255).astype(np.uint8)
            elif f["kind"] == "crack":
                x, y, ang = float(f["x"]), float(f["y"]), f["ang"]
                L = int(f["L"] * (1 + .25 * (stage - f["stage"])))
                for s in range(L):
                    ang += rng.uniform(-.6, .6)
                    x += math.cos(ang); y += math.sin(ang)
                    X, Y = int(round(x)), int(round(y))
                    if not (0 <= X < W and 0 <= Y < H) or not a[Y, X, 3]: break
                    glowing = stage >= 2 and s % 4 != 3
                    a[Y, X, :3] = hot if glowing else (14, 12, 16)
                    for nx, ny in ((X + 1, Y), (X, Y + 1)):
                        if 0 <= ny < H and 0 <= nx < W and a[ny, nx, 3] and not np.all(a[ny, nx, :3] == np.array(hot, np.uint8)):
                            a[ny, nx, :3] = (12, 10, 14)
                    if rng.random() < .12:                      # a branch
                        bx, by, ba = x, y, ang + rng.choice([-1, 1]) * 1.1
                        for _ in range(int(rng.integers(2, 6))):
                            bx += math.cos(ba); by += math.sin(ba)
                            BX, BY = int(round(bx)), int(round(by))
                            if 0 <= BX < W and 0 <= BY < H and a[BY, BX, 3]: a[BY, BX, :3] = (20, 18, 22)
            elif f["kind"] == "plate":
                w, h = f["w"], f["h"]
                x0, y0 = f["x"] - w // 2, f["y"] - h // 2
                for Y in range(y0 - 1, y0 + h + 1):
                    for X in range(x0 - 1, x0 + w + 1):
                        if not (0 <= X < W and 0 <= Y < H) or not a[Y, X, 3]: continue
                        rim = Y in (y0 - 1, y0 + h) or X in (x0 - 1, x0 + w)
                        if rim:
                            a[Y, X, :3] = (190, 184, 176) if (Y == y0 - 1 or X == x0 - 1) and rng.random() < .7 else (8, 6, 10)
                        else:                                    # the gap: dark internals, struts, hot lines
                            a[Y, X, :3] = (20, 16, 22) if (X + Y) % 3 else (48, 42, 50)
                            if X == x0 + w // 2: a[Y, X, :3] = (70, 64, 72)
                            if stage >= 2 and (Y - y0) % 3 == 1 and rng.random() < .75: a[Y, X, :3] = ember
            elif f["kind"] == "hole":
                r = f["r"] * (1 + .12 * (stage - f["stage"]))
                for Y in range(int(f["y"] - r - 3), int(f["y"] + r + 4)):
                    for X in range(int(f["x"] - r - 3), int(f["x"] + r + 4)):
                        if not (0 <= X < W and 0 <= Y < H) or not a[Y, X, 3]: continue
                        d = math.hypot(X - f["x"], Y - f["y"]) + rng.uniform(-.8, .8)
                        if d < r: a[Y, X] = 0
                        elif d < r + 1.2: a[Y, X, :3] = ember if (X + Y) % 3 else hot
                        elif d < r + 2.4: a[Y, X, :3] = (10, 8, 12)
        return a

    def ember_flicker(self, a, stage, t):
        """The idle loop's B frame: the hot crack pixels breathe."""
        if stage < 2 or t == 0: return a
        a = a.copy()
        hot = np.all(a[..., :3] == np.array(self.pal["hot"], np.uint8), -1)
        a[hot, :3] = self.pal["core"]
        return a

    def breaches(self, stage=4):
        return [(f["x"], f["y"]) for f in self.damage if f["kind"] in ("plate", "hole") and f["stage"] <= stage]

    # ---------------------------------------------------------------- fx overlays

    def smoke(self, t):
        a = np.zeros_like(self.base)
        H, W = self.hull.shape
        dk, md, lt = SMOKE[self.w]
        pts = self.breaches()[:7]
        x0, y0, x1, y1 = bbox(self.hull)
        for i, (bx, by) in enumerate(pts):
            rng = np.random.default_rng(100 + i)
            for j in range(3):                       # three puffs per breach, a third of a cycle apart
                ph = ((t + 2 * j + i) % 6) / 6.0
                r = (2.5 + 4.5 * ph) * min(1.0, 2.25 / self.f)       # the same puff size in cell px on the Drake's 576 cells
                cx = bx + rng.uniform(-2, 2) + 3 * math.sin(ph * 3 + i)
                cy = by - 4 - 22 * ph * min(1.0, 2.25 / self.f)
                cy = max(cy, y0 - 6)
                for Y in range(int(cy - r - 1), int(cy + r + 2)):
                    for X in range(int(cx - r - 1), int(cx + r + 2)):
                        if not (0 <= X < W and 0 <= Y < H): continue
                        d = math.hypot(X - cx, (Y - cy) * 1.1) + .6 * math.sin(X * 1.7 + Y * 2.3 + i)
                        if d > r: continue
                        col = lt if (Y - cy) < -r * .35 and (X - cx) < r * .2 else md if d < r * .8 else dk
                        a[Y, X] = (*col, 245 if ph < .7 else 225)
        return a

    def arcs(self, t):
        a = np.zeros_like(self.base)
        H, W = self.hull.shape
        rng = np.random.default_rng(500 + t)
        pts = self.breaches()
        P = self.pal
        bx0, by0, bx1, by1 = bbox(self.hull)
        for i in range(4):
            sx, sy = pts[int(rng.integers(len(pts)))]
            x, y = float(sx), float(sy)
            n = int(rng.integers(20, 30))
            dx0, dy0 = rng.choice([-1, 1]), rng.choice([-1, 1])
            path = []
            for s in range(n):                        # a jagged diagonal bolt
                dx = dx0 if rng.random() < .75 else -dx0
                dy = dy0 if rng.random() < .75 else -dy0
                if not (bx0 + 3 <= x + dx < bx1 - 3): dx0 = -dx0; dx = -dx
                if not (by0 + 3 <= y + dy < by1 - 3): dy0 = -dy0; dy = -dy
                x += dx; y += dy
                path.append((int(x), int(y), s))
            for X, Y, s in path:                      # a dark cue sheath round it (one solid piece: no sliver)
                for oy in (-1, 0, 1):
                    for ox in (-1, 0, 1):
                        if 0 <= X + ox < W and 0 <= Y + oy < H and not a[Y + oy, X + ox, 3]: a[Y + oy, X + ox] = (*P["mid"], 255)
            for X, Y, s in path:
                if 0 <= X < W and 0 <= Y < H:
                    a[Y, X] = (*(P["core"] if s % 3 else P["light"]), 255)
        return a

    # ---------------------------------------------------------------- death

    def shards(self, n=14):
        if hasattr(self, "_shards"): return self._shards
        rng = np.random.default_rng(7)
        ys, xs = np.nonzero(self.hull)
        seeds = np.stack([xs, ys], 1)[rng.choice(len(xs), n, replace=False)].astype(float)
        H, W = self.hull.shape
        yy, xx = np.mgrid[:H, :W]
        d = (xx[..., None] - seeds[:, 0]) ** 2 + (yy[..., None] - seeds[:, 1]) ** 2
        self._shards = (np.argmin(d, -1), seeds)
        return self._shards

    def blast(self, a, cx, cy, r, seed, smoke=False):
        P = self.pal
        H, W = a.shape[:2]
        rng = np.random.default_rng(seed)
        dk, md, lt = SMOKE[self.w]
        for Y in range(int(cy - r - 2), int(cy + r + 3)):
            for X in range(int(cx - r - 2), int(cx + r + 3)):
                if not (0 <= X < W and 0 <= Y < H): continue
                ang = math.atan2(Y - cy, X - cx)
                rr = r * (1 + .18 * math.sin(ang * 7 + seed) + .1 * math.sin(ang * 13 + seed * 2))
                d = math.hypot(X - cx, Y - cy)
                if d > rr: continue
                k = d / rr
                if smoke: col = lt if k < .4 else md if k < .8 else dk
                else: col = P["core"] if k < .38 else (255, 246, 214) if k < .5 and self.w in ("Ember", "Verdant") else P["light"] if k < .7 else P["mid"] if k < .88 else P["dark"]
                a[Y, X] = (*col, 255)
        return a

    def death(self):
        lab, seeds = self.shards()
        x0, y0, x1, y1 = bbox(self.hull)
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        size = max(x1 - x0, y1 - y0)
        core = self.P(*self.rig["core"])               # the reactor / heart the overload bursts from
        wrecked = self.damaged(4)
        cells = []
        # 0: flash through the breaches
        a = self.glow(wrecked, .5)
        hot = np.all(a[..., :3] == np.array(self.pal["hot"], np.uint8), -1)
        a[hot, :3] = self.pal["core"]
        for i, (bx, by) in enumerate(self.breaches()[:6]): self.orb(a, bx, by, 2.5)
        self.flash(a, core[0], core[1], 6)
        cells.append(a)

        def split(d, burn):
            out = np.zeros_like(wrecked)
            H, W = lab.shape
            for i, (sx, sy) in enumerate(seeds):
                m = (lab == i) & self.hull & (wrecked[..., 3] > 0)
                vx, vy = sx - cx, sy - cy; n = math.hypot(vx, vy) or 1
                ox, oy = int(round(vx / n * d)), int(round(vy / n * d))
                ys, xs = np.nonzero(m)
                Y, X = ys + oy, xs + ox
                ok = (Y >= 0) & (Y < H) & (X >= 0) & (X < W)
                px = wrecked[ys[ok], xs[ok]].copy()
                px[:, :3] = (px[:, :3] * (1 - burn)).astype(np.uint8)
                out[Y[ok], X[ok]] = px
            return out
        # 1: split apart, seams white-hot
        a = split(2, .1)
        seam = self.hull & (a[..., 3] == 0)
        a[seam] = (*self.pal["light"], 255)
        for bx, by in self.breaches()[:4]: self.blast(a, bx, by, 4, int(bx))
        cells.append(a)
        # 2: core overload
        a = split(4, .3)
        self.blast(a, core[0], core[1], size * .26, 3)
        cells.append(a)
        # 3: fireball peak
        a = split(6, .55)
        self.blast(a, cx, cy, size * .44, 5)
        for i in range(5):
            ang = i * 1.26
            self.blast(a, cx + math.cos(ang) * size * .3, cy + math.sin(ang) * size * .3, size * .12, 10 + i)
        cells.append(a)
        # 4: shockwave ring and smoke
        a = split(8, .8)
        keep = np.random.default_rng(3).random(len(seeds)) < .5
        a[~keep[lab] & (a[..., 3] > 0)] = 0
        H, W = lab.shape
        yy, xx = np.mgrid[:H, :W]
        d = np.hypot(xx - cx, yy - cy)
        ring = (d > size * .44) & (d < size * .48)
        a[ring] = (*self.pal["light"], 255)
        for i in range(6):
            ang = i * 1.05 + .4
            self.blast(a, cx + math.cos(ang) * size * .2, cy + math.sin(ang) * size * .2, size * .11, 20 + i, smoke=True)
        cells.append(a)
        # 5: fading embers
        a = np.zeros_like(wrecked)
        rng = np.random.default_rng(9)
        for i in range(5):
            self.blast(a, cx + rng.uniform(-.3, .3) * size, cy + rng.uniform(-.3, .3) * size - 6, size * .07, 30 + i, smoke=True)
        for _ in range(26):
            X = int(cx + rng.uniform(-.42, .42) * size); Y = int(cy + rng.uniform(-.42, .42) * size)
            a[Y, X] = (*(self.pal["light"] if rng.random() < .5 else self.pal["mid"]), 255)
        cells.append(a)
        return cells

    # ---------------------------------------------------------------- sheets

    def build(self):
        B = self.base
        exp = self.w in EXPANDED
        body = {}
        for t in range(4): body[t] = self.idle(B, t, 4)
        body[4] = self.hit(B)
        body[5], body[6] = self.tell(B, 0, 0, 2), self.tell(B, 0, 1, 2)
        body[7], body[8] = self.tell(B, 1, 0, 2), self.tell(B, 1, 1, 2)
        body[9] = self.tell(B, 0, 2, 2)
        body[10], body[11] = self.tell(B, 2, 0, 2), self.tell(B, 2, 1, 2)
        death = self.death()
        for i, c in enumerate((0, 1, 3, 4, 5)): body[12 + i] = death[c]
        body[17], body[18] = self.retreat(B, 0), self.retreat(B, 1)
        body[19] = self.glow(self.flames(B, 0), .15)
        if exp:
            for t in range(6): body[20 + t] = self.idle(B, t, 6)
            for p in range(3):
                for s in range(3): body[26 + 3 * p + s] = self.tell(B, p, s, 2)
        n = 35 if exp else 20
        C = self.C
        cells = {i: self.render(body[i]) for i in range(n)}
        sheets = {}
        atlas = np.zeros((C * (n // COLS), C * COLS, 4), np.uint8)
        for i, c in cells.items():
            r, k = divmod(i, COLS); atlas[r * C:(r + 1) * C, k * C:(k + 1) * C] = c
        sheets[""] = atlas
        # damage hull: stage rows, idle loop A, B
        dmg = np.zeros((4 * C, 2 * C, 4), np.uint8)
        idle_ticks = (0, 3) if exp else (0, 2)
        for st in range(1, 5):
            D = self.damaged(st)
            for col, t in enumerate(idle_ticks):
                c = self.render(self.ember_flicker(self.idle(D, t, 6 if exp else 4), st, col))
                dmg[(st - 1) * C:st * C, col * C:(col + 1) * C] = c
        sheets["_damage"] = dmg
        fx = np.zeros((2 * C, 6 * C, 4), np.uint8)
        for t in range(6):
            fx[:C, t * C:(t + 1) * C] = self.render(self.smoke(t))
            fx[C:, t * C:(t + 1) * C] = self.render(self.arcs(t))
        sheets["_damage_fx"] = self.fx_margin(fx)
        strip = np.zeros((C, 6 * C, 4), np.uint8)
        ibox = bbox(cells[20 if exp else 0][..., 3] > 24)
        for i, d in enumerate(death):
            c = self.render(d)
            c[:, :max(0, ibox[0] - 6)] = 0; c[:, ibox[2] + 6:] = 0      # never wider than the idle art (+12 px)
            strip[:, i * C:(i + 1) * C] = c
        sheets["_death"] = strip
        # damaged attack poses
        poses = ([("tell0_0", 0, 0), ("tell0_1", 0, 1), ("tell0_2", 0, 2), ("tell1_0", 1, 0), ("tell1_1", 1, 1), ("tell1_2", 1, 2),
                  ("tell2_0", 2, 0), ("tell2_1", 2, 1), ("tell2_2", 2, 2)] if exp else
                 [("tell0_0", 0, 0), ("tell0_1", 0, 1), ("tell1_0", 1, 0), ("tell1_1", 1, 1), ("tell2_0", 2, 0), ("tell2_1", 2, 1), ("fire", 0, 2)])
        attack = {}
        for st in range(1, 5):
            D = self.damaged(st)
            for name, p, s in poses:
                attack[f"{self.w.lower()}_{name}_stage{st}"] = self.render(self.tell(D, p, s, 2))
        self.cells, self.sheets, self.attack, self.death_cells = cells, sheets, attack, death
        return sheets, attack

    def fx_margin(self, fx):
        C = self.C; m = round(13 * C / 384)
        for r in range(2):
            for k in range(6):
                c = fx[r * C:(r + 1) * C, k * C:(k + 1) * C]
                c[:m] = 0; c[-m:] = 0; c[:, :m] = 0; c[:, -m:] = 0
                if r == 1:                       # arc pieces cut short by the margin read as clipped slivers: drop them
                    lab, sizes = components(c[..., 3] > 0)
                    for i, n in enumerate(sizes):
                        if i and n <= 320 * (C / 384) ** 2: c[lab == i] = 0
        return fx

    # ---------------------------------------------------------------- measurement

    def rig_points(self):
        """Every part's muzzle in every body drawing, 384 reference px: the hull never moves between drawings, so it is
        the muzzle's cell pixel, checked opaque in each drawing (death debris cells keep it)."""
        k = 384 / self.C
        out = {}
        n = len(self.cells)
        for name in self.rig["parts"]:
            x, y = self.muzzle_src(name)
            cx, cy = self.cell_xy(x, y)
            rx, ry = int(cx * k), int(cy * k)
            pts = []
            for f in range(n):
                c = self.cells[f]
                X = min(self.C - 1, round((rx + .5) / k - .5)); Y = min(self.C - 1, round((ry + .5) / k - .5))
                if not (12 <= f <= 16) and c[Y, X, 3] < 128:
                    raise SystemExit(f"{self.w} {name} off the art in drawing {f}")
                pts.append((rx, ry))
            out[name] = pts
        return out

    def hearts(self):
        """BossHearts.Body: centre + half axes (world u at BossWorldSize 3.3, y up) of the ellipse round every idle pixel."""
        s = 3.3 / self.C
        idle = range(20, 26) if self.w in EXPANDED else range(0, 4)
        pts = []
        for f in idle:
            ys, xs = np.nonzero(self.cells[f][..., 3] >= 128)
            pts.append(np.stack([(xs + .5 - self.C / 2) * s, (self.C - 1 - ys + .5 - self.C / 2) * s], 1))
        p = np.concatenate(pts)
        cx = round((p[:, 0].min() + p[:, 0].max()) / 2, 2); cy = round((p[:, 1].min() + p[:, 1].max()) / 2, 2)
        hx = max(abs(p[:, 0] - cx)); hy = max(abs(p[:, 1] - cy))
        k = math.sqrt(max(((p[:, 0] - cx) / hx) ** 2 + ((p[:, 1] - cy) / hy) ** 2))
        ax = math.ceil(hx * k * 100) / 100; ay = math.ceil(hy * k * 100) / 100
        worst = math.sqrt(max(((p[:, 0] - cx) / ax) ** 2 + ((p[:, 1] - cy) / ay) ** 2))
        return dict(cx=cx, cy=cy, ax=ax, ay=ay, worst=round(worst, 4))


def save(a, path):
    Image.fromarray(a, "RGBA").save(path, optimize=True)


def contact(b, pts, out):
    """All sheets of one boss on one page, every attack origin marked on the body cells."""
    C = b.C
    th = 192
    k = th / C
    pages = []
    body = Image.new("RGBA", (COLS * th, (len(b.cells) // COLS) * (th + 14)), (24, 24, 38, 255))
    d = ImageDraw.Draw(body)
    colors = [(0, 255, 120), (255, 230, 0), (0, 200, 255), (255, 120, 0), (255, 255, 255), (180, 120, 255), (120, 255, 255),
              (255, 160, 200), (160, 255, 0), (255, 80, 255)]
    for i, c in b.cells.items():
        r, col = divmod(i, COLS)
        im = Image.fromarray(c).resize((th, th), Image.NEAREST)
        X, Y = col * th, r * (th + 14) + 14
        body.alpha_composite(im, (X, Y))
        d.text((X + 3, Y - 13), str(i), fill=(255, 255, 255))
        if 12 <= i <= 16: continue
        for j, (name, p) in enumerate(pts.items()):
            x, y = p[i][0] * 384 / C * 0 + p[i][0] * (C / 384) * k, p[i][1] * (C / 384) * k
            cc = colors[j % len(colors)]
            d.ellipse([X + x - 4, Y + y - 4, X + x + 4, Y + y + 4], outline=(0, 0, 0), width=2)
            d.line([X + x - 5, Y + y, X + x + 5, Y + y], fill=cc); d.line([X + x, Y + y - 5, X + x, Y + y + 5], fill=cc)
            if i in (0, 20): d.text((X + x + 5, Y + y - 10), name, fill=cc)
    pages.append(body)
    for key in ("_damage", "_damage_fx", "_death"):
        s = b.sheets[key]
        h, w = s.shape[:2]
        im = Image.fromarray(s).resize((int(w * k), int(h * k)), Image.NEAREST)
        bg = Image.new("RGBA", im.size, (24, 24, 38, 255)); bg.alpha_composite(im)
        if key == "_damage_fx":                      # also over the stage-4 hull, as in the game
            hull = Image.fromarray(b.sheets["_damage"][3 * C:4 * C, :C]).resize((th, th), Image.NEAREST)
            comp = Image.new("RGBA", (6 * th, 2 * th), (24, 24, 38, 255))
            for r in range(2):
                for t in range(6):
                    comp.alpha_composite(hull, (t * th, r * th))
            comp.alpha_composite(im)
            bg = comp
        pages.append(bg)
    att = list(b.attack.items())
    per = len(att) // 4
    ap = Image.new("RGBA", (per * 96, 4 * 96), (24, 24, 38, 255))
    for i, (name, c) in enumerate(att):
        ap.alpha_composite(Image.fromarray(c).resize((96, 96), Image.NEAREST), ((i % per) * 96, (i // per) * 96))
    pages.append(ap)
    for key in ("_shots", "_card"):
        im = Image.open(LIVE / f"{b.w}{key}.png").convert("RGBA")
        im = im.resize((im.width * 3 // 4, im.height * 3 // 4))
        bg = Image.new("RGBA", im.size, (24, 24, 38, 255)); bg.alpha_composite(im); pages.append(bg)
    W = max(p.width for p in pages); H = sum(p.height + 10 for p in pages) + 20
    sheet = Image.new("RGBA", (W, H), (12, 12, 20, 255))
    y = 20
    ImageDraw.Draw(sheet).text((4, 4), f"{b.w}: body / damage / damage_fx (over stage 4) / death / damaged attack poses x4 stages / shots / card", fill=(255, 255, 255))
    for p in pages:
        sheet.alpha_composite(p, (0, y)); y += p.height + 10
    sheet.convert("RGB").save(out)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    worlds = [w for w in args if w in RIG] or list(RIG)
    install = "--no-install" not in sys.argv
    sheets_dir = Path(sys.argv[sys.argv.index("--sheets") + 1]) if "--sheets" in sys.argv else OUT
    OUT.mkdir(exist_ok=True); sheets_dir.mkdir(parents=True, exist_ok=True)
    report = {}
    for w in worlds:
        b = Boss(w)
        sheets, attack = b.build()
        pts = b.rig_points()
        (HERE / f"rig_{w}.json").write_text(json.dumps({"cell": b.C, "frames": len(b.cells), "points": pts}, indent=0))
        od = OUT / w; od.mkdir(exist_ok=True)
        for key, a in sheets.items():
            save(a, od / f"{w}{key}.png")
            if install:
                old = Image.open(OLD / f"{w}{key}.png")
                assert old.size == (a.shape[1], a.shape[0]), (w, key, old.size, a.shape)
                save(a, LIVE / f"{w}{key}.png")
        for name, a in attack.items():
            save(a, od / f"{name}.png")
            if install:
                assert (IMPACT / f"{name}.png").exists(), name
                save(a, IMPACT / f"{name}.png")
        contact(b, pts, sheets_dir / f"boss_{w.lower()}.png")
        report[w] = dict(cell=b.C, scale=round(b.f, 3), old_edge=int(b.old_edge), hearts=b.hearts(),
                         points={n: p[20 if w in EXPANDED else 0] for n, p in pts.items()})
        print(json.dumps({w: report[w]}))
    old = json.loads((HERE / "report.json").read_text()) if (HERE / "report.json").exists() else {}
    old.update(report)
    (HERE / "report.json").write_text(json.dumps(old, indent=1))


if __name__ == "__main__":
    main()
