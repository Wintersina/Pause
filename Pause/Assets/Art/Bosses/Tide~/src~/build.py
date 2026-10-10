#!/usr/bin/env python3
"""Build the staged Iron Kraken art from one painted hull and one maw edit.

All changes to the painted sprite use nearest-neighbour sampling at 384 px.
The localized effects are painted at native resolution; no palette reduction.
"""
from __future__ import annotations

import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
ART = ROOT.parents[1]
C = 384
MINT = (124, 242, 192)
WHITE = (237, 255, 242)
VIOLET = (115, 131, 245)
INK = (5, 10, 17)


def rgba():
    return Image.new("RGBA", (C, C), (0, 0, 0, 0))


def source(name):
    im = Image.open(SRC / name).convert("RGBA")
    # Preserve every painted value/detail while moving accidental red rust and
    # lamps into the Tide brass / violet hue families.
    hsv=np.asarray(im.convert("RGB").convert("HSV")).copy()
    h,s,v=hsv[...,0],hsv[...,1],hsv[...,2]
    valid=s>13
    h[valid&(h<11)]=14                    # 0..15 deg -> warm brown 20 deg
    h[valid&(h>=244)]=227                 # near-red magenta -> violet pink
    brown=valid&(h>=11)&(h<=32)
    s[brown]=np.minimum(s[brown],112)     # below 0.45 after RGB rounding
    rgb=Image.fromarray(hsv,"HSV").convert("RGB")
    rgb.putalpha(im.getchannel("A"))
    im=rgb
    im = im.resize((344, 344), Image.Resampling.NEAREST)
    out = rgba()
    out.alpha_composite(im, (20, 20))
    return out


def selective_rim(im):
    a=np.asarray(im).copy()
    alpha=a[...,3]
    solid=alpha>70
    up=np.pad(alpha[:-1,:],((1,0),(0,0)))
    left=np.pad(alpha[:,:-1],((0,0),(1,0)))
    edge=solid&((up<24)|(left<24))
    # Two stepped pixels of pale upper-left water light on solid metal only.
    inner=(np.pad(edge[:-1,:],((1,0),(0,0)))|np.pad(edge[:,:-1],((0,0),(1,0))))&solid&~edge
    a[inner,:3]=(a[inner,:3].astype(float)*0.55+np.array([115,188,163])*0.45).astype(np.uint8)
    a[edge,:3]=(a[edge,:3].astype(float)*0.25+np.array([188,250,223])*0.75).astype(np.uint8)
    return Image.fromarray(a,"RGBA")


BASE = selective_rim(source("hull_key.png"))
FIRE = source("maw_fire_key.png")
DEATH = source("death_peak_key.png")


def warp(im, phase):
    """Move the distal arm pixels while leaving the central hull registered."""
    a = np.asarray(im)
    yy, xx = np.indices((C, C))
    edge = np.clip((np.abs(xx - 192) - 62) / 120, 0, 1) ** 1.5
    dx = np.sign(xx - 192) * edge * [0, 2, -2, 1][phase]
    dy = edge * [0, -4, 4, 2][phase] * (0.75 + 0.25 * np.sin(yy / 33))
    sx = np.clip(np.rint(xx - dx).astype(int), 0, C - 1)
    sy = np.clip(np.rint(yy - dy).astype(int), 0, C - 1)
    return Image.fromarray(a[sy, sx], "RGBA")


def glow_ellipse(layer, box, color=MINT, strength=1):
    d = ImageDraw.Draw(layer, "RGBA")
    x0, y0, x1, y1 = box
    for grow, alpha in [(10, 12), (6, 25), (3, 52)]:
        d.ellipse((x0-grow, y0-grow, x1+grow, y1+grow), fill=(*color, min(255, alpha*strength)))
    d.ellipse(box, fill=(*WHITE, min(255, 90*strength)))


def core_fx(im, phase):
    layer = rgba()
    d = ImageDraw.Draw(layer, "RGBA")
    # The eye is a ring and pupil, so its dark centre survives each pulse.
    eye = [(184, 112, 199, 127), (182, 110, 201, 129), (181, 109, 202, 130), (183, 111, 200, 128)][phase]
    d.arc(eye, 188, 353, fill=(*WHITE, 100+phase*25), width=2)
    d.line([(191, 113), (194, 106)], fill=(*WHITE, 160+phase*22), width=1)
    d.line([(190, 174+phase%2), (194, 176+phase%2)], fill=(*MINT, 100+phase*28), width=2)
    im.alpha_composite(layer)
    return im


def idle(phase):
    return core_fx(warp(BASE, phase), phase)


