#!/usr/bin/env python3
"""Compose the Desert Harvester sheets from generated painted keys.

All resampling is nearest-neighbour. Colour changes are per-pixel HSV steering,
not palette reduction, so the generated material texture survives.
"""
from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
C = 384
INK = (8, 10, 18)
SHADOW = (37, 39, 60)
OCHRE = (74, 66, 53)
COPPER = (139, 83, 51)
COPPER_LIGHT = (153, 96, 58)
SAND = (134, 123, 96)
BONE = (230, 220, 192)
SAND_LIGHT = (217, 203, 160)
SUN = (255, 241, 200)
HEART = (255, 246, 220)
GLASS = (159, 201, 192)
BG = (11, 11, 26, 255)


def blank(size=(C, C)):
    return Image.new("RGBA", size, (0, 0, 0, 0))


def clean_hues(im: Image.Image) -> Image.Image:
    """Keep source value variations; constrain only hue and saturation families."""
    im = im.convert("RGBA")
    a = im.getchannel("A")
    h = np.array(im.convert("RGB").convert("HSV"), copy=True)
    H, S, V = h[..., 0], h[..., 1], h[..., 2]
    deg = H.astype(np.float32) * 360 / 255
    sat = S.astype(np.float32) / 255
    val = V.astype(np.float32) / 255

    # Copper stays matte; sand, gold and every generated spark stay pale.
    # Low-chroma rust reads as sand-scoured plating, not copper acreage.
    scoured = (deg >= 17) & (deg < 34) & (sat > .075) & (sat <= .32) & (val < .88)
    H[scoured] = round(44 / 360 * 255)
    S[scoured] = np.minimum(S[scoured], 76)
    V[scoured] = np.minimum(228, V[scoured].astype(np.int16)+9).astype(np.uint8)
    copper = (deg >= 17) & (deg < 34) & (sat > .32) & (val < .88)
    H[copper] = round(26 / 360 * 255)
    S[copper] = np.minimum(S[copper], 153)
    V[copper] = np.minimum(V[copper], 153)

    warm = ~copper & (sat > .075) & ((deg < 83) | (deg > 300))
    H[warm] = round(44 / 360 * 255)
    S[warm] = np.minimum(S[warm], 78)

    # Cold colour belongs only to dark indigo cavities, with tiny glass/patina traces.
    cold = ~copper & ~warm & (sat > .075)
    dark = cold & (val < .30)
    H[dark] = round(238 / 360 * 255)
    S[dark] = np.minimum(S[dark], 108)
    V[dark] = np.minimum(V[dark], 77)
    light_cold = cold & ~dark
    H[light_cold] = round(44 / 360 * 255)
    S[light_cold] = np.minimum(S[light_cold], 74)

    # Near neutrals keep their source tonal detail. The darkest ones receive an
    # indigo tint, making recesses distinct without turning the hull blue.
    neutral_shadow = (sat <= .075) & (val < .22)
    H[neutral_shadow] = round(238 / 360 * 255)
    S[neutral_shadow] = 45
    # Bright whites become the permitted sun-white family, still very pale.
    bright = (V > 215) & ~copper
    H[bright] = round(44 / 360 * 255)
    S[bright] = np.minimum(S[bright], 62)
    rgb = Image.fromarray(h, "HSV").convert("RGB")
    rgb.putalpha(a)
    return rgb


