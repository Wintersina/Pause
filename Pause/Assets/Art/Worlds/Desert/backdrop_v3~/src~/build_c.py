"""Build run C from three generated painted source sheets. New outputs only."""
from __future__ import annotations

import json
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SRC = Path(__file__).resolve().parent
SOURCES = {k: Image.open(SRC / f"candidate_c_{k}.png").convert("RGBA") for k in ("smoke", "sand", "lights")}
R = Image.Resampling.NEAREST
INK = np.array([12, 12, 25], np.uint8)
SAND = np.array([[47, 45, 51], [69, 65, 65], [85, 80, 73], [105, 98, 80], [124, 115, 91], [151, 139, 109], [179, 166, 131], [217, 203, 160], [230, 220, 192]], np.uint8)
SMOKE = np.array([[15, 17, 27], [24, 25, 39], [34, 35, 49], [47, 47, 61], [61, 59, 69], [78, 73, 76], [96, 88, 80], [128, 117, 100], [170, 157, 132]], np.uint8)
METAL = np.array([[11, 12, 22], [27, 27, 39], [39, 38, 46], [54, 50, 51], [69, 59, 55], [86, 68, 59], [108, 81, 65], [129, 95, 71], [150, 111, 78]], np.uint8)
LIGHT = (255, 241, 200)

LOOPS = [
    ("smoke_a", 8, "smoke", 0, "smoke", "smoke", 6, "refinery stacks; buried hull vents", .65, True),
    ("smoke_b", 8, "smoke", 1, "steam", "smoke", 7, "pipe vents; dry wells", .65, True),
    ("dustdevil", 8, "smoke", 2, "sand", "dust", 8, "open dune sea; worm-ridge wake", .60, True),
    ("sandplume", 8, "smoke", 3, "sand", "dust", 8, "dune crests; buried gantries", .60, True),
    ("sandfall", 8, "sand", 0, "sand", "dust_2", 8, "canyon ledges; refinery walls", .68, True),
    ("grit_gust", 8, "sand", 2, "sand", "dust_2", 10, "dune sea; crawler caravan", .43, False),
    ("heat_vent", 8, "lights", 2, "shimmer", "dust_3", 8, "heat vents; glass flats", .28, True),
    ("heat_shimmer", 8, "lights", 2, "shimmer", "shimmer", 7, "dune crests; machine exhaust", .20, True),
    ("mirage_glint", 8, "lights", 2, "shimmer", "shimmer", 6, "salt flats; glass pans", .27, False),
    ("glass_sparkle", 8, "lights", 3, "sparkle", "shimmer_2", 8, "fused glass; dead solar farms", .55, False),
    ("steam_vent", 4, "smoke", 1, "steam", "leaks", 7, "pipe vents; drill heads", .65, True),
    ("pipe_drip_sand", 4, "sand", 3, "sand", "leaks", 8, "broken pipes; refineries", .66, True),
    ("grit_rain", 4, "sand", 0, "rain", "leaks", 10, "gantry underhangs; canyon walls", .45, False),
    ("sand_slide", 4, "sand", 1, "sand", "leaks", 7, "dune lee; worm trail", .68, True),
    ("beacon_white", 4, "lights", 0, "beacon", "lights", 5, "mast beacons; ruined towers", 1.0, True),
    ("beacon_pale_gold", 4, "lights", 0, "beacon", "lights", 5, "crawler masts; dry wells", 1.0, True),
    ("window_lights", 4, "lights", 1, "window", "lights", 5, "refinery windows; buried hulls", 1.0, False),
    ("strobe_white", 4, "lights", 0, "beacon", "lights_2", 7, "drill heads; solar array posts", 1.0, True),
    ("work_lamp_flicker", 4, "lights", 0, "work", "lights_2", 6, "pipe runs; crawler bay", 1.0, True),
]

def source_crop(sheet, row, col):
    im = SOURCES[sheet]
    edge = im.width / 4
    # Inset gutters removes bleed from neighbouring generated concepts.
    return im.crop((round(col*edge+8), round(row*edge+8), round((col+1)*edge-8), round((row+1)*edge-8)))

