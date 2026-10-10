"""Paint-derived Tide ultimate kit. Run from any directory with Pillow and numpy.

The three candidate_*.png files are image-generated paintings. All authored cells
are 64-pixel native art; only the final atlas export uses nearest-neighbour x2.
"""
from pathlib import Path
import math
import colorsys
import numpy as np
from PIL import Image, ImageDraw

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
N = Image.Resampling.NEAREST
BOX = Image.Resampling.BOX
PINK = ['#391331', '#69204E', '#952568', '#C52C91', '#FF4FD8', '#FF8AE6', '#FFE0F8', '#FFF7FD']
WATER = ['#0E2A30', '#31565A', '#568782', '#7AADA5', '#8FD0C4', '#BFEFE4', '#D9F5EE', '#F0FFFA']


def rgba(hexval, a=255):
    h = hexval.lstrip('#')
    return tuple(int(h[i:i+2], 16) for i in (0, 2, 4)) + (a,)


def clean(im):
    """Cluster source painting into two rich, allowed material ramps."""
    a = np.array(im.convert('RGBA'))
    for y in range(a.shape[0]):
        for x in range(a.shape[1]):
            r, g, b, alpha = [int(q) for q in a[y, x]]
            if alpha < 23:
                a[y, x] = (0, 0, 0, 0)
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            pink = (h > .76 or h < .08) and s > .13
            if v > .94 and s < .19:
                pink = True
            ramp = PINK if pink else WATER
            idx = max(0, min(7, round(v * 7.2)))
            # Keep the generated material's highlights crisp and bright.
            if pink and v > .87:
                idx = 7 if s < .17 else (6 if s < .40 else 5)
            if not pink and v > .88:
                idx = max(5, idx)
            rr, gg, bb, _ = rgba(ramp[idx])
            aa = 255 if alpha > 224 else max(32, min(224, 32 * round(alpha / 32)))
            a[y, x] = (rr, gg, bb, aa)
    return Image.fromarray(a, 'RGBA')


