"""Build Tide run C from the painted key sheets. Pillow only; nearest-neighbour resampling."""
from __future__ import annotations

import colorsys
import json
import math
from pathlib import Path

from PIL import Image, ImageDraw, ImageEnhance

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "src~"
CELL = 256
INK = (3, 13, 18)
WATER = [(5, 20, 24), (8, 33, 38), (11, 52, 56), (17, 75, 73), (30, 109, 99), (74, 154, 134)]
MINT = [(9, 47, 46), (22, 94, 83), (49, 151, 120), (93, 204, 158), (124, 242, 192), (213, 255, 231)]
FOAM = [(20, 66, 69), (36, 99, 100), (81, 149, 145), (139, 199, 186), (191, 224, 211), (228, 244, 230)]
SMOKE = [(7, 24, 29), (13, 35, 40), (22, 49, 53), (35, 66, 68), (53, 88, 87), (85, 121, 113)]
STEAM = [(18, 42, 47), (37, 65, 68), (59, 94, 94), (85, 128, 125), (121, 161, 151), (172, 197, 181)]
VIOLET = [(16, 25, 45), (28, 43, 72), (45, 67, 104), (66, 94, 141), (104, 132, 174), (165, 187, 211)]
PINK = [(34, 15, 34), (64, 27, 59), (104, 47, 83), (157, 76, 118), (206, 117, 155), (242, 181, 198)]


def source(name):
    return Image.open(SRC / name).convert("RGBA")


surf = source("painted_surf_keys.png")
smokefire = source("painted_smoke_fire_keys.png")
detail = source("painted_leaks_lights_keys.png")


def key_crop(sheet, box, size, palette, alpha_cap=255, dark_cut=0, hard=False):
    """Reduce painted source to game-pixel scale and a curated Tide material ramp."""
    im = sheet.crop(box)
    im.thumbnail(size, Image.Resampling.NEAREST)
    out = Image.new("RGBA", im.size)
    src = im.load(); dst = out.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b, a = src[x, y]
            val = max(r, g, b)
            if a < 20 or val < dark_cut:
                continue
            # Painted value bands survive; hue is art directed into Tide's palette.
            lum = (max(r, g, b) * .65 + (r + g + b) / 3 * .35) / 255
            idx = min(5, max(0, int(lum * 6.2)))
            if hard and idx == 0:
                col = INK
            else:
                col = palette[idx]
            aa = min(alpha_cap, a)
            if not hard:
                aa = min(alpha_cap, int(aa * (.45 + .55 * lum)))
            dst[x, y] = (*col, aa)
    return out


def crop_grid(sheet, col, row, size, palette, alpha, cut=0, hard=False):
    w, h = sheet.size
    return key_crop(sheet, (round(col*w/4), round(row*h/2), round((col+1)*w/4), round((row+1)*h/2)), size, palette, alpha, cut, hard)


def on_canvas(key, anchor="center", scale=1):
    if scale != 1:
        key = key.resize((max(1, round(key.width*scale)), max(1, round(key.height*scale))), Image.Resampling.NEAREST)
    out = Image.new("RGBA", (CELL, CELL))
    x = (CELL-key.width)//2
    y = 236-key.height if anchor == "bottom" else (CELL-key.height)//2
    out.alpha_composite(key, (x, y))
    return out


def warp(im, phase, mode, anchor):
    """Cyclic painted-key deformation, with zero displacement at attached bases."""
    dst = Image.new("RGBA", im.size)
    pix = im.load(); q = dst.load()
    for y in range(CELL):
        fall = max(0, (236-y)/210) if anchor == "bottom" else 1
        if mode == "smoke":
            dx = round(5*fall*math.sin(phase + y/31))
            dy = round(2*fall*math.sin(phase + y/43))
        elif mode == "water":
            dx = round(4*math.sin(phase + y/17))
            dy = round(2*math.cos(phase + y/29))
        elif mode == "flow":
            dx = round(2*fall*math.sin(phase + y/19))
            dy = round(3*fall*math.cos(phase + y/29))
        else:
            dx = round(1*math.sin(phase)); dy = 0
        sy = y-dy
        if sy < 0 or sy >= CELL: continue
        for x in range(6, CELL-6):
            sx = x-dx
            if 6 <= sx < CELL-6:
                q[x,y] = pix[sx,sy]
    return dst


def tint_alpha(im, factor):
    r,g,b,a = im.split()
    return Image.merge("RGBA", (r,g,b,a.point(lambda x: round(x*factor))))


