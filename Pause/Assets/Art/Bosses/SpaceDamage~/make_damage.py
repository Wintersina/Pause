from PIL import Image, ImageDraw, ImageChops
import random, math
from pathlib import Path
P=Path(__file__).parent
atlas=Image.open(P/'../../Resources/Bosses/Space.png').convert('RGBA')
base=[atlas.crop((0,4*384,384,5*384)) for _ in (0,1)]
INK='#05060c'; DARK='#0b0f15'; SHADOW='#1a2129'; MID='#2f343b'; STEEL='#4b5059'; LIGHT='#7a7c82'; BRIGHT='#b0b4b6'; BRASS='#8d643c'; CYAN='#0bd0f6'; PINK='#eb3aa4'; WHITE='#f5fdfd'

def poly(d,p,fill,outline=INK,width=1):
 d.polygon(p,fill=fill)
 if outline:d.line(p+[p[0]],fill=outline,width=width,joint='curve')
def line(d,pts,col,width=1):d.line(pts,fill=col,width=width,joint='curve')
def rivets(d,pts):
 for x,y in pts:
  d.rectangle((x,y,x+2,y+2),fill=INK);d.point((x+1,y),fill=LIGHT)
def scratch(d,pts):
 line(d,pts,INK,3);line(d,pts,LIGHT,1)
def scorch(d,cx,cy,rx,ry,seed):
 rng=random.Random(seed)
 for i in range(38):
  x=cx+rng.randrange(-rx,rx+1);y=cy+rng.randrange(-ry,ry+1)
  if ((x-cx)/rx)**2+((y-cy)/ry)**2>1:continue
  k=rng.randrange(2,7);d.line((x,y,x+rng.randrange(-2,3),y+k),fill=(6,7,14,rng.randrange(85,190)),width=rng.choice([1,2,3]))
def breach(im,pts,seed):
 d=ImageDraw.Draw(im,'RGBA');poly(d,pts,(3,5,12,255),INK,3)
 rng=random.Random(seed)
 xs=[x for x,y in pts];ys=[y for x,y in pts];l,t,r,b=min(xs),min(ys),max(xs),max(ys)
 for i in range(28):
  x=rng.randrange(l+3,r-3);y=rng.randrange(t+3,b-3)
  if not point_in_poly(x,y,pts):continue
  col=rng.choice([SHADOW,MID,'#162e3c','#0b4f7a','#7d5139'])
  d.rectangle((x,y,x+rng.randrange(2,7),y+rng.randrange(1,4)),fill=col)
 for i in range(4):
  x=l+8+i*5;y=t+7+(i%2)*3
  line(d,[(x,y),(x+3,y+6),(x+2,y+11)],BRASS if i%2 else CYAN,2)
 for i in range(12):
  j=rng.randrange(len(pts));x,y=pts[j]; d.rectangle((x-1,y-1,x+1,y+1),fill=LIGHT)

def point_in_poly(x,y,p):
 inside=False;j=len(p)-1
 for i in range(len(p)):
  xi,yi=p[i];xj,yj=p[j]
  if (yi>y)!=(yj>y) and x<(xj-xi)*(y-yi)/(yj-yi)+xi:inside=not inside
  j=i
 return inside

