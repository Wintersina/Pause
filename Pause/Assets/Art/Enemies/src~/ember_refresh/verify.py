#!/usr/bin/env python3
"""Measure the 11 staged Ember idle strips and render a label-free review image."""
from pathlib import Path
import math
import numpy as np
from PIL import Image

HERE=Path(__file__).resolve().parent
ROOT=HERE.parents[5]
TILES=ROOT/'Pause/Assets/Art/Backgrounds/Resources/Worlds/Ember/Backdrop3'
NAMES=['ember_alien','ember_chaser','ember_big','ember_fighter_1',
       'ember_fighter_2','ember_fighter_3','ember_fighter_4',
       'ember_rock_magma','ember_rock_cinder','ember_rock_obsidian','ember_rock_islet']
BG=(11,11,26)


def luminance(a):
    return np.asarray(a,dtype=np.float64)[...,:3]@np.array([.2126,.7152,.0722])


def centroid_area(c):
    yy,xx=np.where(c[:,:,3]>128)
    return (np.array([xx.mean(),yy.mean()]),len(xx)) if len(xx) else (np.zeros(2),0)


def margin(c):
    yy,xx=np.where(c[:,:,3]>16);h,w=c.shape[:2]
    return int(min(xx.min(),yy.min(),w-1-xx.max(),h-1-yy.max())) if len(xx) else -1


def straight_cut(c):
    opaque=c[:,:,3]>128;worst=0
    for sign in (-1,1):
        for x in range(c.shape[1]):
            nx=x+sign
            edge=opaque[:,x]&(~opaque[:,nx]) if 0<=nx<c.shape[1] else opaque[:,x]
            run=0
            for v in edge:
                run=run+1 if v else 0;worst=max(worst,run)
    return worst


def motion(a,b):
    oa=a[:,:,3]>128;ob=b[:,:,3]>128;body=oa|ob
    diff=np.abs(a[:,:,:3].astype(np.int16)-b[:,:,:3].astype(np.int16)).sum(2)>24
    return 100*((oa^ob)|(oa&ob&diff)).sum()/max(1,body.sum())


def palette(a):
    p=a.reshape(-1,4);p=p[p[:,3]>128,:3].astype(np.float32)/255
    mx=p.max(1);mn=p.min(1);de=mx-mn
    s=np.divide(de,mx,out=np.zeros_like(de),where=mx>0)
    h=np.zeros_like(mx);valid=de>0
    r=valid&(mx==p[:,0]);g=valid&(mx==p[:,1]);b=valid&(mx==p[:,2])
    h[r]=(60*(p[:,1][r]-p[:,2][r])/de[r])%360
    h[g]=60*(p[:,2][g]-p[:,0][g])/de[g]+120
    h[b]=60*(p[:,0][b]-p[:,1][b])/de[b]+240
    n=len(p)
    return {
      'red':100*np.sum((s>.5)&((h<15)|(h>=345)))/n,
      'pink':100*np.sum((s>.3)&(h>=300)&(h<=330))/n,
      'amber':100*np.sum((s>.45)&(h>=35)&(h<=48))/n,
      'other':100*np.sum((s>.3)&(h>=65)&(h<=310))/n,
    }


