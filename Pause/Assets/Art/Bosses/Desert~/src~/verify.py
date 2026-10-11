#!/usr/bin/env python3
"""Desert boss dimensions, registration, palette, detail and lane contrast."""
from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parent.parent
TILES=Path('/Users/sina/Developer/Pause/.claude/worktrees/codex-desert-worm/Pause/Assets/Art/Worlds/Desert/backdrop_v3~')
failures=[]


def test(ok,label):
    print(('PASS' if ok else 'FAIL')+'  '+label)
    if not ok:failures.append(label)


def load(name,size):
    im=Image.open(ROOT/name)
    test(im.size==size and im.mode=='RGBA',f'{name}: RGBA {size[0]}x{size[1]} (got {im.mode} {im.size})')
    return np.asarray(im.convert('RGBA'))


def hsv_arr(rgb):
    arr=np.asarray(Image.fromarray(rgb.astype('uint8'),'RGB').convert('HSV')).astype(float)
    return arr[...,0]*360/255,arr[...,1]/255,arr[...,2]/255


def cell_metrics(cell):
    op=cell[...,3]>24
    yy,xx=np.nonzero(op)
    box=(int(xx.min()),int(yy.min()),int(xx.max()),int(yy.max()))
    margin=min(box[0],box[1],383-box[2],383-box[3])
    centroid=(float(xx.mean()),float(yy.mean()))
    unique=len(np.unique(cell[op,:3],axis=0))
    return margin,centroid,unique,int(op.sum()),box


