# User rule (2026-11-28): nothing may fly around freely unless the player can shoot it -- every vehicle must read as moored /
# tethered / stuck. The skimmer and the freighter convoy get MOORING CABLES baked into all 8 frames (all liveries), the convoy's
# contrails become its towline, and the code drops their cruise (bob / sway only). Output: out3/bd_storm_prop_<kind>/r1_<k>.png
import numpy as np, os, sys
from PIL import Image, ImageDraw
from scipy import ndimage
SRC='out2'; DST='out3'
STEEL=(30,33,40); HI=(118,124,136)
def cable_layer(N, pts, width, fade_from=None, ss=4):
    """an antialiased cable polyline on an N x N RGBA layer; fade_from: (index of segment end that fades to 0 alpha)"""
    big=Image.new('RGBA',(N*ss,N*ss),(0,0,0,0)); d=ImageDraw.Draw(big)
    P=[(x*ss,y*ss) for x,y in pts]
    d.line(P, fill=STEEL+(255,), width=int(width*ss), joint='curve')
    d.line([(x-ss*.4,y-ss*.4) for x,y in P], fill=HI+(255,), width=max(1,int(width*ss*.35)))
    lay=np.asarray(big.resize((N,N),Image.LANCZOS)).astype(float)/255
    return lay
def fade_mask(N, a, b):
    """alpha multiplier along segment a->b: 1 at a, 0 at b (the cable vanishing into the haze below)"""
    yy,xx=np.mgrid[0:N,0:N].astype(float)
    ax,ay=a; bx,by=b; vx,vy=bx-ax,by-ay; L2=vx*vx+vy*vy
    t=np.clip(((xx-ax)*vx+(yy-ay)*vy)/L2,0,1)
    u=np.clip((t-.72)/.28,0,1)
    return 1-u*u*(3-2*u)*.85
def buoy(N, c, r=5.5, ss=4):
    """a mooring buoy seen from above: a dark float with a hazard-yellow ring and a small amber lamp"""
    big=Image.new('RGBA',(N*ss,N*ss),(0,0,0,0)); d=ImageDraw.Draw(big); x,y=c[0]*ss,c[1]*ss; R=r*ss
    d.ellipse([x-R*1.15,y-R*.95,x+R*1.15,y+R*1.35],fill=(10,12,18,90))                 # its shadow on the haze
    d.ellipse([x-R,y-R,x+R,y+R],fill=(206,160,40,255)); d.ellipse([x-R*.7,y-R*.7,x+R*.7,y+R*.7],fill=(44,48,58,255))
    d.ellipse([x-R*.28-ss*.6,y-R*.28-ss*.6,x+R*.28-ss*.6,y+R*.28-ss*.6],fill=(255,190,90,255))
    return np.asarray(big.resize((N,N),Image.LANCZOS)).astype(float)/255
def over(a, b):
    oa=b[...,3:]+a[...,3:]*(1-b[...,3:])
    rgb=np.where(oa>1e-4,(b[...,:3]*b[...,3:]+a[...,:3]*a[...,3:]*(1-b[...,3:]))/np.maximum(oa,1e-4),0)
    return np.concatenate([rgb,oa],-1)
def under(frame, lay):
    """cable UNDER the craft: frame over cable"""
    f=frame; a=f[...,3:]; out=f.copy()
    la=lay[...,3:]
    oa=a+la*(1-a)
    out[...,:3]=np.where(oa>1e-4,(f[...,:3]*a+lay[...,:3]*la*(1-a))/np.maximum(oa,1e-4),0)
    out[...,3:]=oa; return out
def extreme(alpha, d, thr=.6):
    ys,xs=np.nonzero(alpha>thr); p=xs*d[0]+ys*d[1]; i=np.argmax(p); return float(xs[i]),float(ys[i])
