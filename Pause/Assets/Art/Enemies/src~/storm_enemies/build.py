#!/usr/bin/env python3
"""Assemble selected image-generated Storm concepts into seven-cell sprites.

Source paint is retained; geometry is sampled only with nearest-neighbour.
Run with an output directory argument to make review copies before publishing.
"""
from pathlib import Path
import argparse
import math
import colorsys
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
DEST = HERE.parents[1]
FLY = HERE.parent / 'storm_colors' / 'flying_src'   # Codex flying repaints (art/enemy-colors-storm); preferred when present
SPEC = {
    'storm_alien': (1, 124),
    'storm_chaser': (1, 150),
    'storm_big': (1, 222),
    'storm_fighter_1': (0, 151),
    'storm_fighter_2': (1, 166),
    'storm_fighter_3': (1, 162),
    'storm_fighter_4': (1, 170),
    'storm_rock_hail': (1, 145),
    'storm_rock_ore': (0, 162),
    'storm_rock_slag': (1, 146),
    'storm_rock_islet': (1, 156),
}
WHITE = (242, 246, 255, 255)
HOT = (255, 250, 225, 255)
YELLOW = (255, 243, 160, 255)
BLUE = (173, 216, 241, 255)
STEEL = (79, 102, 126, 255)
INK = (7, 13, 24, 255)
PHASE = [0., 1., -1., .50]


def source(name, choice):
    fly = FLY / f'{name}_flying.png'
    if fly.exists():
        im = Image.open(fly).convert('RGBA')
    else:
        im = Image.open(HERE / f'{name}_candidates.png').convert('RGBA')
        cw = im.width // 3
        im = im.crop((choice*cw, 0, (choice+1)*cw, im.height))
    a = np.asarray(im.getchannel('A'))
    yy, xx = np.where(a > 112)
    return im.crop((max(0, xx.min()-4), max(0, yy.min()-4),
                    min(im.width, xx.max()+5), min(im.height, yy.max()+5)))


def temper_palette(im):
    """Remove stray generated warm/magenta lights without quantising metal ramps."""
    a = np.array(im)
    rgb = a[:, :, :3].astype(np.float32) / 255
    mx = rgb.max(2); mn = rgb.min(2); diff = mx-mn
    sat = np.divide(diff, mx, out=np.zeros_like(diff), where=mx>0)
    hue = np.zeros_like(mx)
    r = (diff>0)&(mx==rgb[:,:,0]); g=(diff>0)&(mx==rgb[:,:,1]); b=(diff>0)&(mx==rgb[:,:,2])
    hue[r]=(60*(rgb[:,:,1][r]-rgb[:,:,2][r])/diff[r])%360
    hue[g]=60*(rgb[:,:,2][g]-rgb[:,:,0][g])/diff[g]+120
    hue[b]=60*(rgb[:,:,0][b]-rgb[:,:,1][b])/diff[b]+240
    # High-chroma hues outside cold steel and pale-yellow electricity are unwanted.
    bad = (a[:,:,3]>8)&(sat>.22)&(((hue<40)|(hue>240))|((hue>70)&(hue<185)))
    neutral = mx[bad]
    a[:,:,0][bad] = np.clip(neutral*.73*255,0,255).astype('uint8')
    a[:,:,1][bad] = np.clip(neutral*.84*255,0,255).astype('uint8')
    a[:,:,2][bad] = np.clip(neutral*255,0,255).astype('uint8')
    # Generated alphas can be soft at the fringe. Hard pixels retain the fine paint.
    a[:,:,3] = np.where(a[:,:,3]<20,0,a[:,:,3])
    return Image.fromarray(a,'RGBA')


