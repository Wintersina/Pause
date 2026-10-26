"""Hand drawn, point-filtered 16-frame power and ultimate atlases. Requires Pillow."""
from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
from math import sin, cos, pi

OUT = Path(__file__).resolve().parent
S = 256
P = {
'NeonComet':('#55131a','#d83438','#ff7967','Shield Pulse','Comet Streak'),
'VoltViper':('#123960','#258bd0','#a5eaff','EMP','Chain Lightning'),
'SolarFang':('#74300b','#e36e16','#ffd05d','Solar Nova','Solar Fireball'),
'CrimsonHalo':('#61132c','#c93254','#ff9b9c','Capacitor Dump','Rail Gun'),
'IonLancer':('#182e71','#4675dc','#b9d8ff','Ion Battery','Ion Barrage'),
'JadePhantom':('#104937','#29aa77','#b5f5d2','Phase Cloak','Wisp Storm'),
'GoldWarden':('#61400d','#d5a02d','#ffe389','Gold Magnet','Warden Salvo'),
'Lightning':('#69600d','#dccb35','#fff4a5','Thunderclap','Sky Javelins'),
'Ligher':('#74351a','#d57735','#ffbd75','Flare Decoy','Blowtorch'),
'Paranoid':('#2d561e','#70ad42','#dcf8ac','Time Bubble','Seeker Eyes'),
'Ninja':('#26336a','#5c6bc1','#c4d2ff','Blink Dash','Ricochet Star'),
'Saboteur':('#171946','#394aaa','#a3b3ff','Black Hole','Gatling'),
'UFO':('#312158','#7859bd','#d4c5ff','Tractor Pull','Tractor Beam'),
'Dove':('#125768','#3cbdc4','#d0ffff','Mending','Feather Fan'),
'Turtle':('#614226','#bb8952','#f1d9ae','Hard Shell','Shell Disc')}
WHITE='#fff9e5'

def rgba(h,a=255): return tuple(bytes.fromhex(h[1:]))+(a,)
def img():
 im=Image.new('RGBA',(S,S));return im,ImageDraw.Draw(im)
def path(d,pts,p,w=6,closed=False):
 pts=[(round(x),round(y)) for x,y in pts]
 if closed: pts += pts[:1]
 d.line(pts,fill=rgba(p[0],110),width=w+13,joint='curve')
 d.line(pts,fill=rgba(p[1],220),width=w+7,joint='curve')
 d.line(pts,fill=WHITE,width=w+2,joint='curve')
 d.line(pts,fill=p[2],width=w,joint='curve')
def poly(d,pts,p,hot=True):
 pts=[(round(x),round(y)) for x,y in pts]
 d.polygon(pts,fill=p[1]);d.line(pts+[pts[0]],fill=rgba(p[0],180),width=10,joint='curve')
 d.line(pts+[pts[0]],fill=WHITE,width=3,joint='curve')
 d.line(pts+[pts[0]],fill=p[2],width=2,joint='curve')
 if hot:
  cx=sum(x for x,y in pts)/len(pts);cy=sum(y for x,y in pts)/len(pts)
  d.line([(round((x+cx)/2),round((y+cy)/2)) for x,y in pts[:2]],fill=p[2],width=8)