def preview(sheets,tiles,path):
    strip_w=7*256*2;tile_x=strip_w+24
    width=tile_x+4*256*2+20
    height=sum((256 if name=='ember_big' else 192)*2+12 for name in NAMES)
    out=Image.new('RGB',(width,height),BG);y=0
    for name in NAMES:
        sheet=sheets[name];side=sheet.height
        up=sheet.resize((sheet.width*2,side*2),Image.Resampling.NEAREST)
        out.paste(up,(0,y),up)
        key=sheet.crop((0,0,side,side))
        for i,tile in enumerate(tiles):
            x=min(tile.width-side,max(0,(tile.width-side)//3+i*27))
            ty=min(tile.height-side,max(0,(tile.height-side)//3+i*91))
            crop=tile.crop((x,ty,x+side,ty+side)).convert('RGB')
            crop.paste(key,(0,0),key)
            out.paste(crop.resize((side*2,side*2),Image.Resampling.NEAREST),
                      (tile_x+i*256*2,y))
        y+=side*2+12
    out.save(path,optimize=True)


def main():
    tiles=[Image.open(TILES/f'v{i}/mid.png').convert('RGB') for i in range(1,5)]
    means=[float(luminance(np.asarray(t)).mean()) for t in tiles]
    print('Ember mid tile mean luminance:',', '.join(f'v{i}={v:.1f}' for i,v in enumerate(means,1)))
    print('name                 tones bounds cols cut drift area-range motion %              margins            red pink amber other stand-out v1/v2/v3/v4')
    sheets={};failures=[]
    for name in NAMES:
        im=Image.open(HERE/'strips'/f'{name}.png').convert('RGBA');sheets[name]=im
        a=np.asarray(im);side=im.height;cells=[a[:,i*side:(i+1)*side] for i in range(7)]
        key=cells[0];op=key[:,:,3]>128
        rgb=(key[:,:,0].astype(np.int32)<<16)|(key[:,:,1].astype(np.int32)<<8)|key[:,:,2]
        tones=len(np.unique(rgb[op]))
        r=(rgb[:,:-1]!=rgb[:,1:])&op[:,:-1]&op[:,1:]
        d=(rgb[:-1,:]!=rgb[1:,:])&op[:-1,:]&op[1:,:]
        edge=np.zeros_like(op);edge[:,:-1]|=r;edge[:-1,:]|=d
        bounds=int(edge.sum())
        cols=sum(int((c[:,0,3]>128).sum()+(c[:,-1,3]>128).sum()) for c in cells)
        cut=max(straight_cut(c) for c in cells)
        center,area0=centroid_area(key)
        drift=max(float(np.linalg.norm(centroid_area(c)[0]-center)) for c in cells[1:4])
        areas=[centroid_area(c)[1]/area0 for c in cells[:4]]
        motions=[motion(cells[i],cells[(i+1)%4]) for i in range(4)]
        margins=[margin(c) for c in cells]
        pal=palette(a)
        lum=luminance(key)[op];stand=[100*np.mean(lum>=3*m) for m in means]
        checks={'dimensions':im.width==7*side and side==(256 if name=='ember_big' else 192),
                'tones':tones>=300,'boundaries':bounds>=(6000 if side==256 else 3000),
                'cell sides':cols==0,'straight cut':cut<29,
                'idle anchor':drift<=3,'idle area':all(.96<=x<=1.04 for x in areas),
                'idle motion':all(x>=3 for x in motions),
                'margins':min(margins)>=6,
                'red':pal['red']==0,'pink':pal['pink']<=.2,
                'amber':pal['amber']<=1,'cyan/green/violet':pal['other']<=.5,
                'tile contrast':all(x>=(15 if name=='ember_alien' else 10) for x in stand)}
        failures += [f'{name}: {label}' for label,okay in checks.items() if not okay]
        print(f'{name:21s} {tones:5d} {bounds:6d} {cols:4d} {cut:3d} {drift:4.1f} '
              f'{min(areas):.3f}-{max(areas):.3f} '
              f'{"/".join(f"{x:.1f}" for x in motions):>21s} '
              f'{"/".join(str(x) for x in margins):>21s} '
              f'{pal["red"]:4.2f} {pal["pink"]:4.2f} {pal["amber"]:5.2f} {pal["other"]:5.2f} '
              f'{"/".join(f"{x:.1f}" for x in stand)}')
    preview(sheets,tiles,HERE/'preview.png')
    for failure in failures:print('FAIL',failure)
    print('RESULT:', 'FAIL' if failures else 'PASS', f'({len(failures)} failures)')
    raise SystemExit(bool(failures))


if __name__=='__main__':main()
