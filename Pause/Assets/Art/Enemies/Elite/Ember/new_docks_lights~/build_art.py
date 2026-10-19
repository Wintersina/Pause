#!/usr/bin/env python3
"""Build Ember elite light overlays, launch strips, and the review sheet."""
from pathlib import Path
from PIL import Image, ImageDraw
import random

HERE = Path(__file__).resolve().parent
ELITES = HERE.parents[1]
CELL = 384
NAMES = ("ash_wraith", "brass_vulture", "cauterizer", "coalrunner", "kilnback", "sunstoke")
COLORS = {n: ((255, 164, 52), (255, 226, 112)) for n in NAMES}
COLORS["ash_wraith"] = ((255, 154, 40), (255, 215, 92))

def emberize(im):
    """Keep the installed silhouette, but move legacy cool/magenta accents into Ember hues."""
    p=im.load()
    for y in range(im.height):
        for x in range(im.width):
            r,g,b,a=p[x,y]
            if not a: continue
            if (r>105 and b>95 and r>g*1.10 and b>g*1.10) or (b>r*1.18 and b>g*1.05) or (g>r*1.22 and g>b*1.05):
                v=max(r,g,b)/255
                c=(255,155,38) if v>.68 else (154,76,28) if v>.35 else (64,52,42)
                p[x,y]=(*c,a)
    return im

def points(cell):
    p = cell.load(); out = []
    for y in range(12, CELL - 12, 4):
        for x in range(12, CELL - 12, 4):
            r,g,b,a = p[x,y]
            if a > 150 and max(r,g,b) > 105 and max(r,g,b)-min(r,g,b) > 30:
                out.append((max(r,g,b), x, y))
    out.sort(reverse=True); chosen=[]
    for _,x,y in out:
        if all((x-a)**2+(y-b)**2 >= 24**2 for a,b in chosen): chosen.append((x,y))
        if len(chosen) >= 18: break
    return sorted(chosen, key=lambda q:(q[1],q[0]))

