"""usage: ecsheet.py OLDDIR NEWDIR OUT.png vb_old(4) vb_new(4) -- composite AS DRAWN (test/game grade), old row over new row"""
import sys, numpy as np
from PIL import Image
sys.path.insert(0,'.')
from lanesim import drawn
old,new,out=sys.argv[1:4]; vo=[None]+[float(x) for x in sys.argv[4:8]]; vn=[None]+[float(x) for x in sys.argv[8:12]]
def comp(d,v,vb):
    c=None
    for l in ('sky','far','mid','flow'):
        rgb,al=drawn(f'{d}/v{v}/{l}.png',v,l,vb)
        if l=='flow': al=al*.85
        c=np.dstack([rgb[...,:3]])*1.0 if c is None else c*(1-al[...,None])+rgb*al[...,None]
    return Image.fromarray(np.uint8(np.clip(c,0,1)*255)).resize((256,512),Image.Resampling.NEAREST)
sheet=Image.new('RGB',(4*262,2*518+0),(24,24,24))
for i,v in enumerate((1,2,3,4)):
    sheet.paste(comp(old,v,vo),(i*262,0)); sheet.paste(comp(new,v,vn),(i*262,518))
sheet.save(out)
