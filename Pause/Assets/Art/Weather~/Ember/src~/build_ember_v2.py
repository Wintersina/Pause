#!/usr/bin/env python3
"""Build Ember weather sheets from retained image-generated paintings.

All resampling is nearest-neighbour. Drafts are written under src~ so the
reviewed deliverables can be copied into the parent directory just once.
"""
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
DRAFT = HERE / "draft2"
DRAFT.mkdir(exist_ok=True)

ASH = np.array([(26,16,12), (43,42,46), (71,48,43),
                (110,106,112), (149,112,96)], dtype=np.uint8)
GOLD = np.array([(149,112,96), (255,179,71), (255,217,138)], dtype=np.uint8)
STEPS = np.array([0, 28, 40, 52, 64, 76, 88, 100, 112, 124, 136], dtype=np.uint8)


def source_cell(filename, target_size, bounds=None):
    im = Image.open(HERE / filename).convert("RGBA")
    a = np.asarray(im)
    if bounds is None:
        yy, xx = np.nonzero(a[..., 3] > 40)
        bounds = (max(0, xx.min()-3), max(0, yy.min()-3),
                  min(im.width, xx.max()+4), min(im.height, yy.max()+4))
    return np.asarray(im.crop(bounds).resize(target_size, Image.Resampling.NEAREST)).copy()


def paint(a, alpha_cap=132, center_air=.7, kind="body"):
    rgb = a[..., :3].astype(np.float32)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    lum = .25*r + .65*g + .10*b
    warmth = (r > g*1.12) & (g > b*1.05) & (r > 85)
    grey_band = np.digitize(lum, [48, 86, 135, 183])
    gold_band = np.digitize(lum, [130, 205])
    color = np.where(warmth[..., None], GOLD[gold_band], ASH[grey_band])
    h, w = a.shape[:2]
    xx = np.arange(w)[None, :]
    center = np.abs(xx-(w-1)/2) / max(1, w/2)
    air = center_air + (1-center_air)*np.clip(center/.72, 0, 1)
    raw = np.maximum(0, a[..., 3].astype(float)-23) / 232
    raw *= alpha_cap * air
    # Source specks and material edges remain square; alpha uses discrete steps.
    alpha = STEPS[np.argmin(np.abs(raw[..., None]-STEPS), axis=-1)]
    alpha[a[..., 3] < 45] = 0
    if kind == "ash":
        # Remove the softest faint haze at the top and preserve the sharp flakes.
        yy = np.arange(h)[:, None]
        alpha = (alpha * np.where(yy < h*.13, .78, 1)).astype(np.uint8)
    return np.dstack((color, alpha)).astype(np.uint8)


def centered(painted, pos=(256, 256)):
    h, w = painted.shape[:2]
    dst = np.zeros((512, 512, 4), dtype=np.uint8)
    x = pos[0]-w//2
    y = pos[1]-h//2
    dst[y:y+h, x:x+w] = painted
    return dst


def animate_ash(base, phase):
    out = base.copy()
    warm = (out[..., 0] > 200) & (out[..., 1] > 120) & (out[..., 3] > 28)
    lights = np.zeros_like(out)
    lights[warm] = out[warm]
    out[..., 3][warm] = (out[..., 3][warm]*.35).astype(np.uint8)
    # Each painted cinder takes one 6 px upward step in a six-frame cycle.
    # Thirty-six-pixel bands make the last-to-first step the same distance.
    for top in range(18, 486, 36):
        bottom = min(top+36, 486)
        moving = np.roll(lights[top:bottom], -6*phase, axis=0)
        take = moving[..., 3] > out[top:bottom, ..., 3]
        out[top:bottom] = np.where(take[..., None], moving, out[top:bottom])
    return out


