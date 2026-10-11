#!/usr/bin/env python3
"""Storm boss checks beyond the shared BossArt size/layout verifier."""
from pathlib import Path
import colorsys

import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parent.parent
ART=ROOT.parents[1]
C=384
sheet=Image.open(ROOT/'Storm.png')
shots=Image.open(ROOT/'Storm_shots.png')
card=Image.open(ROOT/'Storm_card.png')
assert sheet.mode==shots.mode==card.mode=='RGBA'
assert sheet.size==(1920,1536) and shots.size==(1024,128) and card.size==(1024,288)
im=np.asarray(sheet)
cells=[im[(i//5)*C:(i//5+1)*C,(i%5)*C:(i%5+1)*C] for i in range(20)]

def stats(a):
    alpha=a[...,3]
    ys,xs=np.nonzero(alpha>24)
    assert len(xs)>0
    margin=min(int(xs.min()),int(ys.min()),C-1-int(xs.max()),C-1-int(ys.max()))
    col=np.unique(a[alpha>24,:3],axis=0).shape[0]
    boundary=np.count_nonzero(np.any(a[1:,:,:3]!=a[:-1,:,:3],axis=2)&(alpha[1:]>24)&(alpha[:-1]>24))
    boundary+=np.count_nonzero(np.any(a[:,1:,:3]!=a[:,:-1,:3],axis=2)&(alpha[:,1:]>24)&(alpha[:,:-1]>24))
    return margin,col,int(boundary),len(xs)

print('file          size        mode')
for path in (ROOT/'Storm.png',ROOT/'Storm_shots.png',ROOT/'Storm_card.png',ROOT/'preview.png'):
    x=Image.open(path);print(f'{path.name:14} {x.width:4}x{x.height:<4} {x.mode}')

print('\ncell / pose        margin   colours   boundaries  pixels>24')
names=['idle0','idle1','idle2','idle3','hit','core a','core b','crown a','crown b','fire',
       'coils a','coils b','death0','death1','death2','death3','death4','retreat0','retreat1','portrait']
for i,a in enumerate(cells):
    m,n,b,p=stats(a)
    print(f'{i:02} {names[i]:10}      {m:3}    {n:6}      {b:7}    {p:6}')
    assert m>=6 and n>=1000 and b>=3000

def centroid(a):
    mask=(a[...,3]>100).astype(float)
    y,x=np.indices((C,C))
    return (float((x*mask).sum()/mask.sum()),float((y*mask).sum()/mask.sum()),int(mask.sum()))

base=centroid(cells[0]);print('\nidle registration: x y drift pixels area ratio motion from prior')
for i in range(4):
    x,y,area=centroid(cells[i])
    drift=float(np.hypot(x-base[0],y-base[1]))
    prior=cells[(i-1)%4]
    change=np.max(np.abs(cells[i].astype(np.int16)-prior.astype(np.int16)),axis=2)>24
    body=(cells[i][...,3]>24)|(prior[...,3]>24)
    motion=float((change&body).sum()/body.sum())
    print(f'{i:>4}    {x:6.2f} {y:6.2f}  {drift:5.2f}  {area:6} {area/base[2]:5.3f}      {motion*100:5.2f}%')
    assert drift<=3 and .96<=area/base[2]<=1.04 and motion>=.03

def red_share(a):
    rgba=a.reshape(-1,4)
    rgba=rgba[rgba[:,3]>24]
    rgb=rgba[:,:3].astype(np.float32)/255
    mx=rgb.max(axis=1); mn=rgb.min(axis=1); delta=mx-mn
    sat=np.divide(delta,mx,out=np.zeros_like(delta),where=mx>0)
    red=(rgb[:,0]>=rgb[:,1])&(rgb[:,0]>=rgb[:,2])&(rgb[:,1]/np.maximum(delta,1e-6)<.25)
    # Actual red band: hue <15 or >345, not every red-dominant yellow.
    hue=np.zeros(len(rgb),dtype=float)
    use=delta>1e-6
    rmax=use&(mx==rgb[:,0]);gmax=use&(mx==rgb[:,1])&~rmax;bmax=use&~rmax&~gmax
    hue[rmax]=((rgb[rmax,1]-rgb[rmax,2])/delta[rmax])%6
    hue[gmax]=(rgb[gmax,2]-rgb[gmax,0])/delta[gmax]+2
    hue[bmax]=(rgb[bmax,0]-rgb[bmax,1])/delta[bmax]+4
    hue*=60
    return float(((sat>.5)&((hue<=15)|(hue>=345))).sum()/len(rgb))

for name,arr in [('boss',im),('shots',np.asarray(shots)),('card',np.asarray(card))]:
    share=red_share(arr)
    print(f'{name} red-band share: {share*100:.3f}%')
    assert share<=.02

def standout(tile_name):
    tile=Image.open(ART/'Worlds'/'Storm'/'descent~'/tile_name).convert('RGB').crop((800,280,1184,664))
    bg=np.asarray(tile).astype(float)
    hull=cells[0].astype(float)
    mask=hull[...,3]>100
    lum_bg=(.2126*bg[...,0]+.7152*bg[...,1]+.0722*bg[...,2])/255
    lum_fg=(.2126*hull[...,0]+.7152*hull[...,1]+.0722*hull[...,2])/255
    bright=lum_fg >= np.maximum(.08,3*lum_bg)
    dark=lum_fg*3 <= lum_bg
    return float((bright&mask).sum()/mask.sum()),float(((bright|dark)&mask).sum()/mask.sum()),float(np.mean(lum_bg))

print('\ncloud stand-out (bright >=3:1, either polarity >=3:1, mean tile luma)')
for n in ('storm_cloud_deck_dark.png','storm_cloud_deck.png'):
    print(n, tuple(round(x,3) for x in standout(n)))

print('\nRESULT: PASS')
