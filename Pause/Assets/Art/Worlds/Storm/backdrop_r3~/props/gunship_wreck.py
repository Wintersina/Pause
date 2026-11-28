# The user-approved gunship (out2/bd_storm_prop_gunship/r1_0..3, 4 liveries; "should be crashed on the planet ... covered with ice
# or something") as a DEAD, STUCK derelict for Storm (no ice there): rotors stopped and dark, one rotor torn off, scorched, the hull
# rimed with hail, tangled in broken tether cables, canted nose-down and half swallowed by the cloud deck. Animated only by the
# weather: the cloud boils round it, a thin smoke thread rises from the burn, one red distress lamp blinks once a second.
# Output: out3/bd_storm_prop_gunship/r1_<k>.png (8 frames of 256 px, 125 ms each).
import numpy as np, os
from PIL import Image, ImageDraw
from scipy import ndimage
SRC='out2/bd_storm_prop_gunship'; DST='out3/bd_storm_prop_gunship'
N=256; F=8
def hsv(a):
    r,g,b=a[...,0],a[...,1],a[...,2]; mx=a[...,:3].max(-1); mn=a[...,:3].min(-1); d=np.maximum(mx-mn,1e-9)
    h=np.where(mx==r,((g-b)/d)%6,np.where(mx==g,(b-r)/d+2,(r-g)/d+4))*60; h=np.where(mx==mn,0,h)
    s=np.where(mx>0,(mx-mn)/np.maximum(mx,1e-9),0); return h,s,mx
def fbm(shape, seed, scales=(48,24,12,6), amps=(1,.55,.3,.15), wrap=True):
    rng=np.random.default_rng(seed); out=np.zeros(shape)
    for sc,am in zip(scales,amps):
        n=rng.random(shape); out+=am*ndimage.gaussian_filter(n,sc/3,mode='wrap' if wrap else 'reflect')
    out-=out.min(); return out/max(out.max(),1e-9)
def smooth(e0,e1,x): t=np.clip((x-e0)/(e1-e0),0,1); return t*t*(3-2*t)
def over(a,b):
    oa=b[...,3:]+a[...,3:]*(1-b[...,3:])
    rgb=np.where(oa>1e-4,(b[...,:3]*b[...,3:]+a[...,:3]*a[...,3:]*(1-b[...,3:]))/np.maximum(oa,1e-4),0)
    return np.concatenate([rgb,oa],-1)
def layer_from(draw_fn, ss=4):
    big=Image.new('RGBA',(N*ss,N*ss),(0,0,0,0)); draw_fn(ImageDraw.Draw(big),ss)
    return np.asarray(big.resize((N,N),Image.LANCZOS)).astype(float)/255
