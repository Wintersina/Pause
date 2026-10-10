"""Build the Verdant ultimate from the painted imagegen source in candidates/.

All animation editing is on 64-pixel native cells. Final sheets use 2x nearest.
"""
from pathlib import Path
import colorsys
import math
import random
from PIL import Image, ImageDraw, ImageOps
import numpy as np

ROOT = Path(__file__).resolve().parents[1]
SOURCE = Path(__file__).resolve().parent / "candidates"
NEAREST = Image.Resampling.NEAREST
LANCZOS = Image.Resampling.LANCZOS

PINK = [(79, 18, 70), (124, 28, 106), (176, 34, 145), (214, 43, 178),
        (255, 79, 216), (255, 138, 230), (255, 224, 248)]
LEAF = [(11, 11, 26), (23, 43, 27), (30, 58, 36), (39, 73, 43),
        (47, 90, 51), (60, 105, 59), (74, 122, 68)]
BARK = [(11, 11, 26), (40, 29, 21), (58, 42, 26), (74, 54, 31),
        (90, 63, 38), (112, 82, 49), (138, 108, 58)]


def load(name):
    return Image.open(SOURCE / name).convert("RGBA")


def painted(im, size, crop=None):
    if crop:
        im = im.crop(crop)
    # Downsample the source painting to the native editing grid, then restrict
    # the material hues to Verdant's body ramps and hostile pink ramp.
    im = im.resize(size, LANCZOS)
    arr = np.array(im)
    out = np.zeros_like(arr)
    for y in range(size[1]):
        for x in range(size[0]):
            r, g, b, a = map(int, arr[y, x])
            if a < 18:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            if 45 <= h * 360 <= 180 and s > .30:
                ramp = LEAF
                k = min(6, max(1, int(v * 7)))
            elif 18 <= h * 360 < 55 and s > .36:
                ramp = BARK
                k = min(6, max(1, int(v * 7)))
            else:
                ramp = PINK
                k = min(6, max(1, int(v * 7)))
                if v > .82 and s < .25:
                    k = 6
            out[y, x, :3] = ramp[k]
            out[y, x, 3] = 24 if a < 48 else (64 if a < 96 else (128 if a < 160 else (200 if a < 220 else 255)))
    return Image.fromarray(out, "RGBA")


