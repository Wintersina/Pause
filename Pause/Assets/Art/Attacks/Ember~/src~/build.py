"""Build the Ember boss laser sheets from the selected generated painting.

All composition happens at 64 px per 128 px cell. Final exports are strict
2x nearest-neighbour copies. The generator's source painting is kept here.
"""
from pathlib import Path
from PIL import Image, ImageDraw
import colorsys
import math
import numpy as np

ROOT = Path(__file__).resolve().parent.parent
SRC = Image.open(Path(__file__).with_name('candidate_painted.png')).convert('RGBA')
PINK = [(89, 15, 73), (157, 32, 123), (216, 48, 172), (255, 79, 216), (255, 138, 230), (255, 224, 248)]
ORANGE = [(51, 33, 20), (90, 56, 31), (138, 75, 30), (187, 101, 40), (224, 120, 45), (255, 145, 53)]
WHITE = (255, 244, 225)
BLACK = (11, 11, 26)
BOX = {
    'charge': (22, 28, 477, 445),
    'star': (472, 4, 975, 482),
    'beam': (1060, 6, 1470, 620),
    'muzzle': (12, 470, 485, 1008),
    'impact': (496, 493, 1031, 1014),
    'reticle': (1060, 703, 1507, 1018),
}

def clean_crop(kind, size):
    im = SRC.crop(BOX[kind]).resize(size, Image.Resampling.NEAREST)
    a = np.array(im, dtype=np.uint8)
    # Eliminate diffuse generated backdrop while retaining solid painterly clusters.
    src_a = a[:, :, 3].astype(np.int16)
    alpha = np.where(src_a < 110, 0, np.where(src_a < 170, (src_a-110)*3, 255))
    alpha = np.minimum(alpha, 255).astype(np.uint8)
    r, g, b = [a[:, :, i].astype(np.float32) / 255 for i in range(3)]
    vmax = np.maximum.reduce([r, g, b])
    vmin = np.minimum.reduce([r, g, b])
    sat = np.where(vmax > 0, (vmax-vmin)/np.maximum(vmax, .001), 0)
    warm = (r > g*1.5) & (g > b*1.08) & (sat > .38)
    dark = vmax < .35
    # Indexed multi-tone ramps retain the painting's light/dark detail.
    pi = np.clip((vmax*6.4).astype(int), 0, 5)
    oi = np.clip((vmax*6.1).astype(int), 0, 5)
    out = np.zeros_like(a)
    out[:, :, :3] = np.array(PINK, dtype=np.uint8)[pi]
    out[warm, :3] = np.array(ORANGE, dtype=np.uint8)[oi[warm]]
    out[dark, :3] = np.array(PINK, dtype=np.uint8)[np.minimum(pi[dark], 2)]
    out[(vmax > .91) & (sat < .28), :3] = WHITE
    out[:, :, 3] = alpha
    # Set fully clear RGB to zero to prevent matte fringes.
    out[alpha == 0, :3] = 0
    return Image.fromarray(out, 'RGBA')

def blank(w=64, h=64):
    return Image.new('RGBA', (w,h))

def put(dst, src, x=None, y=None):
    if x is None: x = (dst.width-src.width)//2
    if y is None: y = (dst.height-src.height)//2
    dst.alpha_composite(src, (x,y))

def dot(dst, xy, color=PINK[4], rad=1):
    d=ImageDraw.Draw(dst)
    x,y=xy
    d.ellipse((x-rad,y-rad,x+rad,y+rad), fill=(*color,255))

def arcs(dst, frame, radius, count=5, cx=32, cy=32):
    d=ImageDraw.Draw(dst)
    for k in range(count):
        t=2*math.pi*(k/count+frame*.105)
        x=int(cx+radius*math.cos(t)); y=int(cy+radius*.82*math.sin(t))
        c=PINK[4+(k+frame)%2]
        d.line((x-2,y,x+2,y),fill=(*c,255),width=1)
        d.line((x,y-2,x,y+2),fill=(*c,255),width=1)

laser = Image.new('RGBA',(512,192))
def laser_cell(row,col,im):
    laser.alpha_composite(im,(col*64,row*64))