DECK=np.asarray(Image.open('storm/r3/tiles/v2_mid.png').convert('RGB').crop((300,1200,300+N,1200+N))).astype(float)/255
def wreck(k):
    im=np.asarray(Image.open(f'{SRC}/r1_{k}.png').convert('RGBA')).astype(float)/255
    f0=im[:, :N].copy()
    h,s,v=hsv(f0)
    # the rotors: the cyan rings, found before they go dark
    cyan=(h>165)&(h<205)&(s>.3)&(v>.35)&(f0[...,3]>.5)
    lab,n=ndimage.label(ndimage.binary_closing(cyan,iterations=3)); sizes=ndimage.sum(cyan,lab,range(1,n+1))
    rot=[ndimage.center_of_mass(lab==i+1)[::-1] for i in np.argsort(sizes)[-4:]]
    rot=sorted(rot,key=lambda c:(c[1],c[0]))         # top-left, top-right, bottom-left, bottom-right
    torn=rot[1] if len(rot)==4 else (190,90)             # the top-right rotor (in view above the cloud)
    # dead: every glow out (rotor rings, nav lamps, cockpit), hull darker and duller
    glow=(s>.35)&(v>.55)&(((h>165)&(h<205))|(h<20)|(h>340)|((h>90)&(h<160)))
    f=f0.copy(); g=f[...,:3]
    L=(g@[.2126,.7152,.0722])[...,None]
    g[:]=np.where(glow[...,None], L*.45*np.array([.85,.9,1.0]), g)
    amber=(h>20)&(h<55)&(s>.35)&(f[...,3]>.5)
    g[:]=np.where(amber[...,None], g*.55, g)
    L=(g@[.2126,.7152,.0722])[...,None]; g[:]=(g*.8+L*.2)*.9
    # one rotor torn off: its disc is gone, a ragged stump and scorch left
    yy,xx=np.mgrid[0:N,0:N].astype(float)
    rr=np.hypot(xx-torn[0],yy-torn[1]); jag=fbm((N,N),31+k,(8,4,2),(1,.6,.3))
    hole=rr<(22+jag*9)
    f[...,3]=np.where(hole, f[...,3]*smooth(14,26,rr+jag*10)*0, f[...,3])
    burnc=((torn[0]+N/2)/2+2,(torn[1]+N/2)/2)          # the wing root between the torn rotor and the hull
    rb=np.hypot(xx-burnc[0],yy-burnc[1]); burn=smooth(34,6,rb+jag*14)*(f[...,3]>0)
    g[:]=g*(1-burn[...,None]*.8)+np.array([.05,.04,.04])*burn[...,None]*.8
    # hail rime: blotches and grains of pale blue-white over the upper (sky-facing) surfaces
    up=smooth(.15,.9,1-(yy/N))*.6+.4
    blot=smooth(.6,.85,fbm((N,N),70+k,(20,10,5),(1,.5,.3)))
    grain=(np.random.default_rng(90+k).random((N,N))>.94)*smooth(.35,.6,fbm((N,N),80+k,(12,6),(1,.5)))
    rime=np.clip(blot*.7+grain*.45,0,1)*up*(f[...,3]>.4)*(1-burn)
    RIME=np.array([.80,.87,.95])
    g[:]=g*(1-rime[...,None]*.8)+RIME*rime[...,None]*.8
    # canted nose-down into the deck, 18 degrees
    pre=Image.fromarray((np.clip(f,0,1)*255+.5).astype(np.uint8),'RGBA').rotate(-18,resample=Image.BICUBIC,center=(N/2,N/2))
    base=np.asarray(pre).astype(float)/255
    # tangled tethers: broken cables draped over the hull and trailing off into the cloud
    def cables(d,ss):
        C=(28,30,36,255); H=(120,126,138,255)
        for pts,w in [([(30,96),(44,140),(52,178),(48,220),(40,252)],3.2),([(212,120),(206,160),(218,200),(232,244)],3.0)]:
            P=[(x*ss,y*ss) for x,y in pts]; d.line(P,fill=C,width=int(w*ss),joint='curve'); d.line([(x-ss*.5,y-ss*.5) for x,y in P],fill=H,width=max(1,int(w*ss*.3)))
    tl=layer_from(cables); fade=smooth(250,170,yy)*smooth(2,30,xx)*smooth(254,226,xx)
    tl[...,3]*=fade
    base=over(base,tl)
    frames=[]
    # the cloud deck swallowing its lower part: a wavy boundary, boiling on an 8-frame loop (two noise fields cross-faded)
    nA=fbm((N,N),200+k); nB=fbm((N,N),300+k); nC=fbm((N,N),400+k,(10,5,3),(1,.5,.3))
    smoke_n=fbm((N,N),500+k,(10,5),(1,.6))
    for i in range(F):
        ph=2*np.pi*i/F
        n=nA*(.5+.5*np.cos(ph))+nB*(.5-.5*np.cos(ph))
        # the bank is the DECK's own cloud (a crop of the r3 ground at its drawn scale), so the hull sinks into the deck it lies on
        env=1-((xx-N/2)/(N*.47))**2-((yy-N*.86)/(N*.3))**2
        dd=env+(n-.5)*.7+(nC-.5)*.3
        ca=smooth(0,.4,dd)*smooth(N-1,N-26,yy)*smooth(0,20,np.minimum(xx,N-1-xx))*.9
        rim=(smooth(0,.1,dd)*smooth(.3,.1,dd))[...,None]*.18                       # the billow edge catching the light
        crgb=np.clip(DECK*(.95+.25*(n[...,None]-.5))+rim,0,1)
        cl=np.concatenate([crgb,ca[...,None]],-1)
        fr=over(base,cl)
        # a thin smoke thread from the burn (rotated with the hull), drifting with the wind
        c=np.array(burnc)-N/2; a=np.deg2rad(18); bx=N/2+c[0]*np.cos(a)-c[1]*np.sin(a); by=N/2+c[0]*np.sin(a)+c[1]*np.cos(a)
        t=np.clip((by-yy)/110,0,1)
        cx=bx+t*46+np.sin(t*5+ph)*7
        sm=smooth(9+t*14,0,np.abs(xx-cx))*(yy<by)*(1-t)*smooth(.2,.7,np.roll(smoke_n,int(i*4),0))*.55
        fr=over(fr,np.concatenate([np.broadcast_to(np.array([.42,.44,.48]),(N,N,3)),sm[...,None]],-1))
        # the red distress lamp on the tail fin: lit frames 0-1 (one blink a second)
        if i in (0,1):
            tail=np.array([N/2+6,N*.18])
            rl=np.hypot(xx-tail[0],yy-tail[1])
            lamp=np.concatenate([np.broadcast_to(np.array([1.,.26,.18]),(N,N,3)),(smooth(5,1.2,rl)*.95)[...,None]],-1)
            fr=over(fr,lamp)
        frames.append(fr)
    out=np.concatenate(frames,1)
    # no pink
    hh,ss_,vv=hsv(out); bad=(ss_>.12)&(hh>=280)&(hh<356)&(out[...,3]>0)
    out[...,:3]=np.where(bad[...,None],(out[...,:3]@[.2126,.7152,.0722])[...,None]*np.array([.8,.85,1.0]),out[...,:3])
    return out
if __name__=='__main__':
    os.makedirs(DST,exist_ok=True)
    for k in range(4):
        Image.fromarray((np.clip(wreck(k),0,1)*255+.5).astype(np.uint8),'RGBA').save(f'{DST}/r1_{k}.png')
    print('ok')