def skimmer(k):
    im=np.asarray(Image.open(f'{SRC}/bd_storm_prop_skimmer/r1_{k}.png').convert('RGBA')).astype(float)/255
    N=im.shape[0]; F=im.shape[1]//N
    a0=im[:, :N, 3]
    bow=extreme(a0,(-1,.8)); stern=extreme(a0,(1,-.8))
    port=extreme(a0,(-.6,-1)); star=extreme(a0,(.6,1))
    # bow and stern lines run out along the hull's axis and down to their buoys; two breast lines from the beam
    cl=lambda p,q:(min(N-9,max(9,q[0])),min(N-9,max(9,q[1])))
    lines=[(bow,cl(bow,(bow[0]-30,bow[1]+30))),(stern,cl(stern,(stern[0]+26,stern[1]-28))),(port,cl(port,(port[0]-30,port[1]-24))),(star,cl(star,(star[0]+28,star[1]+26)))]
    lay=np.zeros((N,N,4))
    for a,b in lines:
        l=cable_layer(N,[a,b],3.2); l[...,3]*=fade_mask(N,a,b)
        lay=over(lay,l)
        lay=over(lay,buoy(N,b))
    out=np.concatenate([under(im[:,f*N:(f+1)*N],lay) for f in range(F)],1)
    return out
def convoy(k):
    im=np.asarray(Image.open(f'{SRC}/bd_storm_prop_convoy/r1_{k}.png').convert('RGBA')).astype(float)/255
    N=im.shape[0]; F=im.shape[1]//N
    a0=im[:, :N, 3]
    core=ndimage.binary_opening(a0>.85, iterations=3)
    lab,n=ndimage.label(core); sizes=ndimage.sum(core,lab,range(1,n+1)); keep=np.argsort(sizes)[-3:]+1
    cents=sorted([ndimage.center_of_mass(lab==i)[::-1] for i in keep], key=lambda c:c[0])   # (x, y) rear (left) .. front (right)
    hull=ndimage.binary_dilation(np.isin(lab,keep), iterations=5)
    rear, front = cents[0], cents[-1]
    dx,dy=front[0]-rear[0], front[1]-rear[1]; L=(dx*dx+dy*dy)**.5; ux,uy=dx/L,dy/L
    end0=(max(10,rear[0]-ux*60), min(N-10,rear[1]-uy*60)); end1=(min(N-10,front[0]+ux*52), max(10,front[1]+uy*52))
    out=[]
    for f in range(F):
        fr=im[:,f*N:(f+1)*N].copy()
        lum=fr[...,:3]@[.2126,.7152,.0722]
        streak=(~hull)&(lum>.35)&(fr[...,3]>0)
        fr[streak,3]=0                                   # the contrails are gone
        lay=cable_layer(N,[end0,rear,cents[1],front,end1],3.4)
        m=np.ones((N,N)); m=np.minimum(m, np.where(((np.mgrid[0:N,0:N][1]-rear[0])*ux+(np.mgrid[0:N,0:N][0]-rear[1])*uy)<0, fade_mask(N,rear,end0),1))
        m=np.minimum(m, np.where(((np.mgrid[0:N,0:N][1]-front[0])*ux+(np.mgrid[0:N,0:N][0]-front[1])*uy)>0, fade_mask(N,front,end1),1))
        lay[...,3]*=m
        lay=over(over(lay,buoy(N,end0,7)),buoy(N,end1,7))
        # each ship also rides a breast line out to its own buoy, alternating sides of the line
        px,py=uy,-ux
        for i,c in enumerate(cents):
            sgn=1 if i%2==0 else -1
            q=(c[0]+px*sgn*52, c[1]+py*sgn*52)
            q=(min(N-10,max(10,q[0])),min(N-10,max(10,q[1])))
            l=cable_layer(N,[c,q],2.8); l[...,3]*=fade_mask(N,c,q)
            lay=over(over(lay,l),buoy(N,q,6.5))
        out.append(under(fr,lay))
    return np.concatenate(out,1)
def save(a, kind, k):
    os.makedirs(f'{DST}/bd_storm_prop_{kind}',exist_ok=True)
    Image.fromarray((np.clip(a,0,1)*255+.5).astype(np.uint8),'RGBA').save(f'{DST}/bd_storm_prop_{kind}/r1_{k}.png')
if __name__=='__main__':
    for k in range(4): save(skimmer(k),'skimmer',k)
    for k in range(3): save(convoy(k),'convoy',k)
    print('ok')