def bolt(d, center, power=1, left=False):
    x, y = center
    rr = [12, 9, 6][min(power, 2)]
    for radius, alpha, col in [(19, 15, MINT), (13, 34, MINT), (8, 110, VIOLET), (5, 215, MINT)]:
        d.ellipse((x-radius, y-radius, x+radius, y+radius), fill=(*col, alpha))
    d.ellipse((x-rr//2, y-rr//2, x+rr//2, y+rr//2), fill=(*WHITE, 245))
    sg = -1 if left else 1
    d.line([(x+sg*5, y-5), (x+sg*13, y-13), (x+sg*19, y-12)], fill=(*VIOLET, 220), width=2)
    d.line([(x+sg*4, y+5), (x+sg*14, y+10)], fill=(*MINT, 215), width=2)


def tell(part, charged):
    im = idle(1 if charged else 0)
    layer = rgba()
    d = ImageDraw.Draw(layer, "RGBA")
    if part == 0:
        glow_ellipse(layer, (177, 181, 207, 214), strength=2 if charged else 1)
        for s in (-1, 1):
            d.line([(192+s*8, 188), (192+s*22, 198), (192+s*12, 213)], fill=(*VIOLET, 220 if charged else 130), width=2)
        if charged:
            for x, y in [(175, 222), (209, 220), (191, 234)]:
                d.ellipse((x-2,y-2,x+2,y+2), fill=(*MINT, 240))
    else:
        x = 39 if part == 1 else 345
        for y in (70, 231):
            bolt(d, (x,y), power=2 if charged else 0, left=part==1)
        x2 = 61 if part == 1 else 323
        d.line([(x, 70), (x2, 130), (x, 231)], fill=(*VIOLET, 170 if charged else 75), width=3 if charged else 1, joint="curve")
        if charged:
            for yy in (100, 169, 257):
                d.ellipse((x2-2, yy-2, x2+2, yy+2), fill=(*WHITE, 245))
    im.alpha_composite(layer)
    return im


def fire():
    im = idle(0)
    # Painted second key supplies the open beak and the water jet. It changes
    # nothing beyond this soft local mask; the original hull stays fixed.
    mask = Image.new("L", (C, C))
    d = ImageDraw.Draw(mask)
    d.polygon([(170,151),(214,151),(228,174),(239,220),(251,260),(256,331),(240,354),(145,354),(128,326),(132,260),(148,203)], fill=255)
    # Native stepped taper, avoiding an obvious patch edge.
    m = np.asarray(mask).copy()
    from PIL import ImageFilter
    mask = Image.fromarray(m).filter(ImageFilter.GaussianBlur(2))
    im.paste(FIRE, (0,0), mask)
    layer = rgba()
    d = ImageDraw.Draw(layer, "RGBA")
    for x,y in [(173,230),(210,243),(162,279),(225,299)]:
        d.line([(x,y),(x+5,y+7),(x+1,y+16)], fill=(*VIOLET,190), width=2)
    im.alpha_composite(layer)
    return im


def hit():
    a=np.asarray(idle(0)).copy()
    opaque=a[...,3]>45
    rgb=a[...,:3]
    bright=(rgb.mean(axis=2)>38)&opaque
    rgb[bright]=np.minimum(255, rgb[bright].astype(np.int16)*0.28+np.array([147,242,211])*0.72).astype(np.uint8)
    a[...,:3]=rgb
    im=Image.fromarray(a,"RGBA")
    layer=rgba();glow_ellipse(layer,(169,98,214,144),strength=2)
    im.alpha_composite(layer)
    return im


def star(d, x, y, size, color=WHITE, alpha=255):
    d.line([(x-size,y),(x+size,y)],fill=(*color,alpha),width=max(1,size//8))
    d.line([(x,y-size),(x,y+size)],fill=(*color,alpha),width=max(1,size//8))
    d.line([(x-size//2,y-size//2),(x+size//2,y+size//2)],fill=(*VIOLET,alpha),width=2)
    d.line([(x-size//2,y+size//2),(x+size//2,y-size//2)],fill=(*MINT,alpha),width=2)


def death(stage):
    # The peak is a dedicated painted edit of the same hull. Recede to and
    # from it by revealing real cracks and armor fragments, not flat stars.
    if stage==0:
        im=idle(0)
        layer=rgba();d=ImageDraw.Draw(layer,"RGBA")
        cracks=[[(181,85),(177,93),(184,105),(177,116),(181,125)],
                [(217,96),(211,109),(219,120),(213,135)],
                [(174,172),(183,177),(178,187),(187,193),(185,205)]]
        for line in cracks:
            d.line(line,fill=(*VIOLET,200),width=3)
            d.line(line,fill=(*MINT,225),width=1)
        glow_ellipse(layer,(187,118,198,130),strength=1)
        im.alpha_composite(layer)
        return im
    if stage==1:
        im=BASE.copy()
        yy,xx=np.indices((C,C))
        # Soft radial reveal of the painted split dome and first fragments.
        radius=np.hypot((xx-192)*0.85,yy-168)
        mask=np.clip((135-radius)/72,0,1)
        mask=np.uint8(mask*230)
        im.paste(DEATH,(0,0),Image.fromarray(mask,"L"))
        return im
    if stage==2:
        return DEATH.copy()
    scale=0.90 if stage==3 else 0.67
    w=int(C*scale)
    shrunken=DEATH.resize((w,w),Image.Resampling.NEAREST)
    a=np.asarray(shrunken).copy()
    a[...,3]=(a[...,3].astype(float)*(0.78 if stage==3 else 0.55)).astype(np.uint8)
    im=rgba();im.alpha_composite(Image.fromarray(a,"RGBA"),((C-w)//2,(C-w)//2))
    return im


def retreat(stage):
    im=idle(0)
    a=np.asarray(im).copy()
    a[...,3]=(a[...,3].astype(np.float32)*(0.85 if stage==0 else 0.62)).astype(np.uint8)
    im=Image.fromarray(a,"RGBA")
    layer=rgba(); d=ImageDraw.Draw(layer,"RGBA")
    for k in range(10):
        x=43+k*33+((k*7+stage*9)%13)
        y0=36+((k*23+stage*27)%77)
        y1=min(358,y0+198+(k%3)*22)
        d.line([(x,y0),(x,y1)],fill=(*MINT,65 if stage==0 else 95),width=1+(k%3==0))
        d.line([(x+2,y0+30),(x+2,y1-35)],fill=(*VIOLET,45),width=1)
    im.alpha_composite(layer)
    return im


def portrait():
    im=idle(2)
    layer=rgba();d=ImageDraw.Draw(layer,"RGBA")
    d.arc((174,105,209,140),185,350,fill=(*WHITE,205),width=2)
    im.alpha_composite(layer)
    return im


def build_boss():
    cells=[idle(i) for i in range(4)]+[hit(),tell(0,False),tell(0,True),tell(1,False),tell(1,True),fire(),tell(2,False),tell(2,True)]+[death(i) for i in range(5)]+[retreat(0),retreat(1),portrait()]
    assert len(cells)==20
    sheet=Image.new("RGBA",(C*5,C*4))
    for i,im in enumerate(cells): sheet.alpha_composite(im,((i%5)*C,(i//5)*C))
    sheet.save(ROOT/"Tide.png")
    return cells,sheet


def shot_cell(kind):
    im=Image.new("RGBA",(128,128)); d=ImageDraw.Draw(im,"RGBA")
    if kind<=1:  # pressure pearl, two phases
        x=64+(kind*2);y=64
        for r,col,alpha in [(31,MINT,14),(23,MINT,35),(16,VIOLET,100),(12,MINT,230),(7,WHITE,255)]:
            d.ellipse((x-r,y-r,x+r,y+r),fill=(*col,alpha))
        d.arc((x-15,y-15,x+15,y+15),194,322,fill=(*WHITE,250),width=3)
        for j in range(5):
            sx=x-16-j*6; sy=y-10+j*5+(kind%2)*2
            d.ellipse((sx-2,sy-2,sx+2,sy+2),fill=(*MINT,200-j*25))
    elif kind<=3:  # long crystal/lightning shard
        x=64; lean=-5 if kind==2 else 5
        d.polygon([(x+lean,14),(x+14,52),(x+9,93),(x,116),(x-11,79),(x-15,49)],fill=(*VIOLET,70))
        d.polygon([(x+lean,18),(x+9,54),(x+5,88),(x,109),(x-7,83),(x-9,51)],fill=(*MINT,230))
        d.polygon([(x+lean,19),(x+3,59),(x,96),(x-3,58)],fill=(*WHITE,250))
        d.line([(x-21,74),(x-9,62),(x-17,49)],fill=(*VIOLET,230),width=3)
        d.line([(x+12,58),(x+23,45),(x+19,30)],fill=(*WHITE,210),width=2)
    elif kind==4:  # lane tell
        d.rectangle((56,9,72,119),fill=(*VIOLET,36))
        d.rectangle((62,12,66,116),fill=(*MINT,125))
        for y in range(12,118,13):
            d.line((44,y,84,y),fill=(*MINT,105 if y%2 else 170),width=2)
            d.point((64,y),fill=(*WHITE,240))
    elif kind<=6:  # pressure beam, repeatable center
        width=15 if kind==5 else 22
        d.rectangle((64-width,4,64+width,124),fill=(*MINT,50))
        d.rectangle((57,4,71,124),fill=(*VIOLET,150))
        d.rectangle((61,4,67,124),fill=(*WHITE,240))
        for y in range(10,122,22):
            x=40+(y*7+kind*13)%12
            d.line([(x,y),(x+12,y+7),(x+6,y+17)],fill=(*MINT,240),width=2)
            d.line([(128-x,y+3),(116-x,y+10)],fill=(*VIOLET,215),width=2)
    else:
        for r,a,col in [(42,12,MINT),(31,27,MINT),(22,60,VIOLET),(15,150,MINT),(8,250,WHITE)]:
            d.ellipse((64-r,64-r,64+r,64+r),fill=(*col,a))
        star(d,64,64,28)
        for a in range(0,360,45):
            rad=math.radians(a);x=64+int(math.cos(rad)*40);y=64+int(math.sin(rad)*40)
            d.rectangle((x-2,y-2,x+2,y+2),fill=(*MINT,235))
    return im


def build_shots():
    out=Image.new("RGBA",(1024,128))
    for i in range(8):out.alpha_composite(shot_cell(i),(i*128,0))
    out.save(ROOT/"Tide_shots.png")
    return out


def font(size,bold=False):
    choices=["/System/Library/Fonts/Supplemental/Arial Bold Italic.ttf","/System/Library/Fonts/Supplemental/Arial Bold.ttf"] if bold else ["/System/Library/Fonts/SFNSMono.ttf"]
    for p in choices:
        try:return ImageFont.truetype(p,size)
        except OSError:pass
    return ImageFont.load_default()


def build_card():
    # Compose at half size, then nearest-neighbour enlarge for stepped pixels.
    im=Image.new("RGBA",(512,144));d=ImageDraw.Draw(im,"RGBA")
    d.polygon([(31,12),(504,12),(493,128),(15,128)],fill=(5,15,25,255),outline=(*MINT,210),width=2)
    d.polygon([(40,16),(500,16),(487,93),(26,93)],fill=(15,37,48,255))
    d.polygon([(31,13),(73,13),(56,128),(15,128)],fill=(22,91,82,255))
    d.polygon([(38,17),(64,17),(47,123),(22,123)],fill=(*MINT,240))
    d.line([(73,13),(502,13)],fill=(*WHITE,235),width=1)
    d.line([(65,102),(487,102)],fill=(*VIOLET,130),width=2)
    d.polygon([(46,48),(59,61),(45,74),(32,61)],fill=(*WHITE,255),outline=(*INK,255))
    d.ellipse((42,57,49,64),fill=(*MINT,255))
    name="IRON KRAKEN"; f=font(42,True)
    while d.textbbox((0,0),name,font=f)[2]>398:
        f=font(f.size-1,True)
    d.text((78,35),name,font=f,fill=(3,12,18,255),stroke_width=2,stroke_fill=(*MINT,255))
    d.text((78,34),name,font=f,fill=(217,255,239,255))
    d.text((83,105),"TYRANT OF THE DEEP",font=font(16),fill=(*MINT,255))
    d.text((440,106),"V // 05",font=font(10),fill=(*VIOLET,235))
    im=im.resize((1024,288),Image.Resampling.NEAREST)
    im.save(ROOT/"Tide_card.png")
    return im


def mid_tile(i):
    p=ART/"Backgrounds"/"Resources"/"Worlds"/"Tide"/"Backdrop3"/f"v{i}"/"mid.png"
    im=Image.open(p).convert("RGBA")
    x=(im.width-C)//2;y=(im.height-C)//2
    return im.crop((x,y,x+C,y+C))


def preview(cells,sheet,shots,card):
    out=Image.new("RGBA",(1920,2420),(11,11,26,255))
    out.alpha_composite(sheet,(0,0))
    for i in range(4):
        x=i*C;y=1548
        out.alpha_composite(mid_tile(i+1),(x,y))
        out.alpha_composite(cells[i],(x,y))
    out.alpha_composite(shots,(32,1950))
    out.alpha_composite(card,(32,2100))
    d=ImageDraw.Draw(out)
    for i in range(4):d.text((i*C+8,1548),f"TIDE v{i+1}",fill=WHITE,font=font(13))
    out.convert("RGB").save(ROOT/"preview.png")
    frames=[]
    seq=[0,1,2,3,5,6,7,8,10,11,9,3]
    for k in seq:
        frame=Image.new("RGBA",(768,384),(11,11,26,255))
        frame.alpha_composite(cells[k],(0,0))
        frame.alpha_composite(mid_tile((k%4)+1),(384,0))
        frame.alpha_composite(cells[k],(384,0))
        frames.append(frame.convert("RGB"))
    frames[0].save(ROOT/"preview.gif",save_all=True,append_images=frames[1:],duration=[150]*4+[260,180]*3+[110,180],loop=0,optimize=False)


def main():
    cells,sheet=build_boss();shots=build_shots();card=build_card();preview(cells,sheet,shots,card)
    print("built",ROOT)


if __name__=="__main__":main()
