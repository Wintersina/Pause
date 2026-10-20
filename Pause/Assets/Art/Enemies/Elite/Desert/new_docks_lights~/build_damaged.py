#!/usr/bin/env python3
"""Six-frame Desert damaged burn loops; hull is restored from parked cell."""
from pathlib import Path
from PIL import Image,ImageDraw
import importlib.util,random
HERE=Path(__file__).resolve().parent;ELITES=HERE.parents[1];CELL=384;FRAMES=6
NAMES=('dune_stalker','caravan_hauler','glass_cannon','lens_matriarch','mirror_bastion','scorpion_walker')
NOZZLES={'dune_stalker':[(162,279),(221,279)],'caravan_hauler':[(129,270),(192,270),(253,270)],'glass_cannon':[(151,280),(232,280)],'lens_matriarch':[(168,279),(215,279)],'mirror_bastion':[(138,270),(192,270),(246,270)],'scorpion_walker':[(164,284),(220,284)]}
COL=((160,73,30),(232,142,44),(255,224,128))
sp=importlib.util.spec_from_file_location('lights',HERE/'build_art.py');L=importlib.util.module_from_spec(sp);sp.loader.exec_module(L)
def flame(d,x,y,n,w,rng,dead=False):
 if dead:d.rectangle((x-3,y,x+3,y+3),fill=(*COL[0],170));return
 for row in range(n):
  if row>n*.45 and rng.randrange(5)==0:continue
  t=row/max(1,n-1);hw=max(1,round(w*(1-.72*t)));xx=x+rng.choice((-1,0,0,1));c=COL[0] if row<2 else COL[1] if row<n*.7 else COL[2];d.rectangle((xx-hw,y+row,xx+hw,y+row+2),fill=(*c,220 if row<n*.65 else 170))
def build(n):
 src=L.clean(Image.open(ELITES/f'Desert/desert_elite_{n}.png').convert('RGBA'));park=src.crop((0,0,CELL,CELL));dam=src.crop((6*CELL,0,7*CELL,CELL)).copy()
 for x,y in NOZZLES[n]:dam.paste(park.crop((x-14,max(0,y-18),x+15,CELL)),(x-14,max(0,y-18)))
 fs=[]
 for f in range(FRAMES):
  im=dam.copy();d=ImageDraw.Draw(im)
  for i,(x,y) in enumerate(NOZZLES[n]):flame(d,x,y+4,(12,22,15,24,13,20)[(f*2+i*3)%6],8+(i%2)*2,random.Random(1700+f*41+i),dead=(f+i*2)%6==2)
  fs.append(im)
 strip=Image.new('RGBA',(CELL*6,CELL))
 for i,im in enumerate(fs):strip.alpha_composite(im,(i*CELL,0))
 strip.save(HERE/'damaged'/f'desert_elite_{n}_damaged.png');ps=L.points(src.crop((0,0,CELL,CELL)));ov=Image.new('RGBA',(CELL*6,CELL))
 for f in range(6):ov.alpha_composite(L.lights(ps,f+7,.54,True),(f*CELL,0))
 ov.save(HERE/'damaged'/f'desert_elite_{n}_damaged_lights.png');prev=Image.new('RGBA',strip.size)
 for i,im in enumerate(fs):prev.alpha_composite(Image.alpha_composite(im,ov.crop((i*CELL,0,(i+1)*CELL,CELL))),(i*CELL,0))
 prev=L.clean(prev);prev.save(HERE/'damaged'/f'desert_elite_{n}_damaged_preview.png');g=[prev.crop((i*CELL,0,(i+1)*CELL,CELL)) for i in range(6)];bg=Image.new('RGBA',(CELL,CELL),(9,20,22,255));rgb=[Image.alpha_composite(bg,L.clean(f)).convert('RGB') for f in g];rgb[0].save(HERE/'damaged'/f'desert_elite_{n}_damaged.gif',save_all=True,append_images=rgb[1:],duration=130,loop=0,disposal=2);return strip
if __name__=='__main__':
 rows=[(n,build(n)) for n in NAMES];s=Image.new('RGBA',(1440,960),(9,20,22,255));d=ImageDraw.Draw(s)
 for i,(n,st) in enumerate(rows):
  x=(i%3)*480;y=(i//3)*480;d.text((x+8,y+8),n.replace('_',' ')+' damaged',fill=(255,211,92,255))
  for f in range(6):s.alpha_composite(st.crop((f*CELL,0,(f+1)*CELL,CELL)).resize((145,145),Image.Resampling.NEAREST),(x+8+(f%3)*155,y+42+(f//3)*165))
 s.convert('RGB').save(HERE/'damaged/contact_sheet.png')
