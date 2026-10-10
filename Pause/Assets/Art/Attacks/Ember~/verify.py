"""Audit the Ember boss laser atlases; run from any working directory."""
from pathlib import Path
from PIL import Image
import numpy as np

ROOT=Path(__file__).resolve().parent
FILES={
 'ember_attack_laser.png':((1024,384),(128,128),8,3),
 'ember_attack_laserbody.png':((512,256),(128,256),4,1),
 'ember_attack_lasertell.png':((512,128),(128,128),4,1),
}

def hsv(a):
    rgb=a[:,:,:3].astype(np.float32)/255
    mx=rgb.max(2);mn=rgb.min(2);dif=mx-mn
    h=np.zeros(mx.shape,dtype=np.float32)
    ix=(mx==rgb[:,:,0])&(dif>0)
    h[ix]=((rgb[:,:,1][ix]-rgb[:,:,2][ix])/dif[ix])%6
    ix=(mx==rgb[:,:,1])&(dif>0)
    h[ix]=(rgb[:,:,2][ix]-rgb[:,:,0][ix])/dif[ix]+2
    ix=(mx==rgb[:,:,2])&(dif>0)
    h[ix]=(rgb[:,:,0][ix]-rgb[:,:,1][ix])/dif[ix]+4
    h=(h*60)%360
    s=np.where(mx>0,dif/np.maximum(mx,.0001),0)
    return h,s,mx

def cell(a, x, y, cw, ch):
    return a[y*ch:(y+1)*ch,x*cw:(x+1)*cw]

def changed(a,b):
    union=(a[:,:,3]>=128)|(b[:,:,3]>=128)
    if not union.any():return 0
    return 100*np.count_nonzero(np.any(a!=b,axis=2)&union)/np.count_nonzero(union)

failed=[]
summary=[]
for name,(size,(cw,ch),cols,rows) in FILES.items():
    im=Image.open(ROOT/name);a=np.array(im.convert('RGBA'))
    shape=im.size==size;mode=im.mode=='RGBA'
    n=np.array(im.resize((im.width//2,im.height//2),Image.Resampling.NEAREST).resize(im.size,Image.Resampling.NEAREST))
    rt=np.array_equal(a,n)
    shares=[];borders=[];widths=[];seams=[]
    for y in range(rows):
      for x in range(cols):
        c=cell(a,x,y,cw,ch)
        h,s,v=hsv(c);opaque=c[:,:,3]>=128
        pink=(((h>=302)&(h<=340)&(s>.2))|((v>.89)&(s<.25)))&opaque
        shares.append(100*np.count_nonzero(pink)/max(1,np.count_nonzero(opaque)))
        tile_axis=name.endswith('laserbody.png') or (name.endswith('lasertell.png') and x<2)
        side=np.all(c[:,:6,3]<=24) and np.all(c[:,-6:,3]<=24)
        ends=True if tile_axis else np.all(c[:6,:,3]<=24) and np.all(c[-6:,:,3]<=24)
        borders.append(bool(side and ends))
        if opaque.any():
            xx=np.where(opaque)[1];widths.append(int(xx.max()-xx.min()+1))
        else:widths.append(0)
        if tile_axis:seams.append(bool(np.array_equal(c[0],c[-1])))
    h,s,v=hsv(a);opa=a[:,:,3]>=128
    hue={}
    for label,target in [('cyan',178),('green',82),('violet',259),('amber',37)]:
        angular=np.abs((h-target+180)%360-180)
        hue[label]=float(round(100*np.count_nonzero((angular<=20)&(s>.65)&opa)/max(1,np.count_nonzero(opa)),2))
    red=100*np.count_nonzero(((h>=345)|(h<=15))&(s>.5)&opa)/max(1,np.count_nonzero(opa))
    print(name)
    print('  size OK',shape,'RGBA',mode,'reserved cells empty: n/a (all cells assigned)')
    print('  border >=6px OK',all(borders),'(tiling axis excepted)' if seams else '', 'pink share %',','.join(f'{v:.1f}' for v in shares))
    print('  hue within 20deg, saturated %',hue,'red band %',round(red,3))
    print('  x2 nearest round trip',rt,'vertical seamless',all(seams) if seams else 'n/a','content max width',max(widths),'beam <=100',max(widths)<=100 if seams and name.endswith('laserbody.png') else 'n/a')
    loops=[]
    if name.endswith('laser.png'):loops=[('muzzle',[(1,i) for i in range(4)]),('spark',[(2,i) for i in range(4,8)])]
    if name.endswith('laserbody.png'):loops=[('beam',[(0,i) for i in range(4)])]
    if name.endswith('lasertell.png'):loops=[('sight',[(0,i) for i in range(2)]),('lock',[(0,i) for i in range(2,4)])]
    loop_min=100.0
    for label,coords in loops:
        cc=[cell(a,x,y,cw,ch) for y,x in coords]
        dif=[changed(cc[i],cc[(i+1)%len(cc)]) for i in range(len(cc))]
        print('  loop',label,'step differences %',','.join(f'{v:.1f}' for v in dif),'seam',f'{dif[-1]:.1f}','OK',min(dif)>=3)
        loop_min=min(loop_min,min(dif))
        if min(dif)<3:failed.append(name+': '+label+' loop')
    if not(shape and mode and rt and all(borders) and min(shares)>=30 and red==0 and all(seams) and (name!='ember_attack_laserbody.png' or max(widths)<=100)):
        failed.append(name+': atlas audit')
    summary.append((name.replace('ember_attack_',''),f'{im.width}x{im.height}',f'{min(shares):.1f}',f'{loop_min:.1f}',f'{red:.1f}',f'{hue["amber"]:.2f}',str(max(widths)),str(all(seams)) if seams else 'n/a'))
print('\nFILE                      SIZE       MIN PINK%  MIN LOOP%  RED%  AMBER±20%  MAX W  V-SEAM')
for row in summary:
    print(f'{row[0]:25} {row[1]:10} {row[2]:>9} {row[3]:>10} {row[4]:>5} {row[5]:>11} {row[6]:>6} {row[7]:>7}')
print('RESULT', 'PASS' if not failed else 'FAIL '+', '.join(failed))
raise SystemExit(bool(failed))
