#!/usr/bin/env python3
"""Compose the selected image-generated Ember paintings into seven-cell strips.

All sprite paint comes from *_candidates.png. Sampling and movement use nearest
neighbour pixels; the only drawn additions are narrow rim, lamps and attack marks.
"""
from pathlib import Path
from collections import deque
import math
import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
OUT = HERE / 'strips'
# Candidate column (zero based), longest painted dimension in the cell.
SPEC = {
    'ember_alien': (0, 137),
    'ember_chaser': (0, 156),
    'ember_big': (2, 218),
    'ember_fighter_1': (0, 161),
    'ember_fighter_2': (0, 172),
    'ember_fighter_3': (0, 168),
    'ember_fighter_4': (0, 171),
    'ember_rock_magma': (0, 157),
    'ember_rock_cinder': (2, 157),
    'ember_rock_obsidian': (1, 164),
    'ember_rock_islet': (1, 168),
}
INK=(8,7,9,255); HOT=(255,241,208,255); WHITE=(255,249,232,255)
ORANGE=(235,105,30,255); COPPER=(128,78,46,255); ASH=(213,204,190,255)
PHASE=(0.,1.,-1.,.5)


def largest(mask):
    """Keep the main connected island after texture based background extraction."""
    h,w=mask.shape; seen=np.zeros((h,w),bool); best=[]
    for y,x in zip(*np.where(mask & ~seen)):
        if seen[y,x]: continue
        q=deque([(int(y),int(x))]);seen[y,x]=1; comp=[]
        while q:
            cy,cx=q.popleft();comp.append((cy,cx))
            for ny,nx in ((cy-1,cx),(cy+1,cx),(cy,cx-1),(cy,cx+1)):
                if 0<=ny<h and 0<=nx<w and mask[ny,nx] and not seen[ny,nx]:
                    seen[ny,nx]=1;q.append((ny,nx))
        if len(comp)>len(best):best=comp
    out=np.zeros_like(mask)
    if best:
        yy,xx=np.array(best).T;out[yy,xx]=True
    return out


def extract_background(im):
    """Texture separates the generated sprite from a smooth accidental backdrop.

    The mask is computed at source resolution; no blurred paint reaches the output.
    """
    a=np.asarray(im.convert('RGBA'))
    rgb=Image.fromarray(a[:,:,:3],'RGB')
    smooth=np.asarray(rgb.filter(ImageFilter.GaussianBlur(5))).astype(np.int16)
    contrast=np.abs(a[:,:,:3].astype(np.int16)-smooth).sum(2)
    detail=(contrast>18)&(a[:,:,3]>90)
    # Join outline fragments and fill its enclosed hull.
    m=Image.fromarray((detail*255).astype('uint8'),'L')
    m=m.filter(ImageFilter.MaxFilter(15)).filter(ImageFilter.MinFilter(13))
    mask=largest(np.asarray(m)>0)
    # Fill gaps inside the outline by flooding its complement from the border.
    h,w=mask.shape; exterior=np.zeros_like(mask);q=deque()
    for x in range(w):
        if not mask[0,x]:exterior[0,x]=1;q.append((0,x))
        if not mask[h-1,x]:exterior[h-1,x]=1;q.append((h-1,x))
    for y in range(h):
        if not mask[y,0]:exterior[y,0]=1;q.append((y,0))
        if not mask[y,w-1]:exterior[y,w-1]=1;q.append((y,w-1))
    while q:
        y,x=q.popleft()
        for ny,nx in ((y-1,x),(y+1,x),(y,x-1),(y,x+1)):
            if 0<=ny<h and 0<=nx<w and not mask[ny,nx] and not exterior[ny,nx]:
                exterior[ny,nx]=1;q.append((ny,nx))
    mask=~exterior
    # A 1 px source erosion removes the halo fringe from the source image.
    mask=np.asarray(Image.fromarray((mask*255).astype('uint8'),'L').filter(ImageFilter.MinFilter(3)))>0
    result=a.copy();result[:,:,3]=np.where(mask,255,0).astype('uint8')
    return Image.fromarray(result,'RGBA')


def source(name,choice):
    im=Image.open(HERE/f'{name}_candidates.png').convert('RGBA')
    width=im.width//3
    im=im.crop((choice*width,0,(choice+1)*width,im.height))
    # These three generated sheets contain a smooth, semi-opaque background.
    if name in {'ember_big','ember_fighter_2','ember_rock_obsidian'}:
        im=extract_background(im)
    a=np.asarray(im.getchannel('A'));yy,xx=np.where(a>128)
    if not len(xx):raise ValueError(f'No paint in {name}')
    return im.crop((max(0,xx.min()-3),max(0,yy.min()-3),
                    min(im.width,xx.max()+4),min(im.height,yy.max()+4)))