def disc(d,x,y,r,p):
 x=round(x);y=round(y);r=round(r)
 for rr,color in ((r+11,rgba(p[0],110)),(r+6,p[1]),(r+2,WHITE),(r,p[0]),(max(2,r-5),p[1]),(max(1,r//3),WHITE)):
  d.ellipse((x-rr,y-rr,x+rr,y+rr),fill=color)
def arc(d,x,y,r,a,b,p,w=5):
 box=(round(x-r),round(y-r),round(x+r),round(y+r))
 for extra,c,ww in ((9,rgba(p[0],125),w+11),(4,p[1],w+7),(0,WHITE,w+2),(-1,p[2],w)):
  q=(box[0]-extra,box[1]-extra,box[2]+extra,box[3]+extra)
  d.arc(q,a,b,fill=c,width=ww)
def ring(d,x,y,r,p,w=5): arc(d,x,y,r,0,360,p,w)
def pt(a,r,c=(128,128)):return(c[0]+cos(a)*r,c[1]+sin(a)*r)
def spark(d,x,y,p,r=8):
 path(d,[(x-r,y),(x+r,y)],p,2);path(d,[(x,y-r),(x,y+r)],p,2)
def bolt(d,pts,p):path(d,pts,p,6)
def feather(d,x,y,a,p,scale=1):
 q=lambda u,v:(x+(u*cos(a)-v*sin(a))*scale,y+(u*sin(a)+v*cos(a))*scale)
 poly(d,[q(0,-20),q(12,-8),q(8,12),q(0,24),q(-8,12),q(-12,-8)],p)
 path(d,[q(0,-16),q(0,18)],p,2)
def heart(d,x,y,s,p):
 poly(d,[(x,y+s),(x-s,y-s//4),(x-s,y-s),(x,y-s//3),(x+s,y-s),(x+s,y-s//4)],p)
 disc(d,x,y-2,4,p)
def hexagon(x,y,r):return [pt(j*pi/3,r,(x,y)) for j in range(6)]
def motes(d,f,p,count=8,rad=85,offset=0):
 for j in range(count):
  a=2*pi*(j/count+f/8)+offset;rr=rad+8*sin(j*5+f*pi/4)
  disc(d,*pt(a,rr),2 if j%3 else 4,p)

def power(d,k,f,p):
 t=f%8; a=t*pi/4; q=t/7
 if k=='NeonComet':
  if f<8:
   ring(d,128,128,86+4*sin(a),p,13);ring(d,128,128,66,p,3)
   for j in range(4):poly(d,[pt(a+j*pi/2,76),pt(a+j*pi/2-.13,100),pt(a+j*pi/2+.13,100)],p)
   motes(d,t,p,6,104)
  else:
   ring(d,128,128,68+q*39,p,13-round(q*5));disc(d,128,128,16-q*7,p)
   for j in range(5):
    x,y=pt(j*2*pi/5+a,82+q*20);poly(d,[(x-13,y-9),(x+13,y-9),(x+18,y),(x-9,y+12)],p)
    spark(d,x+18,y,p,6)
 elif k=='VoltViper':
  nodes=[pt(j*2*pi/6,91) for j in range(6)]
  if f<8:
   for j,(x,y) in enumerate(nodes):bolt(d,[(128,128),pt(j*pi/3+a/3,51),(x,y)],p);disc(d,x,y,8,p)
   disc(d,128,128,14,p)
  else:
   for j,(x,y) in enumerate(nodes):
    x*=1;off=11*sin(a+j);bolt(d,[(128,128),pt(j*pi/3,43),(x+off,y-off),pt(j*pi/3,112)],p)
    bolt(d,[(x+off,y-off),nodes[(j+1)%6]],p)
   for j in range(6):disc(d,*nodes[j],7,p)
 elif k=='SolarFang':
  if f<8:
   disc(d,128,128,37+3*sin(a),p);ring(d,128,128,77,p,10)
   for j in range(10):poly(d,[pt(j*2*pi/10+a/8,66),pt(j*2*pi/10+a/8-.12,86),pt(j*2*pi/10+a/8,110+5*sin(a+j)),pt(j*2*pi/10+a/8+.12,86)],p)
  else:
   disc(d,128,128,47-round(18*q),p);ring(d,128,128,75+q*25,p,12)
   for j in range(11):
    ang=j*2*pi/11+a/5;poly(d,[pt(ang-.10,73),pt(ang,112+14*sin(j*7+a)),pt(ang+.12,76),pt(ang+.04,91)],p)
   for j in range(5):spark(d,*pt(j*2*pi/5+a,102),p,7)
 elif k=='CrimsonHalo':
  if f<8:
   for j in range(3):arc(d,128,128,47+j*22,round(t*18+j*55),round(t*18+j*55+215),p,7)
   disc(d,128,128,15,p);path(d,[(128,112),(128,24)],p,9)
  else:
   for j in range(4):arc(d,128,128,50+j*14,220+round(q*45),500-round(q*30),p,7)
   path(d,[(128,211),(128,21)],p,19-round(5*q));poly(d,[(113,115),(128,40),(143,115),(128,148)],p)
   for j in range(5):path(d,[(62+j*34,183),(128,128)],p,3)
 elif k=='IonLancer':
  if f<8:
   x=58+round(t*12)
   poly(d,[(x-19,96),(x+12,96),(x+12,85),(x+30,85),(x+30,159),(x-19,159)],p)
   path(d,[(x+6,107),(x+6,145)],p,8);path(d,[(x+6,122),(209,38)],p,4)
   for j in range(3):spark(d,x+55+j*25,102-j*25,p,5)
  else:
   x=60+round(q*100);poly(d,[(x-26,93),(x+17,93),(x+17,79),(x+35,79),(x+35,159),(x-26,159)],p)
   path(d,[(47,128),(225,128)],p,12);disc(d,x,128,15,p)
   for j in range(5):spark(d,75+j*30,128+36*sin(a+j),p,7)
 elif k=='JadePhantom':
  if f<8:
   for j in range(4,0,-1):
    x=128-j*18+11*sin(a-j*.7);poly(d,[(x,37),(x+37,98),(x+29,188),(x,169),(x-29,188),(x-37,98)],p,j==1)
   for j in range(4):spark(d,45+j*48,199-8*j,p,5)
  else:
   for j in range(5):
    x=45+j*35+12*q;poly(d,[(x,49+j%2*13),(x+27,110),(x+20,204),(x,181),(x-21,204),(x-27,110)],p,j==4)
   for j in range(5):path(d,[(35+j*37,210),(65+j*36,191)],p,3)
 elif k=='GoldWarden':
  if f<8:
   for j in range(4):arc(d,128,128,41+j*19,round(34+j*75+t*21),round(280+j*75+t*21),p,7)
   motes(d,t,p,10,104);disc(d,128,128,10,p)
  else:
   for j in range(7):
    ang=j*2*pi/7+a/4;start=pt(ang,114);end=pt(ang+.7+q,35)
    path(d,[start,pt(ang+.35,78),end],p,7);disc(d,*end,4,p)
   disc(d,128,128,18,p)
 elif k=='Lightning':
  if f<8:
   for j in range(3):
    x=62+j*65;bolt(d,[(x-15,23),(x+7,75),(x-9,75),(x+14,150+8*sin(a+j))],p)
   ring(d,128,128,87,p,5)
  else:
   for j in range(5):
    x=38+j*44;bolt(d,[(x-18,7),(x+9,55),(x-7,59),(x+15,130),(x-2,143),(x+16,210)],p)
    disc(d,x+16,210,7,p)
    spark(d,x+11,173+5*t,p,5)
   path(d,[(26,219),(230,219)],p,6)
 elif k=='Ligher':
  if f<8:
   disc(d,128,128,22+round(5*sin(a)),p)
   for j in range(12):poly(d,[pt(j*pi/6+a/9,38),pt(j*pi/6+a/9-.09,70),pt(j*pi/6+a/9,91+6*sin(j+a)),pt(j*pi/6+a/9+.09,70)],p)
   motes(d,t,p,8,111)
  else:
   disc(d,128,128,30+round(9*sin(a)),p)
   for j in range(14):
    ang=j*2*pi/14+a/8;path(d,[pt(ang,31),pt(ang+.07,73),pt(ang+.14,99)],p,6)
    spark(d,*pt(ang,112),p,5)
 elif k=='Paranoid':
  if f<8:
   arc(d,128,158,94,187,353,p,10);path(d,[(34,156),(34,180),(222,180),(222,156)],p,7)
   for j in range(3):arc(d,128,158,45+j*17+3*sin(a+j),198,342,p,4)
   motes(d,t,p,5,63)
  else:
   arc(d,128,158,98,187,353,p,11)
   for j in range(4):arc(d,128,158,34+j*20+q*5,190+j*5,350-j*5,p,6)
   for j in range(6):path(d,[(45+j*33,185),(54+j*30,170+7*sin(a+j))],p,3)
 elif k=='Ninja':
  if f<8:
   for j in range(5):
    x=42+j*40+2*t;poly(d,[(x-12,91),(x+16,91),(x+28,128),(x+16,165),(x-12,165),(x,128)],p,j==4)
   path(d,[(18,178),(236,78)],p,8)
  else:
   path(d,[(19,176),(231,78)],p,16)
   for j in range(4):poly(d,[(56+j*43,123-j*15),(89+j*43,87-j*13),(97+j*43,118-j*14),(66+j*43,154-j*15)],p,j==3)
   for x,y in ((39,181),(216,76)):
    for j in range(6):disc(d,x+sin(j*2+a)*23,y+cos(j*3+a)*20,4+j%3,p)
 elif k=='Saboteur':
  if f<8:
   for j in range(4):arc(d,128,128,44+j*18,round(j*63+t*28),round(j*63+t*28+245),p,9)
   d.ellipse((96,96,160,160),fill='#070b20');ring(d,128,128,37,p,5);motes(d,t,p,7,105)
  else:
   for j in range(6):
    ang=j*pi/3+a/3;path(d,[pt(ang,112),pt(ang+.3+q*.2,85),pt(ang+.8+q*.4,54),pt(ang+1.3,32)],p,9)
    poly(d,[pt(ang,112),pt(ang+.08,105),pt(ang+.14,121)],p)
   d.ellipse((91,91,165,165),fill='#070b20');ring(d,128,128,43,p,8)
 elif k=='UFO':
  if f<8:
   poly(d,[(103,55),(153,55),(219,203),(37,203)],p,False)
   for j in range(5):path(d,[(104-j*13,76+j*28),(152+j*13,76+j*28)],p,4)
   ring(d,128,55,32,p,8)
   motes(d,t,p,6,92)
  else:
   poly(d,[(109,24),(147,24),(230,220),(26,220)],p,False)
   for j in range(7):path(d,[(110-j*13,43+j*25),(146+j*13,43+j*25)],p,4)
   for j in range(7):disc(d,63+j*23,190-30*q+7*sin(a+j),3+j%3,p)
 elif k=='Dove':
  if f<8:
   heart(d,128,128,29+round(3*sin(a)),p)
   for j in range(7):
    x,y=pt(j*2*pi/7+a/2,89);feather(d,x,y,j*.8+a/4,p,.85)
  else:
   heart(d,128,131,43-round(10*q),p)
   for j in range(10):
    x,y=pt(j*2*pi/10+a/4,78+q*27);feather(d,x,y,j*.6+a/3,p,.85)
   for j in range(5):spark(d,*pt(j*2*pi/5+a,107),p,5)
 elif k=='Turtle':
  if f<8:
   for j in range(7):
    x,y=pt(j*2*pi/7,62);poly(d,hexagon(x,y,33+2*sin(a+j)),p,j==t%7)
   poly(d,hexagon(128,128,39),p)
  else:
   for j in range(7):
    ang=j*2*pi/7; x,y=pt(ang,58+q*48)
    poly(d,hexagon(x,y,29-round(9*q)),p)
    path(d,[(x,y),pt(ang,113)],p,3)
   for j in range(8):spark(d,*pt(j*pi/4,116),p,5)

def attack(d,k,f,p):
 t=f%8;a=t*pi/4;q=t/7
 if k=='NeonComet':
  for x in (106,150):
   poly(d,[(x,37+t),(x+15,112),(x,149),(x-15,112)],p)
   path(d,[(x,139),(x-10,223)],p,9)
   spark(d,x-12+5*sin(a),187+18*sin(a),p,5)
  if f>=8:
   for j in range(8):path(d,[pt(j*pi/4,22),pt(j*pi/4+.1,111)],p,9)
   disc(d,128,128,28,p)
   spark(d,27+25*t,214-9*t,p,7)
 elif k=='VoltViper':
  for j in range(4):bolt(d,[pt(j*pi/2+a/4,98),pt(j*pi/2+a/4+.3,55),pt(j*pi/2+a/4+.7,24)],p)
  disc(d,128,128,11,p)
  if f>=8:
   for j in range(6):bolt(d,[pt(j*pi/3,108),pt(j*pi/3+.3,63),pt(j*pi/3+.8,28)],p)
 elif k=='SolarFang':
  disc(d,128,121,37+round(3*sin(a)),p)
  for j in range(9):poly(d,[pt(j*2*pi/9,38),pt(j*2*pi/9,77+8*sin(a+j)),pt(j*2*pi/9+.14,43)],p)
  if f>=8: ring(d,128,128,72+q*29,p,11)
 elif k=='CrimsonHalo':
  path(d,[(128,17),(128,235)],p,16)
  for j in range(4):poly(d,[(77+j*34,99),(96+j*34,99),(84+j*34,127)],p)
  for j in range(4):spark(d,79+j*33,57+13*sin(a+j),p,5)
  if f>=8:
   path(d,[(18,128),(238,128)],p,23)
   for j in range(8):spark(d,38+j*26,128+43*sin(j+a),p,8)
 elif k=='IonLancer':
  for j in range(3):
   x=64+j*64;poly(d,[(x,22),(x+17,112),(x+11,205),(x-11,205),(x-17,112)],p)
   path(d,[(x,160),(x,231)],p,5)
   spark(d,x,182+18*sin(a+j),p,5)
  if f>=8:
   for j in range(3):path(d,[(64+j*64,22),(64+j*64,235)],p,13)
   for j in range(3):spark(d,64+j*64,39+22*t,p,7)
 elif k=='JadePhantom':
  for j in range(5):
   x,y=pt(j*2*pi/5+a,66);disc(d,x,y,12,p);path(d,[(x,y),(x-20,y+25)],p,4)
  if f>=8:
   for j in range(5):arc(d,*pt(j*2*pi/5,78),30,j*60,j*60+250,p,6)
 elif k=='GoldWarden':
  for j in range(3):
   x=62+j*66;poly(d,[(x,34),(x+18,76),(x+18,174),(x-18,174),(x-18,76)],p)
   disc(d,x,89,9,p)
   spark(d,x+15*sin(a+j),194+9*sin(a+j),p,5)
  if f>=8:
   for j in range(3):disc(d,62+j*66,70+q*90,22,p)
   for j in range(8):path(d,[pt(j*pi/4,27),pt(j*pi/4,110)],p,5)
 elif k=='Lightning':
  for x in (56,128,200):bolt(d,[(x-15,20),(x+8,67),(x-8,79),(x+13,146),(x-4,150),(x+12,231)],p)
  for j in range(3):spark(d,56+j*72,180+17*sin(a+j),p,7)
  if f>=8:
   for j in range(8):bolt(d,[pt(j*pi/4,12),pt(j*pi/4+.25,62),pt(j*pi/4,114)],p)
 elif k=='Ligher':
  for j in range(9):
   ang=(-145+j*13)*pi/180;poly(d,[(128,209),pt(ang,70+8*sin(a+j),(128,209)),pt(ang+.05,117+9*sin(a+j),(128,209)),pt(ang+.12,53,(128,209))],p)
  disc(d,128,208,13,p)
  if f>=8:
   for j in range(6):disc(d,43+j*34,72+25*sin(a+j),9,p)
 elif k=='Paranoid':
  for j in range(3):
   x=59+j*68;y=119+6*sin(a+j)
   poly(d,[(x-25,y),(x-13,y-17),(x+13,y-17),(x+25,y),(x+13,y+17),(x-13,y+17)],p)
   disc(d,x+7,y,7,p)
  if f>=8:
   for j in range(3):path(d,[(59+j*68,120),(128,128)],p,8)
   disc(d,128,128,29,p)
 elif k=='Ninja':
  poly(d,[pt(j*pi/5+a,88 if j%2==0 else 27) for j in range(10)],p)
  disc(d,128,128,10,p)
  if f>=8:
   for j in range(7):path(d,[pt(j*2*pi/7,35),pt(j*2*pi/7,113)],p,7)
 elif k=='Saboteur':
  for j in range(5):
   x=45+j*40;y=55+(j%2)*18
   poly(d,[(x,y),(x+12,y+40),(x+8,y+65),(x-8,y+65),(x-12,y+40)],p)
   path(d,[(x,y+64),(x,y+118+8*sin(a+j))],p,5)
  if f>=8:
   for j in range(11):path(d,[pt(j*2*pi/11,30),pt(j*2*pi/11,113)],p,5)
 elif k=='UFO':
  poly(d,[(113,22),(143,22),(214,228),(42,228)],p,False)
  path(d,[(128,25),(128,227)],p,13)
  for j in range(5):arc(d,128,57+j*34,14,j*23+t*15,j*23+t*15+240,p,4)
  for j in range(4):spark(d,73+j*36,192+11*sin(a+j),p,4)
  if f>=8:
   for j in range(6):disc(d,49+j*31,174-32*q,5,p)
 elif k=='Dove':
  for j in range(7):
   ang=(-150+j*10)*pi/180;x,y=pt(ang,93,(128,204));feather(d,x,y,ang+pi/2,p,1.05)
   path(d,[(128,204),(x,y)],p,3)
  spark(d,128+24*sin(a),129+12*cos(a),p,8)
  if f>=8:
   for j in range(7):feather(d,*pt(j*2*pi/7,91),j*2*pi/7,p,.8)
 elif k=='Turtle':
  poly(d,hexagon(128,128,84),p)
  for j in range(6):path(d,[(128,128),pt(j*pi/3,75)],p,5)
  for j in range(6):spark(d,*pt(j*pi/3+a/4,70),p,5)
  if f>=8:
   for j in range(6):
    x,y=pt(j*pi/3,88+q*19);poly(d,hexagon(x,y,22),p)


# Ships whose power draws only its own strip in flight (ShipFxArt.PowerStripName): no generic power strip.
NO_POWER={'NeonComet','SolarFang','JadePhantom','GoldWarden','Lightning','UFO','Turtle'}


def build():
 for k,p in P.items():
  for typ,fn in (('power',power),('attack',attack)):
   if typ=='power' and k in NO_POWER:continue
   strip=Image.new('RGBA',(4096,256))
   for f in range(16):
    tile,d=img();fn(d,k,f,p);strip.paste(tile,(f*S,0))
   strip.save(OUT/f'{k}_{typ}.png',optimize=True)
 cell=80;rowh=80
 sheet=Image.new('RGB',(145+16*cell,30*rowh),(11,15,29));d=ImageDraw.Draw(sheet)
 font=ImageFont.load_default()
 for n,k in enumerate(P):
  for t,typ in enumerate(('power','attack')):
   y=(2*n+t)*rowh;d.text((8,y+31),k+' '+typ,fill=WHITE,font=font)
   if typ=='power' and k in NO_POWER:continue
   src=Image.open(OUT/f'{k}_{typ}.png')
   for f in range(16):
    tile=src.crop((f*S,0,(f+1)*S,S)).resize((76,76),Image.Resampling.NEAREST)
    bg=Image.new('RGBA',(76,76),(27,32,51,255));bg.alpha_composite(tile)
    sheet.paste(bg.convert('RGB'),(147+f*cell,y+2))
 sheet.save(OUT/'contact_sheet.png',optimize=True)
 lines=['# Powers v2 manifest','','Generated by `python3 generate.py` (Pillow required). The 30 runtime PNGs are RGBA; the contact sheet is RGB. Palettes use the ship hull colours, with a violet-blue singularity for Saboteur.','',
 'Each strip is **4096 × 256 px**: 16 contiguous **256 × 256 px** cells. Frames 0–7 are an eight-frame seamless animated power state or projectile/charge; frames 8–15 are a ship-specific trigger or impact. Cell coordinates are `(256 * frame, 0, 256, 256)`. Transparent pixels surround the art. Intended Unity filter: Point. Shapes use filled shadow/body/highlight tones, pale pixel rims, and stepped glow.','','| Ship | Secondary power | Ultimate |','|---|---|---|']
 for k,p in P.items():lines.append(f'| {k} | ' + (f'{p[3]} (own flight strip)' if k in NO_POWER else f'`{k}_power.png` — {p[3]}') + f' | `{k}_attack.png` — {p[4]} |')
 lines+=['','`contact_sheet.png` stacks power then attack for every ship in roster order. Frames are shown left to right; it is a review aid, not a runtime atlas.','']
 (OUT/'MANIFEST.md').write_text('\n'.join(lines))
if __name__=='__main__':build()
