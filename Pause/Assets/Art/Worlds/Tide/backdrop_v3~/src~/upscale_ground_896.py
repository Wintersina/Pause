import numpy as np, sys, os
from PIL import Image, ImageFilter
R='/Users/sina/Developer/Pause-worktrees/tide-896/Pause/Assets/Art/Backgrounds/Resources/Worlds/Tide/Backdrop3'
O='/private/tmp/claude-501/-Users-sina-Developer-Pause/bdb30dea-5c57-4096-8366-274437827548/scratchpad/tide-896/new'
W,H=896,1792; PAD=64
def up(path, amt):
    a=np.asarray(Image.open(path).convert('RGBA')).astype(np.float32)/255
    h,w=a.shape[:2]
    pm=a.copy(); pm[...,:3]*=pm[...,3:]
    # vertical wrap pad, horizontal edge pad
    p=np.concatenate([pm[-PAD:],pm,pm[:PAD]],0)
    p=np.pad(p,((0,0),(PAD,PAD),(0,0)),mode='edge')
    chans=[Image.fromarray(p[...,c]).resize((int(round(p.shape[1]*W/w)),int(round(p.shape[0]*H/h))),Image.LANCZOS) for c in range(4)]
    # PIL float mode F ok
    s=np.stack([np.asarray(c) for c in chans],-1)
    px=int(round(PAD*W/w)); 
    s=s[px:px+H, px:px+W]
    rgb=s[...,:3]; al=np.clip(s[...,3:],0,1)
    # unsharp on premult rgb
    img=[Image.fromarray((np.clip(rgb[...,c],0,1)*255).astype(np.uint8)) for c in range(3)]
    bl=np.stack([np.asarray(i.filter(ImageFilter.GaussianBlur(1.6))).astype(np.float32)/255 for i in img],-1)
    rgb=rgb+amt*(rgb-bl)
    rgb=np.clip(rgb,0,al.clip(1e-6,1) if False else 1)
    out=np.concatenate([np.where(al>1e-4,rgb/np.maximum(al,1e-4),0),al],-1)
    out[-1]=out[0]   # the old 512x1024 tiles ended on a copy of their first row (seam diff 0); keep that
    return Image.fromarray((np.clip(out,0,1)*255+.5).astype(np.uint8),'RGBA')
if __name__=='__main__':
    amt=float(sys.argv[1]); 
    for v in ['v1','v2','v3','v4']:
        os.makedirs(f'{O}/{v}',exist_ok=True)
        for n in ['sky','far','mid','flow']:
            up(f'{R}/{v}/{n}.png',0.0 if n=='flow' else amt).save(f'{O}/{v}/{n}.png')
