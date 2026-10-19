#!/usr/bin/env python3
"""Registered Desert launch strips; changes are asserted hatch-contained."""
from pathlib import Path
from PIL import Image,ImageChops,ImageDraw,ImageFilter
HERE=Path(__file__).resolve().parent; W=H=768; FRAMES=6
SITES={'buried_bunker':{'kind':'rect','pad':(384,430),'door':(220,300,548,555)},'crawler_hangar':{'kind':'rect','pad':(384,470),'door':(250,390,518,620)},'mirror_array_silo':{'kind':'circle','pad':(384,430),'door':(384,430,125)},'canyon_dock':{'kind':'rect','pad':(360,470),'door':(245,330,520,535)}}
MIST=((215,190,126),(151,143,120),(91,94,84)); INK=(18,18,17,255)
def mask(info,expand=0):
 m=Image.new('L',(W,H));d=ImageDraw.Draw(m);k=v=info['kind']
 if k=='circle':cx,cy,r=info['door'];d.ellipse((cx-r,cy-r,cx+r,cy+r),fill=255)
 else:d.rectangle(info['door'],fill=255)
 return m.filter(ImageFilter.MaxFilter(expand*2+1)) if expand else m
def norm(p):
 im=Image.open(p).convert('RGBA').resize((384,384),Image.Resampling.LANCZOS).resize((768,768),Image.Resampling.NEAREST);q=im.load()
 for y in range(H):
  for x in range(W):
   r,g,b,a=q[x,y]
   if a>20 and r>180 and b>150 and g<105:q[x,y]=(150,86,42,a)
   elif a>20 and g>80 and g>r*1.04 and g>b*1.02:q[x,y]=(145,113,55,a)
 return im
def dust(info,f):
 if f not in (2,3,4,5):return Image.new('RGBA',(W,H))
 if info['kind']=='circle':cx,cy,r=info['door'];x0,y0,x1,y1=cx-r,cy-r,cx+r,cy+r
 else:x0,y0,x1,y1=info['door']
 lay=Image.new('RGBA',(W,H));d=ImageDraw.Draw(lay);a={2:.45,3:.75,4:.9,5:.55}[f]
 for j,c in enumerate(MIST):
  for cx0,cy0,rx,ry in ((.42,.68,.26,.18),(.60,.52,.22,.22),(.30,.40,.18,.16))[:2+(f==4)]:
   xx=x0+(x1-x0)*cx0; yy=y0+(y1-y0)*cy0;ww=(x1-x0)*rx;hh=(y1-y0)*ry;d.ellipse((xx-ww,yy-hh,xx+ww,yy+hh),fill=(*c,round((74+28*j)*a)))
 lay=lay.filter(ImageFilter.GaussianBlur(2));lay.putalpha(ImageChops.multiply(lay.getchannel('A'),mask(info,8)));return lay
def render(base,opened,info,f):
 if f==0:return base.copy()
 out=base.copy();x,y=info['pad']
 if f==1:d=ImageDraw.Draw(out);d.rectangle((x-5,y-5,x+5,y+5),fill=(255,199,58,255));return out
 cov={2:.4,3:.75,4:1,5:.5}[f];r=Image.new('L',(W,H));d=ImageDraw.Draw(r)
 if info['kind']=='circle':cx,cy,rad=info['door'];rr=round(rad*cov**.5);d.ellipse((cx-rr,cy-rr,cx+rr,cy+rr),fill=255)
 else:x0,y0,x1,y1=info['door'];yy=y1-round((y1-y0)*cov);d.rectangle((x0,yy,x1,y1),fill=255)
 r=ImageChops.multiply(r,mask(info));out.paste(opened,(0,0),r);edge=Image.new('RGBA',(W,H));ed=ImageDraw.Draw(edge)
 if info['kind']=='circle' and cov<1:ed.ellipse((cx-rr,cy-rr,cx+rr,cy+rr),outline=INK,width=4)
 elif cov<1:ed.line((x0,yy,x1,yy),fill=INK,width=4)
 edge.putalpha(ImageChops.multiply(edge.getchannel('A'),mask(info)));return Image.alpha_composite(Image.alpha_composite(out,edge),dust(info,f))
def bad(im):
 for r,g,b,a in im.getdata():
  if a>=32:assert not(r>180 and b>150 and g<105 and g*2<min(r,b));assert not(g>100 and g>r*1.45 and g>b*1.2)
def main():
 rows=[]
 for n,info in SITES.items():
  base=norm(HERE/f'{n}.png');gen=norm(HERE/f'{n}_open.png');base.save(HERE/f'{n}.png');opened=base.copy();opened.paste(gen,(0,0),mask(info));opened.save(HERE/f'{n}_open.png');fs=[render(base,opened,info,f) for f in range(FRAMES)]
  for f,im in enumerate(fs):
   allowed=mask(info,8 if f>=2 else 0)
   for p,q,a in zip(base.getdata(),im.getdata(),allowed.getdata()):
    if p!=q:assert a,(n,f)
   bad(im)
  strip=Image.new('RGBA',(W*6,H))
  for i,im in enumerate(fs):strip.alpha_composite(im,(i*W,0))
  strip.save(HERE/f'{n}_launch_v2.png');rows.append((n,fs))
 s=Image.new('RGBA',(960,920),(9,20,22,255));d=ImageDraw.Draw(s)
 for row,(n,fs) in enumerate(rows):
  d.text((8,row*230+8),n.replace('_',' '),fill=(255,211,92,255))
  for i,im in enumerate(fs):s.alpha_composite(im.resize((150,150),Image.Resampling.NEAREST),(i*160+4,row*230+34));d.text((i*160+58,row*230+190),str(i),fill=(213,157,65,255))
 s.convert('RGB').save(HERE/'launch_v2_contact_sheet.png')
 z=Image.new('RGBA',(1120,1160),(9,20,22,255));d=ImageDraw.Draw(z)
 for row,(n,fs) in enumerate(rows):
  x,y=SITES[n]['pad'];d.text((8,row*290+6),n.replace('_',' '),fill=(255,211,92,255))
  for j,f in enumerate((2,3,4,5)):z.alpha_composite(fs[f].crop((x-128,y-128,x+128,y+128)),(j*280+12,row*290+26));d.text((j*280+16,row*290+266),str(f),fill=(213,157,65,255))
 z.save(HERE/'launch_v2_mist_zoom.png')
if __name__=='__main__':main()