def key_pose(name, choice, target):
    side = 256 if name=='storm_big' else 192
    im = source(name,choice)
    factor = target/max(im.size)
    size = tuple(max(1,round(v*factor)) for v in im.size)
    flying = (FLY / f'{name}_flying.png').exists()
    if name=='storm_big' and not flying:
        size=(round(size[0]*1.30),size[1])
    im = im.resize(size,Image.Resampling.NEAREST)
    if not flying:
        im = temper_palette(im)
    else:
        a = np.array(im); a[:,:,3] = np.where(a[:,:,3]<20,0,a[:,:,3]); im = Image.fromarray(a,'RGBA')
    canvas = Image.new('RGBA',(side,side))
    canvas.alpha_composite(im,((side-size[0])//2,(side-size[1])//2))
    return canvas


def local_motion(key, name, phase, index):
    a=np.asarray(key)
    side=key.width
    yy,xx=np.indices((side,side),dtype=np.float32)
    cx=cy=(side-1)/2
    xn=(xx-cx)/(side*.45); yn=(yy-cy)/(side*.45)
    edge=np.clip((np.abs(xn)-.25)/.5,0,1)
    top=np.clip((-.08-yn)/.7,0,1)
    low=np.clip((yn+.15)/.7,0,1)
    dx=np.zeros_like(xx);dy=np.zeros_like(yy)
    if name=='storm_chaser':
        # Four physical rotor angles, stationary eye pod.
        rad=np.hypot(xx-cx,yy-cy)
        blade=np.clip((rad-side*.11)/(side*.13),0,1)
        angle=[0,.14,-.13,.07][index]*blade
        dx=angle*(yy-cy);dy=-angle*(xx-cx)
    elif name=='storm_alien':
        rad=np.hypot(xx-cx,yy-cy)
        ring=np.clip((rad-side*.12)/(side*.16),0,1)
        angle=phase*.067*ring
        dx=angle*(yy-cy);dy=-angle*(xx-cx)
    elif name=='storm_big':
        # The broad envelope is anchored; flank gills and tail fin flex.
        dx=phase*(2.3*edge*np.sign(xn)+2.2*top*np.sin(yy*.08))
        dy=phase*(1.6*edge*np.sin(xx*.09)+2.4*top)
    elif name.startswith('storm_fighter'):
        dx=phase*(3.1*edge*np.sign(xn)+1.6*top*np.sin(yy*.09))
        dy=phase*(2.6*edge*np.sin(xx*.07)+1.7*top)
        if name=='storm_fighter_3':
            rad=np.hypot(xx-cx,yy-(cy-side*.18))
            drum=np.clip((side*.20-rad)/(side*.09),0,1)
            angle=phase*.10*drum
            dx+=angle*(yy-(cy-side*.18));dy-=angle*(xx-cx)
        if name=='storm_fighter_4':
            dx+=phase*1.6*edge*top*np.sign(xn)
    elif name=='storm_rock_islet':
        # Mast and vapour move, slab remains fixed.
        dx=phase*(3.3*top+2.2*low*np.sin(yy*.09))
        dy=phase*.9*top
    else:
        # Mineral surface rotates in place. The irregular outer contour barely moves.
        rad=np.hypot(xn,yn)
        inner=np.clip((.93-rad)/.20,0,1)
        angle=[0,.082,-.075,.04][index]*inner
        dx=angle*(yy-cy);dy=-angle*(xx-cx)
        if name=='storm_rock_ore':
            dx+=phase*1.0*top;dy+=phase*.8*low
    sx=np.clip(np.rint(xx-dx).astype(int),0,side-1)
    sy=np.clip(np.rint(yy-dy).astype(int),0,side-1)
    return Image.fromarray(a[sy,sx],'RGBA')


def light_cycle(im, phase, name):
    arr=np.array(im)
    rgb=arr[:,:,:3].astype(np.int16)
    bright=(arr[:,:,3]>128)&(rgb.min(2)>120)&(rgb[:,:,2]>rgb[:,:,0]*.90)
    offset=[0,25,-18,37][phase]
    if name=='storm_alien':offset=[0,32,-20,45][phase]
    for c,weight in enumerate((.85,.9,1.0)):
        v=rgb[:,:,c]
        v[bright]=np.clip(v[bright]+offset*weight,0,255)
        arr[:,:,c]=v.astype('uint8')
    return Image.fromarray(arr,'RGBA')


def cold_rim(im):
    arr=np.array(im)
    a=arr[:,:,3]>150
    up=np.zeros_like(a);up[1:]=a[:-1]
    left=np.zeros_like(a);left[:,1:]=a[:,:-1]
    edge=a&(~up|~left)
    yy,xx=np.indices(a.shape)
    edge&=(xx<a.shape[1]*.55)|(yy<a.shape[0]*.40)
    edge&=arr[:,:,:3].max(2)>85
    # Pale cold edge on existing paint, leaving ink on shadow sides.
    arr[edge,:3]=np.maximum(arr[edge,:3],np.array([157,178,199],np.uint8))
    return Image.fromarray(arr,'RGBA')


def arc(draw, points, width=2):
    draw.line(points,fill=INK,width=width+2,joint='curve')
    draw.line(points,fill=BLUE,width=width,joint='curve')
    draw.point(points[len(points)//2],fill=WHITE)


def fx_pose(key,name,mode):
    side=key.width;cx=cy=side//2
    frame=local_motion(key,name,.8 if mode==4 else -0.8,1 if mode==4 else 2)
    frame=light_cycle(frame,1 if mode==4 else 3,name)
    d=ImageDraw.Draw(frame)
    if name=='storm_big': spot=(cx,cy+55)
    elif name=='storm_fighter_3':spot=(cx,cy-32)
    elif name=='storm_alien':spot=(cx,cy)
    else:spot=(cx,cy+24)
    x,y=spot;r=(6 if mode==4 else 10)+(2 if side==256 else 0)
    d.ellipse((x-r-2,y-r-2,x+r+2,y+r+2),outline=BLUE,width=2)
    d.ellipse((x-r,y-r,x+r,y+r),fill=WHITE if mode==5 else BLUE,outline=INK,width=1)
    d.ellipse((x-3,y-3,x+3,y+3),fill=HOT)
    d.arc((x-r-4,y-r-4,x+r+4,y+r+4),205,315,fill=YELLOW,width=2)
    if mode==5:
        if name=='storm_fighter_4':
            for k in range(8):
                ang=2*math.pi*k/8
                ex=cx+round(side*.36*math.cos(ang));ey=cy+round(side*.36*math.sin(ang))
                arc(d,[(cx+round(25*math.cos(ang)),cy+round(25*math.sin(ang))),
                       ((cx+ex)//2+(-1)**k*3,(cy+ey)//2), (ex,ey)])
        elif name=='storm_big':
            for k in (-1,0,1):
                arc(d,[(x+k*7,y+6),(x+k*14+3,y+24),(x+k*21-2,y+39),(x+k*27,y+53)],2)
        elif name=='storm_fighter_2':
            for xx in (cx-56,cx+56):
                d.ellipse((xx-6,cy+36,xx+6,cy+48),fill=WHITE,outline=BLUE,width=2)
                arc(d,[(xx,cy+47),(xx+2,cy+56),(xx-1,cy+64)],1)
        elif name=='storm_alien':
            for k in range(6):
                ang=2*math.pi*k/6
                ex=cx+round(53*math.cos(ang));ey=cy+round(53*math.sin(ang))
                arc(d,[(cx+round(13*math.cos(ang)),cy+round(13*math.sin(ang))),
                       ((cx+ex)//2+2,(cy+ey)//2-3),(ex,ey)],1)
        else:
            for k in (-1,0,1):
                arc(d,[(x+k*3,y+6),(x+k*6+2,y+17),(x+k*8-1,y+28)],1)
    elif name in {'storm_fighter_4','storm_big','storm_alien'}:
        for k in (-1,1):
            arc(d,[(x+k*10,y-10),(x+k*17,y-16),(x+k*25,y-12)],1)
    return frame


def hit_pose(key,name):
    f=key.copy();d=ImageDraw.Draw(f)
    side=f.width;cx=cy=side//2
    crack=[(cx-21,cy-20),(cx-11,cy-10),(cx-5,cy-16),(cx+1,cy-3),
           (cx-4,cy+9),(cx+11,cy+22)]
    d.line(crack,fill=INK,width=4,joint='curve')
    d.line([(x-1,y-1) for x,y in crack],fill=WHITE,width=2,joint='curve')
    arc(d,[(cx,cy),(cx+14,cy-8),(cx+26,cy-4)],1)
    arc(d,[(cx-4,cy+8),(cx-19,cy+16),(cx-25,cy+28)],1)
    for dx,dy in [(-28,-23),(23,-28),(28,15),(-21,30)]:
        x,y=cx+dx,cy+dy
        d.polygon([(x,y-3),(x+3,y),(x,y+4),(x-3,y)],fill=STEEL,outline=INK)
        d.point((x-1,y-2),fill=WHITE)
    d.ellipse((cx-8,cy-8,cx+8,cy+8),fill=BLUE,outline=WHITE,width=1)
    d.ellipse((cx-4,cy-4,cx+4,cy+4),fill=HOT)
    return f


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--out',type=Path,default=DEST)
    args=ap.parse_args();args.out.mkdir(parents=True,exist_ok=True)
    for name,(candidate,target) in SPEC.items():
        key=key_pose(name,candidate,target)
        if not (FLY / f'{name}_flying.png').exists(): key=cold_rim(key)   # the pale rim is an outline: not on the flying repaints
        frames=[light_cycle(local_motion(key,name,p,i),i,name) for i,p in enumerate(PHASE)]
        frames += [fx_pose(key,name,4),fx_pose(key,name,5),hit_pose(key,name)]
        side=key.width;strip=Image.new('RGBA',(7*side,side))
        for i,frame in enumerate(frames):strip.alpha_composite(frame,(i*side,0))
        strip.save(args.out/f'{name}.png',optimize=True)
        key.save(HERE/f'{name}_key.png',optimize=True)
        print(name,strip.size)


if __name__=='__main__':main()
