import sys,glob,os,colorsys,json
import numpy as np
from PIL import Image
ART='/Users/sina/Developer/Pause/.claude/worktrees/enemy-palettes/Pause/Assets/Art/'
def cells(path,cell,idx):
    im=Image.open(path).convert('RGBA'); a=np.array(im)
    return [a[:,i*cell:(i+1)*cell] for i in idx if (i+1)*cell<=a.shape[1]]
def rgb2hsv(rgb):
    r,g,b=[rgb[:,i]/255. for i in range(3)]
    mx=rgb.max(1)/255.; mn=rgb.min(1)/255.; d=mx-mn
    h=np.zeros(len(rgb))
    m=d>1e-6
    rc=np.where(m,(mx-r)/np.where(m,d,1),0);gc=np.where(m,(mx-g)/np.where(m,d,1),0);bc=np.where(m,(mx-b)/np.where(m,d,1),0)
    h=np.where(r==mx,bc-gc,np.where(g==mx,2+rc-bc,4+gc-rc))
    h=(h/6)%1.0
    s=np.where(mx>0,d/np.where(mx>0,mx,1),0)
    return h*360,s,mx
def world_pixels(w,which=('idle',)):
    """returns dict name-> Nx3 array of opaque pixels"""
    out={}
    if 'idle' in which:
        for p in sorted(glob.glob(ART+f'Resources/Enemies/{w}_*.png')):
            n=os.path.basename(p)[:-4]
            im=Image.open(p); cs=im.height
            px=[c[c[...,3]>200][:,:3] for c in cells(p,cs,[0,1,2,3])]
            out[n]=np.concatenate(px)
    if 'elite' in which:
        for p in sorted(glob.glob(ART+f'Resources/Elites/{w.capitalize()}/*.png')):
            n=os.path.basename(p)[:-4]
            if any(n.endswith(s) for s in('_parked','_liftoff','_death')):continue
            cs=Image.open(p).height
            c=cells(p,cs,[0,1])
            out['E:'+n]=np.concatenate([x[x[...,3]>200][:,:3] for x in c])
    if 'boss' in which:
        p=ART+f'Resources/Bosses/{w.capitalize()}.png'
        if os.path.exists(p):
            c=cells(p,384,[0,1,2,3])
            out['BOSS']=np.concatenate([x[x[...,3]>200][:,:3] for x in c])
    return out
def mines(w):
    pass
def stats(rgb):
    h,s,v=rgb2hsv(rgb.astype(float))
    chrom=(s>0.25)&(v>0.25)
    return h,s,v,chrom
def kmeans(X,k=6,it=25,seed=0):
    rng=np.random.default_rng(seed)
    if len(X)>40000: X=X[rng.choice(len(X),40000,replace=False)]
    X=X.astype(float); C=X[rng.choice(len(X),k,replace=False)]
    for _ in range(it):
        d=((X[:,None]-C[None])**2).sum(2);l=d.argmin(1)
        for i in range(k):
            if (l==i).any(): C[i]=X[l==i].mean(0)
    cnt=np.bincount(l,minlength=k)/len(X)
    o=np.argsort(-cnt);return C[o],cnt[o]
def joint_hist(rgb):
    h,s,v,ch=stats(rgb)
    # weight equal per pixel; bins: hue 24 (chromatic) x value 3, plus achromatic by value 4
    H=np.zeros((24,3));A=np.zeros(4)
    hb=(h[ch]//15).astype(int)%24; vb=np.minimum((v[ch]*3).astype(int),2)
    np.add.at(H,(hb,vb),1)
    ac=~ch; ab=np.minimum((v[ac]*4).astype(int),3); np.add.at(A,ab,1)
    t=H.sum()+A.sum(); return H/t,A/t
def smooth_overlap(a,b):
    # hue-circular smoothing then histogram intersection
    def sm(H):
        return (np.roll(H,1,0)+2*H+np.roll(H,-1,0))/4
    Ha,Aa=a;Hb,Ab=b
    return np.minimum(sm(Ha),sm(Hb)).sum()+np.minimum(Aa,Ab).sum()
def hexc(c): return '#%02x%02x%02x'%tuple(int(x) for x in c)
if __name__=='__main__':
    worlds=['space','frost','verdant','ember','tide','storm','desert']
    res={}
    allw={}
    for w in worlds:
        d=world_pixels(w,('idle','elite','boss'))
        allw[w]=d
    summary={}
    for w in worlds:
        d=allw[w]
        idle=np.concatenate([v for k,v in d.items() if not k.startswith('E:') and k!='BOSS' and 'mine' not in k])
        h,s,v,ch=stats(idle)
        chh=h[ch]
        # circular mean weighted
        mh=np.degrees(np.arctan2(np.sin(np.radians(chh)).mean(),np.cos(np.radians(chh)).mean()))%360
        hist=np.histogram(chh,bins=12,range=(0,360))[0]/max(1,len(chh))
        C,cnt=kmeans(idle,6)
        summary[w]=dict(n=len(idle),chromFrac=ch.mean(),meanHue=mh,meanS=s[ch].mean(),meanV=v.mean(),meanVchrom=v[ch].mean(),
            hist12=[round(x,2) for x in hist],top=[(hexc(c),round(float(f),2)) for c,f in zip(C,cnt)])
    for w in worlds:
        sm=summary[w];print(w,{k:(round(x,2) if isinstance(x,float) else x) for k,x in sm.items() if k not in('top',)});print('   top',sm['top'])
    J={w:joint_hist(np.concatenate([v for k,v in allw[w].items() if not k.startswith('E:') and k!='BOSS'])) for w in worlds}
    print('\noverlap (idle roster):')
    print('        '+' '.join(f'{w[:7]:>7}' for w in worlds))
    for a in worlds:
        print(f'{a[:7]:>7} '+' '.join(f'{smooth_overlap(J[a],J[b]):7.2f}' for b in worlds))
    # per-enemy
    json.dump(summary,open('summary.json','w'),default=float,indent=1)
