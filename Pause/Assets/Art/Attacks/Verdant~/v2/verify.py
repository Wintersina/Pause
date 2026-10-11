"""Audit the green repaint against the installed Verdant attack sheets."""
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent
OLD = ROOT.parent
ART = ROOT.parents[2]
TILES = ART / "Backgrounds/Resources/Worlds/Verdant/Backdrop3/v4"
NEAREST = Image.Resampling.NEAREST
SPECS = (("lash", (1024,128), (128,128)),
         ("log", (1024,256), (256,128)))


def cells(im, size):
    w, h = size
    return [im.crop((x*w,y*h,(x+1)*w,(y+1)*h))
            for y in range(im.height//h) for x in range(im.width//w)]


def stats(im):
    a = np.asarray(im.convert("RGBA"))
    p = a[a[...,3]>24,:3].astype(np.float32)/255
    hi=p.max(1); lo=p.min(1); delta=hi-lo
    hue=np.zeros(len(p),np.float32)
    r,g,b=p.T
    m=delta>0
    q=m&(hi==r); hue[q]=((g[q]-b[q])/delta[q])%6
    q=m&(hi==g); hue[q]=(b[q]-r[q])/delta[q]+2
    q=m&(hi==b); hue[q]=(r[q]-g[q])/delta[q]+4
    hue*=60
    sat=np.where(hi>0,delta/np.maximum(hi,1e-6),0)
    pink=(((hue>=300)&(hue<=340)&(sat>.2))|((sat<.22)&(hi>.82)))
    green=(hue>=100)&(hue<=150)&(sat>.15)&(hi<=.55)
    audited={str(c):int(np.count_nonzero((np.abs((hue-c+180)%360-180)<=20)&(sat>.6)&(hi>.3)))
             for c in (178,82,259,37)}
    red=int(np.count_nonzero(((hue>=345)|(hue<=15))&(sat>.25)&(hi>.2)))
    return len(p), float(np.mean(green)), float(np.mean(pink)), audited, red


def change(a,b):
    aa=np.asarray(a).astype(np.int16); bb=np.asarray(b).astype(np.int16)
    union=(aa[...,3]>24)|(bb[...,3]>24)
    changed=(np.max(np.abs(aa-bb),axis=2)>20)&union
    return 100*float(np.mean(changed[union]))


def margins(im):
    a=np.asarray(im)[...,3]
    return all(np.max(s)<=24 for s in (a[:6],a[-6:],a[:,:6],a[:,-6:]))


def x2_roundtrip(im):
    native=im.resize((im.width//2,im.height//2),NEAREST)
    return np.array_equal(np.asarray(im),np.asarray(native.resize(im.size,NEAREST)))


def tile_sheet(im, tile):
    canvas=Image.new("RGBA",im.size)
    for y in range(0,im.height,tile.height):
        for x in range(0,im.width,tile.width):
            canvas.paste(tile,(x,y))
    canvas.alpha_composite(im)
    return canvas.convert("RGB")


def previews(ims):
    dark=Image.open(TILES/"sky.png").convert("RGBA").crop((40,80,296,336))
    bright=Image.open(TILES/"mid.png").convert("RGBA").crop((140,100,396,356))
    boards=[]
    for scale in (1,3):
        for kind,_,_ in SPECS:
            old,new=ims[kind]
            for tile,label in ((dark,"dark Verdant"),(bright,"bright canopy")):
                for version,im in (("installed",old),("v2 green",new)):
                    panel=tile_sheet(im,tile).resize((im.width*scale,im.height*scale),NEAREST)
                    boards.append((f"{kind} | {version} | {label} | {scale}x",panel))
    # Small on-screen read: roughly 40 px vine body, 60 px hook and bud.
    for tile,label in ((dark,"dark Verdant"),(bright,"bright canopy")):
        for version,slot in (("installed",0),("v2 green",1)):
            panel=Image.new("RGBA",(512,192))
            for x in (0,256): panel.paste(tile,(x,0))
            lash=cells(ims["lash"][slot],(128,128))
            log=cells(ims["log"][slot],(256,128))
            for cell,x,size in ((lash[0],13,70),(lash[2],88,84),
                                (lash[4],180,84),(lash[6],10,70)):
                sprite=cell.resize((size,size),NEAREST)
                panel.alpha_composite(sprite,(x,14 if x!=10 else 106))
            panel.alpha_composite(log[0].resize((150,75),NEAREST),(300,57))
            boards.append((f"game size | {version} | {label} | ~40 px vine, ~60 px hook",panel.convert("RGB")))
    width=max(p.width for _,p in boards)+24
    height=sum(p.height+31 for _,p in boards)+10
    out=Image.new("RGB",(width,height),(11,11,26))
    draw=ImageDraw.Draw(out)
    y=8
    for label,panel in boards:
        draw.text((12,y),label,fill="white")
        y+=20;out.paste(panel,(12,y));y+=panel.height+11
    out.save(ROOT/"preview.png")

    lash=cells(ims["lash"][1],(128,128));log=cells(ims["log"][1],(256,128))
    frames=[]
    for t in range(8):
        frame=Image.new("RGB",(768,314),(11,11,26))
        draw=ImageDraw.Draw(frame)
        groups=(("link",lash[t%2],0),("hook",lash[2+t%2],130),
                ("root",lash[4+t%2],260),("tell",lash[6+t%2],390))
        for name,cell,x in groups:
            draw.text((x+4,5),name,fill="white");frame.paste(cell,(x,23),cell)
        draw.text((510,5),"rolling log",fill="white")
        frame.paste(log[t],(510,23),log[t])
        frames.append(frame)
    frames[0].save(ROOT/"preview.gif",save_all=True,append_images=frames[1:],
                   duration=83,loop=0,disposal=2,optimize=False)


def verify():
    print("file | size | RGBA | reserved/nonempty | margins 6px | alpha delta | green% by cell | pink% by cell | 178/82/259/37 | red | loop changes% | x2 | links join")
    good=True; ims={}
    for kind,size,cell_size in SPECS:
        name=f"verdant_attack_{kind}.png"
        old=Image.open(OLD/name).convert("RGBA")
        new=Image.open(ROOT/name)
        ims[kind]=(old,new.convert("RGBA"))
        cc=cells(new,cell_size)
        st=[stats(c) for c in cc]
        n=[v[0] for v in st]
        greens=[v[1] for v in st]
        pinks=[v[2] for v in st]
        forbidden=[sum(v[3][str(deg)] for v in st) for deg in (178,82,259,37)]
        red=sum(v[4] for v in st)
        original=np.asarray(old);candidate=np.asarray(new.convert("RGBA"))
        mismatch=100*np.mean((original[...,3]>24)!=(candidate[...,3]>24))
        alpha_exact=np.array_equal(original[...,3],candidate[...,3])
        if kind=="lash":
            loops=(cc[0:2],cc[2:4],cc[4:6],cc[6:8])
            # Runtime trims each section to rows 12..115 and scales by 104/128.
            link_join=all(np.where(np.asarray(c)[...,3]>24)[0].min()==12 and
                          np.where(np.asarray(c)[...,3]>24)[0].max()==115 for c in cc[:2])
            body_range=range(6)
        else:
            loops=(cc,)
            link_join=None
            body_range=range(8)
        steps=[change(loop[i],loop[(i+1)%len(loop)]) for loop in loops for i in range(len(loop))]
        pass_file=(new.size==size and new.mode=="RGBA" and all(n) and
                   all(margins(c) for c in cc) and mismatch<=2 and alpha_exact and
                   all(greens[i]>=.55 and .06<=pinks[i]<=.18 for i in body_range) and
                   (kind=="log" or all(pinks[i]>=.6 for i in (6,7))) and
                   sum(forbidden)==0 and red==0 and min(steps)>=3 and
                   x2_roundtrip(new) and link_join is not False)
        good &= pass_file
        print(f"{kind} | {new.size==size} | {new.mode=='RGBA'} | all used, {all(n)} | {all(margins(c) for c in cc)} | {mismatch:.2f}% (exact={alpha_exact}) | "+
              ",".join(f"{q*100:.1f}" for q in greens)+" | "+
              ",".join(f"{q*100:.1f}" for q in pinks)+" | "+
              "/".join(map(str,forbidden))+f" | {red} | "+
              ",".join(f"{q:.1f}" for q in steps)+
              f" | {x2_roundtrip(new)} | {link_join if link_join is not None else 'n/a'}")
    previews(ims)
    print("overall:","PASS" if good else "FAIL")
    return good


if __name__=="__main__":
    raise SystemExit(0 if verify() else 1)
