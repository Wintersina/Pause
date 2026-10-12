import glob,numpy as np
from PIL import Image
ROOT='/Users/sina/Developer/Pause/.claude/worktrees/enemy-palettes/Pause/Assets/Art/Resources/Enemies/'
B=(207,224,255);SOLID=96
def dil(m):
    o=m.copy();p=np.pad(m,1);h,w=m.shape
    for dy in(-1,0,1):
        for dx in(-1,0,1):
            if (dx==0)!=(dy==0):o|=p[1+dy:1+dy+h,1+dx:1+dx+w]
    return o
for p in sorted(glob.glob(ROOT+'storm_*.png')+glob.glob(ROOT+'Death/storm_*.png')):
    if 'mine' in p:continue
    a=np.array(Image.open(p).convert('RGBA'));c=a.shape[0];o=a.copy()
    for i in range(a.shape[1]//c):
        sl=slice(i*c,(i+1)*c);al=a[:,sl,3];solid=al>=SOLID
        ring=dil(solid)&~solid&(al<SOLID)
        ys,xs=np.nonzero(ring)
        sub=o[:,sl]
        for y,x in zip(ys,xs):
            k=(x*7+y*13+i*5)%3
            d=(0,-9,-18)[k]
            sub[y,x]=(B[0]+d,B[1]+d,B[2]+(d//3),255)
    Image.fromarray(o).save(p)
print('ok')
