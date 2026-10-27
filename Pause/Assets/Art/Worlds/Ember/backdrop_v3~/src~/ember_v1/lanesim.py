"""Local replica of WorldBackdropTest.Composite + CheckReadability for Ember (static layers only).
usage: lanesim.py DIR [VB1 VB2 VB3 VB4]   DIR/v{n}/{sky,far,mid,flow}.png"""
import sys, numpy as np
from PIL import Image
VB=[None,.6,.35,.7,.55]; SH={'sky':.6,'far':.8,'mid':1.0,'flow':1.0}
def lin(c): return np.where(c<=.03928,c/12.92,((c+.055)/1.055)**2.4)
def rel(c): return .2126*lin(c[...,0])+.7152*lin(c[...,1])+.0722*lin(c[...,2])
def drawn(path,v,l,vb):
    a=np.asarray(Image.open(path).convert('RGBA')).astype(np.float32)/255
    lift=max(.3,1+(vb[v]-1)*SH[l]) if abs(vb[v]-1)>1e-4 else 1.0
    rgb=a[...,:3]; m=rgb.max(2); k=np.where(m>1e-4,(1-np.power(np.clip(1-m,0,1),lift))/np.maximum(m,1e-4),0)
    return rgb*k[...,None],a[...,3]
def run(d,vb):
    out={}
    for v in (1,2,3,4):
        W,H=270,600
        comp=None
        for l in ('sky','far','mid','flow'):
            rgb,al=drawn(f'{d}/v{v}/{l}.png',v,l,vb)
            if l=='flow': al=al*.85
            ys=(np.arange(H)*rgb.shape[0]//H); xs=(np.arange(W)*rgb.shape[1]//W)
            rgb=rgb[ys][:,xs]; al=al[ys][:,xs]
            comp=np.dstack([rgb,np.ones((H,W))])[...,:3] if comp is None else comp*(1-al[...,None])+rgb*al[...,None]
        lane=comp[::2,int(W*.2):int(W*.8):2].reshape(-1,3)
        rl=np.sort(rel(lane)); vl=np.sort(lane.max(1)); 
        res=dict(laneMedRel=round(float(rl[len(rl)//2]),4),laneV90=round(float(vl[int(len(vl)*.9)]),3),laneChroma=round(float((lane.max(1)-lane.min(1)).mean()),3))
        for l in ('sky','far','mid'):
            rgb,al=drawn(f'{d}/v{v}/{l}.png',v,l,vb); px=rgb.reshape(-1,3)[::3]; aa=al.reshape(-1)[::3]
            res[l]=(round(float((aa*(.2126*px[:,0]+.7152*px[:,1]+.0722*px[:,2])).sum()/aa.sum()),3), round(float(np.percentile(rgb.max(2),90)),3), round(float((aa*(px.max(1)-px.min(1))).sum()/aa.sum()),3))
        out[v]=res
    return out
if __name__=='__main__':
    vb=[None]+[float(x) for x in sys.argv[2:6]] if len(sys.argv)>2 else VB
    for v,r in run(sys.argv[1],vb).items(): print(v,r)
