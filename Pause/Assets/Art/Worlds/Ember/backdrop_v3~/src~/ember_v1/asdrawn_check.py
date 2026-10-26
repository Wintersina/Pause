import sys, numpy as np
from PIL import Image
VB={1:.6,2:.35,3:.7,4:.55}; SH={'sky':.6,'far':.8,'mid':1.0}; CAP={'sky':(.16,.18,.25),'far':(None,.18,.25),'mid':(None,.18,.25)}
def asdrawn(path,v,l):
    a=np.asarray(Image.open(path).convert('RGBA')).astype(np.float32)/255
    k=1.0; vb=VB[v]; lift=max(.3,k*(1+(vb-1)*SH[l]))
    rgb=a[...,:3]; vmax=rgb.max(2); curve=1-np.power(np.clip(1-vmax,0,1),lift)
    kk=np.where(vmax>1e-4,curve/np.maximum(vmax,1e-4),0); o=rgb*kk[...,None]
    return o,a[...,3],vmax
def measure(path,v,l):
    o,al,_=asdrawn(path,v,l); o=o.reshape(-1,3)[::3]; al=al.reshape(-1)[::3]
    lum=(al*(.2126*o[:,0]+.7152*o[:,1]+.0722*o[:,2])).sum()/al.sum(); ch=(al*(o.max(1)-o.min(1))).sum()/al.sum()
    _,_,vm=asdrawn(path,v,l); 
    return round(float(lum),3),round(float(ch),3)
if __name__=='__main__':
    d=sys.argv[1]
    for v in (1,2,3,4):
        print(v,{l:measure(f'{d}/v{v}_{l}.png',v,l) for l in ('sky','far','mid')})