def animate_geyser(base, phase):
    heights = [438, 451, 470, 478, 460, 444]
    widths = [386, 392, 400, 404, 396, 389]
    # Source base ends at the same foot; only the upper plume surges.
    src = Image.fromarray(base[12:488, 54:458], "RGBA")
    scaled = np.asarray(src.resize((widths[phase], heights[phase]), Image.Resampling.NEAREST))
    dst = np.zeros_like(base)
    y = 488-heights[phase]
    x = (512-widths[phase])//2
    dst[y:488, x:x+widths[phase]] = scaled
    return dst


def warning(body, phase, geyser=False):
    out = body.copy()
    yy, xx = np.indices((512,512))
    retain = (xx*3 + yy*2 + phase*11) % 7 < 6
    # A thin preview with sparse moving specks, well below live density.
    out[..., 3] = np.where(retain, (out[..., 3]*.39).astype(np.uint8), 0)
    if geyser:
        out[:100, :, 3] = (out[:100, :, 3]*.72).astype(np.uint8)
    return out


def make_hazard():
    ash_src = source_cell("ash_candidate_1.png", (224, 476))
    geyser_src = source_cell("geyser_candidate_2.png", (404, 476))
    ash = centered(paint(ash_src, alpha_cap=132, center_air=.66, kind="ash"), (256, 256))
    geyser = centered(paint(geyser_src, alpha_cap=132, center_air=.48), (256, 250))
    sheet = Image.new("RGBA", (3072,1536))
    for i in range(6):
        sheet.paste(Image.fromarray(animate_ash(ash, i), "RGBA"), (512*i, 0))
        sheet.paste(Image.fromarray(animate_geyser(geyser, i), "RGBA"), (512*i, 512))
    for i in range(2):
        sheet.paste(Image.fromarray(warning(animate_ash(ash, i), i), "RGBA"), (512*i, 1024))
        sheet.paste(Image.fromarray(warning(animate_geyser(geyser, i), i, True), "RGBA"), (512*(i+2), 1024))
    sheet.save(DRAFT / "hazard.png")


def ambient_piece(src, col, row, size, kind):
    # The generated study has four rows and columns with wide clear gutters.
    x0 = col*328
    x1 = min(src.width, (col+1)*328)
    y0 = row*300
    y1 = min(src.height, (row+1)*300)
    a = np.asarray(src.crop((x0,y0,x1,y1)))
    yy,xx = np.nonzero(a[...,3] > 90)
    if len(xx) == 0:
        raise ValueError((col,row,"empty source particle"))
    box = (max(0,xx.min()-4), max(0,yy.min()-4), min(x1-x0,xx.max()+5), min(y1-y0,yy.max()+5))
    cut = Image.fromarray(a,"RGBA").crop(box).resize(size, Image.Resampling.NEAREST)
    p = paint(np.asarray(cut), alpha_cap=136, center_air=1)
    if kind in (0,2):
        lum = .25*p[...,0]+.65*p[...,1]+.10*p[...,2]
        p[..., :3] = GOLD[np.digitize(lum, [110,190])]
    elif kind in (1,3):
        lum = .25*p[...,0]+.65*p[...,1]+.10*p[...,2]
        p[..., :3] = ASH[np.digitize(lum, [38,75,120,170])]
    return Image.fromarray(p,"RGBA")


def make_ambient():
    src = Image.open(HERE / "ambient_source.png").convert("RGBA")
    sh = Image.new("RGBA", (2048,512))
    sizes = [
        [(42,68),(39,70),(42,66),(40,72)],
        [(77,55),(53,76),(78,51),(57,78)],
        [(42,39),(38,35),(37,34),(39,35)],
        [(78,65),(82,65),(78,65),(69,80)],
    ]
    for kind in range(4):
        for v in range(4):
            part = ambient_piece(src, v, kind, sizes[kind][v], kind)
            cell = kind*4+v
            px = cell%8*256+(256-part.width)//2
            py = cell//8*256+(256-part.height)//2
            sh.paste(part, (px,py))
    sh.save(DRAFT / "ambient.png")


if __name__ == "__main__":
    make_hazard()
    make_ambient()
