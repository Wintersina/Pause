"""Audit the staged Frost ultimate atlases. Run: python3 verify.py"""
from __future__ import annotations

import colorsys
from pathlib import Path

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
SPECS = {
    "frost_attack_laser.png": (1024, 384, 128, 128, 8, 3),
    "frost_attack_laserbody.png": (512, 256, 128, 256, 4, 1),
    "frost_attack_lasertell.png": (512, 128, 128, 128, 4, 1),
}


def hsv(rgb):
    flat = rgb.reshape(-1,3).astype(np.float32)/255
    mx = flat.max(1); mn = flat.min(1); delta = mx-mn
    sat = np.divide(delta,mx,out=np.zeros_like(mx),where=mx>0)
    h = np.zeros_like(mx)
    nz=delta>0
    masks=[nz & (flat[:,0]==mx), nz & (flat[:,1]==mx), nz & (flat[:,2]==mx)]
    h[masks[0]] = ((flat[masks[0],1]-flat[masks[0],2])/delta[masks[0]])%6
    h[masks[1]] = (flat[masks[1],2]-flat[masks[1],0])/delta[masks[1]]+2
    h[masks[2]] = (flat[masks[2],0]-flat[masks[2],1])/delta[masks[2]]+4
    return h.reshape(rgb.shape[:2])*60,sat.reshape(rgb.shape[:2]),mx.reshape(rgb.shape[:2])


def cells(a, spec):
    _,_,cw,ch,cols,rows=spec
    return [a[y*ch:(y+1)*ch,x*cw:(x+1)*cw] for y in range(rows) for x in range(cols)]


def pct(n,d): return 100*n/max(1,d)


def frame_diff(a,b):
    live=(a[:,:,3]>=128)|(b[:,:,3]>=128)
    changed=np.any(np.abs(a.astype(np.int16)-b.astype(np.int16))>=24,axis=2)
    return pct(np.count_nonzero(changed&live),np.count_nonzero(live))


def gap_ok(cell,vertical=True):
    alpha=cell[:,:,3]
    if np.any(alpha[:,:6]>24) or np.any(alpha[:,-6:]>24): return False
    if vertical and (np.any(alpha[:6,:]>24) or np.any(alpha[-6:,:]>24)): return False
    return True


def audit():
    failures=[]
    print("file | size | RGBA | cells | border | pink min/avg % | 178/82/259/37 % | red px | loops min % | x2 | vertical | seam | width | core")
    for name,spec in SPECS.items():
        im=Image.open(ROOT/name)
        sizeok=im.size==spec[:2]
        rgba=im.mode=='RGBA'
        if not sizeok or not rgba:
            failures.append(name+': size/RGBA')
            print(name,'FAILED size/mode');continue
        a=np.asarray(im)
        cs=cells(a,spec)
        nonempty=all(np.any(c[:,:,3]>=128) for c in cs)
        vertical_border=name!="frost_attack_laserbody.png"
        border=all(gap_ok(c,vertical_border) for c in cs)
        shares=[]; audits=[]; reds=[]
        for c in cs:
            live=c[:,:,3]>=128
            h,s,v=hsv(c[:,:,:3])
            pink=live & ((((h>=295)&(h<=340)&(s>.18))) | ((v>=.92)&(s<=.22)))
            shares.append(pct(pink.sum(),live.sum()))
            # Very dark ink has a mathematically purple hue; it is the required
            # #0B0B1A outline, not a violet pickup-colour signal.
            chromatic=live&(s>.5)&(v>.2)
            nearby=[pct((chromatic&(np.abs((h-target+180)%360-180)<=20)).sum(),live.sum()) for target in (178,82,259,37)]
            audits.append(nearby)
            reds.append(int((chromatic&((h>=345)|(h<=15))).sum()))
        means=np.array(audits).max(0)
        loops=[]; seams=[]
        if name.endswith('laser.png'):
            groups=[cs[8:12],cs[20:24]]
        elif name.endswith('laserbody.png'):
            groups=[cs]
        else:
            groups=[cs[0:2],cs[2:4]]
        for group in groups:
            steps=[frame_diff(group[i],group[(i+1)%len(group)]) for i in range(len(group))]
            loops.extend(steps)
            seams.append(steps[-1] <= max(steps[:-1])*1.35+1)
        threshold=8 if name.endswith('laserbody.png') else 3
        roundtrip=np.array_equal(a, np.repeat(np.repeat(a[::2,::2],2,0),2,1))
        if name.endswith('laserbody.png'):
            vertical=all(np.array_equal(c[0],c[-1]) for c in cs)
            widths=[]
            for c in cs:
                x=np.where(c[:,:,3]>24)[1]
                widths.append(int(x.max()-x.min()+1) if x.size else 0)
            width=max(widths)
            widthok=width<=100
            cores=[]
            for c in cs:
                bright=(c[:,:,0]==255)&(c[:,:,1]>=224)&(c[:,:,2]>=248)&(c[:,:,3]>=128)
                for row in bright:
                    mid=64;left=mid;right=mid
                    while left>0 and row[left-1]:left-=1
                    while right<127 and row[right+1]:right+=1
                    cores.append(right-left+1)
            core=f'{min(cores)}-{max(cores)}'
            coreok=min(cores)>=16 and max(cores)<=20
        elif name.endswith('lasertell.png'):
            vertical=all(np.array_equal(c[0],c[-1]) for c in cs[:2])
            width='-';widthok=True;core='-';coreok=True
        else:
            vertical=True;width='-';widthok=True;core='-';coreok=True
        good=all((sizeok,rgba,nonempty,border,min(shares)>=30,max(means)<.01,sum(reds)==0,min(loops)>=threshold,roundtrip,vertical,all(seams),widthok,coreok))
        if not good:failures.append(name)
        borderstr=('OK sides' if border else 'FAIL') if not vertical_border else ('OK' if border else 'FAIL')
        print(f"{name} | {'OK' if sizeok else 'FAIL'} | {'OK' if rgba else 'FAIL'} | {'OK' if nonempty else 'FAIL'} | {borderstr} | {min(shares):.1f}/{np.mean(shares):.1f} | "+'/'.join(f'{x:.2f}' for x in means)+f" | {sum(reds)} | {min(loops):.1f} | {'OK' if roundtrip else 'FAIL'} | {'OK' if vertical else 'FAIL'} | {'OK' if all(seams) else 'FAIL'} | {width} | {core}")
        print('  pink cells:', ' '.join(f'{x:.0f}' for x in shares),'loop steps:', ' '.join(f'{x:.1f}' for x in loops))
    if failures:
        raise SystemExit('FAILED: '+', '.join(failures))
    print('PASS: all audited checks; no reserved cells. Beam top/bottom join vertically, so its 6 px margin applies to the sides.')


if __name__=='__main__': audit()