def temper(im):
    """Steer generated warm pixels into the user-specified Ember bands.

    Each source texel retains its individual value and texture; there is no
    global quantisation. Partial source-edge alpha becomes a hard pixel edge.
    """
    a=np.array(im);rgb=a[:,:,:3].astype(np.float32)/255
    mx=rgb.max(2);mn=rgb.min(2);de=mx-mn
    s=np.divide(de,mx,out=np.zeros_like(mx),where=mx>0)
    h=np.zeros_like(mx)
    r=(de>0)&(mx==rgb[:,:,0]);g=(de>0)&(mx==rgb[:,:,1]);b=(de>0)&(mx==rgb[:,:,2])
    h[r]=(60*(rgb[:,:,1][r]-rgb[:,:,2][r])/de[r])%360
    h[g]=60*(rgb[:,:,2][g]-rgb[:,:,0][g])/de[g]+120
    h[b]=60*(rgb[:,:,0][b]-rgb[:,:,1][b])/de[b]+240
    # Bright near-white pixels are the small hot sources and the pale metal rim.
    hot=(mx>.73)&(s<.46)
    bad=(s>.15)&~hot&((h<20)|(h>34))
    h[bad]=27
    s[bad]=np.minimum(s[bad],.59)
    # Keep low-value basalt neutral; copper is restricted to an earth-tone trim.
    dark=(mx<.46)&(s>.35);s[dark]=.35
    bright_trim=(mx>.60)&(s>.35)&~hot
    mx[bright_trim]=.60+(mx[bright_trim]-.60)*.05
    # HSV to RGB without a restricted colour table.
    hh=h/60;ci=np.floor(hh).astype(np.int8)%6;f=hh-np.floor(hh)
    p=mx*(1-s);q=mx*(1-f*s);t=mx*(1-(1-f)*s)
    vals=((mx,t,p),(q,mx,p),(p,mx,t),(p,q,mx),(t,p,mx),(mx,p,q))
    out=np.zeros_like(rgb)
    for i,(rr,gg,bb) in enumerate(vals):
        sel=ci==i;out[:,:,0][sel]=rr[sel];out[:,:,1][sel]=gg[sel];out[:,:,2][sel]=bb[sel]
    a[:,:,:3]=np.rint(out*255).clip(0,255).astype('uint8')
    a[:,:,3]=np.where(a[:,:,3]>110,255,0).astype('uint8')
    return Image.fromarray(a,'RGBA')


def pale_rim(im):
    a=np.array(im);solid=a[:,:,3]>128
    up=np.zeros_like(solid);up[1:]=solid[:-1]
    left=np.zeros_like(solid);left[:,1:]=solid[:,:-1]
    edge=solid&(~up|~left)
    yy,xx=np.indices(solid.shape);n=solid.shape[0]
    edge&=(xx<n*.61)|(yy<n*.34)
    # The first and second inboard pixel are pale, while the opposite edge is ink.
    inboard=np.zeros_like(edge);inboard[1:,:]|=edge[:-1,:];inboard[:,1:]|=edge[:,:-1]
    rim=(edge|inboard)&solid
    rim&=(a[:,:,:3].max(2)>55)
    a[rim,:3]=np.maximum(a[rim,:3],np.array([213,204,190],np.uint8))
    return Image.fromarray(a,'RGBA')


def lift_painted_wear(im,name):
    """Expose the source painting's natural light-side scratches as pale ash.

    v2's very bright refinery tile has mean luminance 65.1, so the required 3:1
    threshold is 195.4. Select only as many existing painted highlights as are
    needed for the requested 10% (15% for the Imp) silhouette separation.
    """
    a=np.array(im);body=a[:,:,3]>128
    lum=a[:,:,:3].astype(np.float32)@np.array([.2126,.7152,.0722])
    threshold=3*65.1376881515503
    target=.155 if name=='ember_alien' else .103
    wanted=max(0,math.ceil(target*int(body.sum()))-int((body&(lum>=threshold)).sum()))
    if not wanted:return im
    pix=a[:,:,:3].astype(np.int16)
    mx=pix.max(2);mn=pix.min(2)
    chroma=np.divide(mx-mn,mx,out=np.zeros_like(lum),where=mx>0)
    eligible=body&(lum<threshold)&~((mx>160)&(chroma>.48))
    # Edges of drawn plates and naturally lighter strokes rank above flat fill.
    left=np.roll(lum,1,1);up=np.roll(lum,1,0)
    edge=np.abs(lum-left)+np.abs(lum-up)
    score=lum+edge*.48
    score[~eligible]=-1
    ids=np.flatnonzero(eligible)
    keep=ids[np.argpartition(score.flat[ids],-wanted)[-wanted:]]
    yy,xx=np.unravel_index(keep,body.shape)
    # Individual values retain a short ash ramp derived from the original value.
    rise=np.clip((lum[yy,xx]-65)*.11,0,15).astype(np.uint8)
    a[yy,xx,0]=214+rise;a[yy,xx,1]=203+rise;a[yy,xx,2]=188+rise
    return Image.fromarray(a,'RGBA')


