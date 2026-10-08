"""Build the rime cutter's three 192px death frames from one painted rupture."""

from pathlib import Path
import math
import random

import numpy as np
from PIL import Image, ImageDraw


HERE = Path(__file__).resolve().parent
ART = HERE.parents[2] / "Resources" / "Enemies"
DEATH = ART / "Death"
SIDE = 192
INK = (5, 6, 12)
ICE = [(10, 23, 52), (28, 60, 104), (42, 98, 174),
       (74, 150, 230), (159, 210, 242), (226, 251, 255)]
METAL = [(12, 16, 32), (30, 38, 56), (51, 64, 92),
         (79, 97, 136), (125, 147, 189)]
CYAN = [(21, 105, 200), (24, 152, 247), (70, 221, 253),
        (170, 242, 251), (248, 254, 252)]
COPPER = [(44, 30, 24), (74, 48, 33), (116, 66, 40),
          (159, 95, 52), (196, 135, 78), (220, 173, 110)]


def quantize(image):
    """Hard alpha and Frost material ramps eliminate generated fringe hues."""
    a = np.array(image.convert("RGBA"), dtype=np.uint8)
    rgb = a[:, :, :3].reshape(-1, 3).astype(np.int16)
    result = np.empty_like(rgb, dtype=np.uint8)
    for start in range(0, len(rgb), 8192):
        c = rgb[start:start + 8192]
        copper = (c[:, 0] > c[:, 2] * 1.20) & (c[:, 0] > c[:, 1] * 1.09) & (c[:, 0] > 48)
        cyan = (c[:, 2] > c[:, 0] * 1.16) & (c[:, 1] > c[:, 0] * 1.50) & (c[:, 1] > 95)
        for mask, ramp in ((copper, COPPER),
                           (cyan & ~copper, CYAN + ICE[3:]),
                           (~cyan & ~copper, [INK] + ICE + METAL)):
            if mask.any():
                pal = np.asarray(ramp, dtype=np.int16)
                delta = c[mask, None, :] - pal[None, :, :]
                result[start:start + len(c)][mask] = pal[(delta * delta).sum(2).argmin(1)]
    a[:, :, :3] = result.reshape(a.shape[:2] + (3,))
    levels = np.asarray([0, 64, 112, 160, 208, 255], dtype=np.uint8)
    a[:, :, 3] = levels[np.abs(a[:, :, 3, None].astype(np.int16) - levels).argmin(2)]
    a[a[:, :, 3] == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def idle():
    strip = Image.open(ART / "frost_rock_rime.png").convert("RGBA")
    assert strip.size == (7 * SIDE, SIDE)
    return strip.crop((3 * SIDE, 0, 4 * SIDE, SIDE))


def flash(base):
    out = base.copy()
    cx = cy = SIDE // 2
    halo = Image.new("RGBA", out.size)
    d = ImageDraw.Draw(halo)
    for radius, alpha in ((21, 40), (14, 75), (8, 110)):
        d.ellipse((cx-radius, cy-radius, cx+radius, cy+radius), fill=CYAN[2] + (alpha,))
    out = Image.alpha_composite(out, halo)
    d = ImageDraw.Draw(out)
    for i in range(12):
        angle = i * math.pi / 6
        length = 30 if i % 3 == 0 else 18
        end = (cx + round(math.cos(angle) * length), cy + round(math.sin(angle) * length))
        mid = (cx + round(math.cos(angle) * length * .58),
               cy + round(math.sin(angle) * length * .58))
        d.line([(cx, cy), mid, end], fill=CYAN[2] + (255,), width=2 if i % 3 == 0 else 1)
        d.point(end, fill=CYAN[3] + (255,))
    for end in ((cx-31,cy-42), (cx+37,cy-24), (cx-27,cy+39), (cx+23,cy+43)):
        mid = ((cx+end[0])//2+4, (cy+end[1])//2-3)
        d.line([(cx,cy), mid, end], fill=CYAN[2]+(255,), width=2)
        d.line([(cx,cy), mid], fill=CYAN[4]+(255,), width=1)
    d.ellipse((cx-5,cy-5,cx+5,cy+5), fill=CYAN[3]+(255,))
    d.ellipse((cx-2,cy-2,cx+2,cy+2), fill=CYAN[4]+(255,))
    return out


def rupture():
    src = Image.open(HERE / "rupture_candidate.png").convert("RGBA")
    alpha = np.array(src.getchannel("A"))
    ys, xs = np.nonzero(alpha > 100)
    box = (xs.min(), ys.min(), xs.max()+1, ys.max()+1)
    src = src.crop(tuple(map(int, box)))
    scale = min(166 / src.width, 166 / src.height)
    size = (round(src.width * scale), round(src.height * scale))
    src = src.resize(size, Image.Resampling.NEAREST)
    out = Image.new("RGBA", (SIDE, SIDE))
    out.alpha_composite(src, ((SIDE-size[0])//2, (SIDE-size[1])//2))
    out = quantize(out)
    a = np.array(out)
    yy, xx = np.indices((SIDE, SIDE))
    radius = np.hypot(xx-SIDE/2, yy-SIDE/2)
    original = a.copy()
    for old, new, minimum in ((ICE[5], ICE[4], 15), (ICE[4], ICE[3], 38),
                              (METAL[4], METAL[3], 61), (CYAN[4], CYAN[3], 22)):
        mask = np.all(original[:, :, :3] == old, axis=2) & (radius > minimum) & (a[:, :, 3] > 0)
        a[mask, :3] = new
    return Image.fromarray(a, "RGBA")


def aftermath(burst):
    space = Image.open(DEATH / "space_rock_dark.png").convert("RGBA")
    vapour = space.crop((384, 0, 576, 192))
    a = np.array(vapour)
    rgb = a[:, :, :3].astype(np.int16)
    lum = .28*rgb[:, :, 0] + .59*rgb[:, :, 1] + .13*rgb[:, :, 2]
    pink = (rgb[:, :, 0] > 150) & (rgb[:, :, 0]-rgb[:, :, 1] > 80) & (rgb[:, :, 2]-rgb[:, :, 1] > 70)
    smoke = np.asarray([(30,38,56), (51,64,92), (79,97,136),
                        (125,147,189), (159,210,242), (226,251,255)], dtype=np.uint8)
    a[:, :, :3] = smoke[np.digitize(lum, [34,52,80,112,150])]
    a[pink, :3] = CYAN[2]
    a[:, :, 3] = (a[:, :, 3].astype(float) * .42).astype(np.uint8)
    vapour = Image.fromarray(a, "RGBA")
    vapour = vapour.crop(vapour.getbbox()).resize((115,104), Image.Resampling.NEAREST)
    out = Image.new("RGBA", (SIDE, SIDE))
    out.alpha_composite(vapour, ((SIDE-115)//2, (SIDE-104)//2))
    # Tiny tiles sampled from the rupture retain the actual material texture.
    rng = random.Random(4821)
    for dx, dy, sx, sy in ((-54,-30,66,59), (48,-25,140,63),
                           (38,38,125,145), (-28,48,60,140), (5,-55,94,35)):
        size = rng.choice((3,4,5))
        tile = burst.crop((sx, sy, sx+size, sy+size))
        out.alpha_composite(tile, (96+dx, 96+dy))
    d = ImageDraw.Draw(out)
    for dx, dy in ((-29,-18), (23,-23), (37,15), (-18,32)):
        x, y = 96+dx, 96+dy
        d.line((x-2,y,x+2,y), fill=CYAN[2]+(255,), width=1)
        d.point((x,y), fill=CYAN[4]+(255,))
    return out


def main():
    original = idle()
    cells = [flash(original), rupture()]
    cells.append(aftermath(cells[1]))
    strip = Image.new("RGBA", (SIDE*3, SIDE))
    for i, cell in enumerate(cells):
        strip.paste(cell, (SIDE*i, 0))
    strip.save(DEATH / "frost_rock_rime.png")
    preview = Image.new("RGBA", (4*SIDE*2, SIDE*2), (7,16,31,255))
    for i, cell in enumerate([original] + cells):
        preview.alpha_composite(cell.resize((SIDE*2,SIDE*2), Image.Resampling.NEAREST),
                                (i*SIDE*2, 0))
    preview.convert("RGB").save(HERE / "preview.png")


if __name__ == "__main__":
    main()
