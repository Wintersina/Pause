#!/usr/bin/env python3
"""Build Tide hull-light overlays, launch strips, and review sheets."""
from pathlib import Path
from collections import deque
from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
ELITES = HERE.parents[1]
CELL = 384
NAMES = ("riptide", "bathyscaphe", "trawler", "thunderfin", "pearl_warden")
COLORS = {n: ((127, 240, 255), (255, 190, 72)) for n in NAMES}

def hsv_score(rgb):
    r,g,b = [v/255 for v in rgb]; hi,lo=max(r,g,b),min(r,g,b)
    return hi, (hi-lo)/max(hi, 1e-6)

def feature_points(cell):
    pix=cell.load(); box=cell.getchannel("A").point(lambda v:255 if v>80 else 0).getbbox()
    if not box: return []
    x0,y0,x1,y1=box; candidates=[]
    for y in range(y0+8,y1-7,4):
        for x in range(x0+8,x1-7,4):
            r,g,b,a=pix[x,y]
            if a<150: continue
            value,sat=hsv_score((r,g,b))
            if sat>.22 and value>.36: candidates.append((value*(.35+sat),x,y))
    candidates.sort(reverse=True); points=[]
    for _,x,y in candidates:
        if all((x-px)**2+(y-py)**2>=22**2 for px,py in points): points.append((x,y))
        if len(points)>=18: break
    return sorted(points,key=lambda p:(p[1],p[0]))

def light_layer(points, colors, phase, strength, damaged=False):
    glow=Image.new("RGBA",(CELL,CELL)); core=Image.new("RGBA",(CELL,CELL))
    gd,cd=ImageDraw.Draw(glow),ImageDraw.Draw(core)
    for i,(x,y) in enumerate(points):
        beacon=i<3; emitter=y>CELL*.68
        on=((phase-i)%max(len(points),1)<3 or (beacon and phase in (1,2)) or (emitter and phase in (2,3,4,5)))
        if damaged: on=((i+phase)%3!=0) and (i%4!=0 or phase==0)
        col=colors[1] if (beacon or i%5==0) else colors[0]
        if not on: col=tuple(round(c*.38) for c in col)
        amount=(1.0 if on else .24)*strength
        gd.rectangle((x-4,y-4,x+4,y+4),fill=(*col,round(42*amount)))
        gd.rectangle((x-2,y-2,x+2,y+2),fill=(*col,round(105*amount)))
        if on:
            cd.rectangle((x-1,y-1,x+1,y+1),fill=(*col,round(225*amount)))
            if emitter: cd.point((x,y),fill=(255,249,185,round(245*amount)))
    return Image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(1.1)),core)

def remove_pink(im):
    """Move legacy lilac/magenta source accents into Tide blue-teal."""
    pix=im.load()
    for y in range(im.height):
        for x in range(im.width):
            r,g,b,a=pix[x,y]
            if a>20 and r>170 and b>150 and g<130 and r>g*1.35 and b>g*1.35:
                pix[x,y]=(min(255,round(r*.35)),min(255,round(g*1.25+35)),min(255,round(b*.82+35)),a)
    return im

