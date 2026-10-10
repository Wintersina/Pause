#!/usr/bin/env python3
"""Audit the staged Tide death strips and the new limpet mine row.

Run from anywhere: python3 path/to/tide_death/audit.py
Writes audit.json beside this script and exits nonzero on an objective breach.
Anatomical rupture quality still requires review of preview.png.
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image

from build import HERE, ENEMIES, MINES, NAMES, idle


def centroid(a):
    yy,xx=np.indices(a.shape)
    w=a.astype(float)/255
    return (float((xx*w).sum()/w.sum()),float((yy*w).sum()/w.sum()))


def edge_margins(mask):
    y,x=np.where(mask)
    h,w=mask.shape
    return [int(x.min()),int(y.min()),int(w-1-x.max()),int(h-1-y.max())]


def measures(im):
    rgba=np.array(im.convert('RGBA'))
    rgb=rgba[:,:,:3].astype(np.int16)
    alpha=rgba[:,:,3]
    solid=alpha>16
    hsv=np.array(im.convert('HSV'))
    hue=hsv[:,:,0].astype(float)*360/255
    sat=hsv[:,:,1].astype(float)/255
    val=hsv[:,:,2].astype(float)/255
    red=solid&(sat>.45)&((hue<15)|(hue>=345))
    lime=solid&(sat>.5)&(hue>=70)&(hue<=130)
    cyan=solid&(sat>.55)&(hue>=180)&(hue<=205)
    bright=solid&(val>.68)
    boundaries=int(((np.abs(np.diff(rgb,axis=1)).max(2)>12)&solid[:,1:]&solid[:,:-1]).sum()+((np.abs(np.diff(rgb,axis=0)).max(2)>12)&solid[1:,:]&solid[:-1,:]).sum())
    colors=np.unique(rgba[solid,:3],axis=0).shape[0]
    return dict(area=int((alpha>128).sum()),tones=int(colors),boundaries=boundaries,
                margins=edge_margins(solid),bright_margins=edge_margins(bright),
                centroid=[round(x,2) for x in centroid(alpha)],
                red_share=round(float(red.sum()/max(1,solid.sum())),5),
                lime_share=round(float(lime.sum()/max(1,solid.sum())),5),
                cyan_share=round(float(cyan.sum()/max(1,solid.sum())),5))


def mine_core(frame):
    a=np.array(frame.convert('RGBA'))
    yy,xx=np.indices(a.shape[:2])
    r,g,b=[a[:,:,i].astype(int) for i in range(3)]
    m=(a[:,:,3]>80)&(g>145)&(g>r*1.25)&(b>r*1.15)&(xx>160)&(yy>70)&(yy<235)
    y,x=np.where(m)
    return [round(float(x.mean()),2),round(float(y.mean()),2)]


def audit():
    report={'death':{},'mine':{},'failures':[]}
    for name in NAMES:
        p=HERE/f'{name}.png'
        im=Image.open(p)
        cell=256 if name=='tide_big' else 192
        if im.mode!='RGBA' or im.size!=(3*cell,cell):
            report['failures'].append(f'{name}: dimensions/mode')
            continue
        frames=[im.crop((i*cell,0,(i+1)*cell,cell)) for i in range(3)]
        stats=[measures(f) for f in frames]
        plain=idle(name,cell)
        ix,iy=centroid(np.array(plain.getchannel('A')))
        fx,fy=stats[0]['centroid']
        drift=round(float(np.hypot(fx-ix,fy-iy)),2)
        source_pixels=np.array(plain)
        flash_pixels=np.array(frames[0])
        flash_delta=int(np.any(source_pixels!=flash_pixels,axis=2).sum())
        debris_ratio=round(stats[2]['area']/max(1,stats[0]['area']),3)
        report['death'][name]={'size':list(im.size),'cell0_anchor_drift_px':drift,
                               'cell0_changed_pixels':flash_delta,
                               'debris_to_intact_area':debris_ratio,
                               'frames':stats,
                               'rules':{'intact_flash':drift<=3 and flash_delta>0,
                                        'rupture_changed':not np.array_equal(np.array(frames[0]),np.array(frames[1])),
                                        'hull_gone_area_proxy':debris_ratio<.85,
                                        'bright_margin':all(min(s['bright_margins'])>=6 for s in stats),
                                        'alpha_margin':all(min(s['margins'])>=6 for s in stats),
                                        'no_red':all(s['red_share']<=.02 for s in stats)}}
        for rule,ok in report['death'][name]['rules'].items():
            if not ok:report['failures'].append(f'{name}: {rule}')
    mine=Image.open(MINES/'rail_mines_neon_tide.png')
    ref=Image.open(HERE.parents[2]/'Resources/Enemies/Mines/rail_mines_neon.png')
    edges=(0,313,627,940,1254)
    cells=[mine.crop((edges[i],0,edges[i+1],314)) for i in range(4)]
    mine_stats=[measures(c) for c in cells]
    old_pivot=mine_core(ref.crop((0,0,313,314)))
    new_pivot=mine_core(cells[0])
    pivot_drift=round(float(np.hypot(new_pivot[0]-old_pivot[0],new_pivot[1]-old_pivot[1])),2)
    # At the original atlas scale, four texels represent one native game pixel.
    # The grip's reach from sphere edge to left clamp edge is near 0.33 u.
    clamp_reach_px=83
    report['mine']={'size':list(mine.size),'poses':mine_stats,
                    'reference_core_pivot':old_pivot,'tide_core_pivot':new_pivot,
                    'pivot_drift_px':pivot_drift,
                    'clamp_reach_world_units_estimate':round(clamp_reach_px/4/64,3),
                    'rules':{'dimensions':mine.mode=='RGBA' and mine.size==(1254,314),
                             'pivot':pivot_drift<=8,
                             'bright_margins':all(min(s['bright_margins'])>=6 for s in mine_stats),
                             'no_red':all(s['red_share']<=.02 for s in mine_stats)}}
    for rule,ok in report['mine']['rules'].items():
        if not ok:report['failures'].append(f'mine: {rule}')
    (HERE/'audit.json').write_text(json.dumps(report,indent=2)+'\n')
    for name,data in report['death'].items():
        print(f"{name:20} tones {[f['tones'] for f in data['frames']]} boundaries {[f['boundaries'] for f in data['frames']]} margins {[f['margins'] for f in data['frames']]} drift {data['cell0_anchor_drift_px']}px red {[f['red_share'] for f in data['frames']]} debris {data['debris_to_intact_area']}")
    print('mine',report['mine']['size'],'pivot drift',pivot_drift,'red',[f['red_share'] for f in mine_stats])
    print('failures:',report['failures'])
    return len(report['failures'])


if __name__=='__main__':
    raise SystemExit(bool(audit()))
