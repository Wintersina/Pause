import json,glob,os,colorsys,sys
import numpy as np
from PIL import Image
ROOT='/Users/sina/Developer/Pause-shipscale/Pause/Assets/Art/Resources/'
m=json.load(open('measure.json'))
def stats(c):
    a=c[...,3]>128
    if a.sum()<20: return None
    rgb=c[...,:3].astype(float)/255
    mx=rgb.max(-1);mn=rgb.min(-1)
    s=np.where(mx>0,(mx-mn)/np.maximum(mx,1e-6),0)
    lum=0.299*rgb[...,0]+0.587*rgb[...,1]+0.114*rgb[...,2]
    ys,xs=np.where(a); cy=(ys.min()+ys.max())/2; cx=(xs.min()+xs.max())/2
    top=lum[a&(np.arange(c.shape[0])[:,None]<cy)].mean(); bot=lum[a&(np.arange(c.shape[0])[:,None]>=cy)].mean()
    left=lum[a&(np.arange(c.shape[1])[None,:]<cx)].mean(); right=lum[a&(np.arange(c.shape[1])[None,:]>=cx)].mean()
    # hue of saturated bright pixels
    hsv=np.array([colorsys.rgb_to_hsv(*p) for p in rgb[a&(s>.5)&(mx>.6)][::3]]) if (a&(s>.5)&(mx>.6)).any() else np.zeros((0,3))
    pink=float(((hsv[:,0]>.78)&(hsv[:,0]<.96)).mean()) if len(hsv) else 0
    red=float(((hsv[:,0]<.03)|(hsv[:,0]>.97)).mean()) if len(hsv) else 0
    # dark-outline test: boundary luminance vs interior
    return dict(lum=lum[a].mean(),sat=s[a].mean(),contrast=lum[a].std(),top_minus_bottom=top-bot,left_minus_right=left-right,pink=pink,red=red,bright=float((lum[a]>.75).mean()),dark=float((lum[a]<.18).mean()))
res={}
for kind in ('enemies','elites'):
    for k,e in m[kind].items():
        if kind=='enemies': p=ROOT+'Enemies/%s.png'%k; idx=0
        else:
            p=ROOT+'Elites/%s/%s.png'%(e['world'].capitalize(),k); d=json.load(open(ROOT+'Elites/Defs/%s.json'%k)); idx=((d.get('cells') or {}).get('flight') or [0])[0]
        im=np.array(Image.open(p).convert('RGBA')); h=im.shape[0]
        st=stats(im[:,idx*h:(idx+1)*h])
        if st:
            st['pale']=e['pale']; st['world']=e['world'] if kind=='elites' else ['space','frost','verdant','ember','tide','storm','desert'][e['world']]
            res[kind+':'+k]=st
json.dump(res,open('lang.json','w'),indent=1)
import collections
for kind in ('enemies','elites'):
    print(kind)
    byw=collections.defaultdict(list)
    for k,v in res.items():
        if k.startswith(kind): byw[v['world']].append(v)
    for w,l in byw.items():
        f=lambda key: np.mean([x[key] for x in l])
        print(' %-7s n=%2d lum %.2f sat %.2f contrast %.2f top-bot %+.3f left-right %+.3f bright %.2f dark %.2f pink %.2f pale %d/%d'%(w,len(l),f('lum'),f('sat'),f('contrast'),f('top_minus_bottom'),f('left_minus_right'),f('bright'),f('dark'),f('pink'),sum(x['pale'] for x in l),len(l)))
print('pink outliers (non-space, pink>.15):',[(k,round(v['pink'],2)) for k,v in res.items() if v['world']!='space' and v['pink']>.15])
print('darkest:',sorted([(round(v['lum'],2),k) for k,v in res.items() if k.startswith('elites')])[:6])
print('light dir outliers (bottom-lit):',[(k,round(v['top_minus_bottom'],2)) for k,v in res.items() if v['top_minus_bottom']<-.05])
