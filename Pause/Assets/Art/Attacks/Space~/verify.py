"""Audit and preview the staged Space pod laser sprites. Run from any cwd."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT=Path(__file__).resolve().parent
FILES={
 'space_attack_laser.png':((1024,384),(8,3),(128,128)),
 'space_attack_laserbody.png':((512,256),(4,1),(128,256)),
 'space_attack_lasertell.png':((512,128),(4,1),(128,128)),
}
LOOPS={
 'muzzle':('space_attack_laser.png',[(i,1) for i in range(4)],.03),
 'spark':('space_attack_laser.png',[(i,2) for i in range(4,8)],.03),
 'body':('space_attack_laserbody.png',[(i,0) for i in range(4)],.08),
 'sight':('space_attack_lasertell.png',[(i,0) for i in range(2)],.03),
 'lock':('space_attack_lasertell.png',[(i,0) for i in range(2,4)],.03),
}


def cell(im,x,y,dim):
 w,h=dim
 return im.crop((x*w,y*h,(x+1)*w,(y+1)*h))


def hue_counts(im):
 a=np.asarray(im.convert('RGBA'))
 rgb=a[:,:,:3]/255
 mx=rgb.max(axis=2);mn=rgb.min(axis=2);delta=mx-mn
 sat=np.divide(delta,mx,out=np.zeros_like(delta),where=mx>0)
 h=np.zeros_like(mx)
 mask=delta>0
 r,g,b=[rgb[:,:,i] for i in range(3)]
 m=mask & (mx==r);h[m]=((g[m]-b[m])/delta[m])%6
 m=mask & (mx==g);h[m]=(b[m]-r[m])/delta[m]+2
 m=mask & (mx==b);h[m]=(r[m]-g[m])/delta[m]+4
 h=(h*60)%360
 opaque=a[:,:,3]>=128
 white=opaque & (mx>=.9) & (sat<=.2)
 pink=opaque & (h>=300) & (h<=335) & (sat>.2)
 pickup={str(v):int((opaque & (sat>.65) & (np.minimum(abs(h-v),360-abs(h-v))<=20)).sum()) for v in [178,82,259,37]}
 red=int((opaque & (sat>.5) & ((h>=345)|(h<=15))).sum())
 return float((pink|white).sum()/max(1,opaque.sum())),pickup,red


def diff(a,b):
 x=np.asarray(a);y=np.asarray(b)
 union=(x[:,:,3]>=128)|(y[:,:,3]>=128)
 changed=(x!=y).any(axis=2)&union
 return float(changed.sum()/max(1,union.sum()))


def backdrop_tile(path,bright=False):
 im=Image.open(path).convert('RGBA')
 # Crop real Space backdrop locations; the second lies over a luminous nebula.
 crop=im.crop((80,350,336,606) if not bright else (490,580,746,836))
 if bright:
  sky=Image.open(path.parent/'sky_04.png').convert('RGBA').crop((576,1280,832,1536))
  sky.alpha_composite(crop)
  crop=sky
 return crop.resize((256,256),Image.Resampling.NEAREST).convert('RGB')


def preview(images):
 dark=(11,11,26,255)
 laser=images['space_attack_laser.png'];body=images['space_attack_laserbody.png'];tell=images['space_attack_lasertell.png']
 out=Image.new('RGB',(1600,970),(11,11,26))
 d=ImageDraw.Draw(out)
 d.text((16,10),'SPACE POD LASER  |  native x2 on #0b0b1a',fill='#FFE0F8')
 out.paste(laser,(16,38),laser)
 out.paste(tell,(16,450),tell)
 out.paste(body,(1040,38),body)
 d.text((16,610),'GAME SIZE  |  40 px beam / 60 px flare over Space backdrop tiles',fill='#FFE0F8')
 backpaths=[ROOT.parents[1]/'Backgrounds/Resources/Worlds/Space/Backdrop/sky_01.png',
            ROOT.parents[1]/'Backgrounds/Resources/Worlds/Space/Backdrop/neon_frames.png']
 for k,path in enumerate(backpaths):
  bg=backdrop_tile(path,k==1)
  x=16+k*310;y=650
  out.paste(bg,(x,y))
  beam=cell(body,k,0,(128,256)).resize((64,128),Image.Resampling.NEAREST)
  flare=cell(laser,2 if k==0 else 6,0,(128,128)).resize((64,64),Image.Resampling.NEAREST)
  out.paste(beam,(x+96,y+78),beam)
  out.paste(flare,(x+96,y+54),flare)
  d.text((x,y+260),'dark tile' if k==0 else 'bright tile',fill='#FFE0F8')
 out.save(ROOT/'preview.png')

 def anim_panel(label, im, ix, iy, dim, canvas, pos, target=(128,128)):
  part=cell(im,ix,iy,dim)
  part.thumbnail(target,Image.Resampling.NEAREST)
  px,py=pos
  canvas.paste(part,(px+(target[0]-part.width)//2,py+20),part)
  ImageDraw.Draw(canvas).text((px,py),label,fill='#FFE0F8')
 frames=[]
 for i in range(4):
  frame=Image.new('RGBA',(660,300),dark)
  anim_panel('muzzle',laser,i,1,(128,128),frame,(8,10))
  anim_panel('spark',laser,4+i,2,(128,128),frame,(140,10))
  anim_panel('beam',body,i,0,(128,256),frame,(272,10),target=(128,256))
  anim_panel('sight',tell,i%2,0,(128,128),frame,(404,10))
  anim_panel('lock',tell,2+i%2,0,(128,128),frame,(536,10))
  frames.append(frame.convert('P',palette=Image.Palette.ADAPTIVE))
 frames[0].save(ROOT/'preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,disposal=2)


def main():
 images={n:Image.open(ROOT/n).convert('RGBA') for n in FILES}
 bad=[]
 print('FILE                         SIZE  RGBA  RESERVED  BORDER  PINK / CELL (%)')
 for name,(size,grid,dim) in FILES.items():
  im=images[name]
  size_ok=im.size==size
  rgba=Image.open(ROOT/name).mode=='RGBA'
  shares=[];margins=[];hue={'178':0,'82':0,'259':0,'37':0};red=0
  for y in range(grid[1]):
   for x in range(grid[0]):
    c=cell(im,x,y,dim)
    share,counts,r=hue_counts(c)
    shares.append(share)
    for k,v in counts.items():hue[k]+=v
    red+=r
    a=np.asarray(c.getchannel('A'))
    vertical=name.endswith('laserbody.png') or (name.endswith('lasertell.png') and x<2)
    border=bool((a[:,:6]<=24).all() and (a[:,-6:]<=24).all())
    if not vertical:border &= bool((a[:6,:]<=24).all() and (a[-6:,:]<=24).all())
    margins.append(border)
    if a.max()==0:bad.append(name+f' empty cell {x},{y}')
  print(f'{name:29} {"OK" if size_ok else "FAIL":4}  {"OK" if rgba else "FAIL":4}  none      {"OK" if all(margins) else "FAIL":4}  '+', '.join(f'{n:.0%}' for n in shares))
  print(f'  hue ±20°: 178={hue["178"]} 82={hue["82"]} 259={hue["259"]} 37={hue["37"]}; red={red}')
  if not size_ok or not rgba or not all(margins) or min(shares)<.3 or any(hue.values()) or red:bad.append(name+' audit')
  native=im.resize((im.width//2,im.height//2),Image.Resampling.NEAREST)
  roundtrip=np.array_equal(np.asarray(im),np.asarray(native.resize(im.size,Image.Resampling.NEAREST)))
  print('  x2 nearest round trip:', 'OK' if roundtrip else 'FAIL')
  if not roundtrip:bad.append(name+' x2 round trip')
 print('LOOP       STEP DIFFERENCE incl. last→first  MINIMUM')
 for label,(name,keys,minimum) in LOOPS.items():
  im=images[name];dim=FILES[name][2]
  steps=[diff(cell(im,*keys[i],dim),cell(im,*keys[(i+1)%len(keys)],dim)) for i in range(len(keys))]
  print(f'{label:10} '+', '.join(f'{x:.1%}' for x in steps)+f'  >= {minimum:.0%} '+('OK' if min(steps)>=minimum else 'FAIL'))
  if min(steps)<minimum:bad.append(label+' loop difference')
 body=images['space_attack_laserbody.png'];tell=images['space_attack_lasertell.png']
 for label,im,keys,dim in [('body',body,range(4),(128,256)),('sight',tell,range(2),(128,128))]:
  seam=all(np.array_equal(np.asarray(cell(im,i,0,dim))[0],np.asarray(cell(im,i,0,dim))[-1]) for i in keys)
  print(f'vertical seamless {label}:', 'OK' if seam else 'FAIL')
  if not seam:bad.append(label+' vertical seam')
 widths=[]
 for i in range(4):
  a=np.asarray(cell(body,i,0,(128,256)).getchannel('A'))>24
  xs=np.where(a)[1];widths.append(int(xs.max()-xs.min()+1))
 print('beam content widths:',widths,'<= 100:', 'OK' if max(widths)<=100 else 'FAIL')
 if max(widths)>100:bad.append('beam width')
 preview(images)
 print('previews: preview.png, preview.gif')
 if bad:raise SystemExit('FAIL: '+', '.join(bad))
 print('PASS')

if __name__=='__main__':main()