def stamp(cell, source, box, angle=0, opacity=255, mirror=False):
    x, y, w, h = box
    im = ImageOps.mirror(source) if mirror else source
    im = im.resize((w, h), LANCZOS)
    if angle:
        im = im.rotate(angle, NEAREST, expand=False)
    im = painted(im, im.size)
    if opacity < 255:
        a = im.getchannel("A").point(lambda q: q * opacity // 255)
        im.putalpha(a)
    cell.alpha_composite(im, (x, y))


def sparks(cell, seed, n, radius, upward=False):
    rng = random.Random(seed)
    d = ImageDraw.Draw(cell)
    for _ in range(n):
        a = rng.random() * math.tau
        r = radius * (.35 + .65 * rng.random())
        x = int(32 + math.cos(a) * r)
        y = int((38 if upward else 32) + math.sin(a) * r)
        if upward and y > 39:
            y = 39 - (y - 39)
        if not (4 <= x < 60 and 4 <= y < 60):
            continue
        col = PINK[rng.choice((3, 4, 5, 6))]
        d.point((x, y), fill=(*col, 255))
        if rng.random() < .25 and y > 7:
            d.point((x, y-1), fill=(*PINK[3], 180))


def cell64():
    return Image.new("RGBA", (64, 64))


def build_main():
    organic, cross, impact = load("muzzle_organic.png"), load("muzzle_cross.png"), load("impact_spray.png")
    cells = []
    sizes = [6, 10, 15, 22, 30, 38, 45, 50]
    for i, sz in enumerate(sizes):
        c = cell64()
        src = organic if i < 5 else cross
        stamp(c, src, ((64-sz)//2, (64-sz)//2, sz, sz), angle=(i*7)%20-8)
        sparks(c, 430+i, max(1, i*2), min(26, 10+i*2))
        if i == 7:
            d=ImageDraw.Draw(c)
            d.ellipse((25,25,39,39),fill=(*PINK[6],255))
            d.line((32,13,32,51),fill=(*PINK[6],255),width=1)
            d.line((13,32,51,32),fill=(*PINK[6],255),width=1)
        cells.append(c)
    for i in range(4):
        c = cell64()
        stamp(c, cross, (12,12,40,40))
        d=ImageDraw.Draw(c)
        for j in range(4):
            a=(i*math.tau/4+j*math.tau/4)
            x=int(32+math.cos(a)*17); y=int(32+math.sin(a)*17)
            dx=int(-math.sin(a)*3); dy=int(math.cos(a)*3)
            d.line((x-dx,y-dy,x+dx,y+dy),fill=(*PINK[6 if j%2 else 5],255),width=1)
        sparks(c, 511+i, 9, 23)
        cells.append(c)
    for i, sz in enumerate([30, 22, 13, 6]):
        c = cell64()
        stamp(c, cross if i < 2 else organic, ((64-sz)//2, (64-sz)//2, sz, sz), angle=i*8, opacity=[230, 195, 145, 105][i])
        sparks(c, 621+i, [7,5,3,1][i], max(7, sz//2+5))
        cells.append(c)
    for i, sz in enumerate([20, 34, 56, 34]):
        c = cell64()
        stamp(c, impact, ((64-sz)//2, 59-sz, sz, sz), mirror=i==1, opacity=[255,255,255,120][i])
        sparks(c, 713+i, [3,6,12,7][i], min(27, sz//2), upward=True)
        cells.append(c)
    for i in range(4):
        c = cell64()
        stamp(c, impact, (14,13,36,36), opacity=170)
        sparks(c, 818+i, 13, 26, upward=True)
        cells.append(c)
    assert len(cells)==24
    sheet=Image.new("RGBA",(1024,384))
    for i,c in enumerate(cells):
        sheet.alpha_composite(c.resize((128,128),NEAREST),((i%8)*128,(i//8)*128))
    sheet.save(ROOT/"verdant_attack_laser.png")


def build_body():
    src=load("beam_segment.png")
    # The generator's central hose is painted all the way through its portrait.
    # Crop to it, retaining the edge flecks and enough transparent width.
    base=painted(src,(54,128),crop=(228,0,658,1774))
    sheet=Image.new("RGBA",(512,256))
    for frame in range(4):
        c=Image.new("RGBA",(64,128))
        a=np.array(base)
        a=np.roll(a,frame*31,axis=0)
        # Periodic boundary: blend 6 source rows across the wrap. Both end
        # rows are identical, and adjacent rows approach the same texture.
        for j in range(7):
            t=(j+1)/8
            mixed=(a[j].astype(float)*(1-t)+a[127-j].astype(float)*t).astype('uint8')
            a[j]=mixed
            a[127-j]=mixed
        c.alpha_composite(Image.fromarray(a,"RGBA"),(5,0))
        ar=np.array(c)
        for y in range(128):
            for x in range(7,57):
                if ar[y,x,3] <= 24:
                    continue
                centre=32+round(math.sin((y+frame*31)*math.tau/73)*2)
                distance=abs(x-centre)
                pink_radius=10+round(math.sin((y+frame*31)*math.tau/29)*2)
                texture=int(ar[y,x,:3].max())
                grain=((y*13+x*17+frame*7)%11)-5
                if distance <= 4:
                    ramp=PINK
                    index=5 if texture+grain > 150 else 4
                elif distance <= pink_radius:
                    ramp=PINK
                    index=max(2,min(5,(texture+grain)//48))
                elif distance <= 20:
                    # The dark living sheath is spatially legible even where
                    # the generated painting had pale green highlights.
                    ramp=LEAF
                    index=max(2,min(6,texture//44))
                    if (y+frame*7+x//2)%17 in (0,1) and texture>120:
                        ramp=PINK; index=4
                else:
                    ramp=PINK
                    index=2 if texture<120 else 3
                    ar[y,x,3]=min(ar[y,x,3],80)
                ar[y,x,:3]=ramp[index]
        c=Image.fromarray(ar,"RGBA")
        d=ImageDraw.Draw(c)
        rng=random.Random(903+frame)
        for y in range(4,124):
            pulse=math.sin((y+frame*31)*math.tau/62)
            center=32+round(math.sin((y+frame*19)*math.tau/47))
            half=4 if pulse>.15 else 3
            # White-hot middle third: 8-10 native pixels, granular motion.
            for x in range(center-half,center+half+1):
                if x in (center-half,center+half) and (y+frame*3)%7 < 2:
                    continue
                tone=6 if pulse>.48 and rng.random()>.12 else (5 if pulse>-.25 else 4)
                d.point((x,y),fill=(*PINK[tone],255))
            if (y+frame*31)%62 in (0,1,2):
                d.line((center-8,y,center+8,y),fill=(*PINK[6],240),width=1)
        for k in range(14):
            y=(k*19+frame*13+11)%116+6
            side=-1 if k%2 else 1
            x=32+side*(18+rng.randrange(3,13))
            if 7 <= x <= 56:
                d.point((x,y), fill=(*PINK[4 if k%3 else 6],230))
        for k in range(11):
            y=(k*23+frame*11+9)%112+8
            side=-1 if k%2 else 1
            inner=32+side*(12+k%4)
            outer=32+side*(20+k%5)
            d.line((inner,y+3,outer,y-2),fill=(*PINK[4],225),width=1)
            d.point((outer,y-3),fill=(*PINK[6],255))
        for k in range(9):
            y=(k*23+frame*11+15)%112+8
            x=32+(-1 if k%2 else 1)*(16+k%4)
            d.line((x,y,x,y+2),fill=(*BARK[5],225),width=1)
        # Keep only the central 50 native px and make the vertical boundary
        # exactly identical after overlay; no top/bottom cap is painted.
        ar=np.array(c)
        ar[:,:7]=0; ar[:,57:]=0
        ar[0]=ar[127]
        c=Image.fromarray(ar,"RGBA")
        sheet.alpha_composite(c.resize((128,256),NEAREST),(frame*128,0))
    sheet.save(ROOT/"verdant_attack_laserbody.png")


def build_tell():
    # The painted flare supplies each energized mote; precise bracket geometry
    # is drawn on the 64px grid to keep the aim cue easy to read.
    src=load("muzzle_cross.png")
    motif=painted(src,(5,5),crop=(585,575,655,645))
    sheet=Image.new("RGBA",(512,128))
    for i in range(4):
        c=cell64(); d=ImageDraw.Draw(c)
        if i<2:
            for y in ([16,32,48] if i==0 else [24,40,56]):
                c.alpha_composite(motif,(30,y-2))
                d.line((31,y-1,32,y+1),fill=(*PINK[5],240),width=1)
                d.point((32,y),fill=(*PINK[6],255))
        else:
            # 96px overall reticle; no opaque pixels in the outer 6px.
            col=PINK[4 if i==2 else 5]
            for sx in (-1,1):
                for sy in (-1,1):
                    x=32+sx*23; y=32+sy*23
                    d.line((x,y,x-sx*9,y),fill=(*col,255),width=1)
                    d.line((x,y,x,y-sy*9),fill=(*col,255),width=1)
                    d.point((x-sx*2,y-sy*2),fill=(*PINK[6],255))
            c.alpha_composite(motif,(30,30))
            d.line((29,32,35,32),fill=(*PINK[5],255),width=1)
            d.line((32,29,32,35),fill=(*PINK[6],255),width=1)
        sheet.alpha_composite(c.resize((128,128),NEAREST),(i*128,0))
    sheet.save(ROOT/"verdant_attack_lasertell.png")


if __name__ == "__main__":
    build_main(); build_body(); build_tell()
