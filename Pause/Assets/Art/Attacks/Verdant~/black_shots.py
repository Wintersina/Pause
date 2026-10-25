"""Verdant hostile shots -> black (user, Oct 25): the leaf shard (verdant_attack_shots cells 2,3) and the Bloom Queen's three attack shots (Verdant_shots.png stays pale mint, user Oct 25) become dark brown root / bark / thorn art (near-black brown, tan bark ridges, a small ochre accent). Run on the ORIGINAL art (git history). Deterministic luminance-ramp remap, alpha and layout untouched.
usage: blacken.py ROOT_ASSETS OUTDIR [variant]
"""
import sys, numpy as np
from PIL import Image
# brown root / bark ramp (t = luminance relative to the cell's 98th percentile): near-black brown -> bark -> tan -> a small dry-ochre accent
STOPS=[(0.0,(12,8,5)),(.45,(28,19,11)),(.72,(52,36,21)),(.90,(84,59,34)),(1.0,(112,80,46))]
KEY=(9,6,4)
OCHRE=(222,182,116)   # a dry-ochre catch-light, only on the lit (upper-left) edge of the bright bark
# (file, cell w, cell h, columns, cell indices to convert)
JOBS=[('Art/Resources/Attacks/Verdant/verdant_attack_shots.png',128,128,8,[2,3]),   # the elites' leaf shard (both frames)
      ('Art/Resources/Bosses/Verdant_attackshots.png',128,128,2,[0,1]),             # Bloom Queen attack 1 (lime shard): brown root
      ('Art/Resources/Bosses/Verdant_attackshots.png',128,128,2,[2,3,4,5])]         # attack 2 (spore cloud), 3 (sap drop): stay green, get black internals (INNER)
INNER=[('Art/Resources/Bosses/Verdant_attackshots.png',[2,3,4,5])]   # cells that keep their art and only gain a black heart
INNER_SHARE=.18; INNER_KEY=(11,18,8)   # the darkest 18 % of the interior (eroded 2 px) becomes one near-black: two tone, the lime stays dominant
def lum(rgb): return (.2126*rgb[...,0]+.7152*rgb[...,1]+.0722*rgb[...,2])/255
def conv(cell, variant):
    a=cell[...,3]; m=a>0
    L=lum(cell[...,:3].astype(float)); ref=np.percentile(L[a>128],98) if (a>128).any() else 1
    t=np.clip(L/max(ref,1e-3),0,1)
    xs=[p for p,_ in STOPS]; cs=np.array([c for _,c in STOPS],float)
    body=np.stack([np.interp(t,xs,cs[:,k]) for k in range(3)],-1)
    # catch-light: bright bark pixels whose upper or left neighbour is empty or dark
    Lp=np.pad(t,1); Ap=np.pad(a,1)
    lit=(t>.9)&(a>128)&(((Ap[1:-1,:-2]<128)|(Lp[1:-1,:-2]<.45))|((Ap[:-2,1:-1]<128)|(Lp[:-2,1:-1]<.45)))
    # a subtle near-black keyline: the shot's own outermost opaque pixels (alpha untouched), except where the catch-light sits
    Ab=np.pad(a<128,1,constant_values=True)
    edge=(a>=128)&(Ab[1:-1,:-2]|Ab[1:-1,2:]|Ab[:-2,1:-1]|Ab[2:,1:-1])
    body[edge&~lit]=KEY
    body[lit]=OCHRE
    out=cell.copy(); out[...,:3]=np.where(m[...,None],body,np.array(STOPS[0][1],float)).round().astype('uint8'); return out
def inner(cell):
    a=cell[...,3]; m=a>=128
    for _ in range(2):
        p=np.pad(m,1); m=p[1:-1,1:-1]&p[:-2,1:-1]&p[2:,1:-1]&p[1:-1,:-2]&p[1:-1,2:]
    out=cell.copy()
    if m.sum()<20: return out
    L=lum(cell[...,:3].astype(float)); th=np.sort(L[m])[int(m.sum()*INNER_SHARE)]
    dark=m&(L<=th); out[dark,:3]=INNER_KEY; return out
def run(root,outdir,variant="brown"):
    import os
    outs={}
    for f,cw,ch,cols,idx in JOBS:
        im=np.array(Image.open(root+f).convert('RGBA'))
        out=outs.setdefault(f,im.copy())
        for i in idx:
            r,c=divmod(i,cols); sl=(slice(r*ch,(r+1)*ch),slice(c*cw,(c+1)*cw))
            inn=any(f==jf and i in ids for jf,ids in INNER)
            out[sl]=inner(im[sl]) if inn else conv(im[sl],variant)
    for f,out in outs.items():
        p=outdir+f; os.makedirs(os.path.dirname(p),exist_ok=True); Image.fromarray(out).save(p)
if __name__=='__main__': run(sys.argv[1],sys.argv[2],sys.argv[3] if len(sys.argv)>3 else "brown")
