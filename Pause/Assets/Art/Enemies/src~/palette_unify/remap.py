import numpy as np
from PIL import Image
def to_hsv(rgb):
    r,g,b=[rgb[...,i].astype(float)/255. for i in range(3)]
    mx=np.maximum(np.maximum(r,g),b);mn=np.minimum(np.minimum(r,g),b);d=mx-mn
    dd=np.where(d>1e-9,d,1)
    rc=(mx-r)/dd;gc=(mx-g)/dd;bc=(mx-b)/dd
    h=np.where(r==mx,bc-gc,np.where(g==mx,2+rc-bc,4+gc-rc))
    h=np.where(d>1e-9,(h/6)%1.0,0)*360
    s=np.where(mx>0,d/np.where(mx>0,mx,1),0)
    return h,s,mx
def to_rgb(h,s,v):
    h=(h%360)/60.;i=np.floor(h).astype(int)%6;f=h-np.floor(h)
    p=v*(1-s);q=v*(1-s*f);t=v*(1-s*(1-f))
    r=np.choose(i,[v,q,p,p,t,v]);g=np.choose(i,[t,v,v,q,p,p]);b=np.choose(i,[p,p,t,v,v,q])
    return np.stack([r,g,b],-1)
def sm(x,a,b):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)
def lerp(a,b,t):return a+(b-a)*t
def hue_mix(h0,h1,t):
    d=((h1-h0+180)%360)-180;return (h0+d*t)%360

def tide(h,s,v):
    warm=(h<95)|(h>=330)
    chroma=sm(s,0.04,0.16)
    # warm brass/olive -> deep teal; bright warm -> coral
    wmask=warm*chroma
    coral=wmask*sm(v,0.66,0.80)*sm(s,0.22,0.35)
    tealw=wmask*(1-coral)
    nh=h.copy();ns=s.copy();nv=v.copy()
    nh=hue_mix(nh,174,tealw); ns=lerp(ns,np.clip(0.22+s*1.5,0,0.78),tealw); nv=lerp(nv,np.clip(v*1.04,0,1),tealw)
    nh=hue_mix(nh,22,coral); ns=lerp(ns,np.clip(s*1.2,0.5,0.7),coral)
    # mint glow -> aqua
    mint=((h>=95)&(h<205))*chroma
    nh=hue_mix(nh,166,mint*0.85); ns=lerp(ns,np.clip(s*1.1,0,1),mint)
    return nh,ns,nv
def storm(h,s,v):
    ch=sm(s,0.03,0.12)*(1-sm(v,0.93,1.0))*np.maximum(((h>=170)&(h<=290)).astype(float),1-sm(s,0.15,0.30))
    nh=hue_mix(h,236,ch); 
    ns=lerp(s,np.clip(0.08+s*1.6,0,0.58),ch)
    # electric highlights: bright pixels get blue-white (hue 222)
    hi=sm(v,0.80,0.95)
    nh=hue_mix(nh,222,hi*ch); ns=lerp(ns,np.maximum(ns,0.30),hi*ch)
    nv=lerp(v,v*0.93,ch*(1-hi))
    return nh,ns,nv
def desert(h,s,v):
    ch=sm(s,0.04,0.14)
    sh=1-sm(v,0.28,0.50)   # shadows -> terracotta umber
    warm=((h<75)|(h>=330))
    m=ch*warm
    nh=hue_mix(h,33,m*(1-sh)*0.6); nh=hue_mix(nh,17,m*sh*0.8)
    ns=lerp(s,np.clip(s*1.32+0.05,0,0.8),m)
    ns=np.where(m*sh>0, np.minimum(ns,0.62), ns)
    nv=v
    return nh,ns,nv
FUN={'tide':tide,'storm':storm,'desert':desert}
def remap_rgba(a,world):
    a=a.copy();rgb=a[...,:3]
    h,s,v=to_hsv(rgb)
    nh,ns,nv=FUN[world](h,s,v)
    out=(np.clip(to_rgb(nh,np.clip(ns,0,1),np.clip(nv,0,1)),0,1)*255+0.5).astype(np.uint8)
    m=a[...,3]>0
    a[...,:3]=np.where(m[...,None],out,a[...,:3])
    return a
def remap_file(src,dst,world):
    a=np.array(Image.open(src).convert('RGBA'))
    Image.fromarray(remap_rgba(a,world)).save(dst)
