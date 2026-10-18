"""Helicopter enemy strips (Ember + Tide): Resources/Enemies/<key>.png = 7 square 192 px cells
(0-3 idle: rotor spin, 4-5 tell: hot parts bloom, 6 hit flash).

The Codex-painted gunship (src/<name>.png, top-down, nose down, no outline) is scaled once; each rotor's blades are
lifted off it (the pixels they covered are filled in from their surroundings -> a blade-less "plate"), and every cell
puts the SAME plate down and lays the blades back at a new angle (plus two faint ghost blades = motion blur). So the
hull, pods, canopy and wings are pixel-identical in all seven cells; only rotors (idle), glow (tell) and a white wash
(hit) change. Nearest-neighbour is not used: the work happens at 4x (768 px cells) and is box-filtered down to 192.

  python3 build_helicopter.py ember_fighter_5 [...]
"""
from __future__ import annotations
import math, sys
from pathlib import Path
import numpy as np
from PIL import Image, ImageFilter

HERE = Path(__file__).resolve().parent
REPO = HERE.parents[6]
RES = REPO / "Pause/Assets/Art/Resources/Enemies"
CELL, K = 192, 4
W = CELL * K
SPRITE_W = 640   # work px the sprite is scaled to (160 px final)

# key -> spec. rotors: (hub x, hub y, blade angles deg (screen, 0 = right, 90 = down), blade half-width, blade length,
#                      per-blade length overrides {angle: len}, hub radius, spin direction)   all in SOURCE-crop px
SPECS = {
    "ember_fighter_5": dict(
        src="ember_heli_b.png",
        rotors=[
            dict(hub=(307, 574), angles=(0, 90, 180, 270), hw=33, length=252, cut={0: 232}, hubr=70, spin=1, phase=0.0, disc=250),
            dict(hub=(942, 573), angles=(0, 90, 180, 270), hw=33, length=252, cut={180: 232}, hubr=70, spin=-1, phase=11.0, disc=250),
        ],
        muzzles=[(627, 1100)],
        glow=(255, 236, 150),
    ),
    "tide_fighter_5": dict(
        src="tide_heli_a.png",
        rotors=[
            dict(hub=(625, 590), angles=(37, 143, 217, 323), hw=48, length=700, cut={}, hubr=62, spin=1, phase=0.0, disc=0, gate="bone", clean=True),
        ],
        muzzles=[(625, 1150)],
        glow=(200, 255, 232), hot="teal",
    ),
    "tide_fighter_6": dict(
        src="tide_heli_b.png",
        rotors=[
            dict(hub=(630, 180), angles=(0, 90, 180, 270), hw=34, length=235, cut={90: 180, 270: 180}, hubr=64, spin=1, phase=0.0, disc=0, gate="bone"),
            dict(hub=(630, 690), angles=(0, 90, 180, 270), hw=34, length=275, cut={90: 205, 270: 205}, hubr=78, spin=-1, phase=14.0, disc=0, gate="bone"),
        ],
        muzzles=[(627, 1180), (508, 1045), (746, 1045)],
        glow=(200, 255, 232), hot="teal",
    ),
}
IDLE_STEP = 22.5          # degrees a rotor turns between idle cells (a four-blade cross repeats every 90)
TELL_ANGLES = (11.0, 33.0)
HIT_ANGLE = 5.0


def premult(img):
    a = np.asarray(img.convert("RGBA"), np.float32) / 255.0
    a[..., :3] *= a[..., 3:4]
    return a


def unpremult(p):
    out = p.copy()
    a = np.clip(p[..., 3:4], 0, 1)
    out[..., :3] = np.where(a > 1e-4, p[..., :3] / np.maximum(a, 1e-4), 0)
    out = np.clip(out, 0, 1)
    return Image.fromarray((out * 255 + .5).astype(np.uint8), "RGBA")


def resize_pm(p, size, method):
    ch = []
    for c in range(4):
        im = Image.fromarray(np.ascontiguousarray(p[..., c]), "F").resize(size, method)
        ch.append(np.asarray(im, np.float32))
    return np.stack(ch, -1)


def fill(p, known, iters=60):
    """Diffuse the known premultiplied pixels into the unknown ones."""
    p = p.copy()
    k = known.astype(np.float32)
    for _ in range(iters):
        if k.min() >= 1: break
        pk = p * k[..., None]
        def box(x):
            s = np.zeros_like(x)
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    s += np.roll(np.roll(x, dy, 0), dx, 1)
            return s
        num, den = box(pk), box(k)
        new = (den > 0) & (k < 1)
        p[new] = num[new] / den[new][:, None]
        k = np.where(new, 1.0, k)
    return p