def sprinkle(im, frame, kind):
    d = ImageDraw.Draw(im, "RGBA")
    # 2-4 px luminous particles; deterministic cyclic orbits.
    n = 12 if kind in ("spray", "bubble_stream", "steam_vent", "plankton_glow") else 6
    for i in range(n):
        th = 2*math.pi*(i/n + frame/8)
        if kind == "spray":
            x=128+round((18+i*4)*math.cos(th)); y=151-round(60*abs(math.sin(th)))+i%3*5
        elif kind in ("bubble_stream", "steam_vent"):
            x=128+round((6+i%4*4)*math.sin(th)); y=222-((i*19+frame*11)%154)
        elif kind == "plankton_glow":
            x=128+round((20+i*5)*math.cos(th)); y=128+round((10+i*4)*math.sin(th))
        else:
            x=128+round((30+i*8)*math.cos(th)); y=128+round((12+i*5)*math.sin(th))
        if 8<=x<246 and 8<=y<246:
            d.rectangle((x,y,x+2+(i%2),y+2+(i%2)),fill=(124,242,192,166))
            d.line((x-2,y+4,x,y+2),fill=(49,151,120,110),width=1)
    return im


def lamp_frame(key, frame, name, count):
    im=on_canvas(key)
    if count == 4:
        factors = [0.13, 0.36, 1.0, 0.38]
        if name == "window_lights": factors=[0.30,0.69,1.0,0.48]
        if name == "strobe_white": factors=[0.09,0.23,1.0,0.22]
    else:
        factors=[0.25,0.37,0.56,0.77,1.0,0.77,0.56,0.37]
    im=tint_alpha(im,factors[frame])
    if name.startswith("beacon") or name == "strobe_white":
        if factors[frame]>.7:
            d=ImageDraw.Draw(im,"RGBA")
            glow=(124,242,192) if name=="beacon_mint" else ((236,226,217) if name=="strobe_white" else (225,145,174))
            for radius,alpha in ((29,18),(22,34),(15,57),(9,92)):
                d.ellipse((128-radius,115-radius,128+radius,115+radius),outline=(*glow,alpha),width=2)
    return im


def beam_frame(key, frame, name):
    im=on_canvas(key)
    # A sweep oscillates, then returns: eight equal phase steps including seam.
    ang=round(22*math.sin(frame*2*math.pi/8))
    return im.rotate(ang,resample=Image.Resampling.NEAREST,center=(128,128))


def make_frames(name, count, key, mode, anchor):
    out=[]
    base=on_canvas(key,anchor)
    for i in range(count):
        ph=i*2*math.pi/count
        if mode=="lamp": im=lamp_frame(key,i,name,count)
        elif mode=="beam": im=beam_frame(key,i,name)
        else:
            im=warp(base,ph,mode,anchor)
            if name=="whirlpool":
                # Quarter turn distributed across a cyclic, rotationally repeating whirlpool.
                im=im.rotate(round(90*i/count),resample=Image.Resampling.NEAREST)
            if name in ("spray","bubble_stream","steam_vent","plankton_glow"):
                im=sprinkle(im,i,name)
            if name in ("flare","flare_b","burn"):
                im=tint_alpha(im, .84+.16*math.sin(ph+1.0))
        # An unobtrusive fixed source point is retained for bottom-attached loops.
        if anchor=="bottom":
            d=ImageDraw.Draw(im,"RGBA")
            c=(25,82,77,140) if mode=="smoke" else (60,161,133,200)
            d.rectangle((126,234,130,237),fill=(*INK,170))
            d.line((127,235,129,235),fill=c,width=1)
        out.append(im)
    return out


# Source keys are deliberately retained as painted input. Additional motion is authored above.
specs=[]
def add(atlas,name,n,key,mode="water",anchor="center",fps=8,use="open water",alpha=1.0):
    specs.append(dict(atlas=atlas,name=name,count=n,key=key,mode=mode,anchor=anchor,fps=fps,use=use,alpha=alpha))

for name,col,pal,cap in [("smoke_a",0,SMOKE,165),("smoke_b",1,STEAM,174),("smoke_c",2,SMOKE,160)]:
    key=key_crop(smokefire,(col*443,0,(col+1)*443,495),(160,210),pal,cap)
    add("smoke",name,8,key,"smoke","bottom",7,"landmarks oilrig/refineryrig and sites vent_stack",.66)
add("flames","flare",8,key_crop(smokefire,(1330,0,1774,495),(118,170),MINT,255,0,True),"flow","bottom",10,"refineryrig flare stacks",.82)
add("flames","flare_b",8,key_crop(smokefire,(0,500,440,887),(128,145),MINT,255,0,True),"flow","bottom",10,"oilrig and pump_platform flare stacks",.78)
add("flames","burn",8,key_crop(smokefire,(290,500,990,887),(205,115),MINT,235,0,True),"flow","center",8,"oil slick by tanker wreck",.62)

