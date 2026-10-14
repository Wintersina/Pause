#!/usr/bin/env python3
"""Build Frost weather flipbooks from selected image-generated paintings.

All spatial resizing and rotation uses nearest-neighbour sampling. The source
paintings are retained beside this script for art review.
"""
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
P = np.array([
    (27, 51, 80),      # blue-slate shade
    (65, 91, 111),     # snow-grey shadow
    (113, 149, 169),   # snow-grey body
    (143, 207, 232),   # pale cyan
    (191, 233, 245),   # lit snow
    (240, 251, 255),   # ice-white flecks
], dtype=np.uint8)
ALPHA_STEPS = np.array([0, 32, 48, 64, 80, 96, 112, 128, 144], dtype=np.uint8)


def quantize(src, opacity, threshold=18):
    """Keep the painted texture, with Frost ramps and stepped, restrained alpha."""
    a = np.asarray(src.convert('RGBA')).astype(np.float32)
    lum = .24*a[..., 0] + .67*a[..., 1] + .09*a[..., 2]
    band = np.digitize(lum, [67, 112, 153, 194, 228])
    rgb = P[band]
    raw = np.clip((a[..., 3]-threshold)/(255-threshold), 0, 1) * opacity
    stepped = ALPHA_STEPS[np.argmin(abs(raw[..., None]-ALPHA_STEPS), axis=-1)]
    return np.dstack((rgb, stepped)).astype(np.uint8)


def seam_tile(a, width=40):
    """Make the image's two x edges identical before rolling loop frames."""
    a = a.astype(np.float32)
    for d in range(width):
        l, r = d, 511-d
        t = .5 * (1 + d/width)
        vl, vr = a[:, l].copy(), a[:, r].copy()
        common = (vl+vr)/2
        a[:, l] = common*(1-t) + vl*t
        a[:, r] = common*(1-t) + vr*t
    edge = ((a[:, 0] + a[:, -1])/2).astype(np.uint8)
    a[:, 0] = edge
    a[:, -1] = edge
    return a.astype(np.uint8)


def front_base():
    source = Image.open(HERE/'blizzard_candidate_2.png').convert('RGBA')
    painting = source.resize((512, 246), Image.Resampling.NEAREST)
    arr = quantize(painting, 152)
    under = Image.open(HERE/'blizzard_candidate_1.png').convert('RGBA')
    under = quantize(under.resize((512, 246), Image.Resampling.NEAREST), 88)
    # A shallow open channel gives bullets and ships a readable passage.
    y = np.arange(246)[:, None]
    center = np.abs(y-123)
    atten = np.where(center < 38, .52, np.where(center < 65, .74, 1.0))
    arr[..., 3] = (arr[..., 3]*atten).astype(np.uint8)
    # A second painted squall supplies the scattered snow between the rims.
    under_weight = np.where(center < 55, .7, .4)
    u_alpha = (under[..., 3]*under_weight).astype(np.uint8)
    take_under = u_alpha > arr[..., 3]
    arr[..., :3] = np.where(take_under[..., None], under[..., :3], arr[..., :3])
    arr[..., 3] = np.maximum(arr[..., 3], u_alpha)
    canvas = np.zeros((512, 512, 4), np.uint8)
    canvas[133:379] = arr
    return seam_tile(canvas)


def warning_from_front(a, phase):
    w = a.copy()
    y, x = np.indices((512,512))
    # Preserve wispy painted highlights and sparse crystal texture.
    keep = ((x + 3*y + phase*17) % 7 < 5) | ((w[..., 0] > 180) & ((x+phase*5) % 5 == 0))
    w[..., 3] = np.where(keep, (w[..., 3]*.48).astype(np.uint8), 0)
    w[:, :6, 3] = 0
    w[:, -6:, 3] = 0
    return w


