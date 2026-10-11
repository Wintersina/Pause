#!/usr/bin/env python3
"""Measure the Storm Rod Mine atlas and mine death strip; exit nonzero on failure."""
from pathlib import Path
import argparse
import json
import numpy as np
from PIL import Image

HERE=Path(__file__).resolve().parent
MINES=HERE.parent
DEATH=MINES.parent/'storm_death'
EDGES=(0,330,638,947,1254)


def bounds(mask):
    y,x=np.where(mask)
    if not len(x):return None
    h,w=mask.shape
    return [int(x.min()),int(y.min()),int(w-1-x.max()),int(h-1-y.max())]


def centroid(mask):
    yy,xx=np.indices(mask.shape)
    w=mask.astype(float)
    return [round(float((xx*w).sum()/max(w.sum(),1)),2),round(float((yy*w).sum()/max(w.sum(),1)),2)]


def measure(im):
    a=np.array(im.convert('RGBA'))
    rgb=a[:,:,:3].astype(np.int16);alpha=a[:,:,3]
    hsv=np.array(im.convert('HSV'))
    hue=hsv[:,:,0].astype(float)*360/255
    sat=hsv[:,:,1].astype(float)/255
    val=hsv[:,:,2].astype(float)/255
    active=alpha>16;solid=alpha>128
    red=active&(sat>.45)&((hue<15)|(hue>345))
    violet=active&(sat>.35)&(hue>=250)&(hue<=300)
    green=active&(sat>.35)&(hue>=65)&(hue<=170)
    orange=active&(sat>.35)&(hue>=15)&(hue<45)
    bright=active&(val>.65)
    horiz=(np.abs(np.diff(rgb,axis=1)).max(axis=2)>13)&solid[:,:-1]&solid[:,1:]
    vert=(np.abs(np.diff(rgb,axis=0)).max(axis=2)>13)&solid[:-1,:]&solid[1:,:]
    return {'solid_px':int(solid.sum()),'active_px':int(active.sum()),
            'tones':int(np.unique(a[solid,:3],axis=0).shape[0]),
            'boundaries':int(horiz.sum()+vert.sum()),
            'margins':bounds(active),'bright_margins':bounds(bright),
            'centroid':centroid(alpha),
            'red_share':round(float(red.sum()/max(active.sum(),1)),6),
            'violet_share':round(float(violet.sum()/max(active.sum(),1)),6),
            'green_share':round(float(green.sum()/max(active.sum(),1)),6),
            'orange_share':round(float(orange.sum()/max(active.sum(),1)),6),
            'partial_alpha_share':round(float(((alpha>0)&(alpha<255)).sum()/max(active.sum(),1)),4)}


def core_x(im):
    a=np.array(im.convert('RGBA'))
    yy,xx=np.indices(a.shape[:2])
    mask=(xx>160)&(xx<225)&(yy>127)&(yy<188)&(a[:,:,0]>175)&(a[:,:,2]>185)&(a[:,:,3]>100)
    y,x=np.where(mask)
    return round(float(np.median(x)),2) if len(x) else None


