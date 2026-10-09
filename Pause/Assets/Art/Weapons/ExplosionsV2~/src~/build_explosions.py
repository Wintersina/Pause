"""Build the staged explosion atlas from the selected painted imagegen keys.

All working sprites are 64 px game-pixel drawings, exported at 2x nearest.
The selected source quadrants are actual image layers in the resulting cells;
the frame poses, motion, tint ramps, glow steps, and cleanup are art-directed here.
"""
from __future__ import annotations

import colorsys
import hashlib
import math
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw, ImageEnhance, ImageFont


HERE = Path(__file__).resolve().parent
OUT = HERE.parent
ART = OUT.parents[1]
CELL = 128
WORK = 64
ROWS = ["metal", "rock", "mine", "ice", "spore", "magma"]
# Selected quadrants: (peak, separated debris). 0 1 / 2 3.
SELECT = {"metal": (0, 3), "rock": (0, 2), "mine": (0, 2),
          "ice": (0, 2), "spore": (1, 2), "magma": (0, 3)}
PALETTES = {
    "metal": ["#11131e", "#222536", "#383b51", "#59576d", "#898a9a", "#b2b6c1", "#463252", "#743c8b", "#ad55bc", "#e18ae4", "#ead7eb", "#f5f0f8"],
    "rock": ["#19191c", "#303036", "#49464a", "#66605c", "#867569", "#aa9273", "#6d3c18", "#9d5b20", "#c78327", "#e2af4a", "#f6d18a", "#fff0bf"],
    "mine": ["#0c1220", "#1b2435", "#303447", "#535466", "#7f8094", "#164d70", "#0c8fb6", "#4ddbe7", "#9cecf1", "#662e80", "#a545b1", "#d893dc", "#e8f5f5"],
    "ice": ["#0b1e38", "#183654", "#275478", "#386b9b", "#568cba", "#7cb1d3", "#3f9ec8", "#53c8e8", "#9cdef0", "#ceeff5", "#ecfafa"],
    "spore": ["#1a2618", "#2c3920", "#465437", "#657247", "#809653", "#416b16", "#639d1a", "#8cc72b", "#b5da52", "#d7ea82", "#edf6bb"],
    "magma": ["#1a1816", "#302b26", "#4b4037", "#684e36", "#856140", "#6d3d15", "#a75312", "#ce7417", "#e99923", "#f5be48", "#fce08a", "#fff3b0"],
}
GLOW = {"metal": "#ba61ca", "rock": "#c38a38", "mine": "#48bfd3",
        "ice": "#73c9e2", "spore": "#9acd3e", "magma": "#e39a29"}


def rgb(hex_color: str) -> tuple[int, int, int]:
    return tuple(bytes.fromhex(hex_color.lstrip("#")))