def damage(im,stage,variant):
 im=im.copy();d=ImageDraw.Draw(im,'RGBA')
 if variant:
  # The alternate idle frame changes only local light and one loosened chip.
  d.rectangle((187,219,190,222),fill='#b72a79')
  d.rectangle((316,282,321,285),fill='#c93183')
  d.rectangle((258,145,260,146),fill=LIGHT)
 # The accumulating gouge on the port upper armour follows existing plate seams.
 scratch(d,[(115,151),(122,146),(130,149),(139,141)])
 scratch(d,[(274,159),(281,163),(286,171)])
 scorch(d,120,151,20,15,10)
 poly(d,[(250,143),(265,140),(273,146),(267,157),(251,160),(246,152)],MID,INK,2)
 line(d,[(252,144),(260,147),(267,144)],LIGHT,1)
 line(d,[(254,154),(262,150),(267,157)],INK,2)
 rivets(d,[(249,148),(268,151)])
 # One failed core segment remains a small, uneven dark sector at stage one.
 poly(d,[(177,215),(184,211),(186,219),(181,225),(175,222)],'#57203f',None)
 if stage>=2:
  scorch(d,103,184,35,30,22)
  scorch(d,294,198,29,24,23)
  poly(d,[(105,128),(119,122),(132,127),(130,140),(121,146),(108,143)],SHADOW,INK,2)
  poly(d,[(109,129),(124,126),(126,135),(117,140),(107,137)],'#0a1118',INK,1)
  line(d,[(108,134),(118,132),(124,139)],CYAN,2)
  line(d,[(115,126),(114,137),(121,142)],BRASS,2)
  # Two sheared brass stays with exposed fasteners.
  poly(d,[(98,159),(104,161),(107,169),(104,176),(99,172)],'#a27845',INK)
  line(d,[(105,177),(111,182)],'#5c392a',2)
  rivets(d,[(98,162),(109,181)])
  poly(d,[(274,201),(292,197),(300,208),(289,217),(279,215)],SHADOW,INK,2)
  for pts,col in [([(276,204),(284,204),(288,210),(294,210)],CYAN), ([(279,211),(284,213),(289,208),(296,212)],PINK)]:line(d,pts,col,2)
  # Right nozzle misfires; the original tapered outer silhouette stays put.
  poly(d,[(306,274),(319,279),(328,275),(330,287),(319,294),(307,287)],SHADOW,INK,2)
  line(d,[(310,278),(321,281),(328,278)],LIGHT,1)
  d.rectangle((315,287,321,290),fill='#813053')
 if stage>=3:
  scorch(d,278,175,44,42,33)
  scorch(d,126,202,37,47,34)
  breach(im,[(251,163),(260,155),(276,158),(284,168),(277,176),(284,188),(272,198),(259,194),(251,185),(244,182)],35)
  d=ImageDraw.Draw(im,'RGBA')
  line(d,[(244,166),(250,159),(255,162),(259,156)],LIGHT,2)
  line(d,[(282,170),(278,178),(286,183)],LIGHT,2)
  # Port pod has split fairing, but retains the old outline.
  poly(d,[(43,190),(55,183),(66,190),(70,211),(61,232),(48,229),(41,216)],SHADOW,INK,2)
  poly(d,[(46,193),(57,188),(62,196),(59,218),(50,224),(45,214)],MID,INK,1)
  line(d,[(46,203),(58,211),(68,210)],LIGHT,2)
  line(d,[(52,218),(59,208),(67,201)],INK,2)
  line(d,[(52,228),(58,238),(56,248)],BRASS,3)
  # Crack the narrow upper cockpit glass.
  line(d,[(185,150),(190,154),(187,159),(195,163),(198,170)],INK,3)
  line(d,[(186,150),(191,153),(188,158),(196,162)],BRIGHT,1)
  line(d,[(190,154),(196,151),(200,154)],LIGHT,1)
  # A dim, irregular core and damaged rim.
  poly(d,[(184,208),(194,206),(204,212),(209,222),(202,234),(188,235),(180,225)],'#551836',INK,2)
  poly(d,[(187,211),(198,210),(203,217),(200,227),(189,230),(184,221)],'#a51f68',None)
  d.rectangle((191,216,197,222),fill='#ff8cce' if variant==0 else '#d1418e')
  d.point((194,218),fill=WHITE if variant==0 else PINK)
 if stage>=4:
  scorch(d,188,145,49,38,43)
  scorch(d,310,243,31,42,44)
  breach(im,[(99,176),(108,169),(119,175),(123,190),(114,199),(108,209),(95,203),(91,191)],45)
  d=ImageDraw.Draw(im,'RGBA')
  line(d,[(93,178),(101,173),(108,174)],LIGHT,2)
  # Plate hanging into the space left by the breach.
  poly(d,[(121,190),(134,194),(139,209),(134,231),(127,240),(121,236),(126,214)],MID,INK,2)
  line(d,[(124,196),(133,201),(132,223)],LIGHT,1)
  line(d,[(121,188),(126,203)],BRASS,2)
  breach(im,[(213,247),(226,239),(237,244),(241,259),(234,270),(221,268),(212,259)],49)
  # Remove the original bright exhaust before drawing the failed engine.
  im.paste((0,0,0,0),(301,287,339,331))
  d=ImageDraw.Draw(im,'RGBA')
  # Dead right engine: replace plume with a cold broken nozzle, inside the footprint.
  poly(d,[(299,269),(311,268),(325,273),(334,285),(326,299),(316,304),(306,294)],DARK,INK,3)
  poly(d,[(308,276),(320,276),(327,285),(320,291),(309,287)],SHADOW,INK,1)
  line(d,[(312,278),(320,280),(325,285)],LIGHT,1)
  d.rectangle((315,292,321,295),fill='#542040')
  # Hanging pipe and fractured brass bracket.
  line(d,[(259,190),(267,205),(266,221),(272,230)],INK,5)
  line(d,[(259,190),(267,205),(266,221),(272,230)],'#7d5139',3)
  d.ellipse((269,228,274,233),fill=LIGHT)
  poly(d,[(241,130),(247,132),(250,143),(248,148),(243,143)],'#80553d',INK)
  line(d,[(249,148),(255,155)],LIGHT,1)
  # Guttering magenta core, with very little clean white left.
  poly(d,[(184,209),(198,209),(207,218),(204,231),(189,233),(180,223)],'#391226',INK,2)
  poly(d,[(187,215),(198,214),(202,219),(198,226),(188,227)],'#a51f68',None)
  d.rectangle((192,219,195 if variant==0 else 194,222),fill='#fb8bcf' if variant==0 else '#b93179')
 return im