def main():
    a=load('Desert.png',(1920,1536))
    shots=load('Desert_shots.png',(1024,128))
    card=load('Desert_card.png',(1024,288))
    cells=[a[(i//5)*384:(i//5+1)*384,(i%5)*384:(i%5+1)*384] for i in range(20)]
    all_present=all(np.any(c[...,3]>24) for c in cells)
    test(all_present,'20/20 boss cells nonempty')
    test(all(np.any(shots[:,i*128:(i+1)*128,3]>24) for i in range(8)),'8/8 shot cells nonempty')
    print('cell  name             margin  centroid     unique_RGB  opaque_px')
    names=['idle0','idle1','idle2','idle3','hit','tell_maw_a','tell_maw_b','tell_boom_a','tell_boom_b','fire_maw','tell_lens_a','tell_lens_b','death0','death1','death2','death3','death4','retreat0','retreat1','portrait']
    stats=[]
    for i,c in enumerate(cells):
        m,p,n,op,box=cell_metrics(c);stats.append((m,p,n,op,box))
        print(f'{i:2d}    {names[i]:14s} {m:3d}    ({p[0]:5.1f},{p[1]:5.1f}) {n:9,d} {op:9,d}')
    test(min(s[0] for s in stats)>=6,f'alpha>24 border margin >=6 px (min {min(s[0] for s in stats)})')
    p0=np.array(stats[0][1]); drift=max(float(np.linalg.norm(np.array(stats[i][1])-p0)) for i in range(1,4))
    test(drift<=3,f'idle alpha centroids within 3 px of idle0 (max {drift:.2f})')
    floor=min(s[2] for s in stats[:12])
    test(floor>=2400,f'painted idle/tell detail >=2400 unique RGB/cell (min {floor:,})')

    opaque=a[...,3]>24
    h,s,v=hsv_arr(a[...,:3])
    count=int(opaque.sum())
    red=opaque&(s>.5)&((h>=345)|(h<=15))
    amber=opaque&(s>.45)&(h>=30)&(h<=48)
    pink=opaque&(s>.4)&(h>=312)&(h<=326)
    violet=opaque&(s>.25)&(h>=250)&(h<=300)
    copper=opaque&(s>.16)&(h>=22)&(h<=30)&(v<=.605)
    over_copper=opaque&(s>.16)&(h>=20)&(h<=32)&((s>.625)|(v>.605))
    sand=opaque&(h>=40)&(h<=48)&(s>.075)
    shadow=opaque&(h>=232)&(h<=248)&(s>=.28)&(s<=.45)&(v>=.1)&(v<=.3)
    verdigris=opaque&(h>=135)&(h<=155)&(s>.075)
    glass=opaque&(h>=160)&(h<=170)&(s>.075)
    hue_below_20=opaque&(h<20)&(s>.15)
    sand_p95=float(np.quantile(s[sand],.95))
    print(f'palette: red {red.sum()/count:.4%}, amber {amber.sum()/count:.4%}, pink {pink.sum()/count:.4%}, violet {violet.sum()/count:.4%}, matte_copper {copper.sum()/count:.2%}, copper_overcap {over_copper.sum()/count:.4%}')
    test(red.sum()/count<=.01,'red-band share <=1%')
    test(amber.sum()/count<=.003,'hue 30-48 / S>.45 share <=0.3%')
    test(pink.sum()/count<.001 and violet.sum()/count<.001,'pink/violet absent apart from tiny pixels')
    test(over_copper.sum()/count<.003,'copper S<=.62 and V<=.60')
    test(copper.sum()/count<=.35,'matte copper <=35% of opaque boss pixels')
    idle=cells[0]
    hi,si,vi=hsv_arr(idle[...,:3]); oi=idle[...,3]>24
    idle_copper=oi&(si>.16)&(hi>=22)&(hi<=30)&(vi<=.605)
    test(idle_copper.sum()/oi.sum()<=.35,f'idle copper <=35% ({idle_copper.sum()/oi.sum():.2%})')
    print(f'materials: sand_p95_S {sand_p95:.3f}, shadow {shadow.sum()/count:.3%}, verdigris {verdigris.sum()/count:.4%}, glass {glass.sum()/count:.4%}, chromatic_hue_below_20 {hue_below_20.sum()} px')
    test(sand_p95<=.33,'sand saturation p95 <=.33')
    test(shadow.sum()/count<=.25,'indigo shadow tint <=25%')
    test(verdigris.sum()/count<=.03 and glass.sum()/count<=.04,'verdigris <=3%, glass <=4%')
    test(hue_below_20.sum()==0,'no chromatic boss pixels below hue 20')

    sh=shots[...,3]>24
    hh,ss,vv=hsv_arr(shots[...,:3])
    bad=sh&(ss>.45)&((hh<20)|((hh>=30)&(hh<=48))|((hh>=250)&(hh<=330)))
    test(bad.sum()/sh.sum()<=.003,f'shots stay pale; forbidden share {bad.sum()/sh.sum():.4%}')

    # Composite the same idle cell at centre of each real mid tile; compare
    # luminance under its solid silhouette to the ground it covers.
    c=cells[0]
    mask=c[...,3]>180
    fore=(c[...,:3].astype(float)/255)**2.2
    fy=(.2126*fore[...,0]+.7152*fore[...,1]+.0722*fore[...,2])
    print('tile  ground_Y  boss_Y  median_local_contrast  bright_boss_px')
    contrasts=[]
    for i in range(1,5):
        tile=np.asarray(Image.open(TILES/f'v{i}'/'mid.png').convert('RGB'))
        cy,cx=tile.shape[0]//2,tile.shape[1]//2
        patch=tile[cy-192:cy+192,cx-192:cx+192]
        norm=(patch.astype(float)/255)**2.2
        ty=.2126*norm[...,0]+.7152*norm[...,1]+.0722*norm[...,2]
        b=float(np.mean(fy[mask]));g=float(np.mean(ty[mask]))
        # The machine contains both dark cavities and pale metal against a
        # textured tile. Mean-to-mean contrast cancels those useful edges.
        local=(np.maximum(fy,ty)+.05)/(np.minimum(fy,ty)+.05)
        contrast=float(np.median(local[mask]))
        bright=float(np.mean(fy[mask]>.35))
        contrasts.append(contrast)
        print(f'v{i}    {g:.3f}     {b:.3f}         {contrast:.2f}:1           {bright:.2%}')
    test(min(contrasts)>=1.6,f'median local stand-out vs all four mid tiles >=1.6:1 (min {min(contrasts):.2f}:1)')
    test((ROOT/'preview.png').exists() and (ROOT/'preview.gif').exists() and (ROOT/'manifest.json').exists(),'previews and manifest present')
    print('RESULT:', 'FAIL' if failures else 'PASS',f'({len(failures)} failures)')
    raise SystemExit(1 if failures else 0)


if __name__=='__main__':main()