def source_key(name: str, quadrant: int) -> Image.Image:
    im = Image.open(HERE / f"{name}_key_candidates.png").convert("RGBA")
    w, h = im.size
    x, y = quadrant % 2, quadrant // 2
    quad = im.crop((x * w // 2, y * h // 2, (x + 1) * w // 2, (y + 1) * h // 2))
    alpha = np.asarray(quad.getchannel("A"))
    yy, xx = np.where(alpha >= 38)
    if len(xx) == 0:
        raise ValueError(f"Empty painted key: {name} {quadrant}")
    # Keep the dispersed outliers, with breathing room. The source is genuinely
    # transparent; no checkerboard or generated background is baked in.
    x0, x1 = max(0, int(xx.min()) - 5), min(quad.width, int(xx.max()) + 6)
    y0, y1 = max(0, int(yy.min()) - 5), min(quad.height, int(yy.max()) + 6)
    return quad.crop((x0, y0, x1, y1))


def palette_key(source: Image.Image, name: str, diameter: int) -> Image.Image:
    """Sample the painted key at the game's square pixel cadence, then clean it."""
    src = source.resize((diameter, diameter), Image.Resampling.NEAREST)
    a = np.array(src, dtype=np.uint8)
    colors = np.array([rgb(c) for c in PALETTES[name]], dtype=np.float32)
    output = np.zeros_like(a)
    mask = a[:, :, 3] >= 23
    # Small color-space bias toward local value and hue keeps rock from going
    # grey and spore from turning yellow-white during the 12-color reduction.
    pix = a[:, :, :3].astype(np.float32)
    dist = np.sum((pix[:, :, None, :] - colors[None, None, :, :]) ** 2, axis=3)
    indices = dist.argmin(axis=2)
    output[:, :, :3] = colors[indices].astype(np.uint8)
    levels = np.array([0, 22, 42, 68, 98, 140, 188, 226, 255], dtype=np.uint8)
    output[:, :, 3] = levels[np.abs(a[:, :, 3, None].astype(int) - levels[None, None, :]).argmin(axis=2)]
    output[~mask, 3] = 0
    return Image.fromarray(output, "RGBA")


def paste_center(dst: Image.Image, src: Image.Image, strength: float = 1.0,
                 dx: int = 0, dy: int = 0, hollow: float = 0.0) -> None:
    layer = src.copy()
    a = np.array(layer.getchannel("A"), dtype=np.float32)
    if hollow:
        yy, xx = np.mgrid[0:src.height, 0:src.width]
        rr = np.sqrt(((xx - (src.width - 1) / 2) / (src.width / 2)) ** 2 +
                     ((yy - (src.height - 1) / 2) / (src.height / 2)) ** 2)
        a *= np.minimum(1, (rr / hollow) ** 1.8)
    layer.putalpha(Image.fromarray(np.uint8(np.clip(a * strength, 0, 255)), "L"))
    dst.alpha_composite(layer, ((WORK - src.width) // 2 + dx, (WORK - src.height) // 2 + dy))


def drifting_debris(dst: Image.Image, src: Image.Image, strength: float, frame_index: int) -> None:
    """Separate the painted debris key into irregular traveling material clumps."""
    opacity = np.asarray(src.getchannel("A"), dtype=np.uint8)
    yy, xx = np.mgrid[0:src.height, 0:src.width]
    mid = (src.width - 1) / 2
    # A warped Voronoi cut has natural broken silhouettes rather than the
    # evenly spaced spokes a radial slice would create.
    seeds = []
    for j in range(15):
        theta = j * 2.39996 + .19
        radius = src.width * (.15 + .31 * math.sqrt((j + .5) / 15))
        seeds.append((mid + radius * math.cos(theta), mid + radius * math.sin(theta)))
    wx = xx + 1.25 * np.sin(yy * .76 + xx * .21)
    wy = yy + 1.1 * np.sin(xx * .68 - yy * .17)
    distance = np.stack([(wx - sx)**2 + (wy - sy)**2 for sx,sy in seeds])
    regions = np.argmin(distance, axis=0)
    travel = frame_index - 4
    hollow = .28 if frame_index <= 6 else .46
    for j,(sx,sy) in enumerate(seeds):
        piece = src.copy()
        piece.putalpha(Image.fromarray(np.where(regions == j, opacity, 0).astype(np.uint8), "L"))
        theta = math.atan2(sy-mid,sx-mid) + .15 * (j%3-1)
        dx = round(travel * math.cos(theta) * (.65+.12*(j%4)))
        dy = round(travel * math.sin(theta) * (.65+.12*(j%4)))
        paste_center(dst, piece, strength, dx=dx, dy=dy, hollow=hollow)


def stepped_glow(dst: Image.Image, name: str, cx: int, cy: int,
                 radius: int, strength: float) -> None:
    if radius <= 0:
        return
    color = rgb(GLOW[name])
    layer = Image.new("RGBA", (WORK, WORK))
    d = ImageDraw.Draw(layer)
    # Ordered from outer to inner so pixels form crisp concentric alpha bands.
    for frac, alpha in [(1.0, 13), (.81, 23), (.61, 35), (.42, 52), (.23, 70)]:
        rr = max(1, round(radius * frac))
        d.ellipse((cx - rr, cy - rr, cx + rr, cy + rr), fill=(*color, round(alpha * strength)))
    dst.alpha_composite(layer)


def sparks(dst: Image.Image, name: str, frame: int, intensity: float) -> None:
    d = ImageDraw.Draw(dst)
    pal = [rgb(c) for c in PALETTES[name]]
    n = 9 if name in ("metal", "mine", "magma") else 7
    for j in range(n):
        theta = 2 * math.pi * j / n + (0.28 if j % 2 else -0.12)
        velocity = 1.0 + .28 * ((j * 7) % 4)
        rad = min(19.5, (6.0 + frame * 1.55) * velocity)
        x = round(31.5 + math.cos(theta) * rad)
        y = round(31.5 + math.sin(theta) * rad)
        if not (12 <= x < 52 and 12 <= y < 52):
            continue
        alpha = round((145 if j % 3 == 0 else 90) * intensity)
        c = pal[-2 if j % 3 == 0 else -4]
        d.point((x, y), fill=(*c, alpha))
        if j % 3 == 0 and frame in (2, 3, 4, 5):
            px = round(x - math.cos(theta) * 2)
            py = round(y - math.sin(theta) * 2)
            d.line((px, py, x, y), fill=(*c, max(10, alpha // 2)), width=1)


def frame(name: str, index: int, peak: Image.Image, debris: Image.Image) -> Image.Image:
    img = Image.new("RGBA", (WORK, WORK))
    # One game-pixel brush after source reduction. Each size and fade is chosen
    # to match the 1,1,1,3,2,2,2,2,3,3 playback tick table.
    pose = [
        (8, 0, .26, 0, 2, .33),
        (22, 0, .82, 0, 10, .78),
        (36, 0, .91, 0, 17, .75),
        (37, 24, .66, .28, 17, .55),
        (31, 32, .44, .64, 16, .39),
        (24, 36, .24, .80, 14, .26),
        (15, 36, .10, .67, 10, .13),
        (0, 34, 0, .45, 0, 0),
        (0, 32, 0, .27, 0, 0),
        (0, 30, 0, .12, 0, 0),
    ][index]
    peak_size, debris_size, peak_a, debris_a, glow_r, glow_a = pose
    if glow_r:
        stepped_glow(img, name, 32, 32, glow_r, glow_a)
    if peak_size:
        layer = peak.resize((peak_size, peak_size), Image.Resampling.NEAREST)
        paste_center(img, layer, peak_a)
    if debris_size:
        layer = debris.resize((debris_size, debris_size), Image.Resampling.NEAREST)
        if index >= 5:
            drifting_debris(img, layer, debris_a, index)
        else:
            paste_center(img, layer, debris_a)
    if index <= 7:
        sparks(img, name, index, [0.25, .65, 1, .94, .79, .56, .32, .17][index])
    if index in (0, 1, 2):
        # A pinpoint near-white impact, deliberately never a large opaque star.
        d = ImageDraw.Draw(img)
        c = rgb(PALETTES[name][-1])
        d.point((32, 32), fill=(*c, 230 if index == 1 else 194))
        if index == 1:
            d.point((31, 32), fill=(*c, 176))
            d.point((32, 31), fill=(*c, 160))
    return img.resize((CELL, CELL), Image.Resampling.NEAREST)


def overlays(atlas: Image.Image) -> None:
    def bloom(radius: int, bright: int) -> Image.Image:
        im = Image.new("RGBA", (WORK, WORK))
        d = ImageDraw.Draw(im)
        for frac, alpha in [(1, 12), (.75, 24), (.51, 42), (.30, 70), (.14, bright)]:
            r = max(1, round(radius * frac))
            d.ellipse((32-r, 32-r, 32+r, 32+r), fill=(255,255,255,alpha))
        for step in range(2, radius + 3):
            a = round(54 * (1 - step / (radius + 4)))
            for x,y in ((32+step,32),(32-step,32),(32,32+step),(32,32-step)):
                d.point((x,y),fill=(255,255,255,a))
        d.point((32,32),fill=(255,255,255,bright))
        return im.resize((CELL,CELL),Image.Resampling.NEAREST)
    atlas.alpha_composite(bloom(13, 206),(10*CELL,0))
    atlas.alpha_composite(bloom(8, 166),(11*CELL,0))
    ring = Image.new("RGBA", (WORK,WORK))
    dr = ImageDraw.Draw(ring)
    for r, a in [(24,16),(23,33),(22,59),(21,35)]:
        dr.ellipse((32-r,32-r,32+r,32+r),outline=(255,255,255,a),width=1)
    atlas.alpha_composite(ring.resize((CELL,CELL),Image.Resampling.NEAREST),(12*CELL,0))


def font(size: int) -> ImageFont.FreeTypeFont | ImageFont.ImageFont:
    for p in ("/System/Library/Fonts/Helvetica.ttc", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
        if Path(p).exists():
            return ImageFont.truetype(p,size)
    return ImageFont.load_default()


def frost_ground(size: tuple[int,int], bright: bool) -> Image.Image:
    base = Image.open(ART / "Backgrounds/Resources/Worlds/Frost/Backdrop3/v1/mid.png").convert("RGBA")
    ground = Image.new("RGBA",size)
    for y in range(0,size[1],base.height):
        for x in range(0,size[0],base.width):
            ground.alpha_composite(base,(x,y))
    if bright:
        ground = Image.blend(ground,Image.new("RGBA",size,"#405878"),.34)
        ground = ImageEnhance.Brightness(ground).enhance(1.2)
    return ground.convert("RGB")


def make_preview(atlas: Image.Image) -> None:
    old = Image.open(ART / "Resources/Weapons/Explosions.png").convert("RGBA")
    # Exactly 2x cells; ten old frames left, ten new frames right.
    margin, label_h, row_h = 74, 52, 256
    width = margin * 2 + 20 * CELL * 2
    height = 2 * (label_h + 6 * row_h + 32)
    view = Image.new("RGB",(width,height))
    dark = Image.new("RGB",(width,height//2),"#101724")
    light = frost_ground((width,height//2), True)
    view.paste(dark,(0,0));view.paste(light,(0,height//2))
    draw = ImageDraw.Draw(view)
    f = font(27);small=font(19)
    for bg_index in range(2):
        ybase=bg_index*(height//2)
        draw.text((margin,ybase+10),"ORIGINAL",font=f,fill="#f7f7fa",stroke_width=1,stroke_fill="#111622")
        draw.text((margin+10*256,ybase+10),"V2",font=f,fill="#f7f7fa",stroke_width=1,stroke_fill="#111622")
        for r,name in enumerate(ROWS):
            y=ybase+label_h+r*row_h
            draw.text((8,y+105),name.upper(),font=small,fill="white",stroke_width=2,stroke_fill="#101724")
            for c in range(10):
                for side,src in enumerate((old,atlas)):
                    cell=src.crop((c*CELL,r*CELL,(c+1)*CELL,(r+1)*CELL)).resize((256,256),Image.Resampling.NEAREST)
                    x=margin+(side*10+c)*256
                    view.paste(cell,(x,y),cell)
    view.save(OUT/"preview.png",optimize=True)


def cell(atlas:Image.Image,row:int,col:int)->Image.Image:
    return atlas.crop((col*CELL,row*CELL,(col+1)*CELL,(row+1)*CELL))


def make_ingame(atlas:Image.Image)->None:
    view=frost_ground((1080,2400),False)
    # Add a faint cool grade similar to the Frost playfield's brighter midground.
    view=Image.blend(view,Image.new("RGB",view.size,"#1d3150"),.16).convert("RGBA")
    ship=Image.open(ART/"Resources/ShipArt/Hulls/Dove.png").convert("RGBA").crop((0,0,256,256))
    rows=[(3,2,.85,365,"SMALL / ICE"),(2,3,1.25,1080,"MEDIUM / MINE"),(5,3,1.8,1790,"LARGE / MAGMA")]
    d=ImageDraw.Draw(view)
    f=font(34);sm=font(25)
    d.text((72,55),"EXPLOSION V2   •   FROST PLAYFIELD",font=f,fill="#d9e9f7",stroke_width=1,stroke_fill="#09131f")
    for r,fr,world,y,label in rows:
        dim=round(world*157)
        if r==2: dim=round(dim*1.15)  # TargetExplosion.WorldSizeFor mine multiplier.
        sprite=cell(atlas,r,fr).resize((dim,dim),Image.Resampling.NEAREST)
        # Sprite canvas has empty margins; this is the actual in-game scale.
        view.alpha_composite(sprite,(622-dim//2,y-dim//2))
        ship_dim=round(1.15*157)
        boat=ship.resize((ship_dim,ship_dim),Image.Resampling.NEAREST)
        view.alpha_composite(boat,(252-ship_dim//2,y-ship_dim//2))
        d.text((74,y+165),label,font=f,fill="#e0ecfa",stroke_width=1,stroke_fill="#0d182b")
        d.text((75,y+207),f"{world:.2f}u • {dim}px atlas canvas at 157 px/u",font=sm,fill="#bfd2e2",stroke_width=1,stroke_fill="#0d182b")
    view.convert("RGB").save(OUT/"preview_ingame.png",optimize=True)


def make_gif(atlas:Image.Image)->None:
    ticks=[1,1,1,3,2,2,2,2,3,3]
    order=[i for i,count in enumerate(ticks) for _ in range(count)]+[9]*4
    frames=[]
    f=font(18)
    for frame_index in order:
        bg=Image.new("RGBA",(500,6*138+22),"#111b2b")
        d=ImageDraw.Draw(bg)
        for r,name in enumerate(ROWS):
            y=11+r*138
            d.text((22,y+51),name.upper(),font=f,fill="#cad8e6")
            bg.alpha_composite(cell(atlas,r,frame_index),(186,y+5))
        frames.append(bg.convert("RGB"))
    # GIF stores centiseconds: four 50 ms holds and twenty 40 ms holds make
    # this 24-step loop exactly one second, i.e. a 24 fps playback clock.
    durations=[50 if i%6==5 else 40 for i in range(len(frames))]
    frames[0].save(OUT/"preview.gif",save_all=True,append_images=frames[1:],duration=durations,loop=0,optimize=False)


def verify(atlas:Image.Image)->None:
    assert atlas.size==(2048,768) and atlas.mode=="RGBA"
    arr=np.asarray(atlas)
    assert np.any(arr[:,:,3]==0) and np.any((arr[:,:,3]>0)&(arr[:,:,3]<255))
    rows=[]
    for r,name in enumerate(ROWS):
        hashes=[];max_bbox=0;late=[]
        for c in range(10):
            sprite=cell(atlas,r,c)
            alpha=np.asarray(sprite.getchannel("A"))
            assert np.any(alpha>0),(name,c)
            hashes.append(hashlib.sha256(sprite.tobytes()).hexdigest())
            yy,xx=np.where(alpha>0)
            bbox=max(int(xx.max()-xx.min()+1),int(yy.max()-yy.min()+1))
            max_bbox=max(max_bbox,bbox)
            assert bbox<=89,(name,c,bbox)  # <=70% of 128, rounded down.
            late.append(int(alpha.sum()))
        assert len(set(hashes))==10,name
        assert late[9]<late[6]<max(late[1:4]),(name,late)
        rows.append((name,max_bbox,late))
    for c in (10,11,12):
        assert cell(atlas,0,c).getbbox(),c
        pix=np.asarray(cell(atlas,0,c))
        assert np.all(pix[pix[:,:,3]>0,0]==pix[pix[:,:,3]>0,1])
        assert np.all(pix[pix[:,:,3]>0,1]==pix[pix[:,:,3]>0,2])
    # Count alpha-weighted opaque pixels whose saturated hue is near player red.
    # Restrict hue analysis to the ten burst cells; the white overlays are neutral.
    red_weight=0; chroma_weight=0
    for r in range(6):
        part=np.asarray(atlas.crop((0,r*CELL,10*CELL,(r+1)*CELL))).reshape(-1,4)
        for R,G,B,A in part:
            if A<50: continue
            h,s,v=colorsys.rgb_to_hsv(R/255,G/255,B/255)
            if s<.22 or v<.16: continue
            chroma_weight+=int(A)
            if min(abs(h*360-354),360-abs(h*360-354))<=28:
                red_weight+=int(A)
    assert red_weight/max(1,chroma_weight)<.01,(red_weight,chroma_weight)
    print("verified: 2048x768 RGBA; 60 distinct nonempty frames; max bboxes:",[(n,b) for n,b,_ in rows])
    print("alpha-weighted forbidden-red fraction:",round(red_weight/max(1,chroma_weight),6))
    print("late-frame alpha ratios:",[(n,round(s[9]/s[3],3)) for n,_,s in rows])


def main()->None:
    atlas=Image.new("RGBA",(16*CELL,6*CELL))
    for r,name in enumerate(ROWS):
        qpeak,qdebris=SELECT[name]
        peak=source_key(name,qpeak)
        debris=source_key(name,qdebris)
        for i in range(10):
            # Quantize separately at each pose size: no blurred resampling.
            peak_key=palette_key(peak,name,max(8,[8,22,36,37,31,24,15,8,8,8][i]))
            debris_key=palette_key(debris,name,max(8,[8,8,8,24,32,36,36,34,32,30][i]))
            atlas.alpha_composite(frame(name,i,peak_key,debris_key),(i*CELL,r*CELL))
    overlays(atlas)
    verify(atlas)
    atlas.save(OUT/"Explosions.png",optimize=True)
    make_preview(atlas)
    make_ingame(atlas)
    make_gif(atlas)


if __name__=="__main__":
    main()