def painted_piece(sheet, row, col, kind):
    image = source_crop(sheet, row, col)
    a = np.asarray(image)[:, :, 3]
    yy, xx = np.where(a > 55)
    if len(xx):
        image = image.crop((max(0, xx.min()-1), max(0, yy.min()-1), min(image.width, xx.max()+2), min(image.height, yy.max()+2)))
    target = {"smoke": (76, 91), "steam": (72, 87), "sand": (84, 94), "rain": (98, 92),
              "shimmer": (96, 50), "sparkle": (102, 60), "beacon": (65, 95), "work": (65, 90), "window": (108, 58)}[kind]
    if sheet == "sand" and row == 3: target=(70,94)
    image.thumbnail(target, R)
    image = image.resize(target, R)
    raw = np.asarray(image).copy()
    rgb = raw[:, :, :3].astype(float)
    alpha = raw[:, :, 3].copy()
    mx, mn = rgb.max(2), rgb.min(2)
    saturation = np.divide(mx-mn, np.maximum(mx, 1))
    # The generator produced isolated red/yellow fringe noise. Remove it before quantisation.
    bad = ((rgb[:, :, 0] > rgb[:, :, 1]*1.35) & (rgb[:, :, 0] > rgb[:, :, 2]*1.55) & (saturation > .48)) | ((rgb[:, :, 0] > 140) & (rgb[:, :, 1] > 140) & (rgb[:, :, 2] < 60))
    alpha[bad | (alpha < 58)] = 0
    lum = rgb @ np.array([.25, .60, .15])
    ranks = np.clip((lum / 255 * 8).astype(int), 0, 8)
    if kind == "smoke":
        ramp = SMOKE
    elif kind in ("beacon", "work", "window"):
        ramp = METAL
    else:
        ramp = SAND
    raw[:, :, :3] = ramp[ranks]
    if kind in ("smoke", "steam", "sand", "rain"):
        alpha = np.minimum(alpha, 174)
    if kind == "shimmer":
        alpha = np.minimum(alpha, 65)
    if kind == "sparkle":
        alpha = np.minimum(alpha, 145)
    raw[:, :, 3] = alpha
    return Image.fromarray(raw, "RGBA")

def twinkle(draw, x, y, size=1, value=LIGHT, alpha=190):
    draw.rectangle((x-size, y, x+size, y), fill=(*value, alpha))
    draw.rectangle((x, y-size, x, y+size), fill=(*value, alpha))

