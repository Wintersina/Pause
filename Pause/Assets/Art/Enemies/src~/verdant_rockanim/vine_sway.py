from PIL import Image, ImageDraw
import numpy as np, math, sys
R='/Users/sina/Developer/Pause-rockanim/Pause/Assets/Art/Resources/Enemies/'
src=np.array(Image.open(R+'verdant_rock_vine.png'))
f0=src[:,:192].copy()
EDGE=[(0,98),(56,98),(75,108),(88,121),(100,121),(110,108),(128,96),(192,96)]
# rock-static: above polyline
xs=np.arange(192)
edge=np.interp(xs,[p[0] for p in EDGE],[p[1] for p in EDGE])
yy,xx=np.mgrid[0:192,0:192]
alpha=f0[...,3]>0
moving=alpha&(yy>edge[xx])
VINES=[(0,80,100,0.0),(80,114,118,0.25),(114,192,98,0.5)]  # xmin,xmax,attach y,phase lag
def sway(k_phase,A,lag):
    return math.sin(2*math.pi*(k_phase-lag))
def make(ph,A=5.0):
    out=f0.copy(); out[moving]=0
    for x0,x1,ya,lag in VINES:
        s=math.sin(2*math.pi*(ph-lag*0.5))
        for y in range(192):
            w=max(0,min(1,(y-ya)/75))**1.2
            sh=int(round(A*s*w))
            cols=np.where(moving[y]&(xx[y]>=x0)&(xx[y]<x1))[0]
            for x in cols:
                nx=x+sh
                if 0<=nx<192 and (moving[y,nx] or not alpha[y,nx]): out[y,nx]=f0[y,x]
    return out
if __name__=='__main__':
    phs=[0.0,0.25,0.5,0.75,0.375,0.875]   # 0..3 loop; 4,5 tell: hard whip left / right
    amps=[5,5,5,5,8,8]
    out=src.copy()
    frames=[]
    for i,(p,A) in enumerate(zip(phs,amps)):
        f=make(p,A); out[:,i*192:(i+1)*192]=f
    Image.fromarray(out).save('vine_new.png')
    np.save('vine_moving.npy',moving)
    m=np.array(Image.fromarray(f0)); ov=m.copy(); ov[moving,0]=255
    bg=Image.new('RGBA',(192,192),(20,20,40,255));bg.alpha_composite(Image.fromarray(ov));bg.resize((768,768),Image.NEAREST).save('mask.png')
    bg=Image.new('RGBA',(1152,192),(20,20,40,255));bg.alpha_composite(Image.fromarray(out[:,:1152]));bg.resize((2304,384),Image.NEAREST).save('vn_prev.png')
