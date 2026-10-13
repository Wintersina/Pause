from PIL import Image
import numpy as np, sys
R='/Users/sina/Developer/Pause-rockanim/Pause/Assets/Art/Resources/Enemies/'
src=np.array(Image.open(R+'verdant_rock_spore.png'))
f0=src[:,:192].copy()
VENTS=[(53,105,15),(104,98,24),(76,145,15),(132,140,16)]
yy,xx=np.mgrid[0:192,0:192]
def lit(a):
    r,g,b=a[...,0].astype(int),a[...,1].astype(int),a[...,2].astype(int)
    return (a[...,3]>200)&(g>140)&(g-b>50)&(g+25>=r)
L=lit(f0)
masks=[]
for cx,cy,r in VENTS:
    masks.append(L&((xx-cx)**2+(yy-cy)**2<=r*r))
region=np.zeros((192,192),bool)
for m in masks: region|=m
LID=np.array([58,84,18,255],np.uint8); LID2=np.array([104,138,28,255],np.uint8)
def frame(open_frac,flare=0):
    f=f0.copy()
    for (cx,cy,r),m in zip(VENTS,masks):
        ys,xs=np.where(m)
        top,bot=ys.min(),ys.max(); c=(top+bot)/2; half=(bot-top)/2*open_frac
        for y,x in zip(ys,xs):
            d=abs(y-c)
            if open_frac<=0: edge=1.0
            else: edge=d-half
            if open_frac>0 and d<=half: 
                continue
            # lid
            f[y,x]=LID2 if (open_frac>0 and d-half<1.5) or (open_frac<=0 and abs(y-c)<=1) else LID
            if open_frac<=0 and abs(y-c)<=0.6: f[y,x]=np.array([150,200,40,255],np.uint8)  # closed seam glint
    if flare:
        for m in masks:
            sel=m
            f[sel,0]=np.clip(f[sel,0]*1.0+40,0,255); f[sel,1]=255; f[sel,2]=np.clip(f[sel,2]+60,0,255)
    return f
seq=[frame(1.0),frame(0.45),frame(0.0),frame(0.45),frame(0.0),frame(1.0,flare=1)]
out=src.copy()
for i,f in enumerate(seq): out[:,i*192:(i+1)*192]=f
Image.fromarray(out).save('spore_new.png')
np.save('spore_region.npy',region)
