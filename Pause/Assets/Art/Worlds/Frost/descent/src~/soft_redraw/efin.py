# Frost planetfall entry: raw paintings -> install-size candidates.
#  decks: 2048x1024 opaque RGBA, periodic in X and Y (wrap Repeat both ways), soft (true-res downsample + smooth upscale + blur)
#  breakthrough: 5 cells of 1024x1024 (5120x1024), straight alpha, built from ONE painting by scripted expansion
import sys, os, json
sys.path.insert(0,'/private/tmp/claude-501/-Users-sina-Developer/50b8c9d7-da6c-44fe-8c92-736a84a1fed0/scratchpad/pl')
import numpy as np
from PIL import Image
from scipy import ndimage
from wxfin import arr, img, premul, unpremul, resize, smooth, ice
from wxkey import key
O='out2/bd_frost_entry_'
def soft_rgb(rgb, W, H, nat, sigma):
    h,w=rgb.shape[:2]
    a=np.dstack([rgb,np.ones((h,w))])
    a=resize(a,max(8,int(w*nat)),max(8,int(h*nat)),Image.BOX)        # down to the true drawing resolution
    a=resize(a,W,H,Image.BICUBIC)                                      # smooth upscale
    a=np.dstack([ndimage.gaussian_filter(a[...,c],sigma,mode='wrap') for c in range(4)])
    return a[...,:3]
