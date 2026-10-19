#!/usr/bin/env python3
"""Desert hull-light overlays and final review sheet. Art only."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter
HERE=Path(__file__).resolve().parent; ELITES=HERE.parents[1]; CELL=384
NAMES=('dune_stalker','caravan_hauler','glass_cannon','lens_matriarch','mirror_bastion','scorpion_walker')
def hsv(rgb):
 r,g,b=[v/255 for v in rgb]; hi=max(r,g,b); lo=min(r,g,b); return hi,(hi-lo)/max(hi,1e-6)
def points(cell):
 pix=cell.load(); box=cell.getchannel('A').point(lambda v:255 if v>80 else 0).getbbox(); out=[]
 if not box:return out
 x0,y0,x1,y1=box; c=[]
 for y in range(y0+8,y1-7,4):
  for x in range(x0+8,x1-7,4):
   r,g,b,a=pix[x,y]
   if a>=150:
    v,s=hsv((r,g,b))
    if s>.18 and v>.34:c.append((v*(.35+s),x,y))
 c.sort(reverse=True)
 for _,x,y in c:
  if all((x-a)**2+(y-b)**2>=20**2 for a,b in out):out.append((x,y))
  if len(out)>=18:break
 return sorted(out,key=lambda p:(p[1],p[0]))
def lights(ps,phase,strength,damaged=False):
 glow=Image.new('RGBA',(CELL,CELL)); core=Image.new('RGBA',(CELL,CELL)); gd=ImageDraw.Draw(glow); cd=ImageDraw.Draw(core)
 for i,(x,y) in enumerate(ps):
  beacon=i<3; emitter=y>CELL*.68; on=((phase-i)%max(1,len(ps))<3 or (beacon and phase in (1,2)) or (emitter and phase in (2,3,4,5)))
  if damaged:on=((i+phase)%3!=0) and (i%4!=0 or phase==0)
  col=(255,224,100) if beacon or i%5==0 else (255,181,48)
  if not on:col=tuple(round(v*.38) for v in col)
  k=(1 if on else .24)*strength; gd.rectangle((x-4,y-4,x+4,y+4),fill=(*col,round(38*k))); gd.rectangle((x-2,y-2,x+2,y+2),fill=(*col,round(100*k)))
  if on:cd.rectangle((x-1,y-1,x+1,y+1),fill=(*col,round(225*k)))
 return Image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(1.0)),core)
def clean(im):
 p=im.load()
 for y in range(im.height):
  for x in range(im.width):
   r,g,b,a=p[x,y]
   if a>20 and r>180 and b>150 and g<105 and g*2<min(r,b):p[x,y]=(150,86,42,a)
   elif a>20 and g>80 and g>r*1.04 and g>b*1.02:p[x,y]=(145,113,55,a)
 return im
def build(name):
 src=Image.open(ELITES/f'Desert/desert_elite_{name}.png').convert('RGBA'); out=Image.new('RGBA',src.size); prev=Image.new('RGBA',src.size); ps=points(src.crop((0,0,CELL,CELL))); assert len(ps)>=6,name
 for f in range(src.width//CELL):
  phase,strength,damaged=(0,.14,False) if f==0 else (1,.28,False) if f==1 else (2,.64,False) if f==2 else (7,.54,True) if f==6 else ((f-3)%8,1,False)
  ov=lights(ps,phase,strength,damaged); out.alpha_composite(ov,(f*CELL,0)); prev.alpha_composite(clean(Image.alpha_composite(src.crop((f*CELL,0,(f+1)*CELL,CELL)),ov)),(f*CELL,0))
 out.save(HERE/f'desert_elite_{name}_lights.png'); prev.save(HERE/f'desert_elite_{name}_preview_lights.png'); fs=[prev.crop((i*CELL,0,(i+1)*CELL,CELL)) for i in (3,4,5,7,8,0)]; bg=Image.new('RGBA',(CELL,CELL),(9,20,22,255)); rgb=[Image.alpha_composite(bg,f).convert('RGB') for f in fs]; rgb[0].save(HERE/f'desert_elite_{name}_lights.gif',save_all=True,append_images=rgb[1:],duration=130,loop=0,disposal=2)
def sheet():
 sites=['buried_bunker','crawler_hangar','mirror_array_silo','canyon_dock']; cw,ch=360,300; s=Image.new('RGBA',(cw*4,ch*3),(9,20,22,255)); d=ImageDraw.Draw(s)
 for i,n in enumerate(sites):
  p=HERE/f'{n}_launch_v2.png'
  if p.exists():im=Image.open(p).crop((0,0,768,768)); im.thumbnail((cw-20,ch-45),Image.Resampling.LANCZOS); s.alpha_composite(im,(i*cw+(cw-im.width)//2,34)); d.text((i*cw+8,8),n.replace('_',' '),fill=(255,211,92,255))
 for j,n in enumerate(NAMES):
  im=Image.open(HERE/f'desert_elite_{n}_preview_lights.png').crop((3*CELL,0,4*CELL,CELL)); im.thumbnail((cw-20,ch-45),Image.Resampling.NEAREST); x=(j%4)*cw; y=ch+(j//4)*ch; s.alpha_composite(im,(x+(cw-im.width)//2,y+34)); d.text((x+8,y+8),n.replace('_',' ')+' lit',fill=(255,211,92,255))
 s.convert('RGB').save(HERE/'contact_sheet.png')
if __name__=='__main__':
 for n in NAMES:build(n)
 sheet()
