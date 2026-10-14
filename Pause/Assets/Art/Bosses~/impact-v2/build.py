#!/usr/bin/env python3
"""Deterministically build seven localized, coarse-pixel boss hit strips."""
from __future__ import annotations

import colorsys
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent
S = 64
SCALE = 4
FRAMES = 12
BOSSES = ('space', 'frost', 'verdant', 'ember', 'tide', 'storm', 'desert')
INK = '#101124'
PALETTES = {
    'space': ('#233d89', '#406fe2', '#55d8ff', '#c6faff', '#ffffff'),
    'frost': ('#226880', '#4dc2d4', '#9aefff', '#d7ffff', '#ffffff'),
    'verdant': ('#18583d', '#2baf63', '#86ef64', '#c9ffad', '#ffffff'),
    'ember': ('#692c1d', '#e15422', '#ff9e24', '#ffe063', '#fff9cf'),
    'tide': ('#17677a', '#239eac', '#67dbdc', '#b8ffff', '#ffffff'),
    'storm': ('#3d397a', '#7369d1', '#aaa5f5', '#dedcff', '#ffffff'),
    'desert': ('#775327', '#bf8733', '#efc567', '#ffe7a3', '#fffdf0'),
}


def line(d, points, color, width=1):
    d.line([(round(x), round(y)) for x, y in points], fill=INK, width=width + 2, joint='curve')
    d.line([(round(x), round(y)) for x, y in points], fill=color, width=width, joint='curve')


def poly(d, points, fill, outline=INK):
    points = [(round(x), round(y)) for x, y in points]
    d.polygon(points, fill=fill)
    d.line(points + points[:1], fill=outline, width=1, joint='curve')


def dot(d, x, y, color, r=1):
    x, y = round(x), round(y)
    d.rectangle((x-r, y-r, x+r, y+r), fill=color)


def v(angle, radius):
    return (math.cos(angle)*radius, math.sin(angle)*radius)


def at(angle, radius, cx=32, cy=32):
    x, y = v(angle, radius)
    return cx+x, cy+y


def star(d, cx, cy, radius, color, diagonals=True):
    for a in ([0, math.pi/2, math.pi, 3*math.pi/2] +
              ([math.pi/4, 3*math.pi/4, 5*math.pi/4, 7*math.pi/4] if diagonals else [])):
        line(d, [(cx, cy), (cx+math.cos(a)*radius, cy+math.sin(a)*radius)], color, 1)
    dot(d, cx, cy, '#ffffff', 1)


