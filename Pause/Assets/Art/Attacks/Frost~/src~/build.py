"""Assemble the Frost ultimate from the selected painted imagegen source.

All paint is sampled at the 64 px native grid, cleaned to fixed material ramps,
then exported at exactly 2x with nearest-neighbour resampling.
"""
from __future__ import annotations

import colorsys
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw


ROOT = Path(__file__).resolve().parent.parent
SRC = Image.open(Path(__file__).with_name("painted_candidate_selected.png")).convert("RGBA")
N = Image.Resampling.NEAREST
INK = (11, 11, 26, 255)
PINK = ["#7A1E78", "#A32AA1", "#C02AA8", "#FF4FD8", "#FF8AE6", "#FFC1EF", "#FFE0F8", "#FFF4FD"]
ICE = ["#4A5F8A", "#6579A2", "#7E96C4", "#A9BFE0", "#CFDDF2", "#EAF2FF", "#F5F8FF"]
PA = [tuple(int(c[i:i+2], 16) for i in (1, 3, 5)) for c in PINK]
IA = [tuple(int(c[i:i+2], 16) for i in (1, 3, 5)) for c in ICE]


def clean(im: Image.Image, white_is_pink=False) -> Image.Image:
    """Retain the generated paint's facets, quantised to legal ramps."""
    a = np.asarray(im.convert("RGBA"), dtype=np.uint8).copy()
    rgb = a[:, :, :3].astype(np.float32)
    v = np.max(rgb, 2) / 255
    lo = np.min(rgb, 2) / 255
    sat = np.where(v > 0, (v - lo) / np.maximum(v, 0.001), 0)
    r, g, b = rgb[:, :, 0], rgb[:, :, 1], rgb[:, :, 2]
    pink = (r > b * .94) & (r > g * 1.08) & (sat > .13)
    # Generated white facets inside the energy also belong to the pink ramp.
    pink |= (v > .84) & (r >= b * .98) & (r >= g * 1.01)
    if white_is_pink:
        pink |= (v > .78) & (sat < .16)
    light = .2126 * r + .7152 * g + .0722 * b
    # The source uses luminous high-value paint. Its green channel preserves
    # the magenta shade distinction far better than raw luma does.
    pi = np.digitize(g, [40, 75, 105, 150, 190, 220, 244])
    ii = np.digitize(light, [65, 100, 135, 170, 205, 235])
    # Preserve dark outlines only on the icy solid material.
    outline = ~pink & (v < .24) & (a[:, :, 3] > 160)
    a[:, :, :3] = np.where(pink[:, :, None], np.array(PA)[pi], np.array(IA)[ii]).astype(np.uint8)
    a[outline, :3] = INK[:3]
    alpha = a[:, :, 3]
    alpha[alpha < 32] = 0
    alpha[(alpha >= 32) & (alpha < 64)] = 16
    alpha[(alpha >= 64) & (alpha < 96)] = 48
    alpha[(alpha >= 96) & (alpha < 144)] = 96
    alpha[(alpha >= 144) & (alpha < 200)] = 176
    alpha[alpha >= 200] = 255
    a[alpha == 0, :3] = 0
    return Image.fromarray(a, "RGBA")


def energy_ramp(im: Image.Image) -> Image.Image:
    """Expose the generated plasma's magenta midtones at game scale."""
    a = np.asarray(im).copy()
    h, w = a.shape[:2]
    yy, xx = np.mgrid[:h, :w]
    radius = np.sqrt(((xx-w/2)/(w*.5))**2 + ((yy-h/2)/(h*.5))**2)
    noise = ((xx*13 + yy*7 + (xx*yy)%17) % 11)
    live = (a[:,:,3] >= 96) & (radius > .24) & (radius < 1.05)
    # Preserve a quarter of the painted ice facets as a secondary material.
    icy = (a[:,:,2] > a[:,:,0]+4) & (noise < 3)
    live &= ~icy
    idx = np.where(radius < .57, 4 + (noise>6), 3 + (noise>6))
    a[live,:3] = np.array(PA)[idx[live]]
    return Image.fromarray(a,'RGBA')


