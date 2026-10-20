#!/usr/bin/env python3
"""Build the six-frame weak damaged-engine loops for Ember elites."""
from pathlib import Path
import importlib.util, random
from PIL import Image, ImageDraw

HERE=Path(__file__).resolve().parent; ROOT=HERE.parent; CELL=384; COUNT=6
SHIPS={
 "ash_wraith":{"nozzles":[(167,244),(217,244)],"flame":((112,44,18),(240,112,24),(255,220,112)),"half":12,"lengths":(17,25,12,22,10,19)},
 "brass_vulture":{"nozzles":[(192,292)],"flame":((118,44,16),(244,120,25),(255,224,120)),"half":14,"lengths":(18,29,14,25,12,22)},
 "cauterizer":{"nozzles":[(135,302),(248,302)],"flame":((120,42,14),(246,118,22),(255,226,124)),"half":13,"lengths":(18,27,13,24,11,21)},
 "coalrunner":{"nozzles":[(148,284),(192,284),(237,284)],"flame":((112,38,12),(235,106,20),(255,220,108)),"half":12,"lengths":(17,26,12,23,10,20)},
 "kilnback":{"nozzles":[(162,282),(224,282)],"flame":((120,43,13),(245,121,24),(255,226,124)),"half":13,"lengths":(18,28,14,24,11,22)},
 "sunstoke":{"nozzles":[(160,272),(223,273)],"flame":((130,45,12),(250,126,26),(255,231,132)),"half":13,"lengths":(19,29,14,25,12,22)},
}
spec=importlib.util.spec_from_file_location("ember_build_art",HERE/"build_art.py"); art=importlib.util.module_from_spec(spec); spec.loader.exec_module(art)

def clean(name,cfg):
    src=art.emberize(Image.open(ROOT/f"ember_elite_{name}.png").convert("RGBA")); parked=src.crop((0,0,CELL,CELL)); out=src.crop((7*CELL,0,8*CELL,CELL)).copy()
    for x,y in cfg["nozzles"]:
        h=cfg["half"]; box=(max(0,x-h),max(0,y-20),min(CELL,x+h+1),CELL); out.paste(parked.crop(box),box)
    return out

def flame(d,x,y,length,width,colors,rng,dead=False):
    outer,mid,core=colors
    if dead:
        d.rectangle((x-3,y,x+3,y+3),fill=(*outer,210))
        if rng.random()<.55: d.rectangle((x-1,y+5,x+1,y+7),fill=(*mid,190))
        return
    for row in range(length):
        t=row/max(1,length-1); half=max(1,round(width*(1-.75*t)*(.72 if row>length*.58 else 1))); j=rng.choice((-1,0,0,0,1)) if row>3 else 0
        if row>length*.45 and rng.random()<.18+.16*t: continue
        c=outer if row<2 else mid if row<length*.7 else core
        d.rectangle((x-half+j,y+row,x+half+j,y+row+2),fill=(*c,220 if row<length*.65 else 180))
    for _ in range(1+rng.randrange(2)):
        yy=y+length+rng.randrange(2,8)
        if yy<CELL-2:
            xx=max(0,min(CELL-3,x+rng.randrange(-width//2,width//2+1))); d.rectangle((xx,yy,xx+2,yy+2),fill=(*mid,165))

def build(name,cfg):
    hull=clean(name,cfg); frames=[]
    for f in range(COUNT):
        out=hull.copy(); lay=Image.new("RGBA",(CELL,CELL)); d=ImageDraw.Draw(lay); rng=random.Random(900+f*41)
        for i,(x,y) in enumerate(cfg["nozzles"]):
            phase=(f*2+i*3)%COUNT; flame(d,x,y+5,cfg["lengths"][phase],7+(i%2)*2,cfg["flame"],rng,(f+i*2)%6==2)
        out.alpha_composite(lay); frames.append(out)
    strip=Image.new("RGBA",(CELL*COUNT,CELL));
    for i,im in enumerate(frames): strip.alpha_composite(im,(i*CELL,0))
    strip.save(HERE/"damaged"/f"ember_elite_{name}_damaged.png")
    src=art.emberize(Image.open(ROOT/f"ember_elite_{name}.png").convert("RGBA")); pts=art.points(src.crop((0,0,CELL,CELL))); lights=Image.new("RGBA",(CELL*COUNT,CELL))
    for f in range(COUNT): lights.alpha_composite(art.light_layer(pts,name,f+7,.50,True),(f*CELL,0))
    lights.save(HERE/"damaged"/f"ember_elite_{name}_damaged_lights.png")
    return strip,lights,frames

def main():
    results={n:build(n,c) for n,c in SHIPS.items()}
    for n,(strip,lights,frames) in results.items():
        prev=Image.new("RGBA",strip.size)
        for i,im in enumerate(frames): prev.alpha_composite(Image.alpha_composite(im,lights.crop((i*CELL,0,(i+1)*CELL,CELL))),(i*CELL,0))
        prev=art.emberize(prev); prev.putalpha(prev.getchannel("A").point(lambda v: 0 if v<16 else v)); prev.save(HERE/"damaged"/f"ember_elite_{n}_damaged_preview.png")
        fs=[prev.crop((i*CELL,0,(i+1)*CELL,CELL)) for i in range(COUNT)]; fs[0].save(HERE/"damaged"/f"ember_elite_{n}_damaged.gif",save_all=True,append_images=fs[1:],duration=130,loop=0,disposal=2)
    sheet=Image.new("RGBA",(1440,960),(12,12,10,255)); d=ImageDraw.Draw(sheet)
    for j,n in enumerate(SHIPS):
        x=(j%3)*480; y=(j//3)*480; d.text((x+12,y+10),n.replace('_',' ')+' damaged',fill=(255,220,130,255))
        for f in range(COUNT): sheet.alpha_composite(results[n][0].crop((f*CELL,0,(f+1)*CELL,CELL)).resize((145,145),Image.Resampling.NEAREST),(x+12+(f%3)*155,y+42+(f//3)*175))
    sheet.convert("RGB").save(HERE/"damaged/contact_sheet.png")
if __name__=="__main__": main()