def periodic(rgb, edge=.28):
    """make X/Y periodic: blend with a half-rolled copy near the edges; variance-preserving so the blend zone keeps its contrast"""
    def ax(a, axis):
        n=a.shape[axis]; t=np.arange(n); d=np.minimum(t,n-1-t)/(n*edge)
        w=smooth(0,1,d); w=w.reshape((-1,1,1) if axis==0 else (1,-1,1))
        b=np.roll(a,n//2,axis)
        m=a.mean((0,1),keepdims=True)
        mix=w*a+(1-w)*b; k=np.sqrt(w*w+(1-w)**2)
        return np.clip(m+(mix-m)/k,0,1)
    return ax(ax(rgb,1),0)
def ramp(rgb, stops, tint, red_cap=True):
    L=(rgb*[.2126,.7152,.0722]).sum(-1); lo,hi=np.percentile(L,[1,99.7]); t=np.clip((L-lo)/max(hi-lo,1e-3),0,1)
    xs=np.linspace(0,1,len(stops)); S=np.array(stops)/255
    r=np.dstack([np.interp(t,xs,S[:,c]) for c in range(3)])
    out=r*tint+rgb*(1-tint)
    if red_cap: out[...,0]=np.minimum(out[...,0],out[...,2]*.92)
    return np.clip(out,0,1)
def level(rgb, target):   # mean of max(r,g,b) -> target (the Frost ceiling test compares against it), by gamma (bisection)
    lo,hi=0.2,5.0; m=rgb.max(-1)[::3,::3]
    for _ in range(40):
        g=(lo*hi)**.5
        if (m**g).mean()>target: lo=g
        else: hi=g
    return rgb**g
BRIGHT=[(28,52,104),(58,98,158),(112,152,204),(176,206,236),(232,243,255)]
DARK=[(10,16,34),(24,36,64),(46,62,98),(82,104,146),(150,176,214)]
def deck(raw, out, stops, target, nat=.5, sigma=1.6, tint=.7, edge=.28):
    rgb=arr(Image.open(raw))[...,:3]
    rgb=soft_rgb(rgb,2048,1024,nat,sigma)
    rgb=periodic(rgb,edge)
    rgb=ramp(rgb,stops,tint); rgb=level(rgb,target)
    rgb[...,0]=np.minimum(rgb[...,0],rgb[...,2])
    a=np.dstack([rgb,np.ones(rgb.shape[:2])]); img(a).save(out)
    return float(rgb.max(-1).mean())
# ---------- breakthrough ----------
def burst_src(raw, nat=.5, sigma=1.0, tint=.6, full=.8, floor=.05, R=500):
    a=arr(key(Image.open(raw),full=full,floor=floor)); a=ice(a,tint); p=premul(a)
    h,w=p.shape[:2]; p=resize(p,int(w*nat),int(h*nat),Image.BOX)
    ys,xs=np.nonzero(p[...,3]>.02); cy,cx=(ys.min()+ys.max())/2,(xs.min()+xs.max())/2
    rad=max(ys.max()-cy,cy-ys.min(),xs.max()-cx,cx-xs.min())+2
    k=R/rad; S=int(round(2*rad*k))
    y0,x0=int(cy-rad),int(cx-rad); pad=np.zeros((int(2*rad)+1,int(2*rad)+1,4))
    sy0,sx0=max(0,y0),max(0,x0); sub=p[sy0:y0+pad.shape[0],sx0:x0+pad.shape[1]]
    pad[sy0-y0:sy0-y0+sub.shape[0],sx0-x0:sx0-x0+sub.shape[1]]=sub
    q=resize(pad,S,S,Image.BICUBIC); q=np.dstack([ndimage.gaussian_filter(q[...,c],sigma) for c in range(4)])
    c=np.zeros((1024,1024,4)); o=(1024-S)//2; c[o:o+S,o:o+S]=q[:1024-o,:1024-o]
    return np.clip(c,0,1)   # premultiplied, content radius ~R, centred
SC=[.40,.58,.74,.87,.97]; HOLE=[0,0,0,0,0]; HK=[.40,.27,.15,.06,0]; FADE=[1,1,.95,.82,.6]; GLOW=[.9,.55,.25,.08,0]; SPIN=[0,3,6,8,10]
def burst(raw, out, R=500, gamma=1.25, spin=1.0, round_=.84, pinch=True, zoom=0.0, **kw):
    p=burst_src(raw,R=R,**kw)
    H=W=1024; c=(H-1)/2; yy,xx=np.mgrid[0:H,0:W].astype(float); dy,dx=yy-c,xx-c
    r=np.hypot(dy,dx); th=np.arctan2(dy,dx); frames=[]
    for k in range(5):
        s=SC[k]
        # outward blow-out: outer material travels further (radial power warp), slight swirl increasing with radius
        rn=np.clip(r/(R*s),0,4); rs=R*(HK[k]+(1-HK[k])*rn**gamma)
        a=th-np.deg2rad(SPIN[k]*spin)*rn
        sy,sx=c+rs*np.sin(a),c+rs*np.cos(a)
        f=np.dstack([ndimage.map_coordinates(p[...,i],[sy,sx],order=1,mode='constant') for i in range(4)])
        f*=(1-smooth(round_,1.0,rs/R))[...,None]   # round the painting's squarish outline
        if pinch and HK[k]>0:   # soften the radial pinch at the centre
            bl=np.dstack([ndimage.gaussian_filter(f[...,i],10+30*HK[k]) for i in range(4)]); m=np.exp(-(r/(R*s*(.25+HK[k])))**2)[...,None]; f=f*(1-m)+bl*m
        hole=smooth(HOLE[k]*.6,HOLE[k]*1.2+1e-3,r/(R*s)) if HOLE[k]>0 else np.ones_like(r)
        f*= (hole*FADE[k])[...,None]
        g=np.exp(-(r/(R*s*.42))**2)*GLOW[k]   # bright cold core at the moment of the punch
        glow=np.dstack([g*.86,g*.96,g*1.0,g])
        if zoom>0 and k>0:   # radial smear outward: spray flung off the ring
            acc=f.copy(); n=8; d=zoom*k/4
            for j in range(1,n+1):
                q=1-d*j/n; acc+=np.dstack([ndimage.map_coordinates(f[...,i],[c+dy*q,c+dx*q],order=1,mode='constant') for i in range(4)])*(1-j/(n+1))
            acc/= 1+sum(1-j/(n+1) for j in range(1,n+1)); f=np.maximum(f*.75+acc*.25, acc)
        f=f+glow*(1-f[...,3:])
        f*=smooth(0,30,np.minimum.reduce([yy,xx,H-1-yy,W-1-xx]))[...,None]
        frames.append(unpremul(np.clip(f,0,1)))
    img(np.concatenate(frames,1)).save(out); return frames
def gif(frames, out, bg=(40,58,92)):
    ims=[]
    for f in frames+[frames[-1]]*2:
        b=Image.new('RGBA',(1024,1024),bg+(255,)); b.alpha_composite(img(f)); ims.append(b.resize((400,400),Image.LANCZOS).convert('P',palette=Image.ADAPTIVE))
    ims.append(Image.new('RGBA',(400,400),bg+(255,)).convert('P'))
    ims[0].save(out,save_all=True,append_images=ims[1:],duration=[83]*7+[400],loop=0)