def light_layer(pts, name, phase, strength, damaged=False):
    out=Image.new("RGBA", (CELL,CELL)); d=ImageDraw.Draw(out); a,b=COLORS[name]
    for i,(x,y) in enumerate(pts):
        on = ((phase-i) % max(7,len(pts)) < 3) or (i < 2 and phase in (1,2))
        if damaged: on = ((i+phase)%4 != 0) and (i%5 != 0 or phase == 0)
        c = b if i%5==0 else a
        if not on: c=tuple(v//3 for v in c)
        alpha=round((215 if on else 55)*strength)
        d.rectangle((x-2,y-2,x+2,y+2),fill=(*c,alpha))
        if on: d.point((x,y),fill=(255,244,170,round(245*strength)))
    return out

def build_lights(name):
    src=emberize(Image.open(ELITES/"Ember"/f"ember_elite_{name}.png").convert("RGBA"))
    pts=points(src.crop((0,0,CELL,CELL)))
    overlay=Image.new("RGBA",src.size); preview=Image.new("RGBA",src.size)
    for i in range(src.width//CELL):
        if i==0: phase,strength,damaged=0,.12,False
        elif i==1: phase,strength,damaged=1,.22,False
        elif i==2: phase,strength,damaged=2,.55,False
        elif i==7: phase,strength,damaged=7,.48,True
        else: phase,strength,damaged=(i-3)%8,1.0,False
        lay=light_layer(pts,name,phase,strength,damaged)
        overlay.alpha_composite(lay,(i*CELL,0))
        preview.alpha_composite(Image.alpha_composite(src.crop((i*CELL,0,(i+1)*CELL,CELL)),lay),(i*CELL,0))
    overlay.save(HERE/f"ember_elite_{name}_lights.png")
    preview=emberize(preview); preview.putalpha(preview.getchannel("A").point(lambda v: 0 if v<16 else v)); preview.save(HERE/f"ember_elite_{name}_preview_lights.png")
    fs=[preview.crop((i*CELL,0,(i+1)*CELL,CELL)) for i in (3,4,5,6,7,2)]
    fs[0].save(HERE/f"ember_elite_{name}_lights.gif",save_all=True,append_images=fs[1:],duration=130,loop=0,disposal=2)

SITES={
    "crucible_silo":("exec-0f7218fc-d1d6-4829-bf1b-24364711c1cb.png",(.50,.52),"circle"),
    "lava_moat_bunker":("exec-7f25ce4d-df50-404a-9d84-c43997d36d50.png",(.50,.50),"circle"),
    "brass_gantry_cradle":("exec-a379c73b-f708-47b8-b223-faf6f067f2ef.png",(.50,.53),"vertical"),
    "kiln_mouth":("exec-b64c9732-1a17-4ffa-9d0d-ed9e1602f3bb.png",(.50,.54),"mouth"),
}
GEN=Path("/Users/sina/.codex/generated_images/01a15233-bb9a-70d3-b7bc-aa5a61a6e955")

def normalize(name, fn):
    im=Image.open(GEN/fn).convert("RGBA")
    alpha=im.getchannel("A"); box=alpha.getbbox(); im=im.crop(box)
    # Remove the generated atmospheric fringe; the installed Ember atlas is cutout art.
    a=im.getchannel("A").point(lambda v: 0 if v<48 else v)
    im.putalpha(a)
    im=emberize(im)
    scale=min(1.0,768/max(im.size)); im=im.resize((round(im.width*scale),round(im.height*scale)),Image.Resampling.LANCZOS)
    im.putalpha(im.getchannel("A").point(lambda v: 0 if v<16 else v)); im=emberize(im)
    im.save(HERE/f"{name}.png"); return im

def launch(base,pad,kind,frame):
    out=base.copy(); w,h=out.size; x,y=pad; d=ImageDraw.Draw(out)
    t=(.20,.15,.08,.035,0,.10)[frame]; q=max(1,round(min(w,h)*t))
    if kind=="circle": d.ellipse((x-q*2,y-q,x+q*2,y+q),fill=(5,7,7,242)) if q else None
    elif kind=="vertical": d.rectangle((x-q,y-q*2,x+q,y+q*2),fill=(5,7,7,242)) if q else None
    else: d.rectangle((x-q*2,y-q,x+q*2,y+q),fill=(5,7,7,242)) if q else None
    if frame==1:
        s=max(2,round(min(w,h)*.012)); d.rectangle((x-s,y-round(min(w,h)*.18)-s,x+s,y-round(min(w,h)*.18)+s),fill=(255,233,168,255))
    return out

def build_sites():
    for name,(fn,norm,kind) in SITES.items():
        base=normalize(name,fn); pad=(round(base.width*norm[0]),round(base.height*norm[1]))
        strip=Image.new("RGBA",(base.width*6,base.height))
        for i in range(6): strip.alpha_composite(launch(base,pad,kind,i),(i*base.width,0))
        strip.save(HERE/f"{name}_launch.png")

def sheet():
    W,H=360,330; s=Image.new("RGBA",(W*4,H*3),(12,12,10,255)); d=ImageDraw.Draw(s)
    for i,n in enumerate(SITES):
        im=Image.open(HERE/f"{n}.png").convert("RGBA"); im.thumbnail((W-20,H-48),Image.Resampling.LANCZOS)
        s.alpha_composite(im,(i*W+(W-im.width)//2,38+(H-48-im.height)//2)); d.text((i*W+10,12),n.replace('_',' '),fill=(255,220,130,255))
    for j,n in enumerate(NAMES):
        im=Image.open(HERE/f"ember_elite_{n}_preview_lights.png").crop((3*CELL,0,4*CELL,CELL)); im.thumbnail((W-20,H-48),Image.Resampling.NEAREST)
        x=(j%4)*W; y=H+(j//4)*H; s.alpha_composite(im,(x+(W-im.width)//2,y+38+(H-48-im.height)//2)); d.text((x+10,y+12),n.replace('_',' ')+' lit',fill=(255,220,130,255))
    s.convert("RGB").save(HERE/"contact_sheet.png")

if __name__=="__main__":
    for n in NAMES: build_lights(n)
    build_sites(); sheet()