surf_names=["wave_crest_a","wave_crest_b","foam_ring","ripple","wake","whirlpool","spray","caustic"]
for j,name in enumerate(surf_names):
    key=crop_grid(surf,j%4,j//4,(216,172) if name not in ("wake","spray") else (198,198),FOAM if name!="caustic" else MINT,170 if name!="spray" else 205)
    if name=="foam_ring":
        # Remove the painted pylon; the loop is an overlay around a separate solid prop.
        p=key.load();cx=key.width//2;cy=key.height//2
        for yy in range(key.height):
            for xx in range(key.width):
                if abs(xx-cx)<18 and abs(yy-cy)<35: p[xx,yy]=(0,0,0,0)
    if name=="wake":
        # Remove the key's boat so the wake can trail any vessel.
        p=key.load();cx=key.width//2
        for yy in range(key.height):
            for xx in range(key.width):
                if abs(xx-cx)<10 and yy>key.height*.57:p[xx,yy]=(0,0,0,0)
    add("surf",name,8,key,"water","bottom" if name=="spray" else "center",8,"water near rigs, pilings and vessels" if name!="caustic" else "sunken_city shallow water",.46 if name!="spray" else .58)

add("leaks","bubble_stream",4,key_crop(smokefire,(875,500,1320,887),(110,170),MINT,175),"flow","bottom",8,"submerged pipe outlets",.65)
add("leaks","steam_vent",4,key_crop(smokefire,(445,0,880,495),(105,160),STEAM,170),"smoke","bottom",7,"sites vent_stack and pipe seams",.56)
add("leaks","pipe_drip",4,key_crop(detail,(0,0,384,512),(122,148),MINT,190,42),"flow","bottom",6,"pipes pipe_straight/bend outlets",.55)
add("leaks","rain_curtain",4,key_crop(detail,(384,0,768,512),(150,190),FOAM,112,34),"water","center",12,"storm over all ground classes",.28)
add("leaks","plankton_glow",4,key_crop(smokefire,(1330,500,1774,887),(154,154),VIOLET,132),"water","center",6,"dark water and sunken_city",.42)

add("lights","beacon_mint",4,key_crop(detail,(768,0,1152,512),(92,128),MINT,255,50,True),"lamp","center",4,"lighthouse and pump_platform beacon",.85)
add("lights","beacon_pink",4,key_crop(detail,(0,512,384,1024),(75,125),PINK,255,55,True),"lamp","center",4,"rare rig warning lamp",.55)
add("lights","window_lights",4,key_crop(detail,(384,512,768,1024),(152,115),MINT,230,42),"lamp","center",5,"sunken_city and rig windows",.5)
add("lights","strobe_white",4,key_crop(detail,(768,512,1152,1024),(88,115),FOAM,255,55,True),"lamp","center",5,"landing pad/rig warning strobe",.66)
add("lights","searchlight_sweep",8,key_crop(detail,(1152,512,1536,1024),(184,140),FOAM,126,47),"beam","center",8,"oilrig and ship searchlights",.35)
add("lights","lighthouse_beam",8,key_crop(detail,(1152,512,1536,1024),(205,152),MINT,140,47),"beam","center",7,"lighthouse_00",.37)


def write():
    pages={}; manifest=[]
    for s in specs:
        frames=make_frames(s['name'],s['count'],s['key'],s['mode'],s['anchor'])
        names=[]
        for i,im in enumerate(frames):
            group=s['atlas']; idx=sum(1 for p in pages.get(group,[]) for e in p['sprites'])
            page=idx//16
            while len(pages.setdefault(group,[]))<=page:
                pages[group].append(dict(image=Image.new('RGBA',(1024,1024)),sprites=[]))
            p=pages[group][page]; cell=idx%16; x=(cell%4)*256;y=(cell//4)*256
            p['image'].alpha_composite(im,(x,y))
            n=f"{s['name']}_{i:02d}"
            p['sprites'].append(dict(n=n,x=x,y=1024-y-256,w=256,h=256))
            names.append(n)
        manifest.append(dict(name=s['name'],atlas=s['atlas'],frames=names,fps=s['fps'],anchor=[128,236] if s['anchor']=='bottom' else [128,128],anchor_rule='Every frame has an unchanged source point at x=128, y=236 from cell top; no sideways/base wobble.' if s['anchor']=='bottom' else 'cell centre',use=s['use'],draw_alpha=s['alpha'],painted_key='src~/painted_surf_keys.png' if s['atlas']=='surf' else ('src~/painted_smoke_fire_keys.png' if s['atlas'] in ('smoke','flames') or s['name'] in ('bubble_stream','steam_vent','plankton_glow') else 'src~/painted_leaks_lights_keys.png')))
    files={}
    for group,pgs in pages.items():
        files[group]=[]
        for n,p in enumerate(pgs):
            stem=group if n==0 else f'{group}_{n+1}'
            p['image'].save(ROOT/f'{stem}.png')
            (ROOT/f'{stem}.json').write_text(json.dumps({'sprites':p['sprites']},indent=2)+'\n')
            files[group].append(stem+'.png')
    # A page map is required because 1024² at 256 cells holds only sixteen poses.
    doc=json.loads((ROOT/'manifest.json').read_text())
    doc['run_c']=dict(source='Built-in image generation painted key sheets in src~; nearest-neighbour art-directed cyclic in-betweens',cell_size=256,atlas_size=1024,rect_y='from bottom (Unity)',pages=files,loops=manifest)
    (ROOT/'manifest.json').write_text(json.dumps(doc,indent=2)+'\n')
    print('built',len(manifest),'loops',sum(len(v) for v in files.values()),'atlas pages')

if __name__=='__main__':write()
