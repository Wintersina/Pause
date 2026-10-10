"""Audit the three staged Tide laser atlases; exits nonzero on failed checks."""
from pathlib import Path
import colorsys
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SPECS = [
    ('tide_attack_laser.png', (1024,384), (128,128), 8, 3),
    ('tide_attack_laserbody.png', (512,256), (128,256), 4, 1),
    ('tide_attack_lasertell.png', (512,128), (128,128), 4, 1),
]
BAD_HUES = {'cyan178':178, 'green82':82, 'violet259':259, 'amber37':37}


def cells(a, cw, ch, cols, rows):
    return [[a[y*ch:(y+1)*ch,x*cw:(x+1)*cw] for x in range(cols)] for y in range(rows)]


def hue_audit(a):
    rgb=a[:,:,:3].reshape(-1,3)
    alpha=a[:,:,3].reshape(-1)
    keep=alpha>=128
    rgb=rgb[keep]
    hue=[]; sat=[]; val=[]
    for r,g,b in rgb:
        h,s,v=colorsys.rgb_to_hsv(int(r)/255,int(g)/255,int(b)/255)
        hue.append(h*360);sat.append(s);val.append(v)
    h=np.array(hue);s=np.array(sat);v=np.array(val)
    pink=((h>=312)&(h<=326)&(s>.12)) | ((rgb.min(axis=1)>=230)&((rgb.max(axis=1).astype(int)-rgb.min(axis=1).astype(int))<=30))
    saturated=s>.5
    counts={k:round(100*np.count_nonzero(saturated & (np.abs((h-degree+180)%360-180)<=20))/max(np.count_nonzero(saturated),1),2) for k,degree in BAD_HUES.items()}
    red=int(np.count_nonzero((s>.5) & ((h>=345)|(h<=15))))
    return float(round(100*np.mean(pink),1)),counts,red


def diff(a,b):
    mask=(a[:,:,3]>=128)|(b[:,:,3]>=128)
    changed=np.any(a!=b,axis=2)
    return float(round(100*np.count_nonzero(changed & mask)/max(np.count_nonzero(mask),1),1))


def loop(name, group, min_diff=3):
    steps=[diff(group[i],group[(i+1)%len(group)]) for i in range(len(group))]
    regular=steps[:-1]
    seam=steps[-1]
    # The wrap may be as dynamic as any regular step, but should not spike.
    smooth=seam<=max(regular)*1.5 and seam>=min_diff
    print(f'  loop {name:<8} steps {steps} | min >= {min_diff}%: {min(steps)>=min_diff} | wrap comparable: {smooth}')
    return min(steps)>=min_diff and smooth


def main():
    errors=[]; data={}; summary=[]
    for name,size,cell,cols,rows in SPECS:
        im=Image.open(ROOT/name)
        a=np.array(im.convert('RGBA'))
        data[name]=cells(a,*cell,cols,rows)
        sizeok=im.size==size; rgba=im.mode=='RGBA'
        rt=np.array(Image.fromarray(a[::2,::2],'RGBA').resize(im.size,Image.Resampling.NEAREST))
        roundtrip=np.array_equal(a,rt)
        shares=[]; borders=[]; hue_tot={k:[] for k in BAD_HUES}; reds=[]
        for y,row in enumerate(data[name]):
            for x,c in enumerate(row):
                opaque=c[:,:,3]>24
                if not opaque.any():
                    errors.append(f'{name} ({x},{y}) empty')
                    continue
                yy,xx=np.where(opaque)
                if name=='tide_attack_laserbody.png':
                    border=min(xx.min(),cell[0]-1-xx.max()) # vertical seam is live art
                else:
                    border=min(xx.min(),yy.min(),cell[0]-1-xx.max(),cell[1]-1-yy.max())
                borders.append(border)
                share,hues,red=hue_audit(c)
                shares.append(share); reds.append(red)
                for k,v in hues.items():hue_tot[k].append(v)
                if share<30:errors.append(f'{name} ({x},{y}) pink {share}%')
                if border<6:errors.append(f'{name} ({x},{y}) border {border}px')
                if red:errors.append(f'{name} ({x},{y}) red {red}px')
        print(f'{name}: size {sizeok} {im.size} | RGBA {rgba} | reserved N/A (all cells assigned)')
        print(f'  border >=6px {min(borders)>=6} (min {min(borders)}; body checks sides, continuous seam checks below) | x2 round trip {roundtrip}')
        print(f'  pink % per cell: {shares} (min {min(shares)}; >=30 {min(shares)>=30})')
        print(f'  pickup hue % of saturated pixels (max cell): '+', '.join(f'{k} {max(v):.2f}' for k,v in hue_tot.items())+f' | red pixels {sum(reds)}')
        summary.append((name,sizeok,rgba,min(borders),min(shares),max(max(v) for v in hue_tot.values()),sum(reds),roundtrip))
        if not sizeok:errors.append(f'{name} size')
        if not rgba:errors.append(f'{name} RGBA')
        if not roundtrip:errors.append(f'{name} x2')
        if any(max(v)>=8 for v in hue_tot.values()):errors.append(f'{name} pickup hue >=8%')
    laser=data['tide_attack_laser.png'];body=data['tide_attack_laserbody.png'][0];tell=data['tide_attack_lasertell.png'][0]
    for name,group,minimum in [('muzzle',laser[1][:4],3),('spark',laser[2][4:],3),('body',body,8),('sight',tell[:2],3),('lock',tell[2:],3)]:
        if not loop(name,group,minimum):errors.append(f'{name} loop')
    widths=[]; seams=[]
    for i,c in enumerate(body):
        yy,xx=np.where(c[:,:,3]>24)
        widths.append(int(xx.max()-xx.min()+1))
        seams.append(bool(np.array_equal(c[0],c[-1])))
    print(f'  body widths {widths} px (<=100 {max(widths)<=100}) | vertical top/bottom seamless {seams}')
    if max(widths)>100:errors.append('body width')
    if not all(seams):errors.append('body vertical seam')
    sightseams=[bool(np.array_equal(c[0],c[-1])) for c in tell[:2]]
    print(f'  sight top/bottom tile seam {sightseams}')
    if not all(sightseams):errors.append('sight vertical seam')
    print('\nfile | size | RGBA | reserved | border min | pink min | pickup hue max | red | x2')
    for name,sz,mode,margin,pink,hue,red,rt in summary:
        print(f'{name} | {"OK" if sz else "FAIL"} | {"OK" if mode else "FAIL"} | none | {margin}px | {pink:.1f}% | {hue:.2f}% | {red} | {"OK" if rt else "FAIL"}')
    print('RESULT:', 'PASS' if not errors else 'FAIL: '+', '.join(errors))
    return 0 if not errors else 1


if __name__=='__main__': raise SystemExit(main())
