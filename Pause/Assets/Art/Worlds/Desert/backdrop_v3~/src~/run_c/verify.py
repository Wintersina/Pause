"""Audit the run C sprite contract, animation, blink energy and forbidden hues."""
from __future__ import annotations
import colorsys, json
from pathlib import Path
import numpy as np
from PIL import Image

ROOT=Path(__file__).resolve().parents[2]
LOOPS=json.loads((ROOT/'manifest_c.json').read_text())['run_c']['loops']
NAMES={l['name'] for l in LOOPS}
BLINK={'beacon_white':4.,'beacon_pale_gold':4.,'window_lights':2.2,'strobe_white':4.,'work_lamp_flicker':4.}

def hsv_metrics(frame):
    a=np.asarray(frame)
    rgb=a[:,:,:3].reshape(-1,3).astype(float)/255
    mask=a[:,:,3].ravel()>8
    rgb=rgb[mask]
    if not len(rgb):return dict(hard=0,red=0,violet=0,pink=0,copper=0,glass=0,green=0,sand_p95=0)
    mx=rgb.max(1);mn=rgb.min(1);d=mx-mn
    s=np.divide(d,mx,out=np.zeros_like(mx),where=mx>0)
    h=np.zeros_like(mx)
    m=d>1e-6
    ix=(mx==rgb[:,0])&m;h[ix]=((rgb[ix,1]-rgb[ix,2])/d[ix])%6
    ix=(mx==rgb[:,1])&m;h[ix]=(rgb[ix,2]-rgb[ix,0])/d[ix]+2
    ix=(mx==rgb[:,2])&m;h[ix]=(rgb[ix,0]-rgb[ix,1])/d[ix]+4
    h=(h*60)%360
    hard=((h>=30)&(h<=48)&(s>.45))
    red=(((h>=345)|(h<=15))&(s>.5))
    violet=((h>=250)&(h<=300)&(s>.2))
    pink=((h>=312)&(h<=326)&(s>.2))
    copper=((h>=22)&(h<=30)&(s>.15))
    glass=((h>=160)&(h<=170)&(s>.05))
    green=((h>=90)&(h<=180)&(s>.3))
    sand=(h>=35)&(h<=52)&(s>.04)
    return dict(hard=float(hard.mean()),red=float(red.mean()),violet=float(violet.mean()),pink=float(pink.mean()),copper=float(copper.mean()),glass=float(glass.mean()),green=float(green.mean()),sand_p95=float(np.percentile(s[sand],95)) if sand.any() else 0)

def cells(atlas):
    im=Image.open(ROOT/(atlas+'.png'))
    assert im.size==(1024,1024) and im.mode=='RGBA',f'{atlas} size/mode'
    js=json.loads((ROOT/(atlas+'.json')).read_text())
    seen=set()
    for s in js['sprites']:
        assert set(s)=={'n','x','y','w','h'}
        assert s['w']==s['h']==256 and s['x']%256==0 and s['y']%256==0
        assert 0<=s['x']<=768 and 0<=s['y']<=768 and s['n'] not in seen
        seen.add(s['n'])
    assert len(js['sprites'])<=16
    return im,{s['n']:im.crop((s['x'],768-s['y'],s['x']+256,1024-s['y'])) for s in js['sprites']}

def step_change(a,b):
    aa=np.asarray(a);bb=np.asarray(b)
    opaque=(aa[:,:,3]>8)|(bb[:,:,3]>8)
    if not opaque.any():return 0
    changed=np.any(np.abs(aa.astype(int)-bb.astype(int))>6,axis=2)
    return float(np.count_nonzero(changed&opaque)/np.count_nonzero(opaque))

def bright(a):
    p=np.asarray(a).astype(float)
    return float(np.sum((p[:,:,:3]@np.array([.2126,.7152,.0722]))*p[:,:,3]/255))

def main():
    atlas_cache={};errors=[];table=[];all_frames=[];base_offsets=[]
    for loop in LOOPS:
        atlas=loop['atlas'][:-4]
        if atlas not in atlas_cache:atlas_cache[atlas]=cells(atlas)[1]
        fr=[atlas_cache[atlas][f"{loop['name']}_{i:02d}"] for i in range(loop['frames'])]
        all_frames+=fr
        margins=[];maxalpha=0
        for f in fr:
            ar=np.asarray(f); yy,xx=np.where(ar[:,:,3]>8)
            assert len(xx),loop['name']
            margins.append(min(xx.min(),yy.min(),255-xx.max(),255-yy.max()))
            maxalpha=max(maxalpha,int(ar[:,:,3].max()))
            if loop['ground_attached']:
                by,bx=np.where(ar[236:238,:,3]>8)
                if len(bx)==0:errors.append(f"{loop['name']}: missing fixed anchor")
                else:
                    offset=max(abs(float(bx.mean())-128),abs(float(by.mean()+236)-236))
                    base_offsets.append(offset)
                    if offset>.5:errors.append(f"{loop['name']}: base offset {offset:.2f}")
        changes=[step_change(fr[i],fr[(i+1)%len(fr)]) for i in range(len(fr))]
        adjacent=np.median(changes[:-1]); seam=changes[-1]
        vals=[bright(f) for f in fr]
        ratio=max(vals)/max(1,min(vals))
        if min(margins)<6:errors.append(f"{loop['name']}: margin {min(margins)}")
        if min(changes)<.04:errors.append(f"{loop['name']}: frame change {min(changes):.3f}")
        if seam>max(.2,adjacent*2.0):errors.append(f"{loop['name']}: seam {seam:.3f} vs adjacent {adjacent:.3f}")
        if loop['name'] in BLINK and ratio<BLINK[loop['name']]:errors.append(f"{loop['name']}: blink {ratio:.2f} < {BLINK[loop['name']]}")
        if loop['name'] in ('smoke_a','smoke_b','dustdevil','sandplume','sandfall','grit_gust','steam_vent','grit_rain','sand_slide') and maxalpha>179:
            errors.append(f"{loop['name']}: alpha {maxalpha}>179")
        table.append((loop['name'],loop['frames'],min(margins),min(changes),seam,adjacent,ratio,maxalpha,vals))
    metrics=hsv_metrics(Image.fromarray(np.concatenate([np.asarray(f) for f in all_frames],axis=0),'RGBA'))
    if metrics['hard']>.003:errors.append('forbidden saturated gold')
    for k in ('red','violet','pink','green'):
        if metrics[k]>0:errors.append(f'forbidden {k}: {metrics[k]}')
    if metrics['copper']>.12:errors.append('copper coverage')
    if metrics['glass']>.04:errors.append('glass coverage')
    if metrics['sand_p95']>.33:errors.append('sand p95 saturation')
    print('loop               n margin min_change seam median_adj blink_ratio max_alpha brightness_per_frame')
    for name,n,m,c,seam,adj,ratio,alpha,vals in table:
        print(f'{name:18} {n:1} {m:6} {c:10.3f} {seam:5.3f} {adj:10.3f} {ratio:11.2f} {alpha:9} '+','.join(str(round(v)) for v in vals))
    print(f'anchor base offset max px: {max(base_offsets):.1f} (measured bottom-contact centroid against 128,236)')
    print('hue metrics visible-pixel share:',{k:round(v,5) for k,v in metrics.items()})
    print('RESULT:', 'PASS' if not errors else 'FAIL')
    for e in errors:print(' -',e)
    if errors:raise SystemExit(1)

if __name__=='__main__':main()