def isolate_source(name: str, target=342) -> Image.Image:
    src = Image.open(SRC / name).convert("RGBA")
    aa = np.asarray(src.getchannel("A"))
    yy, xx = np.nonzero(aa > 24)
    box = (max(0, int(xx.min())-2), max(0, int(yy.min())-2),
           min(src.width, int(xx.max())+3), min(src.height, int(yy.max())+3))
    part = clean_hues(src.crop(box))
    scale = min(target / part.width, target / part.height)
    part = part.resize((round(part.width*scale), round(part.height*scale)), Image.Resampling.NEAREST)
    out = blank()
    out.alpha_composite(part, ((C-part.width)//2, (C-part.height)//2))
    return outline(out)


def outline(im: Image.Image) -> Image.Image:
    arr = np.array(im, copy=True)
    solid = arr[..., 3] > 155
    inner = solid.copy()
    for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
        inner &= np.roll(solid, (dy, dx), axis=(0, 1))
    edge = solid & ~inner
    arr[edge, :3] = np.asarray(INK)
    # Selective pale rim on the upper left, sparing long sticker-like outlines.
    above = np.roll(solid, 1, axis=0)
    left = np.roll(solid, 1, axis=1)
    rim = inner & (~np.roll(above, 1, axis=0) | ~np.roll(left, 1, axis=1))
    rim &= (np.indices(solid.shape)[1] + 2*np.indices(solid.shape)[0]) % 5 == 0
    arr[rim, :3] = np.asarray(BONE)
    return Image.fromarray(arr, "RGBA")


def finish_palette(im: Image.Image) -> Image.Image:
    """Remove warm blend colours introduced when pale FX cross copper."""
    alpha=im.getchannel('A')
    hsv=np.array(im.convert('RGB').convert('HSV'),copy=True)
    h,s,v=hsv[...,0],hsv[...,1],hsv[...,2]
    deg=h.astype(float)*360/255
    chromatic=s>38
    hot_copper=chromatic&(deg>=20)&(deg<34)&((s>158)|(v>153))
    h[hot_copper]=round(44/360*255)
    s[hot_copper]=np.minimum(s[hot_copper],76)
    red=chromatic&((deg<24)|(deg>=345))
    red_matte=red&(v<=153)
    red_bright=red&~red_matte
    h[red_matte]=round(27/360*255)
    s[red_matte]=np.minimum(s[red_matte],153)
    h[red_bright]=round(44/360*255)
    s[red_bright]=np.minimum(s[red_bright],76)
    amber=chromatic&(deg>=30)&(deg<=48)&(s>115)
    s[amber]=76
    violet=chromatic&(deg>=250)&(deg<=330)
    h[violet]=round(44/360*255)
    s[violet]=np.minimum(s[violet],76)
    # An RGB value such as (8,6,5) has no perceptible hue; encode ink as
    # neutral so integer HSV round trips cannot report a spurious red pixel.
    s[v<13]=0
    rgb=Image.fromarray(hsv,'HSV').convert('RGB')
    rgb.putalpha(alpha)
    return rgb


def material_marks(im: Image.Image) -> Image.Image:
    marks=blank();d=ImageDraw.Draw(marks,'RGBA')
    # Tiny non-emissive oxidation on seams and cool glass flecks in the dish.
    patina=(111,160,138)
    for line in [((80,169),(84,172)),((91,210),(95,213)),((263,176),(267,173)),
                 ((249,230),(252,225)),((137,149),(140,151))]:
        d.line(line,fill=(*patina,255),width=1)
    for x,y in ((119,73),(130,56),(145,43),(203,48),(219,65),(232,84),(213,105),(139,105)):
        d.point((x,y),fill=(*GLASS,255))
        d.point((x+1,y),fill=(*BONE,255))
    im.alpha_composite(marks)
    return im


BASE = material_marks(isolate_source("candidate_c.png", 342))
DEATH_KEY = isolate_source("death_key.png", 348)


def apply_local_motion(im: Image.Image, phase: int, boom_drop: int = 0) -> Image.Image:
    """Move wheel buckets, slightly tip lens, and lower right boom at native pixels."""
    a = np.asarray(im)
    yy, xx = np.indices((C, C))
    sx = xx.astype(np.float32)
    sy = yy.astype(np.float32)
    # Ringed scoop annulus. Central throat and outer chassis remain still.
    cx, cy = 174, 286
    u, v = xx-cx, yy-cy
    rr = np.hypot(u, v)
    wheel = (rr > 45) & (rr < 86)
    angle = [0, -.073, -.145, -.218][phase]
    ct, st = math.cos(angle), math.sin(angle)
    sx[wheel] = cx + ct*u[wheel] + st*v[wheel]
    sy[wheel] = cy - st*u[wheel] + ct*v[wheel]
    # Segmented mirror tilts only two pixels, with the gimbal seam fixed.
    dish = np.maximum(0, 1-((xx-174)/103)**2-((yy-83)/68)**2)
    tilt = [0, 1.8, 0, -1.8][phase]
    sx += tilt*dish
    sy += .45*tilt*dish
    if boom_drop:
        # This piecewise warp has zero motion where the boom meets the hull.
        bx = np.clip((xx-249)/45, 0, 1)
        by = np.clip((yy-82)/55, 0, 1)
        sy -= boom_drop*bx*by
    ix = np.clip(np.rint(sx).astype(int), 0, C-1)
    iy = np.clip(np.rint(sy).astype(int), 0, C-1)
    return Image.fromarray(a[iy, ix], "RGBA")


def star(d: ImageDraw.ImageDraw, x, y, r, power=1, opaque=True):
    """Hard-edged pale energy with a stepped, translucent outer halo."""
    r = max(2, round(r))
    if not opaque:
        alpha = round(60*power)
    else:
        alpha = round(105*power)
    d.ellipse((x-r-7, y-r-7, x+r+7, y+r+7), fill=(*SAND_LIGHT, alpha//3))
    d.ellipse((x-r-3, y-r-3, x+r+3, y+r+3), fill=(*SUN, min(145, alpha)))
    q = r*2.4
    pts = [(x, y-q), (x+r*.30, y-r*.33), (x+q, y), (x+r*.30, y+r*.33),
           (x, y+q), (x-r*.30, y+r*.33), (x-q, y), (x-r*.30, y-r*.33)]
    d.polygon(pts, fill=(*SUN, round(205*power)))
    d.ellipse((x-r*.62, y-r*.62, x+r*.62, y+r*.62), fill=(*HEART, min(255,round(245*power))))


def dust(d: ImageDraw.ImageDraw, x, y, phase, heavy=False):
    rng = np.random.default_rng(630+phase*73+x)
    count = 38 if heavy else 16
    for k in range(count):
        offx = int(rng.normal(0, 13 if heavy else 7))
        offy = int(rng.normal(-phase*3-3, 13 if heavy else 8))
        if not (12 < x+offx < 370 and 12 < y+offy < 370):
            continue
        rad = int(rng.integers(1, 4 if heavy else 3))
        col = SAND_LIGHT if k%3 else BONE
        alpha = int(rng.integers(36, 122 if heavy else 87))
        d.ellipse((x+offx-rad, y+offy-rad, x+offx+rad, y+offy+rad), fill=(*col,alpha))


def arc(d: ImageDraw.ImageDraw, coords, power=1, width=2):
    d.line(coords, fill=(*SAND_LIGHT, round(80*power)), width=width+5, joint="curve")
    d.line(coords, fill=(*SUN, round(180*power)), width=width+2, joint="curve")
    d.line(coords, fill=(*HEART, min(255,round(250*power))), width=width, joint="curve")


def idle(phase: int) -> Image.Image:
    im = apply_local_motion(BASE, phase)
    fx = blank()
    d = ImageDraw.Draw(fx, "RGBA")
    # Lens focus and moving glints follow the tilted mirror petals.
    for j in range(4):
        theta = (phase*.33+j*1.52)-1.5
        x = 174+round(53*math.cos(theta))
        y = 83+round(29*math.sin(theta))
        d.line((x-2,y,x+2,y), fill=(*SUN, 155+j*16), width=1)
        d.line((x,y-2,x,y+2), fill=(*HEART, 155+j*16), width=1)
    star(d,174,83,2.3,.53)
    star(d,174,286,2.5,.60)
    # Right drill vent and tiny animated sand-scoured cable sway.
    dust(d, 326, 50, phase)
    for side in (-1,1):
        x=174+side*97
        d.line([(x,158),(x+side*(4+phase%2),172),(x+side*(6-phase%2),190)],
               fill=(*OCHRE,190),width=2)
        d.point((x+side*(6-phase%2),190), fill=(*BONE,165))
    im.alpha_composite(fx)
    return im


def maw_open(im, charge, fired=False):
    fx=blank(); d=ImageDraw.Draw(fx,"RGBA")
    x,y=174,286
    radius=17+(9 if charge else 2)
    d.ellipse((x-radius-3,y-radius-3,x+radius+3,y+radius+3), fill=(*INK,245), outline=(*COPPER_LIGHT,255),width=3)
    d.ellipse((x-radius+3,y-radius+3,x+radius-3,y+radius-3), fill=(*SHADOW,245), outline=(*BONE,245),width=2)
    for k in range(12):
        t=k*math.tau/12+(.10 if charge else 0)
        ax=x+int((radius-2)*math.cos(t)); ay=y+int((radius-2)*math.sin(t))
        bx=x+int((radius-9)*math.cos(t)); by=y+int((radius-9)*math.sin(t))
        d.line((ax,ay,bx,by),fill=(*SAND_LIGHT,255),width=3)
    star(d,x,y,6 if charge else 3,1 if charge else .72)
    if charge:
        for k in range(5):
            a=k*math.tau/5+charge*.35
            px=x+int(35*math.cos(a)); py=y+int(35*math.sin(a))
            star(d,px,py,2,.55)
    if fired:
        star(d,x,319,13,1)
        for dx in (-47,-24,0,24,47):
            arc(d,[(x,y+23),(x+dx//2,318),(x+dx,351)],.75,width=2)
            star(d,x+dx,350,3,.8)
        dust(d,x,330,3,True)
    im.alpha_composite(fx)
    return im


def boom_charge(im, charged):
    fx=blank(); d=ImageDraw.Draw(fx,"RGBA")
    x,y=328,207+(4 if charged else 1)
    # A lit spinning drill face, with white-hot metal teeth, below the boom.
    d.ellipse((x-17,y-19,x+17,y+19),fill=(*SHADOW,207),outline=(*BONE,255),width=3)
    for k in range(9):
        t=k*math.tau/9+(.28 if charged else .04)
        px=x+int(18*math.cos(t)); py=y+int(20*math.sin(t))
        d.line((x+int(10*math.cos(t)),y+int(12*math.sin(t)),px,py),fill=(*SUN,245),width=2)
    star(d,x,y,7 if charged else 4,.95 if charged else .70)
    dust(d,328,145,3 if charged else 1,charged)
    if charged:
        arc(d,[(306,174),(319,188),(311,204),(327,213)],.72,2)
        arc(d,[(345,171),(338,185),(345,197),(332,209)],.72,2)
    im.alpha_composite(fx)
    return im


def lens_charge(im, charged):
    fx=blank(); d=ImageDraw.Draw(fx,"RGBA")
    x,y=174,83
    for rad in (26,42,59):
        d.arc((x-rad,y-rad*.58,x+rad,y+rad*.58),205,335,fill=(*SUN,130 if charged else 80),width=2)
        d.arc((x-rad,y-rad*.58,x+rad,y+rad*.58),22,155,fill=(*BONE,130 if charged else 80),width=1)
    for k in range(8):
        t=k*math.tau/8
        xx=x+int(47*math.cos(t)); yy=y+int(25*math.sin(t))
        d.line((xx,yy,x+int(8*math.cos(t)),y+int(5*math.sin(t))),fill=(*SUN,180 if charged else 105),width=2)
    star(d,x,y,14 if charged else 7,1 if charged else .85)
    if charged:
        d.ellipse((x-81,y-51,x+81,y+51),outline=(*SUN,120),width=2)
        arc(d,[(174,96),(169,126),(177,153)],.6,1)
    im.alpha_composite(fx)
    return im


def hit():
    im=idle(1); a=np.array(im,copy=True)
    mask=(a[...,3]>180)&(a[...,:3].max(axis=2)>35)
    a[mask,:3]=np.clip(a[mask,:3].astype(float)*.25+np.asarray(SUN)*.75,0,255)
    im=Image.fromarray(a,"RGBA")
    fx=blank();d=ImageDraw.Draw(fx,"RGBA")
    star(d,174,196,12,1);star(d,113,198,5,.8);star(d,246,219,5,.8)
    im.alpha_composite(fx)
    return im


def fracture(im, stage):
    fx=blank(); d=ImageDraw.Draw(fx,"RGBA")
    for i in range(7+stage*3):
        angle=(i*.68+stage*.41)
        r=36+i%5*10
        x=174+int(r*math.cos(angle));y=190+int(r*math.sin(angle))
        pts=[(x,y),(x+int(9*math.cos(angle)),y+int(9*math.sin(angle))),
             (x+int(17*math.cos(angle+.3)),y+int(17*math.sin(angle+.3)))]
        d.line(pts,fill=(*INK,230),width=3)
        d.line(pts,fill=(*SUN,120+stage*25),width=1)
    im.alpha_composite(fx)
    return im


def death(stage):
    if stage==0:
        im=fracture(idle(2),1)
        fx=blank();d=ImageDraw.Draw(fx,"RGBA")
        for x,y,r in ((174,192,13),(174,286,11),(174,83,9),(329,207,7)):
            star(d,x,y,r,1)
        im.alpha_composite(fx);return im
    if stage==1:
        im=fracture(idle(3),2)
        fx=blank();d=ImageDraw.Draw(fx,"RGBA")
        for x,y,r in ((174,185,26),(174,286,16),(171,87,13)):
            star(d,x,y,r,1)
        for a in range(0,360,30):
            t=math.radians(a);x=174+int(38*math.cos(t));y=189+int(37*math.sin(t))
            d.polygon([(x,y),(x+int(12*math.cos(t-.6)),y+int(12*math.sin(t-.6))),
                       (x+int(22*math.cos(t)),y+int(22*math.sin(t)))],
                      fill=(*COPPER,235),outline=(*INK,240))
        im.alpha_composite(fx);return im
    # The generated explosion supplies actual painted burst, wreckage and dust.
    if stage==2:
        im=DEATH_KEY.copy()
        fx=blank();d=ImageDraw.Draw(fx,"RGBA")
        star(d,185,184,29,1)
        im.alpha_composite(fx);return im
    if stage==3:
        im=DEATH_KEY.copy()
        fx=blank();d=ImageDraw.Draw(fx,"RGBA")
        star(d,185,184,42,1)
        d.ellipse((25,25,359,359),outline=(*SUN,153),width=4)
        d.ellipse((34,34,350,350),outline=(*SAND_LIGHT,100),width=3)
        im.alpha_composite(fx);return im
    # Bright shockwave recedes, with recognisable dark wreckage left behind.
    a=np.array(DEATH_KEY,copy=True)
    a[...,3]=(a[...,3].astype(np.float32)*.73).astype(np.uint8)
    im=Image.fromarray(a,"RGBA")
    fx=blank();d=ImageDraw.Draw(fx,"RGBA")
    d.ellipse((36,35,348,349),outline=(*SUN,163),width=4)
    d.ellipse((44,43,340,341),outline=(*BONE,115),width=2)
    dust(d,93,170,3,True);dust(d,283,211,2,True)
    im.alpha_composite(fx);return im


def retreat(stage):
    im=idle(stage+1)
    if stage==1:
        im=im.resize((368,368),Image.Resampling.NEAREST)
        frame=blank();frame.alpha_composite(im,(8,3));im=frame
    fx=blank();d=ImageDraw.Draw(fx,"RGBA")
    for j,x in enumerate((83,111,151,199,236,274,319)):
        y=21+(j%3)*23
        d.line((x,y,x+(j%2)*3,y+32+stage*21),fill=(*SAND_LIGHT,56+j*8),width=2)
    dust(d,174,337,stage+1,True)
    im.alpha_composite(fx);return im


def portrait():
    im=idle(0)
    im=lens_charge(im,False)
    return maw_open(im,False)


def sheet_cells():
    c=[idle(i) for i in range(4)]+[hit()]
    c += [maw_open(idle(0),False),maw_open(idle(1),True),
          boom_charge(apply_local_motion(BASE,2,4),False),
          boom_charge(apply_local_motion(BASE,3,11),True),
          maw_open(idle(2),True,True)]
    c += [lens_charge(idle(0),False),lens_charge(idle(1),True),death(0),death(1),death(2)]
    c += [death(3),death(4),retreat(0),retreat(1),portrait()]
    assert len(c)==20
    return c


def draw_slug(phase):
    im=projectile_key(0,46,106,phase)
    fx=blank((128,128));d=ImageDraw.Draw(fx,"RGBA")
    for i in range(12):
        q=(i*37+phase*19)%43-21; yy=8+(i*17)%48
        d.rectangle((64+q-1,yy,64+q+1,yy+2),fill=(*SAND_LIGHT,83+i%3*25))
    im.alpha_composite(fx)
    return finish_palette(im)


def draw_shard(phase):
    im=projectile_key(1,43,111,phase)
    fx=blank((128,128));d=ImageDraw.Draw(fx,"RGBA")
    x=64+phase*2
    for k in range(9):
        yy=25+k*9
        xx=x+((-1)**k)*(12+k%3*3)
        d.point((xx,yy),fill=(*BONE,178))
    d.line([(x-3,21),(x+2,67),(x,106)],fill=(*GLASS,135),width=1)
    im.alpha_composite(fx)
    return finish_palette(im)


def projectile_key(which,width,height,phase):
    source=Image.open(SRC/'shots_key.png').convert('RGBA')
    mid=source.width//2
    source=source.crop((which*mid,0,(which+1)*mid,source.height))
    a=np.asarray(source.getchannel('A'))
    yy,xx=np.nonzero(a>24)
    crop=source.crop((int(xx.min())-4,int(yy.min())-4,int(xx.max())+5,int(yy.max())+5))
    crop=clean_hues(crop)
    crop=crop.resize((width,height),Image.Resampling.NEAREST)
    if phase:
        crop=crop.rotate(4 if which else -4,resample=Image.Resampling.NEAREST,expand=False)
    out=blank((128,128))
    out.alpha_composite(crop,((128-width)//2+phase*2,(128-height)//2))
    return out


def shots_sheet():
    cells=[draw_slug(0),draw_slug(1),draw_shard(0),draw_shard(1)]
    lane=blank((128,128));d=ImageDraw.Draw(lane,"RGBA")
    d.polygon([(52,8),(76,8),(83,22),(80,35),(88,51),(82,69),
               (87,89),(79,119),(50,119),(43,98),(48,81),(41,66),
               (47,46),(43,25)],fill=(*SAND,98))
    d.line([(52,9),(48,24),(51,39),(45,54),(50,68),(47,87),(52,105),(50,118)],fill=(*SAND_LIGHT,177),width=3)
    d.line([(77,9),(80,24),(76,40),(83,55),(78,70),(82,87),(76,106),(79,118)],fill=(*BONE,151),width=2)
    d.line([(62,13),(67,30),(61,47),(66,65),(62,82),(67,102),(64,116)],fill=(*OCHRE,105),width=3)
    for y in range(19,119,13):
        x=55+(y*7)%11
        d.line((x,y,x+10,y+3),fill=(*BONE,143),width=2)
    for k in range(40):
        x=22+(k*43)%84;y=13+(k*71)%106
        d.point((x,y),fill=(*BONE,50+k%3*25))
    cells.append(lane)
    for phase in (0,1):
        im=blank((128,128));d=ImageDraw.Draw(im,"RGBA")
        d.rectangle((44,6,84,122),fill=(*SAND_LIGHT,76))
        d.rectangle((49,6,79,122),fill=(*BONE,145))
        d.rectangle((55,6,73,122),fill=(*SUN,242))
        d.rectangle((60,6,68,122),fill=(*HEART,255))
        for k in range(12):
            yy=11+k*9;dx=4 if (k+phase)%2 else -4
            d.line([(47,yy),(37+dx,yy+6)],fill=(*SUN,177),width=2)
            d.line([(81,yy),(91-dx,yy+6)],fill=(*BONE,177),width=2)
        cells.append(im)
    charge=blank((128,128));d=ImageDraw.Draw(charge,"RGBA")
    star(d,64,64,18,1)
    for k in range(12):
        t=k*math.tau/12
        x=64+int(50*math.cos(t));y=64+int(50*math.sin(t))
        star(d,x,y,2,.55)
        d.line([(x,y),(64+int(29*math.cos(t)),64+int(29*math.sin(t)))],fill=(*SUN,161),width=2)
    cells.append(charge)
    out=blank((1024,128))
    for i,c in enumerate(cells):out.alpha_composite(c,(128*i,0))
    return out


def font(size, italic=True):
    path='/System/Library/Fonts/Supplemental/Arial Bold Italic.ttf' if italic else '/System/Library/Fonts/Supplemental/Arial Bold.ttf'
    return ImageFont.truetype(path,size)


def fit_font(text,max_width,start):
    for size in range(start,28,-1):
        f=font(size)
        if ImageDraw.Draw(blank((1,1))).textbbox((0,0),text,font=f)[2]<=max_width:
            return f
    return font(29)


def card():
    im=blank((1024,288));d=ImageDraw.Draw(im,"RGBA")
    # Same slanted nameplate structure as the approved Ember card.
    d.polygon([(47,23),(1010,23),(984,270),(14,270)],fill=(22,18,25,255),outline=(*INK,255))
    d.polygon([(56,33),(999,33),(972,257),(27,257)],fill=(30,30,55,255))
    d.polygon([(56,33),(146,33),(110,257),(27,257)],fill=(*COPPER,255))
    d.polygon([(61,43),(137,43),(107,248),(39,248)],fill=(104,67,47,255))
    d.line([(59,40),(995,40)],fill=(*SAND_LIGHT,255),width=6)
    d.polygon([(148,210),(975,210),(970,257),(120,257)],fill=(17,18,31,255))
    d.line([(155,211),(969,211)],fill=(*SAND_LIGHT,220),width=2)
    # An eight-tooth bucket emblem inside the familiar left badge.
    d.ellipse((65,101,122,158),fill=(*INK,255),outline=(*BONE,255),width=5)
    for k in range(8):
        t=k*math.tau/8
        x=94+int(25*math.cos(t));y=130+int(25*math.sin(t))
        d.rectangle((x-3,y-3,x+3,y+3),fill=(*SAND_LIGHT,255))
    star(d,94,130,5,1)
    title='DUNE-EATER';subtitle='FERAL STRIP-MINER'
    f=fit_font(title,798,83)
    d.text((157,71),title,font=f,fill=(*BONE,255),stroke_width=6,stroke_fill=(*INK,255))
    d.text((157,69),title,font=f,fill=(*HEART,255),stroke_width=1,stroke_fill=(*SAND_LIGHT,255))
    sf=fit_font(subtitle,795,33)
    d.text((165,213),subtitle,font=sf,fill=(*SUN,255),stroke_width=2,stroke_fill=(*INK,255))
    return im


def tile_preview(path: Path) -> Image.Image:
    tile=Image.open(path).convert('RGBA')
    x=(tile.width-384)//2
    y=(tile.height-384)//2
    return tile.crop((x,y,x+384,y+384))


def previews(cells, sheet, shots, card_im):
    paths=[Path('/Users/sina/Developer/Pause/.claude/worktrees/codex-desert-worm/Pause/Assets/Art/Worlds/Desert/backdrop_v3~')/f'v{i}'/'mid.png' for i in range(1,5)]
    p=Image.new('RGBA',(1920,2660),BG)
    p.alpha_composite(sheet,(0,0))
    for i,path in enumerate(paths):
        tile=tile_preview(path)
        tile.alpha_composite(cells[i])
        p.alpha_composite(tile,(i*475,1560))
    p.alpha_composite(shots,(0,1980))
    p.alpha_composite(card_im,(0,2150))
    p.save(ROOT/'preview.png')
    tile=tile_preview(paths[0])
    frames=[]
    sequence=[0,1,2,3,5,6,7,8,10,11,9]
    for idx in sequence:
        canvas=Image.new('RGBA',(C,C),BG)
        canvas.alpha_composite(tile)
        canvas.alpha_composite(cells[idx])
        frames.append(canvas.convert('RGB').resize((768,768),Image.Resampling.NEAREST))
    frames[0].save(ROOT/'preview.gif',save_all=True,append_images=frames[1:],duration=[145]*4+[280,280,280,280,280,280,360],loop=0,optimize=False)


def manifest():
    names=['idle_0','idle_1','idle_2','idle_3','hit',
           'tell_maw_a','tell_maw_b','tell_boom_a','tell_boom_b','fire_maw',
           'tell_lens_a','tell_lens_b','death_0','death_1','death_2',
           'death_3','death_4','retreat_0','retreat_1','portrait']
    data={
        'boss':'THE HARVESTER','card_name':'DUNE-EATER','title':'FERAL STRIP-MINER',
        'world':'Desert','cell_size':[384,384],'atlas_grid':[5,4],
        'frames':[{'index':i,'row':i//5,'column':i%5,'name':n} for i,n in enumerate(names)],
        'firing_parts':[
            {'part':1,'name':'MAW','attack':'aimed sand-slug fan','muzzle_px_per_cell':[174,286],'tell_frames':[5,6],'fire_frame':9},
            {'part':2,'name':'BOOM','attack':'dune lob and lane eruption','muzzle_px_per_cell':[328,207],'tell_frames':[7,8]},
            {'part':3,'name':'LENS','attack':'sun-lens heat beam','muzzle_px_per_cell':[174,83],'tell_frames':[10,11]}],
        'shots':{'cell_size':[128,128],'order':['sand_slug_0','sand_slug_1','glass_shard_0','glass_shard_1','lane_telegraph','sun_beam_0','sun_beam_1','muzzle_charge']},
        'palette':{'sun_white':'#FFF1C8','flash':'#F4E3B0','heart':'#FFF6DC','matte_copper':'#9A5E3A','bone':'#E6DCC0'},
        'sources':{'selected_hull':'src~/candidate_c.png','other_hulls':['src~/candidate_a.png','src~/candidate_b.png'],'death':'src~/death_key.png'},
        'notes':['All firing light is pale sun-white; the copper is matte.','Only nearest-neighbour resampling is used.','The code supplies the hostile pink shot rim at runtime.']}
    (ROOT/'manifest.json').write_text(json.dumps(data,indent=2)+'\n')


def main():
    cells=[finish_palette(c) for c in sheet_cells()]
    out=blank((1920,1536))
    for i,c in enumerate(cells):out.alpha_composite(c,((i%5)*C,(i//5)*C))
    out.save(ROOT/'Desert.png')
    shots=shots_sheet();shots.save(ROOT/'Desert_shots.png')
    card_im=card();card_im.save(ROOT/'Desert_card.png')
    previews(cells,out,shots,card_im)
    manifest()
    print('Built Desert boss, 20 cells, shots, card, previews, manifest')


if __name__=='__main__':main()