def frame_for(spec, index):
    name, count, sheet, row, kind, atlas, fps, use, draw_alpha, attached = spec
    if kind not in ("beacon","work","window") and index%2:
        left=frame_for(spec,index-1)
        right=frame_for(spec,(index+1)%count)
        # A transparency transition keeps the generated grain and hard pixel edges.
        # No spatial resampling, ordered dither, or blur is applied.
        return Image.blend(left,right,.5)
    key = index//2 if count==8 else (index//2 if kind not in ("beacon","work","window") else index)
    # Circular phase with painted key frames and pixel-clean transitions.
    if kind == "beacon": key = 1
    if kind == "work": key = 3
    if kind == "window": key = 1
    piece = painted_piece(sheet, row, key, kind)
    canvas = Image.new("RGBA", (128, 128))
    if kind in ("beacon", "work", "window"):
        base = np.asarray(piece).copy()
        bright = base[:, :, :3].max(2) > 75
        base[:, :, :3] = np.minimum(base[:, :, :3], 72)
        base[:, :, 3] = np.minimum(base[:, :, 3], 105)
        base[:, :, 3][bright] = np.minimum(base[:, :, 3][bright], 25)
        piece = Image.fromarray(base, "RGBA")
    x = 64-piece.width//2
    if attached:
        pa=np.asarray(piece)[:,:,3]
        _,bx=np.where(pa[-8:,:]>50)
        if len(bx):x=64-round(float(bx.mean()))
    y = (118-piece.height) if attached else (64-piece.height//2)
    if kind == "shimmer": y = 68 if name == "heat_shimmer" else 56
    canvas.alpha_composite(piece, (x, y))
    d = ImageDraw.Draw(canvas, "RGBA")
    if kind in ("sand", "steam", "smoke", "rain"):
        for j in range(9 if kind == "sand" else 5):
            px = 26 + (j*37 + index*13 + row*11) % 77
            py = 29 + (j*23 - index*8 + row*17) % 77
            c = (202, 188, 152, 115) if kind != "smoke" else (132, 122, 103, 95)
            d.rectangle((px, py, px+1, py+1), fill=c)
            if j % 3 == 0: d.line((px-4, py+1, px-1, py+1), fill=(c[0],c[1],c[2],70))
    if kind == "shimmer":
        for j in range(5):
            px = 17 + (j*23+index*7) % 80
            py = 83 + j*3 if name == "heat_shimmer" else 52+j*5
            d.line((px,py,px+12,py), fill=(220,208,178,25+j*4),width=1)
        if name == "heat_vent":
            twinkle(d,64+(index*3)%9-4,37+(index*11)%25,1,alpha=180)
    if kind == "sparkle":
        for j in range(7):
            px = 22+(j*19+index*7)%83; py=42+(j*17+index*11)%45
            twinkle(d,px,py,1,alpha=90+((j+index)%3)*35)
    if kind in ("beacon", "work", "window"):
        # OFF, igniting, FULL ON, fading. Bloom is hard pixel-stepped at authoring resolution.
        levels = [0, .24, 1.0, .42]
        strength = levels[index]
        if name == "window_lights": strength = [0,.38,1,.58][index]
        if name == "strobe_white": strength = [0,.08,1,.06][index]
        if name == "work_lamp_flicker": strength = [.04,.35,1,.20][index]
        if name == "beacon_pale_gold": core=(250,240,204)
        else: core=LIGHT
        positions = [(64,56)] if kind == "beacon" else ([(88,59)] if kind == "work" else [(37,64),(60,63),(84,62)])
        if strength:
            glow = Image.new("RGBA",(128,128)); gd=ImageDraw.Draw(glow,"RGBA")
            bloom_steps=((28,25),(19,50),(11,95),(5,220)) if kind != "window" else ((13,20),(9,40),(6,70),(3,150))
            for cx,cy in positions:
                for radius,aa in bloom_steps:
                    gd.ellipse((cx-radius,cy-radius,cx+radius,cy+radius), fill=(*core,round(aa*strength)))
            canvas = Image.alpha_composite(canvas,glow)
            d=ImageDraw.Draw(canvas,"RGBA")
            for cx,cy in positions:
                if strength > .15:
                    r=1 if strength < .5 else 3
                    d.rectangle((cx-r,cy-r,cx+r,cy+r), fill=(*core,round(255*strength)))
                if strength > .8: d.rectangle((cx-1,cy-1,cx+1,cy+1),fill=(*LIGHT,255))
    if count==8 and index>=4:
        # Extra fine phase detail makes the return half different from the four source keys.
        d=ImageDraw.Draw(canvas,"RGBA")
        for j in range(5):
            px=38+(j*29+index*13)%52; py=44+(j*17+index*7)%59
            d.rectangle((px,py,px+1,py+1),fill=(211,199,167,85 if kind not in ("shimmer","sparkle") else 35))
    if attached:
        d=ImageDraw.Draw(canvas,"RGBA")
        d.rectangle((0,118,127,118),fill=(0,0,0,0))
        foot=(75,67,57,255) if kind in ("beacon","work") else ((159,146,115,55) if kind=="shimmer" else (159,146,115,150))
        d.point((64,118),fill=foot)
    return canvas.resize((256,256),R)

def main():
    atlases={}; sprites={}; loops=[]
    for spec in LOOPS:
        name,count,_,_,kind,atlas,fps,use,opacity,attached=spec
        if atlas not in atlases: atlases[atlas]=Image.new("RGBA",(1024,1024)); sprites[atlas]=[]
        start=len(sprites[atlas]); assert start+count<=16
        for i in range(count):
            cell=start+i; col,row=cell%4,cell//4
            fr=frame_for(spec,i)
            atlases[atlas].paste(fr,(col*256,row*256))
            sprites[atlas].append({"n":f"{name}_{i:02d}","x":col*256,"y":768-row*256,"w":256,"h":256})
        loops.append({"name":name,"atlas":atlas+".png","frames":count,"fps":fps,
            "anchor_px_top_left":[128,236] if attached else [128,128],
            "ground_attached":attached,"base_rule":"every frame's emitter/base remains at (128,236), no sideways offset or wobble" if attached else "free drifting overlay",
            "intended_use":use,"draw_alpha":opacity,
            "source":"painted generated key frames; nearest-neighbour x2; animated detail and blink states"})
    for atlas,im in atlases.items():
        im.save(ROOT/(atlas+".png"))
        (ROOT/(atlas+".json")).write_text(json.dumps({"sprites":sprites[atlas]},indent=2)+"\n")
    manifest={"run_c":{"loops":loops,"sources":[f"src~/candidate_c_{s}.png" for s in SOURCES],
        "atlas_convention":"1024x1024 RGBA, 256 cells, JSON y from bottom",
        "selection":"Three generated candidates inspected; smoke source selected for vapor/dust devil, sand source for grain/ledge/pipe, lights source for fixtures/shimmer. Saturated generated edge artifacts removed."}}
    (ROOT/"manifest_c.json").write_text(json.dumps(manifest,indent=2)+"\n")
    preview(atlases,loops)

def preview(atlases,loops):
    # Every animation frame at the authored x2 scale, on dark art-review ground.
    out=Image.new("RGB",(2048,len(loops)*258),(11,11,26))
    dr=ImageDraw.Draw(out)
    for ri,loop in enumerate(loops):
        atlas=atlases[loop["atlas"][:-4]]
        js=json.loads((ROOT/(loop["atlas"][:-4]+".json")).read_text())["sprites"]
        for fi in range(loop["frames"]):
            s=next(s for s in js if s["n"]==f'{loop["name"]}_{fi:02d}')
            row=3-s["y"]//256
            fr=atlas.crop((s["x"],row*256,s["x"]+256,row*256+256))
            out.paste(fr,(fi*256,ri*258),fr)
            if loop["name"] in ("beacon_white","beacon_pale_gold","window_lights","strobe_white","work_lamp_flicker"):
                a=np.asarray(fr).astype(float); lum=a[:,:,:3]@np.array([.2126,.7152,.0722]); value=int(np.sum(lum*a[:,:,3]/255))
                dr.text((fi*256+5,ri*258+238),str(value),fill=(224,216,196))
    out.save(ROOT/"preview_c.png")
    ground=Image.open(ROOT/"v1/mid.png").convert("RGBA").resize((600,1300),R)
    gr=np.asarray(ground).copy();gr[:,:,:3]=(gr[:,:,:3].astype(float)*.62).astype(np.uint8)
    ground=Image.fromarray(gr,"RGBA")
    gif=[]
    places=[(84,95),(390,155),(222,230),(415,340),(85,410),(300,470),(175,525),(400,570),(85,630),(340,680),(180,735),(410,770),(90,820),(265,880),(475,930),(100,990),(340,1030),(195,1100),(455,1180)]
    for t in range(8):
        canvas=ground.copy()
        for loop,pos in zip(loops,places):
            atlas=atlases[loop["atlas"][:-4]]; js=json.loads((ROOT/(loop["atlas"][:-4]+".json")).read_text())["sprites"]
            s=next(s for s in js if s["n"]==f'{loop["name"]}_{t%loop["frames"]:02d}')
            row=3-s["y"]//256; fr=atlas.crop((s["x"],row*256,s["x"]+256,row*256+256)).resize((120,120),R)
            a=fr.getchannel("A").point(lambda x:int(x*min(1,loop["draw_alpha"]*1.3)))
            fr.putalpha(a);canvas.alpha_composite(fr,(pos[0]-60,pos[1]-60))
        gif.append(canvas.convert("RGB"))
    gif[0].save(ROOT/"preview_c.gif",save_all=True,append_images=gif[1:],duration=130,loop=0,optimize=False)

if __name__=="__main__": main()