def build_lights(name):
    base=Image.open(ELITES/f"Tide/tide_elite_{name}.png").convert("RGBA")
    out=Image.new("RGBA",base.size); preview=Image.new("RGBA",base.size)
    points=feature_points(base.crop((0,0,CELL,CELL)))
    if len(points)<6: raise RuntimeError(f"not enough light candidates: {name}")
    for frame in range(base.width//CELL):
        if frame==0: phase,strength,damaged=0,.14,False
        elif frame==1: phase,strength,damaged=1,.28,False
        elif frame==2: phase,strength,damaged=2,.64,False
        elif frame==6: phase,strength,damaged=7,.54,True
        else: phase,strength,damaged=(frame-3)%8,1.0,False
        overlay=light_layer(points,COLORS[name],phase,strength,damaged)
        out.alpha_composite(overlay,(frame*CELL,0))
        preview.alpha_composite(remove_pink(Image.alpha_composite(base.crop((frame*CELL,0,(frame+1)*CELL,CELL)),overlay)),(frame*CELL,0))
    out.save(HERE/f"tide_elite_{name}_lights.png"); preview.save(HERE/f"tide_elite_{name}_preview_lights.png")
    frames=[preview.crop((i*CELL,0,(i+1)*CELL,CELL)) for i in (3,4,5,7,8,9)]
    frames[0].save(HERE/f"tide_elite_{name}_lights.gif",save_all=True,append_images=frames[1:],duration=130,loop=0,disposal=2)

SITES={
    "floating_carrier_pad":{"pad":(.5,.49),"kind":"carrier"},
    "lighthouse_silo":{"pad":(.5,.48),"kind":"silo"},
    "submarine_pen":{"pad":(.5,.56),"kind":"pen"},
    "wreck_hull_bay":{"pad":(.5,.56),"kind":"wreck"},
}

def clean_bg(im):
    im=im.convert("RGBA"); pix=im.load(); w,h=im.size; q=deque(); seen=set()
    for x in range(w): q.extend(((x,0),(x,h-1)))
    for y in range(h): q.extend(((0,y),(w-1,y)))
    while q:
        x,y=q.popleft()
        if (x,y) in seen or not(0<=x<w and 0<=y<h): continue
        seen.add((x,y)); r,g,b,a=pix[x,y]
        if a==0 or max(r,g,b)<48:
            pix[x,y]=(r,g,b,0); q.extend(((x-1,y),(x+1,y),(x,y-1),(x,y+1)))
    if max(im.size)>768: im=im.resize((768,768),Image.Resampling.LANCZOS)
    return im

def shaded_bay(d,box):
    x0,y0,x1,y1=box; d.rectangle(box,fill=(6,22,28,255),outline=(17,9,8,255),width=5)
    for x in range(x0+12,x1-8,24):
        d.line((x,y0+8,x,y1-10),fill=(18,55,60,255),width=4); d.line((x+5,y0+8,x+5,y1-10),fill=(5,15,20,255),width=2)
    d.line((x0+10,y1-18,x1-10,y1-18),fill=(121,76,40,255),width=4)

def panel(d,box,horizontal=False):
    x0,y0,x1,y1=map(round,box)
    d.rectangle((x0,y0,x1,y1),fill=(47,73,79,255),outline=(18,25,27,255),width=5)
    d.rectangle((x0+8,y0+8,x1-8,y1-8),outline=(144,99,47,255),width=3)
    if horizontal:
        for xx in range(x0+24,x1-10,34): d.line((xx,y0+10,xx,y1-10),fill=(22,42,47,255),width=2)
    else:
        for yy in range(y0+24,y1-10,34): d.line((x0+10,yy,x1-10,yy),fill=(22,42,47,255),width=2)
    for p in ((x0+10,y0+10),(x1-10,y0+10),(x0+10,y1-10),(x1-10,y1-10)): d.rectangle((p[0]-2,p[1]-2,p[0]+2,p[1]+2),fill=(220,162,66,255))

def doors(d,cx,cy,kind,frame):
    if frame==0: return
    if kind=="carrier":
        gap=(0,18,54,94,128,46)[frame]; box=(cx-112,cy-112,cx+112,cy+112); shaded_bay(d,(cx-102,cy-102,cx+102,cy+102))
        if gap<128: panel(d,(box[0],box[1],cx-gap,box[3]),True); panel(d,(cx+gap,box[1],box[2],box[3]),True)
    elif kind=="silo":
        r=(92,72,48,20,0,56)[frame]
        if r==0: shaded_bay(d,(cx-38,cy-38,cx+38,cy+38))
        else:
            d.ellipse((cx-42,cy-42,cx+42,cy+42),fill=(7,23,28,255),outline=(19,11,8,255),width=4)
            d.pieslice((cx-r,cy-r,cx,cy+r),90,270,fill=(52,79,82,255),outline=(178,111,48,255),width=3)
            d.pieslice((cx,cy-r,cx+r,cy+r),270,90,fill=(52,79,82,255),outline=(178,111,48,255),width=3)
            d.line((cx,cy-r,cx,cy+r),fill=(224,163,67,255),width=3)
    elif kind=="pen":
        gap=(0,14,40,72,108,34)[frame]; shaded_bay(d,(cx-118,cy-74,cx+118,cy+92))
        if gap<108: panel(d,(cx-150,cy-82,cx-gap,cy+82),True); panel(d,(cx+gap,cy-82,cx+150,cy+82),True)
    else:
        gap=(0,12,34,62,92,28)[frame]; shaded_bay(d,(cx-104,cy-90,cx+104,cy+94))
        if gap<92: panel(d,(cx-120,cy-108,cx-gap,cy+108),True); panel(d,(cx+gap,cy-108,cx+120,cy+108),True)
    if frame==1: d.rectangle((cx+125,cy-128,cx+132,cy-121),fill=(255,208,79,255))

def build_sites():
    for name,cfg in SITES.items():
        p=HERE/f"{name}.png"; base=clean_bg(Image.open(p)); base.save(p)
        cx,cy=round(cfg["pad"][0]*base.width),round(cfg["pad"][1]*base.width)
        strip=Image.new("RGBA",(base.width*6,base.height))
        for f in range(6):
            im=base.copy(); doors(ImageDraw.Draw(im),cx,cy,cfg["kind"],f); strip.paste(im,(f*base.width,0))
        strip.save(HERE/f"{name}_launch_v2.png")

def contact_sheet():
    cw,ch=360,300; sheet=Image.new("RGBA",(cw*4,ch*3),(7,24,29,255)); d=ImageDraw.Draw(sheet)
    for i,name in enumerate(SITES):
        im=Image.open(HERE/f"{name}_launch_v2.png").crop((0,0,Image.open(HERE/f"{name}.png").width,Image.open(HERE/f"{name}.png").height)); im.thumbnail((cw-20,ch-45),Image.Resampling.LANCZOS); x=i*cw; sheet.alpha_composite(im,(x+(cw-im.width)//2,34)); d.text((x+10,10),name.replace('_',' '),fill=(255,216,104,255))
    for j,name in enumerate(NAMES):
        im=Image.open(HERE/f"tide_elite_{name}_preview_lights.png").crop((3*CELL,0,4*CELL,CELL)); im.thumbnail((cw-20,ch-45),Image.Resampling.NEAREST); x=(j%4)*cw; y=ch+(j//4)*ch; sheet.alpha_composite(im,(x+(cw-im.width)//2,y+34)); d.text((x+10,y+10),name.replace('_',' ')+' lit',fill=(255,216,104,255))
    sheet.convert('RGB').save(HERE/'contact_sheet.png')

if __name__=='__main__':
    for n in NAMES: build_lights(n)
    build_sites(); contact_sheet()
