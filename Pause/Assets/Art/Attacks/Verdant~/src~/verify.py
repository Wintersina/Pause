"""Audit the new Verdant lash/log atlases and produce task-specific previews."""
from pathlib import Path
import colorsys
import numpy as np
from PIL import Image, ImageDraw

HERE=Path(__file__).resolve().parent
ROOT=HERE.parent
N=Image.Resampling.NEAREST
SPECS=[('verdant_attack_lash.png',(1024,128),(128,128)),
       ('verdant_attack_log.png',(1024,256),(256,128))]

def cells(im,sz):
    w,h=sz
    return [im.crop((x*w,y*h,(x+1)*w,(y+1)*h))
            for y in range(im.height//h) for x in range(im.width//w)]

def measure(c):
    a=np.asarray(c)
    z=a[a[:,:,3]>24,:3]/255
    if not len(z):return None
    h,s,v=np.array([colorsys.rgb_to_hsv(*q) for q in z]).T
    h*=360
    pink=(((h>=300)&(h<=340)&(s>.2))|((s<.22)&(v>.82)))
    forbidden=[]
    for deg in (178,82,259,37):
        gap=np.abs((h-deg+180)%360-180)
        forbidden.append(int(np.sum((gap<=20)&(s>.6)&(v>.3))))
    red=int(np.sum(((h>=345)|(h<=15))&(s>.25)&(v>.2)))
    mask=a[:,:,3]>24
    xx=np.where(mask)[1]
    width=int(xx.max()-xx.min()+1) if len(xx) else 0
    border=all(np.max(b)<=24 for b in (a[:6,:,3],a[-6:,:,3],a[:,:6,3],a[:,-6:,3]))
    return len(z),float(np.mean(pink)),forbidden,red,width,border

def diff(a,b):
    x=np.asarray(a).astype(np.int16);y=np.asarray(b).astype(np.int16)
    opaque=(x[:,:,3]>24)|(y[:,:,3]>24)
    changed=(np.max(np.abs(x-y),axis=2)>20)&opaque
    return 100*float(changed[opaque].mean()) if np.any(opaque) else 0

def x2(im):
    return np.array_equal(np.asarray(im),np.asarray(im.resize((im.width//2,im.height//2),N).resize(im.size,N)))

def paste(bg,fg,xy):bg.paste(fg,xy,fg)

def previews(ims):
    lash=cells(ims[0],(128,128));log=cells(ims[1],(256,128))
    canvas=Image.new('RGB',(2070,1240),'#0b0b1a');d=ImageDraw.Draw(canvas)
    d.text((14,10),'VERDANT LASH  /  2x ATLAS',fill='white')
    paste(canvas,ims[0].resize((2048,256),N),(10,30))
    d.text((14,300),'VERDANT ROLLING LOG  /  2x ATLAS',fill='white')
    paste(canvas,ims[1].resize((2048,512),N),(10,320))
    d.text((14,850),'GAME SIZE / dark and bright Verdant backdrop tiles',fill='white')
    tiles=[]
    art=ROOT.parents[1]/'Backgrounds/Resources/Worlds/Verdant/Backdrop3/v4'
    for name,xy in [('sky.png',(40,80)),('mid.png',(140,100))]:
        p=art/name
        tile=Image.open(p).convert('RGB').crop((xy[0],xy[1],xy[0]+256,xy[1]+256)) if p.exists() else Image.new('RGB',(256,256),'#173620')
        tiles.append(tile)
    for j,tile in enumerate(tiles):
        x=12+j*276;y=872
        canvas.paste(tile,(x,y))
        paste(canvas,log[j].resize((142,71),N),(x+56,y+108))
        paste(canvas,lash[0].resize((54,54),N),(x+16,y+62))
        paste(canvas,lash[2].resize((54,54),N),(x+17,y+114))
        paste(canvas,lash[4].resize((54,54),N),(x+18,y+165))
        paste(canvas,lash[6].resize((72,72),N),(x+174,y+15))
    canvas.crop((0,0,2070,1140)).save(HERE/'preview.png')
    # Every loop appears simultaneously; frame 7 flows back to frame 0.
    frames=[]
    for t in range(8):
        f=Image.new('RGB',(790,220),'#0b0b1a');q=ImageDraw.Draw(f)
        for j,(label,c) in enumerate([('link',lash[t%2]),('tip',lash[2+t%2]),('root',lash[4+t%2]),('dash',lash[6+t%2])]):
            x=j*135+5;q.text((x,8),label,fill='white');paste(f,c,(x,35))
        q.text((545,8),'log roll 1/8 turn / frame',fill='white')
        paste(f,log[t].resize((235,118),N),(545,38))
        frames.append(f)
    frames[0].save(HERE/'preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,disposal=2,optimize=False)

def verify():
    print('file | size | RGBA | reserved/nonempty | 6px border | pink per cell % | hue 178/82/259/37 | red | loop differences % incl wrap | x2 | vertical join | content width')
    images=[];overall=True
    for name,size,sz in SPECS:
        im=Image.open(ROOT/name);images.append(im)
        cc=cells(im,sz);mm=[measure(c) for c in cc]
        checks=[im.size==size,im.mode=='RGBA',len(cc)==8 and all(m for m in mm),all(m[5] for m in mm),min(m[1] for m in mm)>=.30,
                all(sum(m[2])==0 and m[3]==0 for m in mm),x2(im)]
        if 'lash' in name:
            loops=[('link',cc[0:2]),('tip',cc[2:4]),('root',cc[4:6]),('dash',cc[6:8])]
            # Each 6-pixel inset link is an exact seamless tile after trimming
            # its transparent inset and placing sections with a 12px overlap.
            join=all(np.array_equal(np.asarray(c)[12],np.asarray(c)[114]) and
                     np.array_equal(np.asarray(c)[14],np.asarray(c)[112]) for c in cc[:2])
            width_ok=max(m[4] for m in mm)<=100
            vertical='trimmed 12px join='+str(join)
        else:
            loops=[('roll',cc)]
            join=True;vertical='n/a'
            width_ok=max(m[4] for m in mm)<=240
        steps=[(label,[diff(seq[i],seq[(i+1)%len(seq)]) for i in range(len(seq))]) for label,seq in loops]
        loops_ok=all(v>=3 for _,vals in steps for v in vals)
        checks.extend([join,width_ok,loops_ok])
        overall &= all(checks)
        print(f'{name} | {checks[0]} | {checks[1]} | no reserved; {checks[2]} | {checks[3]} | '+
              ','.join(f'{100*m[1]:.1f}' for m in mm)+' | '+
              '/'.join(str(sum(m[2][j] for m in mm)) for j in range(4))+' | '+
              str(sum(m[3] for m in mm))+' | '+
              '; '.join(label+':'+','.join(f'{v:.1f}' for v in vals) for label,vals in steps)+' | '+
              str(checks[6])+' | '+vertical+' | '+str(max(m[4] for m in mm))+' '+str(width_ok))
    previews(images)
    print('overall:', 'PASS' if overall else 'FAIL')
    return overall

if __name__=='__main__':raise SystemExit(0 if verify() else 1)