def nick_flat_edges(im):
    """Hand-size single-pixel chips interrupt accidental ruler-straight sides."""
    a=np.array(im);solid=a[:,:,3]>128;h,w=solid.shape
    for sign in (-1,1):
        for x in range(w):
            nx=x+sign
            edge=solid[:,x]&~solid[:,nx] if 0<=nx<w else solid[:,x]
            padded=np.concatenate(([False],edge,[False]))
            starts=np.where(np.diff(padded.astype(np.int8))==1)[0]
            ends=np.where(np.diff(padded.astype(np.int8))==-1)[0]
            for start,end in zip(starts,ends):
                if end-start>=29:
                    for y in range(start+10,end-1,11):
                        a[y,x,3]=0;solid[y,x]=False
    return Image.fromarray(a,'RGBA')


def key_pose(name,choice,target):
    side=256 if name=='ember_big' else 192
    im=source(name,choice)
    factor=target/max(im.size)
    size=tuple(max(1,round(v*factor)) for v in im.size)
    im=temper(im.resize(size,Image.Resampling.NEAREST))
    canvas=Image.new('RGBA',(side,side))
    canvas.alpha_composite(im,((side-size[0])//2,(side-size[1])//2))
    return lift_painted_wear(pale_rim(canvas),name)


def motion(key,name,phase,index):
    a=np.asarray(key);side=key.width
    yy,xx=np.indices((side,side),dtype=np.float32);cx=cy=(side-1)/2
    xn=(xx-cx)/(side*.45);yn=(yy-cy)/(side*.45)
    flank=np.clip((abs(xn)-.25)/.55,0,1)
    fore=np.clip((yn+.02)/.55,0,1)
    aft=np.clip((-.12-yn)/.65,0,1)
    dx=np.zeros_like(xx);dy=np.zeros_like(yy)
    if name=='ember_alien':
        rad=np.hypot(xx-cx,yy-cy);outer=np.clip((rad-side*.12)/(side*.17),0,1)
        angle=phase*.048*outer;dx=angle*(yy-cy);dy=-angle*(xx-cx)
    elif name=='ember_chaser':
        jaw=flank*fore;dx=phase*3.4*jaw*np.sign(xn);dy=phase*1.5*jaw
        dx+=phase*2.2*aft
    elif name=='ember_big':
        jaw=fore*np.clip((abs(xn)-.05)/.4,0,1)
        dx=phase*(2.8*jaw*np.sign(xn)+1.6*flank*aft*np.sign(xn))
        dy=phase*(2.1*jaw+1.5*aft*np.sin(xx*.11))
    elif name=='ember_fighter_4':
        dx=phase*2.8*flank*np.sign(xn);dy=phase*(2.1*flank*np.sin(xx*.08)+1.3*aft)
    elif name.startswith('ember_fighter'):
        dx=phase*(3.4*flank*np.sign(xn)+1.3*aft*np.sin(yy*.09))
        dy=phase*(2.4*flank*np.sin(xx*.07)+1.4*aft)
        if name=='ember_fighter_2':
            dx=dx*.57+phase*1.2*flank*fore*np.sign(xn)
            dy=dy*.57
    elif name=='ember_rock_islet':
        dx=phase*(2.6*aft+1.5*fore*np.sin(yy*.10));dy=phase*(1.3*aft+.8*fore)
    else:
        rad=np.hypot(xn,yn);inner=np.clip((.93-rad)/.24,0,1)
        turn=(0,.062,-.059,.031) if name=='ember_rock_obsidian' else (0,.100,-.095,.052)
        angle=turn[index]*inner
        dx=angle*(yy-cy);dy=-angle*(xx-cx)
    sx=np.clip(np.rint(xx-dx).astype(int),0,side-1)
    sy=np.clip(np.rint(yy-dy).astype(int),0,side-1)
    return Image.fromarray(a[sy,sx],'RGBA')


def cycle(frame,index,name):
    a=np.array(frame);rgb=a[:,:,:3].astype(np.int16)
    white=(a[:,:,3]>128)&(rgb.min(2)>165)&(rgb[:,:,0]>=rgb[:,:,2])
    change=(0,18,-15,27)[index]
    for c in range(3):a[:,:,c][white]=np.clip(rgb[:,:,c][white]+change,0,255)
    return Image.fromarray(a,'RGBA')


def hot_ring(d,x,y,r,discharge=False):
    d.ellipse((x-r-2,y-r-2,x+r+2,y+r+2),outline=INK,width=1)
    d.arc((x-r-1,y-r-1,x+r+1,y+r+1),195,345,fill=ASH,width=2)
    d.ellipse((x-r+2,y-r+2,x+r-2,y+r-2),outline=ORANGE,width=2)
    d.ellipse((x-3,y-3,x+3,y+3),fill=WHITE if discharge else HOT)


def tell(key,name,stage):
    side=key.width;cx=cy=side//2
    frame=motion(key,name,.7 if stage==4 else -.7,1 if stage==4 else 2)
    d=ImageDraw.Draw(frame)
    spot={'ember_big':(cx,cy+66),'ember_fighter_1':(cx,cy+29),
          'ember_fighter_2':(cx,cy+51),'ember_fighter_3':(cx,cy+26),
          'ember_rock_obsidian':(cx,cy+8),'ember_rock_islet':(cx,cy+25)}.get(name,(cx,cy+18))
    x,y=spot;r=(7 if stage==4 else 11)+(2 if side==256 else 0)
    hot_ring(d,x,y,r,stage==5)
    if stage==5:
        if name=='ember_fighter_4':
            for i in range(8):
                ang=2*math.pi*i/8;xx=cx+round(57*math.cos(ang));yy=cy+round(57*math.sin(ang))
                d.ellipse((xx-3,yy-3,xx+3,yy+3),fill=HOT,outline=ORANGE,width=1)
        elif name=='ember_big':
            for k in (-1,1):
                xx=x+k*19;yy=y+27
                d.ellipse((xx-6,yy-6,xx+6,yy+6),fill=COPPER,outline=INK,width=2)
                d.ellipse((xx-2,yy-2,xx+2,yy+2),fill=ORANGE)
        else:
            d.line([(x,y+8),(x-2,y+18),(x+1,y+29)],fill=WHITE,width=2)
            for k in (-1,1):d.line([(x+k*5,y+9),(x+k*9,y+22)],fill=ORANGE,width=1)
    return frame


def hit(key,name):
    f=key.copy();d=ImageDraw.Draw(f);side=f.width;cx=cy=side//2
    points=[(cx-19,cy-17),(cx-8,cy-8),(cx-11,cy+2),(cx+2,cy+10),(cx+11,cy+23)]
    d.line(points,fill=INK,width=4,joint='curve')
    d.line([(x-1,y-1) for x,y in points],fill=WHITE,width=2,joint='curve')
    d.ellipse((cx-8,cy-8,cx+8,cy+8),fill=ORANGE,outline=ASH,width=1)
    d.ellipse((cx-4,cy-4,cx+4,cy+4),fill=WHITE)
    for ox,oy in ((-29,-24),(27,-26),(32,18),(-24,33)):
        x,y=cx+ox,cy+oy
        d.polygon(((x,y-3),(x+3,y),(x,y+4),(x-3,y)),fill=COPPER,outline=INK)
        d.point((x-1,y-2),fill=ASH)
    return f


def main():
    OUT.mkdir(exist_ok=True)
    for name,(choice,target) in SPEC.items():
        key=key_pose(name,choice,target)
        frames=[cycle(motion(key,name,p,i),i,name) for i,p in enumerate(PHASE)]
        frames.extend((tell(key,name,4),tell(key,name,5),hit(key,name)))
        side=key.width;sheet=Image.new('RGBA',(side*7,side))
        for i,f in enumerate(frames):sheet.alpha_composite(nick_flat_edges(f),(i*side,0))
        sheet.save(OUT/f'{name}.png',optimize=True)
        key.save(HERE/f'{name}_key.png',optimize=True)
        print(name,sheet.size)


if __name__=='__main__':main()