body=Image.new('RGBA',(768,1536))
for s in range(1,5):
 for v in range(2):body.alpha_composite(damage(base[v],s,v),((v*384),(s-1)*384))
body.save(P/'Space_damage.png')

smoke_colors=['#23202e','#342b3b','#4d3d50','#675568']
def smoke_frame(k):
 im=Image.new('RGBA',(384,384));d=ImageDraw.Draw(im,'RGBA');rng=random.Random(900+k)
 for ox,oy,strength in [(111,174,1),(269,160,1),(308,264,1)]:
  for j in range(5):
   phase=(j*11+k*5)%54; y=oy-phase; x=ox+int(7*math.sin((phase+k*3)/13))+j*2-4
   radius=7+j//2+(phase//13)
   if x-radius<0 or y-radius<0:continue
   col=smoke_colors[min(3,j//2)]
   a=max(70,220-phase*2)
   puff=[(x-radius-2,y+2),(x-radius+1,y-radius//2),(x-3,y-radius//2-2),
         (x+2,y-radius//2),(x+radius-2,y-radius//3-2),(x+radius+2,y),
         (x+radius-1,y+radius//2),(x+3,y+radius//2+1),
         (x-3,y+radius//2-1),(x-radius,y+radius//3)]
   d.polygon(puff,fill=(*bytes.fromhex(col[1:]),a))
   d.line((x-radius+2,y-radius//3,x-2,y-radius//2-1,x+3,y-radius//2),fill=(103,85,112,max(35,a//3)),width=1)
  for j in range(3):
   x=ox+rng.randrange(-6,7);y=oy+rng.randrange(-7,5)
   d.rectangle((x,y,x+1,y+2),fill=(244,63,163,170-j*35))
 return im

def electric_frame(k):
 im=Image.new('RGBA',(384,384));d=ImageDraw.Draw(im,'RGBA');rng=random.Random(740+k)
 anchors=[(270,176),(113,190),(258,198),(132,158),(310,282)]
 for n in range(2+(k%3)):
  x,y=anchors[(n+k)%len(anchors)]; endx=x+rng.choice([-1,1])*rng.randrange(19,39);endy=y+rng.randrange(-23,23)
  pts=[(x,y)]
  for t in range(1,6):
   q=t/6;pts.append((round(x+(endx-x)*q+rng.randrange(-5,6)),round(y+(endy-y)*q+rng.randrange(-5,6))))
  pts.append((endx,endy))
  line(d,pts,(255,45,174,150),5);line(d,pts,(10,208,246,255),3);line(d,pts,(245,253,253,255),1)
  d.rectangle((x-1,y-1,x+1,y+1),fill=WHITE)
  for q in range(3):
   sx=endx+rng.randrange(-10,11);sy=endy+rng.randrange(-10,11)
   d.line((sx-2,sy,sx+2,sy),fill=CYAN,width=1);d.line((sx,sy-2,sx,sy+2),fill=WHITE,width=1)
 if k in (1,4):
  x,y=(270,176) if k==1 else (112,188)
  d.ellipse((x-5,y-5,x+5,y+5),outline=PINK,width=2)
  d.line((x-7,y,x+7,y),fill=WHITE,width=1);d.line((x,y-7,x,y+7),fill=WHITE,width=1)
 return im
fx=Image.new('RGBA',(2304,768))
smokes=[];electrics=[]
for k in range(6):
 smokes.append(smoke_frame(k));electrics.append(electric_frame(k))
 fx.alpha_composite(smokes[-1],(384*k,0));fx.alpha_composite(electrics[-1],(384*k,384))
fx.save(P/'Space_damage_fx.png')

def on_bg(im):
 bg=Image.new('RGBA',(384,384),'#0b0b1a');bg.alpha_composite(im);return bg.convert('RGB')
preview=Image.new('RGB',(384*2*5,384*2),'#0b0b1a')
for i in range(5):
 cell=base[0].copy() if i==0 else body.crop((0,(i-1)*384,384,i*384))
 if i:
  cell.alpha_composite(smokes[2]);cell.alpha_composite(electrics[3])
 preview.paste(on_bg(cell).resize((768,768),Image.Resampling.NEAREST),(i*768,0))
preview.save(P/'preview.png')
gifs=[]
for k in range(12):
 cell=body.crop(((k//6)*384,3*384,((k//6)+1)*384,4*384))
 cell.alpha_composite(smokes[k%6]);cell.alpha_composite(electrics[k%6])
 gifs.append(on_bg(cell).resize((768,768),Image.Resampling.NEAREST))
gifs[0].save(P/'preview.gif',save_all=True,append_images=gifs[1:],duration=100,loop=0,optimize=False,disposal=2)
print('wrote damage, fx, preview and gif')
