"""Build the Verdant lash and rolling-log atlases from image-generated paintings.

The source paintings are in candidates/. All frame work is on a 64 px native
grid; the exported atlases are exact 2x nearest-neighbour copies.
"""
from pathlib import Path
import colorsys
import math
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
SRC = HERE / 'candidates'
N = Image.Resampling.NEAREST
L = Image.Resampling.LANCZOS
INK = (11, 11, 26)
BARK = [(11,11,26),(40,29,21),(58,42,26),(74,54,31),(90,63,38),(112,82,49),(138,108,58)]
LEAF = [(11,11,26),(23,43,27),(30,58,36),(39,73,43),(47,90,51),(60,105,59),(74,122,68)]
PINK = [(63,15,57),(105,21,92),(154,30,131),(204,39,172),(255,79,216),(255,138,230),(255,224,248)]


def clean(source, size):
    """Quantize the source painting to rich fixed ramps and stepped alpha."""
    im = source.resize(size, L)
    a = np.array(im.convert('RGBA'))
    out = np.zeros_like(a)
    rgb = a[:, :, :3] / 255.0
    alpha = a[:, :, 3]
    for y in range(size[1]):
        for x in range(size[0]):
            if alpha[y,x] < 42:
                continue
            r,g,b = rgb[y,x]
            h,s,v = colorsys.rgb_to_hsv(r,g,b)
            hue=h*360
            if 45 < hue < 160 and s > .18 and g >= r*.8:
                ramp=LEAF
            elif (5 < hue < 80 and r >= b*1.1 and s > .15) or (r>b*1.3 and g>b*.8 and s<.8):
                ramp=BARK
            elif (hue >= 286 or hue < 18) and (r > g*1.15 or b > g*1.2):
                ramp=PINK
            elif v > .79 and s < .25:
                ramp=PINK
            else:
                ramp=BARK if r >= g else LEAF
            level = min(6,max(1,int(v*7.2)))
            if ramp is PINK and v>.84 and s<.22:
                level=6
            out[y,x,:3]=ramp[level]
            out[y,x,3]=255 if alpha[y,x]>205 else (192 if alpha[y,x]>135 else (96 if alpha[y,x]>72 else 24))
    return Image.fromarray(out,'RGBA')


def put(base, part, xy):
    base.alpha_composite(part, xy)


def pink_mark(draw, points, width=2, hot=False):
    draw.line(points, fill=(*PINK[3],255),width=width+2,joint='curve')
    draw.line(points, fill=(*PINK[5 if hot else 4],255),width=width,joint='curve')
    if hot:
        draw.line(points, fill=(*PINK[6],255),width=1,joint='curve')


def lash_cells():
    raw=Image.open(SRC/'lash_candidate_b.png').convert('RGBA')
    # Three painted forms in this candidate: vertical vine, hooked tip, bud.
    vine=clean(raw.crop((92,0,620,1030)),(36,64))
    hook=clean(raw.crop((605,300,1024,960)),(49,48))
    bud=clean(raw.crop((595,960,1024,1510)),(50,44))
    frames=[]
    for t in range(2):
        c=Image.new('RGBA',(64,64)); d=ImageDraw.Draw(c)
        # The 52-pixel interior is a repeat tile. Its two boundary rows are
        # matched, while the atlas retains the required transparent border.
        segment=vine.crop((0,6+t*3,36,58+t*3))
        if t:
            segment=Image.fromarray(np.roll(np.asarray(segment),4,axis=0),'RGBA')
        put(c,segment,(14,6))
        d=ImageDraw.Draw(c)
        for j in range(5):
            y=12+j*10
            x=31+int(3*math.sin(j*1.4+t))
            pink_mark(d,[(x-3,y-3),(x,y),(x+2,y+3)],2,j%2==t)
        # Make the actual overlap boundary exact, including RGB and alpha.
        a=np.array(c); a[57]=a[6]; a[56]=a[7]
        frames.append(Image.fromarray(a,'RGBA'))
    for t in range(2):
        c=Image.new('RGBA',(64,64))
        part=hook.rotate((-3 if t else 2),N,expand=False)
        put(c,part,(8+t,8))
        d=ImageDraw.Draw(c)
        pink_mark(d,[(29,19),(35+t,12),(43+t,9)],3,True)
        frames.append(c)
    for t in range(2):
        c=Image.new('RGBA',(64,64))
        part=bud.resize((49+t*2,43+t*2),N)
        put(c,part,(7-t,9-t))
        d=ImageDraw.Draw(c)
        pink_mark(d,[(31,31),(30,21-t),(31,13-t)],3,True)
        frames.append(c)
    # A dotted, advancing arc preview. The two frames are phase-shifted.
    for t in range(2):
        c=Image.new('RGBA',(64,64));d=ImageDraw.Draw(c)
        for j in range(11):
            ang=math.radians(212+j*11)
            x=int(32+24*math.cos(ang));y=int(34+23*math.sin(ang))
            if (j+t)%3==0:
                d.ellipse((x-2,y-2,x+2,y+2),fill=(*PINK[6],255))
            else:
                d.ellipse((x-1,y-1,x+1,y+1),fill=(*PINK[4],255))
        frames.append(c)
    return frames


def log_cells():
    raw=Image.open(SRC/'log_candidate_b.png').convert('RGBA')
    base=clean(raw.crop((19,64,1903,769)),(120,54))
    ba=np.array(base)
    frames=[]
    for t in range(8):
        c=Image.new('RGBA',(128,64));a=np.array(c)
        # End splinters and cut rings anchor the log. Rotate the painted bark
        # surface by one eighth of its 54-pixel circumference each frame.
        a[5:59,4:124]=ba
        for x in range(17,111):
            col=ba[:,x-4,:]
            rolled=np.roll(col,t*7,axis=0)
            mask=ba[:,x-4,3]>24
            # Keep the painted cylinder silhouette solid as texture rotates;
            # transparent source pixels must not punch holes in the trunk.
            moved=np.where((rolled[:,3]>24)[:,None],rolled[:,:3],col[:,:3])
            a[5:59,x,:3][mask]=moved[mask]
        c=Image.fromarray(a,'RGBA')
        d=ImageDraw.Draw(c)
        # Rolling seams follow the painted woodgrain, with a moving hot crest.
        for k in range(4):
            cy=12+((k*14+t*7)%43)
            pts=[(18,cy+1),(34,cy-3),(47,cy+2),(61,cy-2),(78,cy+1),(93,cy-3),(109,cy)]
            pink_mark(d,pts,3,hot=(k+t)%2==0)
        # End grain rings remain visible on every frame as the trunk rolls.
        for xx in (12,115):
            d.arc((xx-4,10,xx+4,54),65,295,fill=(*PINK[5],255),width=3)
            d.arc((xx-2,13,xx+2,51),65,295,fill=(*PINK[6],255),width=1)
        frames.append(c)
    return frames


def write():
    lash=Image.new('RGBA',(512,64))
    for i,c in enumerate(lash_cells()):put(lash,c,(i*64,0))
    lash.resize((1024,128),N).save(ROOT/'verdant_attack_lash.png')
    log=Image.new('RGBA',(512,128))
    for i,c in enumerate(log_cells()):put(log,c,((i%4)*128,(i//4)*64))
    log.resize((1024,256),N).save(ROOT/'verdant_attack_log.png')

if __name__=='__main__':write()
