#!/usr/bin/env python3
"""Project-specific visual contract measurements for the staged Tide boss."""
from pathlib import Path
import sys

import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parent.parent
ART=ROOT.parents[1]
SIZE=384


def hsv(rgb):
    rgb=rgb.astype(np.float32)/255
    mx=rgb.max(axis=-1);mn=rgb.min(axis=-1);d=mx-mn
    sat=np.divide(d,mx,out=np.zeros_like(mx),where=mx>0)
    hue=np.zeros_like(mx)
    r,g,b=rgb[...,0],rgb[...,1],rgb[...,2]
    ix=(mx==r)&(d>0);hue[ix]=((g[ix]-b[ix])/d[ix])%6
    ix=(mx==g)&(d>0);hue[ix]=(b[ix]-r[ix])/d[ix]+2
    ix=(mx==b)&(d>0);hue[ix]=(r[ix]-g[ix])/d[ix]+4
    return hue*60%360,sat,mx


def lum(rgb):
    return np.dot(rgb.astype(np.float32)/255,[0.2126,0.7152,0.0722])


def main():
    a=np.asarray(Image.open(ROOT/'Tide.png').convert('RGBA'))
    shots=Image.open(ROOT/'Tide_shots.png')
    card=Image.open(ROOT/'Tide_card.png')
    assert a.shape==(1536,1920,4) and shots.size==(1024,128) and card.size==(1024,288)
    assert shots.mode=='RGBA' and card.mode=='RGBA'
    rows=[];centroids=[];allpix=[]
    print('cell  pose              alpha>24 margin  opaque RGB colours  alpha centroid')
    names=['idle0','idle1','idle2','idle3','hit','tell0a','tell0b','tell1a','tell1b','fire','tell2a','tell2b','death0','death1','death2','death3','death4','retreat0','retreat1','portrait']
    for i in range(20):
        c=a[(i//5)*SIZE:(i//5+1)*SIZE,(i%5)*SIZE:(i%5+1)*SIZE]
        y,x=np.nonzero(c[...,3]>24)
        assert len(x)>0
        margin=int(min(x.min(),y.min(),SIZE-1-x.max(),SIZE-1-y.max()))
        opaque=c[c[...,3]>24,:3]
        colors=len(np.unique(opaque,axis=0))
        yy,xx=np.indices((SIZE,SIZE));alpha=c[...,3].astype(np.float64)
        centroid=(float((xx*alpha).sum()/alpha.sum()),float((yy*alpha).sum()/alpha.sum()))
        print(f'{i:>2}    {names[i]:<11}       {margin:>3}         {colors:>6}          ({centroid[0]:.2f}, {centroid[1]:.2f})')
        rows.append((margin,colors));allpix.append(opaque)
        if i<4:centroids.append(centroid)
    drift=max(np.hypot(x-centroids[0][0],y-centroids[0][1]) for x,y in centroids)
    rgb=np.concatenate(allpix)
    h,s,v=hsv(rgb)
    red=((h<15)|(h>=345))&(s>=0.05)
    red_share=float(red.mean())
    brown=((h>=15)&(h<=28))&(s>0.45)
    brown_over=float(brown.mean())
    print(f'idle centroid max drift: {drift:.2f} px; saturated red-band share: {red_share*100:.3f}%; brown above 0.45 saturation: {brown_over*100:.3f}%')
    print('background tile   p99 luminance (boss / backdrop)   sprite brighter by >0.15')
    idle=a[:SIZE,:SIZE]
    alpha=idle[...,3]>200
    body=lum(idle[...,:3])[alpha]
    for i in range(1,5):
        p=ART/'Backgrounds'/'Resources'/'Worlds'/'Tide'/'Backdrop3'/f'v{i}'/'mid.png'
        tile=np.asarray(Image.open(p).convert('RGB'))
        y=(tile.shape[0]-SIZE)//2;x=(tile.shape[1]-SIZE)//2
        bg=lum(tile[y:y+SIZE,x:x+SIZE])[alpha]
        boss95=float(np.percentile(body,99));bg95=float(np.percentile(bg,99))
        strong=float((body-bg>0.15).mean())
        print(f'v{i:<2}                 {boss95:.3f} / {bg95:.3f}                    {strong*100:.1f}%')
        assert boss95>bg95+0.15
        assert strong>0.12
    assert min(m for m,_ in rows)>=6
    assert min(c for _,c in rows)>=5000
    assert drift<=3
    assert red_share<0.02
    assert brown_over<0.01
    print('AUDIT PASS')


if __name__=='__main__':
    try:main()
    except AssertionError:
        print('AUDIT FAIL',file=sys.stderr)
        raise