def eddy_base():
    source = Image.open(HERE/'eddy_candidate_2.png').convert('RGBA')
    painting = source.resize((430,430), Image.Resampling.NEAREST)
    arr = quantize(painting, 142)
    y,x = np.indices((430,430))
    radius = np.hypot(x-214.5, y-214.5)
    # The source has three broad arms. Thin their inner parts to preserve air.
    attenuation = np.clip((radius-58)/110, .16, 1)
    arr[...,3] = (arr[...,3]*attenuation).astype(np.uint8)
    canvas = np.zeros((512,512,4), np.uint8)
    canvas[41:471,41:471] = arr
    return Image.fromarray(canvas,'RGBA')


def warning_from_eddy(im, phase):
    arr = np.array(im)
    y,x = np.indices((512,512))
    keep = (3*x+2*y+phase*13) % 7 < 5
    arr[...,3] = np.where(keep, (arr[...,3]*.50).astype(np.uint8), 0)
    return arr


def make_hazard():
    sheet = Image.new('RGBA',(3072,1536))
    front = front_base()
    eddy = eddy_base()
    for i in range(6):
        moving = np.roll(front, round(i*512/6), axis=1)
        moving[:, -1] = moving[:, 0]
        sheet.paste(Image.fromarray(moving,'RGBA'),(512*i,0))
        turning = eddy.rotate(-20*i, Image.Resampling.NEAREST)
        sheet.paste(turning,(512*i,512))
    for i in range(2):
        moving = np.roll(front, round(i*512/6), axis=1)
        sheet.paste(Image.fromarray(warning_from_front(moving,i),'RGBA'),(512*i,1024))
        turning = eddy.rotate(-20*(i+1), Image.Resampling.NEAREST)
        sheet.paste(Image.fromarray(warning_from_eddy(turning,i),'RGBA'),(512*(i+2),1024))
    sheet.save(OUT/'hazard.png')


def cut_particle(src, box, size, alpha_cap):
    crop = src.crop(box)
    # On the generated study, tiny low-alpha background flecks surround the art.
    a = np.asarray(crop).copy()
    a[...,3] = np.where(a[...,3]>=68, a[...,3], 0)
    crop = Image.fromarray(a,'RGBA').resize(size,Image.Resampling.NEAREST)
    out = quantize(crop,alpha_cap,threshold=45)
    # Remove dim leftover background, retaining pale faceted painted pixels.
    out[...,3] = np.where(out[...,0] >= 65, out[...,3], 0)
    out[...,3] = np.minimum(out[...,3],128)
    return Image.fromarray(out,'RGBA')


def make_ambient():
    src = Image.open(HERE/'ambient_candidate.png').convert('RGBA')
    sh = Image.new('RGBA',(2048,512))
    # Study locations: upper-left crystals, upper-right mini-flakes,
    # lower-left ice glints, lower-right wind-blown painted streaks.
    groups = [
        [((65,35,320,310),(94,100)),((330,45,550,305),(86,98)),
         ((45,330,310,565),(97,91)),((580,315,795,580),(88,100))],
        [((900,111,982,197),(39,41)),((1083,145,1170,230),(42,43)),
         ((1265,119,1364,210),(47,44)),((1230,295,1325,390),(45,47))],
        [((75,630,155,755),(31,49)),((367,640,462,795),(39,65)),
         ((551,622,657,761),(36,61)),((160,774,290,902),(44,49))],
        [((799,602,1071,698),(105,38)),((1120,616,1390,720),(102,40)),
         ((787,724,1099,830),(110,38)),((1102,748,1433,855),(109,39))],
    ]
    for kind, variants in enumerate(groups):
        for v,(box, size) in enumerate(variants):
            part = cut_particle(src,box,size,145 if kind == 0 else 124)
            cell = kind*4+v
            x=(cell%8)*256+(256-size[0])//2
            y=(cell//8)*256+(256-size[1])//2
            sh.paste(part,(x,y))
    sh.save(OUT/'ambient.png')


if __name__ == '__main__':
    make_hazard()
    make_ambient()