def audit(mine_path,death_path,write=False):
    mine=Image.open(mine_path);death=Image.open(death_path)
    problems=[]
    if mine.size!=(1254,314) or mine.mode!='RGBA':problems.append('atlas size/mode')
    if death.size!=(576,192) or death.mode!='RGBA':problems.append('death size/mode')
    frames=[mine.crop((EDGES[i],0,EDGES[i+1],314)) for i in range(4)]
    df=[death.crop((192*i,0,192*(i+1),192)) for i in range(3)]
    ms=[measure(f) for f in frames];ds=[measure(f) for f in df]
    pivots=[core_x(f) for f in frames]
    # Clamp's solid outer edge, measured on the dormant body band.
    a=np.array(frames[0]);sub=a[95:220,:155,3]>128
    clamp_left=int(np.where(sub)[1].min())
    clamp_reach=192-clamp_left
    # Compare the shared opaque clamp paint where the dormant clamp exists.
    clamp_mask=np.array(frames[0])[:,:145,3]>240
    clamp_equal=[]
    for f in frames[1:]:
        z=np.array(f)
        clamp_equal.append(bool(np.array_equal(z[:,:145][clamp_mask],a[:,:145][clamp_mask])))
    dormant=frames[0].resize((round(frames[0].width*.68),round(314*.68)),Image.Resampling.NEAREST)
    expected=Image.new('RGBA',(192,192))
    expected.alpha_composite(dormant,(round(96-192*.68),round(96-157*.68)))
    orig_a=np.array(expected.getchannel('A'));flash_a=np.array(df[0].getchannel('A'))
    anchor=float(np.linalg.norm(np.array(centroid(orig_a))-np.array(centroid(flash_a))))
    changed=int(np.any(np.array(expected)!=np.array(df[0]),axis=2).sum())
    debris_ratio=round(ds[2]['solid_px']/max(ds[0]['solid_px'],1),3)
    rules={
        'atlas_dimensions':mine.size==(1254,314) and mine.mode=='RGBA',
        'death_dimensions':death.size==(576,192) and death.mode=='RGBA',
        'alpha_margin':all(min(m['margins'])>=6 for m in ms+ds),
        'bright_margin':all(min(m['bright_margins'])>=6 for m in ms+ds),
        'clamp_reach_128px':abs(clamp_reach-128)<=2,
        'clamp_unchanged':all(clamp_equal),
        'core_pivot':all(x is not None and abs(x-pivots[0])<=7 for x in pivots[:3]),
        'no_red':all(m['red_share']==0 for m in ms+ds),
        'no_violet_green_orange':all(m['violet_share']==0 and m['green_share']==0 and m['orange_share']==0 for m in ms+ds),
        'material_tones':all(m['tones']>=100 for m in ms+ds[:2]),
        'pixel_boundaries':all(m['boundaries']>=200 for m in ms+ds[:2]),
        'death_intact_flash':anchor<=3 and changed>20,
        'death_rupture':not np.array_equal(np.array(df[0]),np.array(df[1])) and ds[1]['solid_px']>ds[2]['solid_px'],
        'death_hull_gone':debris_ratio<.35,
    }
    problems.extend(k for k,v in rules.items() if not v)
    report={'atlas':[{'state':n,**m} for n,m in zip(('Dormant','Waking','Charging','Burst'),ms)],
            'death':[{'state':n,**m} for n,m in zip(('Flash','Rupture','Drift'),ds)],
            'pivot_x':pivots,'clamp_left_x':clamp_left,'clamp_reach_px':clamp_reach,
            'clamp_equal':clamp_equal,'death_anchor_drift_px':round(anchor,3),
            'death_changed_pixels':changed,'death_debris_ratio':debris_ratio,
            'rules':rules,'failures':problems}
    print('Asset/state  tones boundaries margin(L,T,R,B) red%  solid')
    for label,stats in (('Atlas',ms),('Death',ds)):
        for i,m in enumerate(stats):
            print(f'{label} {i:1}      {m["tones"]:5} {m["boundaries"]:9} {str(m["margins"]):>16} {m["red_share"]*100:5.2f} {m["solid_px"]:6}')
    print('clamp reach',clamp_reach,'px; core pivots',pivots,'; death anchor',round(anchor,3),'px; debris ratio',debris_ratio)
    print('failures:',problems)
    if write:
        out=HERE/'audit.json'
        if out.exists():raise FileExistsError(f'Refusing to overwrite {out}')
        out.write_text(json.dumps(report,indent=2)+'\n')
    return len(problems)


if __name__=='__main__':
    ap=argparse.ArgumentParser();ap.add_argument('--mine',type=Path,default=MINES/'rail_mines_neon_storm.png')
    ap.add_argument('--death',type=Path,default=DEATH/'storm_mine.png')
    ap.add_argument('--write',action='store_true');args=ap.parse_args()
    raise SystemExit(bool(audit(args.mine,args.death,args.write)))
