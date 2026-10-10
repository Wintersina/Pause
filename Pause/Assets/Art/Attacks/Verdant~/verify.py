"""Verify the three Verdant laser sheets and build review previews."""
from pathlib import Path
import colorsys
from PIL import Image, ImageDraw, ImageFont
import numpy as np

ROOT=Path(__file__).resolve().parent
ART=ROOT.parents[1] / "Backgrounds/Resources/Worlds/Verdant/Backdrop3/v4"
NEAREST=Image.Resampling.NEAREST
SPECS=[("verdant_attack_laser.png",(1024,384),(128,128)),
       ("verdant_attack_laserbody.png",(512,256),(128,256)),
       ("verdant_attack_lasertell.png",(512,128),(128,128))]
INK=(11,11,26)


def cells(im,size):
    w,h=size
    return [im.crop((x*w,y*h,(x+1)*w,(y+1)*h))
            for y in range(im.height//h) for x in range(im.width//w)]


def hue_stats(cell):
    a=np.asarray(cell)
    rgb=a[:,:,:3]
    opaque=a[:,:,3]>24
    z=rgb[opaque]/255
    if not len(z): return None
    hsv=np.array([colorsys.rgb_to_hsv(*q) for q in z])
    h=hsv[:,0]*360; s=hsv[:,1]; v=hsv[:,2]
    pink=(((h>=300)&(h<=340)&(s>.2))|((s<.22)&(v>.82)))
    saturated=(s>.6)&(v>.3)
    forbidden={}
    for name,deg in [("cyan",178),("lime",82),("violet",259),("amber",37)]:
        gap=np.abs((h-deg+180)%360-180)
        forbidden[name]=int(np.sum(saturated&(gap<=20)))
    red=((h>=345)|(h<=15))&(s>.25)&(v>.2)
    return {"opaque":len(z),"pink":float(np.mean(pink)),"forbidden":forbidden,"red":int(np.sum(red))}


def difference(a,b):
    aa=np.asarray(a).astype(np.int16); bb=np.asarray(b).astype(np.int16)
    union=(aa[:,:,3]>24)|(bb[:,:,3]>24)
    changed=(np.max(np.abs(aa-bb),axis=2)>20)&union
    return float(np.mean(changed[union]))*100 if np.any(union) else 0.0


def border_ok(cell,body=False):
    a=np.asarray(cell)[:,:,3]
    # A continuing beam has open top/bottom; other cells pad every side.
    w=6
    strips=[a[:,:w],a[:,-w:]]
    if not body: strips += [a[:w,:],a[-w:,:]]
    return all(np.max(s)<=24 for s in strips)


def roundtrip(im):
    small=im.resize((im.width//2,im.height//2),NEAREST)
    return np.array_equal(np.asarray(im),np.asarray(small.resize(im.size,NEAREST)))


def seam(cell):
    a=np.asarray(cell)
    return np.array_equal(a[0],a[-1]) and np.array_equal(a[1],a[-2])


def content_width(cell):
    a=np.asarray(cell)[:,:,3]>24
    xs=np.where(a)[1]
    return int(xs.max()-xs.min()+1) if len(xs) else 0


def verify():
    print("file | size OK | RGBA | reserved empty | border 6px | pink min/max | forbidden 178/82/259/37 | red | loop steps including seam | x2 | vertical | width")
    all_ok=True
    ims={}
    for name,size,cellsize in SPECS:
        im=Image.open(ROOT/name); ims[name]=im
        cc=cells(im,cellsize)
        body="laserbody" in name
        st=[hue_stats(c) for c in cc]
        size_ok=im.size==size
        rgba=im.mode=="RGBA"
        nonempty=all(s is not None for s in st)
        borders=all(border_ok(c,body) for c in cc)
        pinks=[s["pink"] for s in st if s]
        forb={k:sum(s["forbidden"][k] for s in st if s) for k in ("cyan","lime","violet","amber")}
        red=sum(s["red"] for s in st if s)
        loops=[]
        if name.endswith("laser.png"):
            loops=[("muzzle",cc[8:12]),("spark",cc[20:24])]
        elif body:
            loops=[("body",cc)]
        else:
            loops=[("sight",cc[:2]),("lock",cc[2:])]
        measures=[]
        for label,loop in loops:
            steps=[difference(loop[i],loop[(i+1)%len(loop)]) for i in range(len(loop))]
            measures.append(label+":"+"/".join(f"{n:.1f}" for n in steps))
        loop_ok=all(difference(loop[i],loop[(i+1)%len(loop)])>=3 for _,loop in loops for i in range(len(loop)))
        # These atlases use all their specified cells. There are no reserved cells.
        vertical=all(seam(c) for c in cc) if body or name.endswith("lasertell.png") else None
        width=max(content_width(c) for c in cc) if body else None
        good=size_ok and rgba and nonempty and borders and min(pinks)>=.30 and red==0 and sum(forb.values())==0 and loop_ok and roundtrip(im) and (vertical is not False) and (width is None or width<=100)
        all_ok &= good
        print(f"{name} | {size_ok} | {rgba} | n/a (all used), nonempty={nonempty} | {borders} | {min(pinks)*100:.1f}/{max(pinks)*100:.1f}% | "+
              "/".join(str(forb[k]) for k in ("cyan","lime","violet","amber"))+
              f" | {red} | {', '.join(measures)} | {roundtrip(im)} | {vertical if vertical is not None else 'n/a'} | {width if width is not None else 'n/a'}")
        print("  per-cell pink %: "+" ".join(f"{v*100:.1f}" for v in pinks))
    make_previews(ims)
    print("overall:","PASS" if all_ok else "FAIL")
    return all_ok


def composite(bg,fg,x,y):
    bg.paste(fg,(x,y),fg)


def make_previews(ims):
    # 2x atlas review on the exact dark reference color.
    canvas=Image.new("RGB",(2070,2000),INK)
    d=ImageDraw.Draw(canvas)
    y=14
    for name,_,_ in SPECS:
        d.text((12,y),name,fill="white")
        y+=18
        im=ims[name].resize((ims[name].width*2,ims[name].height*2),NEAREST)
        composite(canvas,im,10,y)
        y+=im.height+20
    d.text((12,y),"GAME SIZE: 40px beam and 60px flare on real Verdant tiles",fill="white")
    y+=22
    # Two 256px crops, one shadowed, one brighter; same exact effects on both.
    dark=Image.open(ART/"sky.png").convert("RGBA").crop((40,80,296,336))
    bright=Image.open(ART/"mid.png").convert("RGBA").crop((140,100,396,356))
    body=ims["verdant_attack_laserbody.png"].crop((0,0,128,256)).resize((52,104),NEAREST)
    flare=ims["verdant_attack_laser.png"].crop((7*128,0,8*128,128)).resize((77,77),NEAREST)
    impact=ims["verdant_attack_laser.png"].crop((2*128,2*128,3*128,3*128)).resize((72,72),NEAREST)
    for n,tile in enumerate((dark,bright)):
        panel=Image.new("RGB",(256,256),INK); composite(panel,tile,0,0)
        composite(panel,body,101,50)
        composite(panel,flare,88,19)
        composite(panel,impact,91,149)
        canvas.paste(panel,(12+n*276,y))
        d.text((12+n*276,y+260),"dark tile" if n==0 else "bright tile",fill="white")
    canvas.crop((0,0,2070,max(y+284,1))).save(ROOT/"preview.png")

    # Every sustained loop animates together, including two-frame sight/lock.
    groups=[("muzzle",cells(ims["verdant_attack_laser.png"],(128,128))[8:12]),
            ("spark",cells(ims["verdant_attack_laser.png"],(128,128))[20:24]),
            ("body",cells(ims["verdant_attack_laserbody.png"],(128,256))),
            ("sight",cells(ims["verdant_attack_lasertell.png"],(128,128))[:2]),
            ("lock",cells(ims["verdant_attack_lasertell.png"],(128,128))[2:])]
    frames=[]
    for t in range(4):
        frame=Image.new("RGB",(5*150,290),INK)
        dd=ImageDraw.Draw(frame)
        for j,(label,sequence) in enumerate(groups):
            dd.text((j*150+8,7),label,fill="white")
            composite(frame,sequence[t%len(sequence)],j*150+10,26)
        frames.append(frame)
    frames[0].save(ROOT/"preview.gif",save_all=True,append_images=frames[1:],duration=83,loop=0,disposal=2,optimize=False)


if __name__=="__main__":
    raise SystemExit(0 if verify() else 1)