def dilate(m, r):
    out = m.copy()
    for _ in range(r):
        o = out.copy()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            o |= np.roll(np.roll(out, dy, 0), dx, 1)
        out = o
    return out


class Heli:
    def __init__(self, key):
        spec = SPECS[key]
        self.key, self.spec = key, spec
        src = Image.open(HERE / spec["src"]).convert("RGBA")
        self.ref = src.getbbox()[:2]                      # spec coordinates are measured in this crop's frame
        a = np.asarray(src)[..., 3]
        ys, xs = np.nonzero(a > 16)                        # the painted sprite (stray faint alpha does not count)
        vis = (xs.min(), ys.min(), xs.max() + 1, ys.max() + 1)
        self.vis = vis
        crop = src.crop(vis)
        self.scale = min(spec.get("fit_w", SPRITE_W) / crop.width, spec.get("fit_h", 700) / crop.height)
        sw, sh = round(crop.width * self.scale), round(crop.height * self.scale)
        pm = resize_pm(premult(crop), (sw, sh), Image.LANCZOS)
        self.ox, self.oy = (W - sw) // 2, (W - sh) // 2
        canvas = np.zeros((W, W, 4), np.float32)
        canvas[self.oy:self.oy + sh, self.ox:self.ox + sw] = pm
        self.base = canvas
        self.build_plate()

    def to_work(self, pt):
        return (self.ox + (pt[0] + self.ref[0] - self.vis[0]) * self.scale, self.oy + (pt[1] + self.ref[1] - self.vis[1]) * self.scale)

    def build_plate(self):
        base = self.base
        yy, xx = np.mgrid[0:W, 0:W].astype(np.float32)
        plate = base.copy()
        self.blades = []
        allmask = np.zeros((W, W), bool)
        zone = np.zeros((W, W), bool)
        for r in self.spec["rotors"]:
            hx, hy = self.to_work(r["hub"])
            s = self.scale
            m = np.zeros((W, W), bool)
            for ang in r["angles"]:
                rad = math.radians(ang)
                dx, dy = xx - hx, yy - hy
                along = dx * math.cos(rad) + dy * math.sin(rad)
                perp = -dx * math.sin(rad) + dy * math.cos(rad)
                ln = r["cut"].get(ang, r["length"]) * s
                m |= (np.abs(perp) < r["hw"] * s) & (along > r["hubr"] * s) & (along < ln)
            zone |= m
            m &= base[..., 3] > .9          # blades are opaque; the faint disc under them is not
            if r.get("gate") == "bone":     # blade stock = bone-white / neutral grey / brass; the navy hull under it stays
                un = base[..., :3] / np.maximum(base[..., 3:4], 1e-4)
                mx, mn = un.max(-1), un.min(-1)
                neutral = ((mx - mn) < .2) & (un.mean(-1) > .22)
                gold = (un[..., 0] > un[..., 2] + .16) & (un[..., 0] > .28) & (un[..., 0] > un[..., 1])
                m &= neutral | gold
            m = dilate(m, 2)
            allmask |= m
            layer = np.zeros_like(base)
            layer[m] = base[m]
            self.blades.append((hx, hy, layer, r))
        known = ~allmask
        # fill the unknown from the plate with the opaque blade pixels excluded (disc / hull / pod beneath)
        plate = fill(base * known[..., None], known, 80)
        # the fill must not invent alpha outside the rotor discs/hull: it only runs under the blade masks, so it cannot
        self.plate = np.where(allmask[..., None], plate, base)
        if any(r.get("clean") for r in self.spec["rotors"]):
            # no blur disc under these blades: whatever faint alpha is left in the bars' zone is blade fringe -> clear it
            z = dilate(zone, 3) & (self.plate[..., 3] < .6)
            self.plate[z] = 0
        self.hubs_only = None

    def rotor_layer(self, theta):
        """Premultiplied RGBA of every rotor's blades at spin angle `theta` (+ phase), with two ghost blades behind."""
        out = np.zeros_like(self.base)
        for hx, hy, layer, r in self.blades:
            ang = (theta * r["spin"] + r["phase"])
            for lag, wgt in ((22.0, .10), (11.0, .22), (0.0, 1.0)):
                rot = self.rotate(layer, hx, hy, ang - lag * r["spin"])
                rot = rot * wgt
                out = rot + out * (1 - rot[..., 3:4])
        return out

    @staticmethod
    def rotate(layer, hx, hy, deg):
        if abs(deg) < 1e-6: return layer
        # rotate about the hub: pad crop around the hub to keep it quick
        R = 330
        x0, y0 = int(hx) - R, int(hy) - R
        sub = layer[max(0, y0):y0 + 2 * R, max(0, x0):x0 + 2 * R]
        out = np.zeros_like(layer)
        ch = []
        cx, cy = hx - max(0, x0), hy - max(0, y0)
        for c in range(4):
            im = Image.fromarray(np.ascontiguousarray(sub[..., c]), "F").rotate(-deg, Image.BICUBIC, center=(cx, cy))
            ch.append(np.asarray(im, np.float32))
        out[max(0, y0):max(0, y0) + sub.shape[0], max(0, x0):max(0, x0) + sub.shape[1]] = np.stack(ch, -1)
        return np.clip(out, 0, 1)

    def frame(self, theta, t_glow=0.0, hit=0.0):
        plate = self.plate.copy()
        if t_glow > 0:
            plate = self.glow(plate, t_glow)
        rot = self.rotor_layer(theta)
        f = rot + plate * (1 - rot[..., 3:4])
        if hit > 0:
            a = f[..., 3:4]
            white = np.array([1, 1, 1], np.float32)
            f[..., :3] = f[..., :3] * (1 - hit) + white * hit * np.clip(a, 0, 1)
        return f

    def glow(self, plate, t):
        c = np.array(self.spec["glow"], np.float32) / 255.0
        un = np.asarray(unpremult(plate), np.float32) / 255.0
        a = un[..., 3]
        if self.spec.get("hot") == "teal":
            hot = (un[..., 1] > .55) & (un[..., 2] > .45) & (un[..., 0] < .62) & (a > .85)
        else:
            hot = (un[..., 0] > .78) & (un[..., 1] > .42) & (un[..., 2] < .45) & (a > .85)
        hotf = hot.astype(np.float32)
        out = plate.copy()
        # the hot parts shift toward the glow colour
        out[..., :3] = out[..., :3] * (1 - hotf[..., None] * t * .98) + c * (hotf[..., None] * t * .98) * out[..., 3:4]
        # bloom round them (halo reaches a little past the hull, additive warm light)
        bl = Image.fromarray((hotf * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(20))
        bloom = np.asarray(bl, np.float32) / 255.0
        bloom = np.clip(bloom * 4.5, 0, 1) * t * 1.0
        warm = np.array([1.0, .62, .18], np.float32)
        add_a = bloom * (1 - out[..., 3])
        out[..., :3] += (warm[None, None] * add_a[..., None])
        out[..., 3] += add_a
        # muzzle flare
        yy, xx = np.mgrid[0:W, 0:W].astype(np.float32)
        fl = np.zeros((W, W), np.float32)
        for mpt in self.spec["muzzles"]:
            mx, my = self.to_work(mpt)
            d = np.hypot(xx - mx, yy - my)
            fl = np.maximum(fl, np.clip(1 - d / (30 * t + 6), 0, 1) ** 1.5 * t)
        fa = fl * (1 - out[..., 3] * 0)  # over the barrel as well
        flc = np.array([1.0, .92, .55], np.float32)
        out[..., :3] = out[..., :3] * (1 - fa[..., None]) + flc * fa[..., None]
        out[..., 3] = np.clip(out[..., 3] + fa * (1 - out[..., 3]), 0, 1)
        return np.clip(out, 0, 1)

    def strip(self):
        cells = []
        thetas = [0, IDLE_STEP, IDLE_STEP * 2, IDLE_STEP * 3]
        frames = [self.frame(t) for t in thetas]
        frames.append(self.frame(TELL_ANGLES[0], .55))
        frames.append(self.frame(TELL_ANGLES[1], 1.0))
        frames.append(self.frame(HIT_ANGLE, 0, .55))
        out = Image.new("RGBA", (CELL * 7, CELL))
        for i, f in enumerate(frames):
            small = resize_pm(f, (CELL, CELL), Image.BOX)
            out.paste(unpremult(small), (i * CELL, 0))
        return out, frames


def main():
    keys = sys.argv[1:] or list(SPECS)
    for key in keys:
        h = Heli(key)
        strip, frames = h.strip()
        dst = HERE / "out"
        dst.mkdir(exist_ok=True)
        strip.save(dst / f"{key}.png")
        print("wrote", dst / f"{key}.png", strip.size)


if __name__ == "__main__":
    main()
