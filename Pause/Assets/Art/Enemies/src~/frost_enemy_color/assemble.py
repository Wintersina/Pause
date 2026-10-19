#!/usr/bin/env python3
"""assemble.py <key> [--wire]: build the 7-cell flipbook from Codex repaints (src/<key>_c<i>_new.png; cell 0 = the hero).
One scale for the whole sheet (hero fitted to the original cell 0 solid bbox, scaled by each canvas size); every cell is placed so
its alpha centroid sits on the original cell's centroid (registration identical to the old sheet); area downsample + alpha snap.
Writes out/<key>.png (preview); --wire copies it to Resources/Enemies."""
import sys
from pathlib import Path
import numpy as np
from PIL import Image
H=Path(__file__).resolve().parent; k=sys.argv[1]
RES=H.parents[2]/'Resources'/'Enemies'
orig=Image.open(H/'../ship_scale_border~/original'/f'{k}.png').convert('RGBA'); cs=orig.height; n=orig.width//cs
def load(i):
    p=H/'src'/(f'{k}_hero.png' if i==0 else f'{k}_c{i}_new.png'); return Image.open(p).convert('RGBA')
def bbox(a,t): ys,xs=np.where(a>=t); return xs.min(),ys.min(),xs.max()+1,ys.max()+1
def cen(a):
    w=(a>0).astype(float); ys,xs=np.indices(a.shape); return (xs*w).sum()/w.sum(),(ys*w).sum()/w.sum()
o0=np.array(orig.crop((0,0,cs,cs)))[...,3]; ob=bbox(o0,96)
h0=load(0); nb=bbox(np.array(h0)[...,3],96)
s0=min((ob[2]-ob[0])/(nb[2]-nb[0]),(ob[3]-ob[1])/(nb[3]-nb[1]))*h0.width   # scale as fraction of canvas side
out=Image.new('RGBA',(cs*n,cs),(0,0,0,0))
for i in range(n):
    im=load(i); s=s0/im.width; w=max(1,round(im.width*s)); 
    a=np.array(im).astype(float); a[...,:3]*=a[...,3:]/255   # premultiply
    pm=Image.fromarray(a.astype(np.uint8),'RGBA')
    small=pm.resize((w,w),Image.BOX); sa=np.array(small).astype(float)
    al=sa[...,3]; rgb=np.where(al[...,None]>0,sa[...,:3]/np.maximum(al[...,None],1)*255,0)
    al2=np.where(al>=110,255,0)
    cell=np.dstack([np.clip(rgb,0,255),al2]).astype(np.uint8)
    cx,cy=cen(cell[...,3]); ox,oy=cen(np.array(orig.crop((i*cs,0,(i+1)*cs,cs)))[...,3])
    canvas=Image.new('RGBA',(cs,cs),(0,0,0,0)); canvas.alpha_composite(Image.fromarray(cell,'RGBA'),(round(ox-cx),round(oy-cy)))
    out.alpha_composite(canvas,(i*cs,0))
(H/'out').mkdir(exist_ok=True); out.save(H/'out'/f'{k}.png')
if '--wire' in sys.argv: out.save(RES/f'{k}.png')
print('ok',k,out.size)
