#!/usr/bin/env python3
"""Assemble the Tempest Engine atlas from generated painted keys.

Only nearest-neighbour resampling is used for the painted sources. All extra
marks are pixel-sized hard shapes; no blur, quantization, or global palette.
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
C = 384
WHITE = (242, 246, 255)
YELLOW = (255, 243, 160)
BLUE = (153, 199, 229)
DEEP = (33, 54, 76)
INK = (6, 10, 17)
PREVIEW_BG = (11, 11, 26, 255)


def blank(size=(C, C)):
    return Image.new("RGBA", size, (0, 0, 0, 0))


def palette_clean(im: Image.Image) -> Image.Image:
    """Steer accidental generator hues into Storm; retain every tonal value."""
    alpha = im.getchannel("A")
    hsv = np.array(im.convert("RGB").convert("HSV"), copy=True)
    h, s, v = hsv[..., 0], hsv[..., 1], hsv[..., 2]
    degrees = h.astype(np.float32) * (360.0 / 255.0)
    chroma = s > 20
    yellow = chroma & (degrees >= 45) & (degrees <= 76) & (v > 75)
    warm_bright = chroma & (degrees > 15) & (degrees < 45) & (v > 135)
    yellow |= warm_bright
    bad = chroma & ~yellow & ((degrees < 190) | (degrees > 240))
    h[bad] = int(218 / 360 * 255)
    h[yellow] = int(57 / 360 * 255)
    # Steel never becomes a cobalt or cyan field. Bright arcs may be stronger.
    metal = ~yellow & (v < 150)
    s[metal] = np.minimum(s[metal], 77)
    arc = ~yellow & ~metal
    s[arc] = np.minimum(s[arc], 124)
    s[yellow] = np.minimum(s[yellow], 94)
    out = Image.fromarray(hsv, "HSV").convert("RGB")
    out.putalpha(alpha)
    return out


def source(name: str) -> Image.Image:
    im = palette_clean(Image.open(SRC / name).convert("RGBA"))
    im = im.resize((344, 344), Image.Resampling.NEAREST)
    out = blank()
    out.alpha_composite(im, (20, 20))
    return out


def rim(im: Image.Image) -> Image.Image:
    a = np.asarray(im).copy()
    alpha = a[..., 3]
    solid = alpha > 105
    up = np.pad(solid[:-1], ((1, 0), (0, 0)), constant_values=False)
    left = np.pad(solid[:, :-1], ((0, 0), (1, 0)), constant_values=False)
    edge = solid & (~up | ~left)
    inner = solid & ~edge & (
        np.pad(edge[:-1], ((1, 0), (0, 0))) |
        np.pad(edge[:, :-1], ((0, 0), (1, 0)))
    )
    a[edge, :3] = np.clip(a[edge, :3].astype(float) * .31 + np.array(WHITE) * .69, 0, 255).astype(np.uint8)
    a[inner, :3] = np.clip(a[inner, :3].astype(float) * .65 + np.array(BLUE) * .35, 0, 255).astype(np.uint8)
    return Image.fromarray(a, "RGBA")


BASE = rim(source("hull_key.png"))
FIRE = source("fire_key.png")
DEATH = source("death_key.png")


def clipped_glow(d: ImageDraw.ImageDraw, x: int, y: int, radius: int, power: float = 1.0):
    for extra, col, alpha in [(12, DEEP, 14), (7, BLUE, 23), (3, BLUE, 50)]:
        r = radius + extra
        d.ellipse((x-r, y-r, x+r, y+r), fill=(*col, round(alpha * power)))
    d.ellipse((x-radius, y-radius, x+radius, y+radius), fill=(*WHITE, min(255, round(175 * power))))


def arc(d: ImageDraw.ImageDraw, points, power=1.0, width=1):
    # Thin hard halo, then a high-value core. Each line is pixel-crisp.
    d.line(points, fill=(*DEEP, round(48 * power)), width=width + 4, joint="curve")
    d.line(points, fill=(*BLUE, round(130 * power)), width=width + 2, joint="curve")
    d.line(points, fill=(*YELLOW, round(185 * power)), width=width, joint="curve")
    d.line(points[1:-1] if len(points) > 3 else points, fill=(*WHITE, round(220 * power)), width=1)


def warp_blades(im: Image.Image, phase: int) -> Image.Image:
    """Rotate exposed turbine blade annuli while inner hull stays registered."""
    if phase == 0:
        return im.copy()
    a = np.asarray(im)
    yy, xx = np.indices((C, C))
    sx = xx.astype(np.float32)
    sy = yy.astype(np.float32)
    angle = [0, .145, -.13, .09][phase]
    for cx, cy, sign in ((60, 243, 1), (324, 243, -1)):
        u = xx - cx
        v = yy - cy
        r = np.hypot(u, v)
        weight = np.clip((r - 24) / 18, 0, 1) * np.clip((81 - r) / 17, 0, 1)
        theta = angle * sign * weight
        ct, st = np.cos(theta), np.sin(theta)
        active = weight > 0
        sx[active] = cx + ct[active] * u[active] + st[active] * v[active]
        sy[active] = cy - st[active] * u[active] + ct[active] * v[active]
    # Small oscillation in the upper windings, with a zero displacement at seams.
    for cx in (100, 284):
        rx = np.abs(xx - cx)
        wy = np.clip((yy - 53) / 15, 0, 1) * np.clip((157 - yy) / 18, 0, 1)
        wx = np.clip((39 - rx) / 10, 0, 1)
        delta = [0, 1.8, -1.8, .9][phase] * wx * wy
        sx += delta * np.sign(xx - cx)
    ix = np.clip(np.rint(sx).astype(int), 0, C-1)
    iy = np.clip(np.rint(sy).astype(int), 0, C-1)
    return Image.fromarray(a[iy, ix], "RGBA")


def reveal_core(im: Image.Image, strength: float, radius=70):
    yy, xx = np.indices((C, C))
    dist = np.hypot((xx-192)*1.04, yy-244)
    mask = np.uint8(np.clip((radius - dist) / 8, 0, 1) * 255 * strength)
    out = im.copy()
    out.paste(FIRE, (0, 0), Image.fromarray(mask, "L"))
    return out


def idle(phase: int):
    im = warp_blades(BASE, phase)
    im = reveal_core(im, [.02, .16, .32, .11][phase], radius=42)
    fx = blank()
    d = ImageDraw.Draw(fx, "RGBA")
    # Deliberately different arc paths and lit coil rings in each drawing.
    for side in (-1, 1):
        x = 192 + side * 93
        y = 91 + (phase % 2) * 11
        arc(d, [(x-6*side, y), (x+2*side, y-4-phase), (x+8*side, y+1), (x+14*side, y-6)], .45 + phase*.09)
        for k in range(2):
            yy = 74 + k*18 + ((phase*7) % 15)
            d.arc((x-27, yy-7, x+27, yy+7), 194, 338, fill=(*BLUE, 83+phase*20), width=1)
    for j in range(3):
        x = 171 + j*21 + (-1 if j == 2 else 1)*phase
        y = 74 + j%2*14
        d.point((x, y), fill=(*WHITE, 125+phase*24))
    clipped_glow(d, 192, 245, 2+phase%2, .29+phase*.07)
    im.alpha_composite(fx)
    return im


def tell(part: int, charged: bool):
    im = idle(2 if charged else 0)
    fx = blank()
    d = ImageDraw.Draw(fx, "RGBA")
    p = 1.5 if charged else .75
    if part == 0:
        im = reveal_core(im, .93 if charged else .57, radius=62 if charged else 51)
        for pts in (
            [(163, 235), (174, 225), (188, 218), (205, 227), (221, 238)],
            [(166, 259), (179, 269), (190, 256), (204, 267), (218, 253)],
        ):
            arc(d, pts, p, 2 if charged else 1)
        clipped_glow(d, 192, 245, 6 if charged else 4, p)
    elif part == 1:
        rod_tips = [(143,101),(154,78),(173,65),(192,43),(211,66),(230,79),(241,101)]
        for i, (x,y) in enumerate(rod_tips):
            clipped_glow(d,x,y,2 if charged else 1,p*.7)
            if charged or i % 2 == 0:
                arc(d,[(x,y),(x+(-4 if x<192 else 4),y-7),(x+(7 if x<192 else -7),y-13)],p*.85)
        arc(d,[(142,104),(156,85),(173,66),(190,54),(212,65),(228,84),(242,104)],p,2 if charged else 1)
    else:
        for cx in (100,284):
            for y in (62,82,103,124,145):
                d.arc((cx-30,y-10,cx+30,y+10),185,355,fill=(*WHITE, min(255,round(93*p))),width=2 if charged else 1)
                if charged:
                    arc(d,[(cx-28,y),(cx-36,y-6),(cx-42,y+3)],p*.7)
            clipped_glow(d,cx,57,4 if charged else 2,p)
        if charged:
            arc(d,[(102,60),(137,51),(168,72),(192,52),(218,72),(248,50),(282,59)],.75,1)
    im.alpha_composite(fx)
    return im


def fire():
    im = idle(0)
    yy, xx = np.indices((C, C))
    # Reveal painted core and downward three-prong discharge only.
    core = np.hypot((xx-192)*.91, yy-242) < 83
    fan = (yy >= 268) & (yy <= 361) & (np.abs(xx-192) < (17+(yy-267)*1.04))
    mask = np.uint8((core | fan) * 255)
    im.paste(FIRE, (0,0), Image.fromarray(mask,"L"))
    return im


def hit():
    a = np.asarray(idle(1)).copy()
    solid = a[...,3] > 50
    light = a[...,:3].mean(axis=2) > 30
    mask = solid & light
    a[mask,:3] = np.clip(a[mask,:3].astype(float)*.47 + np.array(WHITE)*.53,0,255).astype(np.uint8)
    im = Image.fromarray(a,"RGBA")
    fx = blank(); d=ImageDraw.Draw(fx,"RGBA")
    arc(d,[(146,186),(160,197),(173,192),(190,213),(207,205),(226,221)],.9,2)
    clipped_glow(d,192,245,9,1.1)
    im.alpha_composite(fx)
    return im


def death(stage: int):
    if stage == 0:
        im = idle(0)
        fx = blank(); d=ImageDraw.Draw(fx,"RGBA")
        for p in ([(158,211),(169,220),(164,234),(180,242)],[(212,200),(205,215),(218,229),(207,243)],[(181,273),(189,266),(202,278),(206,288)]):
            arc(d,p,.9,2)
        im.alpha_composite(fx)
        return im
    if stage == 1:
        im = idle(2)
        yy, xx = np.indices((C,C))
        dist = np.hypot((xx-192)*.9, yy-244)
        mask = np.uint8(np.clip((124-dist)/22,0,1)*255)
        im.paste(DEATH,(0,0),Image.fromarray(mask,"L"))
        return im
    if stage == 2:
        return DEATH.copy()
    shrink = .92 if stage == 3 else .73
    opacity = .82 if stage == 3 else .54
    w = round(C*shrink)
    resized = DEATH.resize((w,w),Image.Resampling.NEAREST)
    a = np.asarray(resized).copy()
    a[...,3] = np.uint8(a[...,3].astype(float)*opacity)
    im = blank(); im.alpha_composite(Image.fromarray(a,"RGBA"),((C-w)//2,(C-w)//2))
    return im


def retreat(stage: int):
    im = idle(3-stage)
    a = np.asarray(im).copy()
    a[...,3] = np.uint8(a[...,3].astype(float)*(.82 if stage==0 else .60))
    im = Image.fromarray(a,"RGBA")
    fx = blank(); d=ImageDraw.Draw(fx,"RGBA")
    for k in range(11):
        x = 37+k*31 + (stage*7+k*5)%11
        y0 = 29+(k*29+stage*37)%69
        y1 = min(357,y0+183+(k%4)*20)
        d.line([(x,y0),(x,y1)],fill=(*BLUE,71+stage*18),width=1)
    im.alpha_composite(fx)
    return im


def portrait():
    im = idle(2)
    fx=blank();d=ImageDraw.Draw(fx,"RGBA")
    d.arc((170,223,214,267),201,337,fill=(*YELLOW,180),width=2)
    im.alpha_composite(fx)
    return im


def boss_sheet():
    cells = [idle(i) for i in range(4)] + [hit(),tell(0,False),tell(0,True),tell(1,False),tell(1,True),fire(),tell(2,False),tell(2,True)] + [death(i) for i in range(5)] + [retreat(0),retreat(1),portrait()]
    assert len(cells)==20
    sheet=blank((C*5,C*4))
    for i,cell in enumerate(cells):
        sheet.alpha_composite(cell,((i%5)*C,(i//5)*C))
    sheet.save(ROOT/"Storm.png")
    return cells,sheet


def shot_crop(name: str, quadrant: int, max_w: int, max_h: int):
    raw = palette_clean(Image.open(SRC/name).convert("RGBA"))
    w,h=raw.size
    x0=(quadrant%2)*(w//2); y0=(quadrant//2)*(h//2)
    q=raw.crop((x0,y0,x0+w//2,y0+h//2))
    a=np.asarray(q)
    ys,xs=np.nonzero(a[...,3]>30)
    q=q.crop((int(xs.min()),int(ys.min()),int(xs.max()+1),int(ys.max()+1)))
    sc=min(max_w/q.width,max_h/q.height)
    size=(max(1,round(q.width*sc)),max(1,round(q.height*sc)))
    return q.resize(size,Image.Resampling.NEAREST)


def shot_cell(kind: int):
    out=blank((128,128))
    if kind==0:
        art=shot_crop("shots_candidate_b.png",0,65,109)
    elif kind==1:
        art=shot_crop("shots_candidate_a.png",0,62,107)
    elif kind==2:
        art=shot_crop("shots_candidate_b.png",1,56,106)
    elif kind==3:
        art=shot_crop("shots_candidate_a.png",1,51,105)
    elif kind==4:
        # Painted beam under a native column warning band.
        art=shot_crop("shots_candidate_a.png",2,22,112)
        layer=blank((128,128)); d=ImageDraw.Draw(layer,"RGBA")
        for width,col,alpha in [(40,DEEP,15),(27,BLUE,33),(13,YELLOW,65),(5,WHITE,107)]:
            d.rectangle((64-width//2,11,64+width//2,117),fill=(*col,alpha))
        out.alpha_composite(layer)
    elif kind==5:
        art=shot_crop("shots_candidate_a.png",2,56,113)
    elif kind==6:
        art=shot_crop("shots_candidate_b.png",2,61,113)
    else:
        art=shot_crop("shots_candidate_a.png",3,90,90)
    out.alpha_composite(art,((128-art.width)//2,(128-art.height)//2))
    return out


def shots_sheet():
    sheet=blank((1024,128))
    cells=[shot_cell(i) for i in range(8)]
    for i,cell in enumerate(cells):sheet.alpha_composite(cell,(128*i,0))
    sheet.save(ROOT/"Storm_shots.png")
    return sheet


def font(name,size):
    path=Path('/System/Library/Fonts/Supplemental')/name
    return ImageFont.truetype(str(path),size)


def fitted_font(text,name,max_size,max_width):
    for size in range(max_size,20,-1):
        f=font(name,size)
        if ImageDraw.Draw(blank((1,1))).textbbox((0,0),text,font=f,stroke_width=0)[2] <= max_width:
            return f
    return font(name,21)


def card():
    out=blank((1024,288)); d=ImageDraw.Draw(out,"RGBA")
    # Same slanted nameplate geometry as Ember's 1024x288 intro card.
    d.polygon([(46,21),(1015,21),(987,270),(13,270)],fill=(11,13,26,255),outline=(*INK,255),width=5)
    d.polygon([(58,34),(1001,34),(980,213),(36,213)],fill=(23,34,60,255))
    d.polygon([(37,214),(981,214),(975,254),(27,254)],fill=(9,17,31,255))
    d.line([(57,39),(999,39)],fill=(*WHITE,255),width=4)
    d.line([(145,49),(996,49)],fill=(*YELLOW,255),width=3)
    d.polygon([(55,33),(146,33),(113,255),(23,255)],fill=(*WHITE,255))
    d.polygon([(60,43),(137,43),(105,253),(27,253)],fill=(122,162,190,255))
    d.polygon([(138,152),(112,253),(83,253),(111,152)],fill=(35,54,76,255))
    d.polygon([(100,92),(126,121),(100,150),(68,121)],fill=(*INK,255))
    d.polygon([(100,103),(115,121),(100,138),(80,121)],fill=(*YELLOW,255))
    title="TEMPEST ENGINE"
    f=fitted_font(title,"Arial Bold Italic.ttf",80,825)
    d.text((157,68),title,font=f,fill=(*WHITE,255),stroke_width=6,stroke_fill=(*INK,255))
    # Stepped highlight and shadow carry the white-yellow neon treatment.
    d.text((157,67),title,font=f,fill=(*WHITE,255),stroke_width=1,stroke_fill=(*YELLOW,255))
    subtitle="STORM-MAKING TURBINE"
    sf=fitted_font(subtitle,"Arial Bold Italic.ttf",32,815)
    d.text((165,215),subtitle,font=sf,fill=(*YELLOW,255),stroke_width=2,stroke_fill=(*INK,255))
    out.save(ROOT/"Storm_card.png")
    return out


def deck(name,x,y):
    im=Image.open(ROOT.parents[1]/"Worlds"/"Storm"/"descent~"/name).convert("RGBA")
    return im.crop((x,y,x+C,y+C))


def preview(cells,sheet,shots,card_im):
    out=Image.new("RGBA",(1920,2880),PREVIEW_BG)
    out.alpha_composite(sheet,(0,0))
    for row,name in enumerate(("storm_cloud_deck_dark.png","storm_cloud_deck.png")):
        for k in range(4):
            x=192+k*C; y=1580+row*395
            out.alpha_composite(deck(name,170+k*390,285),(x,y))
            out.alpha_composite(cells[k],(x,y))
    out.alpha_composite(shots,(448,2394))
    out.alpha_composite(card_im,(448,2540))
    out.save(ROOT/"preview.png")
    frames=[]
    sequence=[0,1,2,3,5,6,7,8,10,11,9]
    for idx in sequence:
        frame=Image.new("RGBA",(768,768),PREVIEW_BG)
        frame.alpha_composite(cells[idx].resize((768,768),Image.Resampling.NEAREST))
        frames.append(frame.convert("P",palette=Image.Palette.ADAPTIVE,colors=256))
    frames[0].save(ROOT/"preview.gif",save_all=True,append_images=frames[1:],duration=[110]*4+[220,300,220,300,220,300,320],loop=0,disposal=2)


def main():
    cells,sheet=boss_sheet()
    shots=shots_sheet()
    card_im=card()
    preview(cells,sheet,shots,card_im)


if __name__=='__main__':main()