def source(name, crop, size):
    im = Image.open(SRC / name).convert('RGBA').crop(crop)
    im.thumbnail(size, BOX)
    canvas = Image.new('RGBA', size)
    canvas.alpha_composite(im, ((size[0] - im.width)//2, (size[1] - im.height)//2))
    return clean(canvas)


def paste_center(dst, im, xy=(32, 32)):
    dst.alpha_composite(im, (round(xy[0]-im.width/2), round(xy[1]-im.height/2)))


def orb(diameter, angle=0):
    base = source('candidate_charge.png', (5, 0, 730, 630), (diameter, diameter))
    return base.rotate(angle, N, expand=False)


def muzzle(diameter, angle=0):
    base = source('candidate_charge.png', (775, 0, 1774, 565), (diameter, diameter))
    return base.rotate(angle, N, expand=False)


def splash(diameter, angle=0):
    base = source('candidate_impact.png', (0, 0, 1239, 1270), (diameter, diameter))
    return base.rotate(angle, N, expand=False)


def arc_motes(im, stage, t):
    d = ImageDraw.Draw(im)
    radius = min(25, 6 + stage * 3)
    for k in range(2 + stage // 2):
        q = 2*math.pi*(k / (2 + stage//2) + t*.067)
        x, y = round(32+radius*math.cos(q)), round(32+radius*.65*math.sin(q))
        if 3 < x < 60 and 3 < y < 60:
            color = rgba(PINK[5 + (k % 2)])
            d.point((x, y), fill=color)
            if stage >= 3:
                d.line((x-1, y, x+1, y), fill=rgba(PINK[4]))


def windup(i):
    c = Image.new('RGBA', (64,64))
    # 12 to 100 exported pixels; ignition blooms fully in pose 8.
    sizes = [6, 10, 16, 22, 29, 37, 43, 50]
    paste_center(c, orb(sizes[i], i*22))
    arc_motes(c, i, i)
    d = ImageDraw.Draw(c)
    if i >= 5:
        span = [0, 0, 0, 0, 0, 19, 24, 27][i]
        col = rgba(PINK[6], 255)
        d.line((32-span,32,32+span,32), fill=col, width=1)
        d.line((32,32-span,32,32+span), fill=col, width=1)
        d.line((32-span//2,31,32+span//2,31), fill=rgba(PINK[5]))
    if i == 7:
        d.ellipse((24,24,40,40), fill=rgba(PINK[7]))
    return c


def muzzle_frame(i):
    c = Image.new('RGBA',(64,64))
    paste_center(c, muzzle(42, i*18))
    d = ImageDraw.Draw(c)
    for j in range(5):
        q = 2*math.pi*(j/5+i*.11)
        x, y = round(32+24*math.cos(q)), round(32+24*math.sin(q))
        d.line((x,y,x+round(3*math.cos(q)),y+round(3*math.sin(q))), fill=rgba(PINK[4+j%2]))
    d.ellipse((27+i%2,27,37+i%2,37), fill=rgba(PINK[7]))
    return c


def fade(i):
    c=Image.new('RGBA',(64,64))
    paste_center(c,muzzle([34,24,15,6][i],i*13))
    if i == 3:
        ImageDraw.Draw(c).point((32,32),fill=rgba(PINK[6]))
    return c


def impact(i):
    c=Image.new('RGBA',(64,64))
    paste_center(c,splash([24,42,56,39][i], i*6), (32,32))
    if i == 3:
        ar=np.array(c)
        ar[:,:,3]=(ar[:,:,3].astype(np.float32)*.56).astype(np.uint8)
        c=Image.fromarray(ar,'RGBA')
    return c


def spark(i):
    c=Image.new('RGBA',(64,64))
    paste_center(c,splash(34, i*14),(32,37))
    d=ImageDraw.Draw(c)
    for k in range(9):
        x=round(32+25*math.sin(k*2.3+i*.7))
        y=round(31-22*abs(math.cos(k*1.7+i*.35)))
        if 4<x<60 and 4<y<60:
            d.point((x,y),fill=rgba(PINK[4+k%3]))
    return c


def make_body():
    # The painting supplies foam, veins, beads, and turbulent plasma. Wrap a
    # 127-row segment to make a seamless 128-row native tile.
    im=Image.open(SRC/'candidate_jet.png').convert('RGBA').crop((84,220,800,1540)).resize((50,127),BOX)
    im=clean(im)
    a=np.array(im)
    # Blend the source's first/last rows into a short periodic transition.
    for k in range(7):
        w=(k+1)/8
        left=a[k].astype(np.float32)
        right=a[126-k].astype(np.float32)
        mix=np.round(left*w+right*(1-w)).astype(np.uint8)
        a[k]=mix
        a[126-k]=mix
    frames=[]
    for f in range(4):
        b=np.roll(a, f*31, axis=0)
        cell=np.zeros((128,64,4),dtype=np.uint8)
        cell[:127,7:57]=b
        # Add an animated 8-10 native px white-hot pressure core with a
        # travelling pink pulse; the surrounding water remains painted.
        for y in range(127):
            phase=2*math.pi*((y-f*31)%127)/32
            center=32+round(1.2*math.sin(y*.075+f*.8))
            half=4+(1 if math.sin(phase)>.32 else 0)
            for x in range(center-11,center+12):
                r=abs(x-center)
                if r<=half:
                    color=PINK[7] if r<=2 or math.sin(phase+x*.48)>.35 else PINK[6]
                    cell[y,x]=rgba(color)
                elif r<=9:
                    color=PINK[5] if math.sin(phase+x*.32)>-.25 else PINK[4]
                    cell[y,x]=rgba(color)
        cell[127]=cell[0]
        frames.append(Image.fromarray(cell,'RGBA'))
    return frames


def sight(i):
    c=Image.new('RGBA',(64,64)); d=ImageDraw.Draw(c)
    shift=0 if i==0 else 8
    for y in range(8+shift,57,16):
        d.rectangle((31,y,32,y+3), fill=rgba(PINK[5],224))
        d.point((31,y+1),fill=rgba(PINK[7]))
        d.point((29,y+3),fill=rgba(PINK[4],96))
        d.point((34,y+3),fill=rgba(PINK[4],96))
    return c


def lock(i):
    c=Image.new('RGBA',(64,64)); d=ImageDraw.Draw(c)
    col=rgba(PINK[5 if i==0 else 4],255)
    soft=rgba(PINK[4],128 if i==0 else 64)
    for sx in (-1,1):
        for sy in (-1,1):
            x=32+sx*24; y=32+sy*24
            d.line((x,y,x-sx*9,y),fill=col,width=1)
            d.line((x,y,x,y-sy*9),fill=col,width=1)
            d.point((x-sx*10,y-sy*10),fill=soft)
    paste_center(c,orb(10 if i==0 else 8, i*30))
    d=ImageDraw.Draw(c)
    d.line((26,32,38,32),fill=rgba(PINK[6 if i==0 else 5]))
    d.line((32,26,32,38),fill=rgba(PINK[6 if i==0 else 5]))
    return c


def atlas(rows, cellsize):
    cw,ch=cellsize
    out=Image.new('RGBA',(cw*len(rows[0])*2,ch*len(rows)*2))
    for y,row in enumerate(rows):
        for x,im in enumerate(row):
            out.alpha_composite(im.resize((cw*2,ch*2),N),(x*cw*2,y*ch*2))
    return out


def make_preview(laser,body,tell):
    # 2x cells over ink; selected cells again at game width over two Tide tiles.
    bg=rgba('#0b0b1a')
    board=Image.new('RGBA',(1200,820),bg)
    d=ImageDraw.Draw(board)
    for y in range(3):
        for x in range(8):
            cell=laser.crop((x*128,y*128,(x+1)*128,(y+1)*128))
            board.alpha_composite(cell,(x*140+8,y*140+18))
    for x in range(4):
        cell=body.crop((x*128,0,(x+1)*128,256))
        board.alpha_composite(cell,(x*130+24,454))
    for x in range(4):
        cell=tell.crop((x*128,0,(x+1)*128,128))
        board.alpha_composite(cell,(550+x*130,454))
    # Game-size views: 40 px beam, 60 px flare over bright and dark real tiles.
    backdrop=OUT.parents[1]/'Backgrounds/Resources/Worlds/Tide/Backdrop3'
    tiles=[]
    for variant in ('v4/sky.png','v3/mid.png'):
        base=Image.open(backdrop/variant).convert('RGBA')
        # Dark upper sea and brighter industrial surf are both real game tiles.
        tile=base.crop((160,320,340,500)).resize((180,180),BOX)
        tiles.append(tile)
    for i in range(3):
        panel=Image.new('RGBA',(180,180),bg if i<2 else rgba('#9AAFA8'))
        if i<2: panel.alpha_composite(tiles[i])
        beam=body.crop(((i%4)*128,0,((i%4)+1)*128,256)).resize((52,160),N)
        flare=laser.crop((3*128,128,4*128,256)).resize((90,90),N)
        panel.alpha_composite(beam,(18,7))
        panel.alpha_composite(flare,(82,44))
        board.alpha_composite(panel,(550+i*195,630))
    board.convert('RGB').save(OUT/'preview.png')
    # Every sustained loop appears in each GIF frame.
    frames=[]
    for i in range(4):
        g=Image.new('RGBA',(460,300),bg)
        for j,(sheet,xy,size) in enumerate([
            (laser,(i*128,128), (128,128)),
            (laser,((i+4)*128,256),(128,128)),
            (body,(i*128,0),(128,256)),
            (tell,((i%2)*128,0),(128,128)),
            (tell,(((i%2)+2)*128,0),(128,128))]):
            ox,oy=xy;w,h=size
            small=sheet.crop((ox,oy,ox+w,oy+h)).resize((w//2,h//2),N)
            g.alpha_composite(small,([4,72,144,220,294][j],18))
        frames.append(g.convert('P',palette=Image.Palette.ADAPTIVE))
    frames[0].save(OUT/'preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,disposal=2)


def main():
    laser=atlas([ [windup(i) for i in range(8)],
                  [muzzle_frame(i) for i in range(4)]+[fade(i) for i in range(4)],
                  [impact(i) for i in range(4)]+[spark(i) for i in range(4)] ], (64,64))
    body=atlas([make_body()],(64,128))
    tell=atlas([[sight(0),sight(1),lock(0),lock(1)]],(64,64))
    laser.save(OUT/'tide_attack_laser.png')
    body.save(OUT/'tide_attack_laserbody.png')
    tell.save(OUT/'tide_attack_lasertell.png')
    make_preview(laser,body,tell)


if __name__=='__main__': main()
