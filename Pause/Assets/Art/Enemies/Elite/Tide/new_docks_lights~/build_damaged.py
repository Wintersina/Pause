#!/usr/bin/env python3
"""Restore damaged Tide engine bells and build weak six-frame burn loops."""
from pathlib import Path
import importlib.util, random
from PIL import Image, ImageDraw

HERE=Path(__file__).resolve().parent; ELITES=HERE.parents[1]; CELL=384; FRAMES=6
SHIPS={
 'riptide': {'nozzles':[(175,286),(210,286)],'flame':((20,109,137),(58,205,224),(213,255,255)),'half':11,'lengths':(19,28,14,24,12,22)},
 'bathyscaphe': {'nozzles':[(148,266),(237,266)],'flame':((31,116,129),(71,221,225),(224,255,242)),'half':15,'lengths':(18,27,13,24,12,21)},
 'trawler': {'nozzles':[(157,284),(226,284)],'flame':((21,103,135),(59,202,231),(221,255,255)),'half':13,'lengths':(18,29,15,25,13,22)},
 'thunderfin': {'nozzles':[(170,278),(212,278)],'flame':((19,95,133),(56,210,235),(224,255,255)),'half':12,'lengths':(17,27,13,24,11,21)},
 'pearl_warden': {'nozzles':[(126,278),(258,278)],'flame':((18,113,131),(54,226,203),(222,255,246)),'half':14,'lengths':(18,28,14,25,12,22)},
}
spec=importlib.util.spec_from_file_location('tide_build_art',HERE/'build_art.py'); LIGHTS=importlib.util.module_from_spec(spec); spec.loader.exec_module(LIGHTS)

def clean(name,cfg):
    src=Image.open(ELITES/f'Tide/tide_elite_{name}.png').convert('RGBA'); parked=src.crop((0,0,CELL,CELL)); out=src.crop((6*CELL,0,7*CELL,CELL)).copy()
    for x192,y192 in cfg['nozzles']:
        x,y=x192,y192; half=cfg['half']; box=(max(0,x-half),max(0,y-18),min(CELL,x+half+1),CELL); out.paste(parked.crop(box),box)
    return LIGHTS.remove_pink(out)

def flame(d,x,y,length,width,colors,rng,dead=False):
    outer,mid,core=colors
    if dead:
        d.rectangle((x-3,y,x+3,y+3),fill=(*outer,190))
        if rng.random()<.5: d.rectangle((x-1,y+5,x+1,y+7),fill=(*mid,190))
        return
    for row in range(length):
        t=row/max(1,length-1); taper=max(.35,1-.78*t); jitter=rng.choice((-1,0,0,0,1)) if row>3 else 0; half=max(1,round(width*taper*(.72 if row>length*.58 else 1)))
        color=outer if row<2 else mid if row<length*.7 else core
        if row>length*.45 and rng.random()<.17+.18*t: continue
        d.rectangle((x-half+jitter,y+row,x+half+jitter,y+row+2),fill=(*color,220 if row<length*.65 else 175))
    for _ in range(1+rng.randrange(2)):
        yy=y+length+rng.randrange(2,9); xx=x+rng.randrange(-max(2,width//2),max(3,width//2+1))
        if yy<CELL-2: d.rectangle((xx,yy,xx+2,yy+2),fill=(*mid,165))

def build(name,cfg):
    hull=clean(name,cfg); frames=[]
    for f in range(FRAMES):
        out=hull.copy(); ov=Image.new('RGBA',(CELL,CELL)); d=ImageDraw.Draw(ov); rng=random.Random(1700+f*37)
        for i,(x,y) in enumerate(cfg['nozzles']):
            phase=(f*2+i*3)%FRAMES; flame(d,x,y+4,cfg['lengths'][phase],8+(i%2)*2,cfg['flame'],rng,dead=(f+i*2)%6==2)
        out.alpha_composite(ov); frames.append(out)
    strip=Image.new('RGBA',(CELL*FRAMES,CELL))
    for i,im in enumerate(frames): strip.alpha_composite(im,(i*CELL,0))
    strip.save(HERE/'damaged'/f'tide_elite_{name}_damaged.png')
    src=Image.open(ELITES/f'Tide/tide_elite_{name}.png').convert('RGBA'); points=LIGHTS.feature_points(src.crop((0,0,CELL,CELL))); lights=Image.new('RGBA',(CELL*FRAMES,CELL))
    for f in range(FRAMES): lights.alpha_composite(LIGHTS.light_layer(points,LIGHTS.COLORS[name],f+7,.54,True),(f*CELL,0))
    lights.save(HERE/'damaged'/f'tide_elite_{name}_damaged_lights.png')
    preview=Image.new('RGBA',strip.size)
    for i,im in enumerate(frames): preview.alpha_composite(Image.alpha_composite(im,lights.crop((i*CELL,0,(i+1)*CELL,CELL))),(i*CELL,0))
    preview.save(HERE/'damaged'/f'tide_elite_{name}_damaged_preview.png')
    fs=[preview.crop((i*CELL,0,(i+1)*CELL,CELL)) for i in range(FRAMES)]
    fs[0].save(HERE/'damaged'/f'tide_elite_{name}_damaged.gif',save_all=True,append_images=fs[1:],duration=130,loop=0,disposal=2)
    return strip

if __name__=='__main__':
    results={n:build(n,c) for n,c in SHIPS.items()}; sheet=Image.new('RGBA',(1440,960),(7,24,29,255)); d=ImageDraw.Draw(sheet)
    for i,(n,strip) in enumerate(results.items()):
        x=(i%3)*480; y=(i//3)*480; d.text((x+10,y+10),n.replace('_',' ')+' damaged',fill=(255,216,104,255))
        for f in range(FRAMES): sheet.alpha_composite(strip.crop((f*CELL,0,(f+1)*CELL,CELL)).resize((145,145),Image.Resampling.NEAREST),(x+8+(f%3)*155,y+42+(f//3)*165))
    sheet.convert('RGB').save(HERE/'damaged/contact_sheet.png')