# Windup: painted orb and cross become successively hotter; orbital sparks converge.
sizes=[7,10,15,22,30,39,47,50]
for i,sz in enumerate(sizes):
    c=blank()
    kind='charge' if i<5 else 'star'
    s=clean_crop(kind,(sz,sz))
    put(c,s)
    if i>1: arcs(c,i, min(25,sz//2+2), count=2+i//2)
    if i==7:
        d=ImageDraw.Draw(c)
        d.ellipse((27,27,36,36),fill=(*WHITE,255))
        d.line((10,32,53,32),fill=(*PINK[5],255),width=1)
    laser_cell(0,i,c)

# Root bloom: source painting rotated and re-lit to create a rolling four-frame loop.
for i in range(4):
    c=blank()
    s=clean_crop('muzzle',(42,44)).rotate(i*90,Image.Resampling.NEAREST,expand=False)
    put(c,s)
    arcs(c,i,23,count=4)
    laser_cell(1,i,c)
for i,sz in enumerate([40,27,15,6]):
    c=blank(); put(c,clean_crop('muzzle',(sz,sz)))
    if i<2: arcs(c,i,sz//2+1,count=3)
    laser_cell(1,4+i,c)

# Rail impact grows to near cell width, then shatters upward into flecks.
for i,sz in enumerate([20,37,56,30]):
    c=blank()
    s=clean_crop('impact',(sz,sz))
    put(c,s, (64-sz)//2, 61-sz)
    d=ImageDraw.Draw(c)
    for k in range(2+i*2):
        x=32 + int((k-(2+i*2)/2)*(3+i*2))
        y=max(4,43-i*5-(k%3)*4)
        if 4<=x<60: d.line((x,y+4,x+(-1 if x<32 else 1),y),fill=(*PINK[4+k%2],255),width=1)
    laser_cell(2,i,c)
for i in range(4):
    c=blank()
    # Source painting gives the torn plasma base; flecks climb with phase.
    put(c,clean_crop('impact',(31,24)),17,31)
    d=ImageDraw.Draw(c)
    for k in range(9):
        x=8+((k*13+i*7)%48)
        y=12+((k*9-i*8)%35)
        color=PINK[3+(k+i)%3]
        d.line((x,y+2,x+(-1 if x<32 else 1),y),fill=(*color,255),width=1)
    laser_cell(2,4+i,c)

# Seamless moving body. A cyclic read of the painted beam provides turbulence;
# bright filaments travel down by one quarter of a vertical period each frame.
beam_src=np.array(clean_crop('beam',(48,128)))
beam_raw=np.array(SRC.crop(BOX['beam']).resize((48,128),Image.Resampling.NEAREST))
body=Image.new('RGBA',(256,128))
for frame in range(4):
    c=np.zeros((128,64,4),dtype=np.uint8)
    for y in range(128):
        phase=(y-1+frame*31)%127
        # Equal top/bottom samples: the final row joins the first.
        if y==127: phase=(frame*31-1)%127
        wob=int(2*math.sin(2*math.pi*y/32+frame*math.pi/2))
        for x in range(7,57):
            dx=x-32-wob
            sx=min(47,max(0,int(dx+24)))
            px=beam_src[phase,sx].copy()
            raw=beam_raw[phase,sx]
            ad=abs(dx)
            # Shape, molten secondary band, pink hot rim, and transparent outer edge.
            edge=20+2.5*math.sin(y*.22+frame*1.7)+1.5*math.sin(y*.51-frame)
            if ad>edge+4: continue
            if ad>edge:
                px[:3]=PINK[2+(y+frame)%2]
                px[3]=max(0,int(95*(edge+4-ad)/4))
            elif ad>17:
                # Retain the painterly brightness changes in the generated edge.
                level=min(5,max(2,int(raw[0])//46))
                px[:3]=PINK[level]
                px[3]=max(150,int(px[3]))
            elif ad<5:
                pulse=(y//8+frame*3)%7
                px[:3]= WHITE if (int(raw[1])>185 or pulse<2) else PINK[5]
                px[3]=255
            elif ad<10:
                level=4+(int(raw[1])>160)
                px[:3]=PINK[level]
                px[3]=255
            elif ad<16:
                # Warm band follows the painted flame's brightness and grain.
                heat=(int(raw[1])+int(raw[2]))//2
                px[:3]=ORANGE[min(5,max(2,heat//37))]
                px[3]=max(220,int(px[3]))
            c[y,x]=px
    # Crackling strands and sparks crawl along both edges, with cyclic positions.
    im=Image.fromarray(c,'RGBA'); d=ImageDraw.Draw(im)
    for k in range(14):
        y=(k*23+frame*17)%127
        side=-1 if k%2 else 1
        x=32+side*(19+k%5)
        d.line((x,y,x+side*(1+k%2),min(127,y+2)),fill=(*PINK[4+k%2],255),width=1)
    # Ensure complete equality at the vertical join after sparks.
    a=np.array(im); a[-1]=a[0]
    body.alpha_composite(Image.fromarray(a,'RGBA'),(frame*64,0))

# Sight line is continuous across vertical copies, with a/b half-period motion.
tell=Image.new('RGBA',(256,64))
for f in range(2):
    c=blank(); d=ImageDraw.Draw(c)
    for y in range(-8,73,16):
        yy=y+f*8
        for xx,rad,color in [(32,1,PINK[5]),(31,0,PINK[3]),(33,0,PINK[3])]:
            if 0<=yy<64: d.ellipse((xx-rad,yy-rad,xx+rad,yy+rad),fill=(*color,255))
        for off in (-3,3):
            if 0<=yy+off<64: d.point((32,yy+off),fill=(*PINK[2],110))
    a=np.array(c);a[-1]=a[0]
    tell.alpha_composite(Image.fromarray(a,'RGBA'),(f*64,0))

for f in range(2):
    c=blank();d=ImageDraw.Draw(c)
    bright=PINK[5] if f else PINK[3]
    dim=PINK[3] if f else PINK[2]
    for sx in (-1,1):
        for sy in (-1,1):
            x=32+sx*23;y=32+sy*23
            d.line((x,y,x-sx*8,y),fill=(*bright,255),width=1)
            d.line((x,y,x,y-sy*8),fill=(*bright,255),width=1)
            d.point((x+sx,y+sy),fill=(*dim,180))
    d.line((27,32,37,32),fill=(*PINK[4],255),width=1)
    d.line((32,27,32,37),fill=(*PINK[4],255),width=1)
    dot(c,(32,32),PINK[5] if f else PINK[3],1)
    for k in range(4):
        theta=math.pi*(k/2+f/4)
        dot(c,(32+int(17*math.cos(theta)),32+int(17*math.sin(theta))),PINK[4],0)
    tell.alpha_composite(c,((f+2)*64,0))

for name,im in [('ember_attack_laser.png',laser),('ember_attack_laserbody.png',body),('ember_attack_lasertell.png',tell)]:
    im.resize((im.width*2,im.height*2),Image.Resampling.NEAREST).save(ROOT/name)
print('Built three sheets from candidate_painted.png')