ORB = energy_ramp(clean(SRC.crop((145, 0, 515, 330)), white_is_pink=True))
MUZZLE = energy_ramp(clean(SRC.crop((555, 0, 1155, 580)), white_is_pink=True))
RAY = clean(SRC.crop((125, 340, 490, 1245)), white_is_pink=True)
IMPACT = clean(SRC.crop((515, 600, 1210, 1250)))


def blank(w=64, h=64):
    return Image.new("RGBA", (w, h))


def place(dst, src, box, opacity=255):
    x, y, w, h = box
    s = src.resize((w, h), N)
    if opacity != 255:
        s.putalpha(s.getchannel("A").point(lambda a: a * opacity // 255))
    dst.alpha_composite(s, (x, y))


def point(draw, x, y, r, color):
    x, y = round(x), round(y)
    draw.rectangle((x-r, y-r, x+r, y+r), fill=color)


def cross(draw, x, y, reach, phase=0, opacity=255):
    # Stepped, unequal arms and a diamond centre read as the charge lens.
    p = (*PA[4], opacity)
    hot = (*PA[7], opacity)
    draw.line((x-reach, y, x+reach, y), fill=p, width=1)
    draw.line((x, y-reach, x, y+reach), fill=p, width=1)
    draw.line((x-1, y, x+1, y), fill=hot, width=2)
    draw.line((x, y-1, x, y+1), fill=hot, width=2)
    if phase:
        draw.point((x-reach-1, y), fill=(*PA[3], 96))
        draw.point((x+reach+1, y), fill=(*PA[3], 96))


def ice_speck(draw, x, y, n=2):
    draw.polygon(((x, y-n), (x+1, y), (x, y+n), (x-1, y)), fill=(*IA[5], 255))
    draw.point((x, y), fill=(*PA[5], 255))


def clipped(im, side_only=False):
    a = np.array(im)
    # Eight export pixels of breathing room around each cell; the beam has
    # vertical continuity, so only its horizontal edges can be cleared.
    a[:, :4, :] = 0
    a[:, -4:, :] = 0
    if not side_only:
        a[:4, :, :] = 0
        a[-4:, :, :] = 0
    return Image.fromarray(a, "RGBA")


def windup(i):
    im = blank()
    sizes = [6, 9, 14, 20, 28, 36, 44, 50]
    s = sizes[i]
    # Early spark seeds are drawn from the painted orb itself.
    place(im, ORB, ((64-s)//2, (64-s)//2, s, s))
    d = ImageDraw.Draw(im)
    if i < 2:
        for k in range(i+2):
            ang = 2 * math.pi * (k / (i+2)) + .4 * i
            x = 32 + (11 + i*3)*math.cos(ang)
            y = 32 + (11 + i*3)*math.sin(ang)
            point(d, x, y, 0, (*PA[4+k%2], 255))
    else:
        for k in range(3+i//2):
            ang = 2*math.pi*k/(3+i//2) + .3*i
            rr = min(25, s*.52+2)
            point(d, 32+rr*math.cos(ang), 32+rr*.67*math.sin(ang), 0, (*PA[3+k%3], 255))
        if i >= 5:
            cross(d, 32, 32, 17 + (i-5)*4, i)
    if i == 7:
        d.ellipse((26, 26, 38, 38), fill=(*PA[7], 255))
        cross(d, 32, 32, 24)
    return clipped(im)


def muzzle(i):
    im = blank()
    s = 40 + [0, 2, 0, -2][i]
    src = MUZZLE.rotate(i*90, resample=N, expand=False)
    place(im, src, ((64-s)//2, (64-s)//2, s, s))
    d = ImageDraw.Draw(im)
    cross(d, 32, 32, [15, 13, 16, 14][i], i)
    for k in range(5):
        ang = 2*math.pi*k/5 + math.pi*i/6
        r = 20 + (k+i)%3
        point(d, 32+r*math.cos(ang), 32+r*math.sin(ang), 0, (*PA[3+k%3], 255))
    return clipped(im)


def fade(i):
    im = blank()
    s = [32, 23, 13, 5][i]
    place(im, ORB, ((64-s)//2, (64-s)//2, s, s), [230, 185, 130, 165][i])
    if i < 3:
        d = ImageDraw.Draw(im)
        cross(d, 32, 32, [11, 7, 3][i], opacity=[230, 185, 130][i])
    else:
        ImageDraw.Draw(im).point((32,32), fill=(*PA[5],255))
    return clipped(im)


def impact(i):
    im = blank()
    # Contact stays low in the cell; the generated upward ice fans grow then fall away.
    sizes = [(22, 24), (38, 41), (56, 54), (29, 29)]
    w, h = sizes[i]
    place(im, IMPACT, ((64-w)//2, 56-h, w, h), [255,255,255,145][i])
    d = ImageDraw.Draw(im)
    for k in range(2+i*2 if i<3 else 3):
        x = 32 + ((k*17+i*7)%43 - 21)
        y = 31 - ((k*11+i*5)%23)
        if 4 <= x <= 59 and 4 <= y <= 59:
            point(d, x, y, 0, (*PA[4+k%2], 255 if i<3 else 130))
    if i == 2:
        for x,y in [(12,21),(49,15),(20,9),(45,31)]: ice_speck(d,x,y)
    return clipped(im)


def spark(i):
    im = blank()
    # A few cropped, painted impact shards keep the flying debris material rich.
    shard = IMPACT.crop((180, 25, 540, 360))
    place(im, shard, (14+(i%2)*2, 14+(i//2), 36, 33), 140)
    d = ImageDraw.Draw(im)
    for k in range(8):
        ang = k*2.39996 + i*.35
        r = 8 + (k*7+i*3)%12
        x = 32 + r*math.cos(ang)
        y = 33 + r*.8*math.sin(ang)
        if 5<=x<=58 and 5<=y<=58:
            point(d,x,y,0,(*PA[(k+i)%4+3],255))
            if k%3==0: ice_speck(d,round(x)+2,round(y)-2,1)
    return clipped(im)


def build_laser():
    out = blank(512, 192)
    rows = [[windup(i) for i in range(8)],
            [muzzle(i) for i in range(4)] + [fade(i) for i in range(4)],
            [impact(i) for i in range(4)] + [spark(i) for i in range(4)]]
    for y, row in enumerate(rows):
        for x, cell in enumerate(row): out.alpha_composite(cell, (x*64, y*64))
    out.resize((1024,384), N).save(ROOT/'frost_attack_laser.png')


def beam_seed():
    # The generated vertical ray supplies the facet placement and flowing plasma.
    # Mirror one painted half-period to make a seamless 127-pixel vertical cycle.
    source = RAY.resize((44, 64), N)
    a = np.array(source)
    cycle = np.concatenate([a, a[-2::-1]], axis=0) # 127 rows
    return cycle


def body(i, seed):
    a = np.zeros((128,64,4), dtype=np.uint8)
    # 31-32 px downward phase steps make the last frame flow into the first.
    ysrc = (np.arange(128) - i*32) % 127
    a[:,10:54,:] = seed[ysrc,:,:]
    yy, xx = np.mgrid[:128,:64]
    dist = np.abs(xx-32)
    noise = (xx*7 + yy*11 + (yy//5)*3) % 9
    energy = (a[:,:,3] >= 96) & (dist>4) & (dist<=11)
    a[energy,:3] = np.array(PA)[3+(noise[energy]>4).astype(int)]
    fringe = (a[:,:,3] >= 96) & (dist>11) & (dist<=14) & (noise>4)
    a[fringe,:3] = PA[3]
    im = Image.fromarray(a, 'RGBA')
    d = ImageDraw.Draw(im)
    for y in range(128):
        phase = (y - i*32) % 127
        wobble = round(math.sin(phase*.18)*1.2)
        pulse = (phase % 32) < 7
        core = 4  # nine native pixels = 18 export pixels
        body = 10 + (1 if (phase//7)%3==0 else 0) # ~44 export pixels
        # Frost-lilac facets remain visible around the rolling hot-pink sheath.
        d.line((32-body+wobble,y,32+body+wobble,y), fill=(*PA[3+(phase//9)%2],215), width=1)
        d.line((32-core-2+wobble,y,32+core+2+wobble,y), fill=(*PA[4+(phase//11)%2],235), width=1)
        d.line((32-core+wobble,y,32+core+wobble,y), fill=(*PA[7 if pulse else 6],255), width=1)
        if pulse and phase%32 in (1,2,3):
            d.point((32+wobble,y), fill=(*PA[7],255))
    # Edge sparks crawl downward with the same cyclic phase.
    for k in range(14):
        y = (k*23 + i*32) % 127
        side = -1 if k%2 else 1
        x = 32 + side*(18 + k%5)
        if 10 <= x <= 54:
            d.line((x,y-2,x+side,y,x,y+2), fill=(*PA[3+k%3],255), width=1)
            if k%3==0: ice_speck(d,x-side*3,y+3,1)
    im = clipped(im, side_only=True)
    a = np.array(im)
    a[127,:,:] = a[0,:,:]  # exact vertical tile seam
    return Image.fromarray(a,'RGBA')


def build_body():
    seed = beam_seed()
    out = blank(256,128)
    for i in range(4): out.alpha_composite(body(i,seed),(i*64,0))
    out.resize((512,256),N).save(ROOT/'frost_attack_laserbody.png')


def sight(i):
    im = blank()
    d = ImageDraw.Draw(im)
    for k,y in enumerate((8,24,40,56)):
        # A 32 px native brightness cycle shifts half a cycle between a/b.
        # The faint in-between motes keep both tiles evenly spaced at the seam.
        bright=(k+i)%2==0
        d.rectangle((31,y,32,y+2), fill=(*PA[6 if bright else 4],255 if bright else 100))
        d.point((30,y+1),fill=(*PA[4],160 if bright else 65))
        d.point((33,y+1),fill=(*PA[4],160 if bright else 65))
        d.point((29,y+1),fill=(*PA[3],48 if bright else 24))
        d.point((34,y+1),fill=(*PA[3],48 if bright else 24))
    return clipped(im)


def lock(i):
    im = blank()
    # Orb paint underlies thin targeting brackets; blink changes both detail and value.
    place(im, ORB, (25,25,14,14), 115 if i else 190)
    d = ImageDraw.Draw(im)
    c = (*PA[4 if i else 5], 210 if i else 255)
    h = (*PA[6], 170 if i else 255)
    for x in (8,55):
        for y in (8,55):
            sx=-1 if x==8 else 1
            sy=-1 if y==8 else 1
            d.line((x,y-sy*7,x,y,x-sx*7,y), fill=c, width=1)
            d.point((x-sx*2,y-sy*2),fill=h)
    cross(d,32,32,4 if i else 6,opacity=175 if i else 255)
    for k in range(4):
        x = 32 + round(18*math.cos(k*math.pi/2+i*.25))
        y = 32 + round(18*math.sin(k*math.pi/2+i*.25))
        d.point((x,y),fill=(*PA[3],200))
    return clipped(im)


def build_tell():
    out = blank(256,64)
    for i, cell in enumerate([sight(0),sight(1),lock(0),lock(1)]):
        out.alpha_composite(cell,(i*64,0))
    out.resize((512,128),N).save(ROOT/'frost_attack_lasertell.png')


if __name__ == '__main__':
    build_laser()
    build_body()
    build_tell()