def draw_space(d, f, p, q):
    radius = 5 + 2.55*f
    # Broken hex shield, with moving gaps rather than a solid ring.
    for i in range(6):
        if i in ((f//3) % 6, ((f//3)+3) % 6):
            continue
        a = math.pi*i/3 + math.pi/6
        line(d, [at(a, radius), at(a+math.pi/3*0.68, radius)], p[1 if i%2 else 2], 1)
    for i in range(6):
        a = i*math.pi/3 + .09*(f%3)
        r = 4 + 1.65*f
        points = [at(a,r), at(a+.13,r+3), at(a-.10,r+5), at(a+.12,r+8), at(a,r+10)]
        line(d, points, p[2 if i%2 else 3], 1)
        if f >= 3:
            line(d, [points[2], at(a+.4,r+8)], p[1], 1)
    if f in (3, 5, 7):
        dot(d, *at(.8+f, min(23, radius+3)), '#ff7bd6', 0)
    star(d, 32, 32, max(1, 5-f//2), p[3], False)


def draw_frost(d, f, p, q):
    spread = 4 + 1.85*f
    for i in range(8):
        a = 2*math.pi*i/8 + .08
        length = 7 + i%3*2
        tip = at(a, min(25,spread+length))
        left = at(a+.19, spread)
        right = at(a-.19, spread)
        poly(d, [left, tip, right, at(a,spread-2)], p[2 if i%2 else 3])
        line(d, [at(a,spread+1), tip], p[4], 1)
    star(d, 32, 32, max(2,7-f//2), p[3])
    for i in range(5):
        a = .41+i*1.31
        dot(d, *at(a, min(25,8+f*1.5+i)), p[2], 0)


def draw_verdant(d, f, p, q):
    for i in range(5):
        a = i*math.tau/5 + .13*f
        r = 4 + 1.65*f
        base = at(a,r)
        tip = at(a+.38, min(24,r+8))
        mid = at(a+.58, r+5)
        poly(d, [base, at(a+.1,r+4), tip, mid, at(a+.48,r+2)], p[2 if i%2 else 1])
        line(d, [base, mid, tip], p[3], 1)
    line(d, [(33,34),(37,36),(40,35),(41,32),(39,30)], p[1], 1)
    for i in range(7):
        dot(d, *at(i*2.4+f*.12, min(25,9+f*1.6+i%4)), p[3], 0)
    dot(d,32,32,p[2],2)
    dot(d,32,32,'#ffffff',0)


def draw_ember(d, f, p, q):
    for i in range(7):
        offset = i-3
        x = 32 + offset*(2+f*.72)
        y = 33 - (5+f*1.75) + abs(offset)*(.7+f*.16)
        r = 2 if i%2 else 1
        poly(d, [(x-r,y+1),(x,y-r-2),(x+r,y+1),(x,y+r+2)], p[2 if i%2 else 1])
        dot(d,x,y,p[3],0)
    for i in range(5):
        x = 19+i*7 + (i%2)*f*.5
        y = 34+f*.7+i%2*3
        poly(d, [(x-2,y),(x,y-2),(x+2,y),(x+1,y+2)], p[0])
    for i in range(6):
        dot(d, 14+i*7+(f%3), 26-(i%3)*5-f*.9, p[2], 0)
    star(d,32,32,max(1,5-f//2),p[3],False)
    dot(d,32,32,p[4],1)


def draw_tide(d, f, p, q):
    spread = 5 + 1.7*f
    # Open splash crown and pointed upward foam.
    line(d, [(32-spread,34),(27-spread*.45,35),(32,37),(37+spread*.45,35),(32+spread,34)], p[1], 1)
    for i in range(7):
        x = 32+(i-3)*spread/3.5
        height = 6+(3-abs(i-3))*2+f*.35
        poly(d, [(x-2,34),(x,34-height),(x+2,34)], p[2 if i%2 else 3])
    for i in range(9):
        a = math.pi + i*math.pi/8
        x,y=at(a, min(25,spread+6+(i%3)*2))
        dot(d,x,y,p[2 if i%2 else 3], 1 if i%3==0 else 0)
    for i in range(4):
        x=19+i*9+(f%2)
        y=41+f*.35+i%2*3
        d.ellipse((x-1,y-1,x+1,y+1),outline=p[2],width=1)
    dot(d,32,32,p[4],1)


def draw_storm(d, f, p, q):
    spread=4+1.8*f
    for i in range(8):
        a=i*math.pi/4
        tip=at(a,min(25,spread+8))
        elbow=at(a+.11,spread+3)
        line(d,[at(a,3),elbow,at(a-.06,spread+5),tip],p[3 if i%2 else 2],1)
        if i%2==0:
            x,y=tip
            poly(d, [(x-2,y),(x-1,y-2),(x+1,y-2),(x+2,y),(x+1,y+2),(x-1,y+2)],p[2])
    star(d,32,32,max(2,6-f//2),p[4])


def draw_desert(d, f, p, q):
    spread=5+2*f
    for side in (-1,1):
        for i in range(12):
            x=32+side*(4+(i%6)*3+f*(.8+(i%3)*.37))
            y=32+((i*7)%13-6)*(1+f*.055)
            x=max(8,min(56,x)); y=max(8,min(56,y))
            dot(d,x,y,p[1+i%3],1 if i%5==0 else 0)
        for i in range(3):
            x=32+side*(spread+2+i*4)
            y=27+i*5+(f%2)
            poly(d,[(x-2,y),(x+1,y-2),(x+2,y+1),(x-1,y+2)],p[i])
        line(d,[(32+side*3,32+side),(32+side*(spread*.65),30+side*2)],p[2],1)
    line(d,[(26,32),(38,32)],p[3],1)
    dot(d,32,32,p[4],1)

DRAW = dict(space=draw_space,frost=draw_frost,verdant=draw_verdant,
            ember=draw_ember,tide=draw_tide,storm=draw_storm,desert=draw_desert)


def make_frame(name, f):
    im=Image.new('RGBA',(S,S),(0,0,0,0))
    d=ImageDraw.Draw(im)
    # Early flash is compact; visible detail unfolds during frames 2-6.
    if f < 2:
        star(d,32,32,4+f*2,PALETTES[name][3],f==1)
        dot(d,32,32,'#ffffff',2 if f==0 else 1)
    else:
        expansion = (0, 0, 0, 1, 2, 3, 4, 4, 3, 2, 1, 0)[f]
        DRAW[name](d,expansion,PALETTES[name],None)
    if f>=7:
        alpha=np.asarray(im.getchannel('A')).copy()
        alpha=(alpha*([.74,.53,.35,.19,.07][f-7])).astype(np.uint8)
        im.putalpha(Image.fromarray(alpha,'L'))
    return im.resize((256,256),Image.Resampling.NEAREST)


def validate(name, strip):
    assert strip.size==(3072,256) and strip.mode=='RGBA'
    arr=np.asarray(strip)
    assert np.any(arr[:,:,3]==0) and np.any(arr[:,:,3]>0)
    for f in range(FRAMES):
        cell=arr[:,f*256:(f+1)*256]
        occupied=np.argwhere(cell[:,:,3]>0)
        assert len(occupied)>0, (name,f)
        assert occupied[:,0].min()>=18 and occupied[:,0].max()<238, (name,f,'y bounds')
        assert occupied[:,1].min()>=18 and occupied[:,1].max()<238, (name,f,'x bounds')
    if name!='space':
        opaque=arr[arr[:,:,3]>0,:3].astype(np.float32)/255
        h=np.array([colorsys.rgb_to_hsv(*rgb)[0] for rgb in opaque]) * 360
        sat=np.array([colorsys.rgb_to_hsv(*rgb)[1] for rgb in opaque])
        assert not np.any((h>=290)&(h<=350)&(sat>.35)), name
    assert arr[:,11*256:12*256,3].sum() < arr[:,4*256:5*256,3].sum()*.12


def main():
    strips={}
    for name in BOSSES:
        strip=Image.new('RGBA',(3072,256))
        for f in range(FRAMES):
            strip.paste(make_frame(name,f),(f*256,0))
        validate(name,strip)
        strip.save(OUT/f'boss_impact_{name}.png', optimize=True)
        strips[name]=strip
    row_h=280
    sheet=Image.new('RGBA',(3072,row_h*8),'#14102a')
    d=ImageDraw.Draw(sheet)
    font=ImageFont.load_default()
    for i,name in enumerate(BOSSES):
        y=i*row_h
        d.text((12,y+5),name.upper(),fill='#c7c4e5',font=font)
        sheet.alpha_composite(strips[name],(0,y+24))
    y=7*row_h
    d.rectangle((0,y,3071,y+279),fill='#777777')
    d.text((12,y+5),'FRAME 4 — 1:1',fill='#ffffff',font=font)
    for i,name in enumerate(BOSSES):
        x=i*256
        d.text((x+8,y+22),name.upper(),fill='#ffffff',font=font)
        sheet.alpha_composite(strips[name].crop((4*256,0,5*256,256)),(x,y+24))
    sheet.convert('RGB').save(OUT/'contact_sheet.png',optimize=True)
    print('Built and validated seven 3072x256 RGBA strips and contact_sheet.png')


if __name__=='__main__':
    main()
