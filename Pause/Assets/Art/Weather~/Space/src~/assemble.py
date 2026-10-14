"""Assemble painted Space weather candidates into an inspection-only sheet.

The source pixels come from the image-generation candidates next to this file.
All resizing and rotation use nearest-neighbour sampling. Final sheet files are
copied into the parent folder only after inspection and verification.
"""

from pathlib import Path
import colorsys
import numpy as np
from PIL import Image

ROOT = Path(__file__).parent
OUT = ROOT / "inspection"
OUT.mkdir(exist_ok=False)

CYAN = np.array([(12, 23, 39), (24, 46, 72), (43, 87, 113),
                 (77, 140, 167), (127, 196, 214), (177, 223, 234)], np.uint8)
VIOLET = np.array([(19, 19, 36), (38, 36, 62), (65, 60, 91),
                   (99, 93, 129), (142, 139, 171), (190, 188, 212)], np.uint8)
PINK = np.array([(31, 17, 42), (58, 26, 69), (91, 41, 96),
                 (135, 55, 127), (187, 70, 158), (228, 91, 191)], np.uint8)


def recolor(im, opacity, salt=0):
    a = np.array(im.convert("RGBA"), dtype=np.uint8)
    rgb = a[:, :, :3].astype(np.float32)
    y, x = np.indices(a.shape[:2])
    lum = (rgb[:, :, 0] * .24 + rgb[:, :, 1] * .52 + rgb[:, :, 2] * .24) / 255
    tone = np.minimum(5, np.maximum(0, (lum * 5.8).astype(int)))
    mag = (rgb[:, :, 0] > rgb[:, :, 1] * 1.28) & (rgb[:, :, 2] > rgb[:, :, 1] * 1.18) & (rgb[:, :, 0] > 60)
    violet = (rgb[:, :, 0] > rgb[:, :, 1] * 1.08) & (rgb[:, :, 2] > rgb[:, :, 1] * 1.08)
    keep_mag = mag & (((x * 13 + y * 29 + salt) % 17) == 0)
    colors = CYAN[tone]
    colors[violet] = VIOLET[tone[violet]]
    colors[keep_mag] = PINK[tone[keep_mag]]
    # The bright source is a weather layer behind gameplay. Lower alpha and
    # remove scattered dark pixels; preserve the painted arc/ribbon texture.
    alpha = np.rint(a[:, :, 3].astype(np.float32) * opacity).astype(np.uint8)
    dark = (tone <= 1) & (((x * 5 + y * 7 + salt) % 5) == 0)
    alpha[dark] = 0
    alpha[alpha < 11] = 0
    a[:, :, :3] = colors
    a[:, :, 3] = alpha
    return Image.fromarray(a, "RGBA")


def fit_gravity():
    src = Image.open(ROOT / "gravity_candidate_2.png").convert("RGBA")
    arr = np.array(src)
    yy, xx = np.nonzero(arr[:, :, 3] > 35)
    src = src.crop((xx.min(), yy.min(), xx.max()+1, yy.max()+1))
    scale = 406 / max(src.size)
    sz = tuple(max(1, round(v*scale)) for v in src.size)
    src = src.resize(sz, Image.Resampling.NEAREST)
    canvas = Image.new("RGBA", (512, 512))
    canvas.paste(src, ((512-sz[0])//2, (512-sz[1])//2))
    return recolor(canvas, .43)


def fit_wind():
    src = Image.open(ROOT / "solar_candidate_2.png").convert("RGBA")
    src = src.resize((768, 256), Image.Resampling.NEAREST)
    a = np.array(src)
    # Choose a 512 px section, then blend the last 72 columns back to the
    # first 72 to make the painted filaments cycle through the horizontal seam.
    core = a[:, 102:614].copy().astype(np.float32)
    n = 72
    for j in range(n):
        t = (j + 1) / n
        core[:, 512-n+j] = core[:, 512-n+j] * (1-t) + core[:, j] * t
    core[:, -1] = core[:, 0]
    canvas = np.zeros((512,512,4), dtype=np.uint8)
    canvas[128:384] = np.rint(core).astype(np.uint8)
    return recolor(Image.fromarray(canvas, "RGBA"), .34, 3)


def warning(base, salt, shift=0):
    a = np.array(base)
    y,x = np.indices(a.shape[:2])
    # Thin out weather paint, leaving a sparse preview of the same material.
    keep = ((x*19 + y*31 + salt) % 17) < 6
    a[:, :, 3] = np.where(keep, np.rint(a[:, :, 3].astype(float)*.72), 0).astype(np.uint8)
    a[:, :, 3][a[:, :, 3] < 12] = 0
    im = Image.fromarray(a, "RGBA")
    if shift:
        im = im.rotate(shift, Image.Resampling.NEAREST, expand=False)
    return im


gravity = fit_gravity()
wind = fit_wind()
sheet = Image.new("RGBA", (3072,1536))
for i in range(6):
    g = gravity.rotate(i*60, Image.Resampling.NEAREST, expand=False)
    sheet.paste(g, (i*512,0))
    w = np.roll(np.array(wind), i*85, axis=1)
    w[:,-1] = w[:,0]
    sheet.paste(Image.fromarray(w,"RGBA"), (i*512,512))
for i in range(2):
    sheet.paste(warning(gravity, 7+i*5, shift=i*30), (i*512,1024))
    # Warnings retain margin even though the live wind is a lane-wide front.
    bw = warning(wind, 11+i*5)
    crop = bw.crop((30,0,482,512))
    sheet.paste(crop, ((i+2)*512+30,1024))
sheet.save(OUT / "hazard.png")


ambient_src = Image.open(ROOT / "ambient_candidate.png").convert("RGBA")
ambient = Image.new("RGBA", (2048,512))
xcenters = [180, 480, 790, 1100]
ycenters = [185, 495, 790, 1068]
for kind in range(4):
    for variant in range(4):
        cx,cy = xcenters[variant],ycenters[kind]
        region = ambient_src.crop((cx-105,cy-105,cx+105,cy+105))
        raw = np.array(region)
        raw[:,:,3][raw[:,:,3] < 45] = 0
        yy,xx = np.nonzero(raw[:,:,3] > 0)
        if len(xx) == 0:
            raise ValueError(f"empty ambient source {kind} {variant}")
        part = Image.fromarray(raw,"RGBA").crop((xx.min(),yy.min(),xx.max()+1,yy.max()+1))
        target = 72 if kind == 0 else 94 if kind == 1 else 84 if kind == 2 else 58
        scale = min(1, target/max(part.size))
        part = part.resize(tuple(max(1,round(s*scale)) for s in part.size), Image.Resampling.NEAREST)
        part = recolor(part,.52,kind*4+variant)
        if kind == 3:
            a = np.array(part)
            lum = a[:,:,:3].max(axis=2)
            tone = np.minimum(5, np.maximum(0, (lum.astype(float)/235*5).astype(int)))
            a[:,:,:3] = PINK[tone]
            part = Image.fromarray(a,"RGBA")
        idx = kind*4+variant
        col,row = idx%8,idx//8
        ambient.paste(part,(col*256+(256-part.width)//2,row*256+(256-part.height)//2))
ambient.save(OUT / "ambient.png")

for name, im in (("gravity",gravity),("wind",wind)):
    alpha = np.array(im)[:,:,3]
    occupied = alpha>8
    print(name,"mean alpha painted",round(float(alpha[occupied].mean())/255,3),"whole cell",round(float(alpha.mean())/255,3),"occupied",int(occupied.sum()))
print("wrote",OUT)
