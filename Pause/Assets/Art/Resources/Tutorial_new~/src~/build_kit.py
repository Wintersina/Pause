"""Build the staged steampunk tutorial kit from generated concept paintings.

The four candidate PNGs alongside this script are imagegen output. All resizes
use nearest neighbour; opaque art is palette snapped and alpha is binary.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import numpy as np

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
OLD = OUT.parent / 'Tutorial'

INK = '#05060c'
IRON = ['#0b0f15', '#1a2129', '#2f343b', '#4b5059', '#7a7c82', '#b0b4b6']
BRASS = ['#241712', '#503021', '#80502a', '#b47734', '#dca64d', '#f3d17b']
COPPER = ['#311b18', '#713522', '#aa5630', '#da8745', '#f2b96c']
AMBER = ['#6a2404', '#c24a06', '#fc7a08', '#fcb809', '#fdf06a', '#fffbe0']
CYAN = ['#0b4f7a', '#0b91cc', '#0bd0f6', '#7af6fc']
GLASS = ['#08101c', '#0c1725', '#112338', '#1c3448']
RED = ['#4b1017', '#86121f', '#d8232c', '#f76555', '#fff0d9']
PAL = [INK] + IRON + BRASS + COPPER + AMBER + CYAN + GLASS
PAL_RGB = np.array([tuple(bytes.fromhex(c[1:])) for c in PAL], dtype=np.int32)


def orig(name):
    return Image.open(OLD / (name + '.png')).convert('RGBA')


def canvas(name):
    return Image.new('RGBA', orig(name).size)


def candidate(name, box):
    im = Image.open(HERE / name).convert('RGBA')
    return im.crop(box)


def snap(im, palette=PAL_RGB):
    """Opaque 1px pixel clusters, using only the kit's explicit colors."""
    a = np.asarray(im.convert('RGBA')).copy()
    rgb = a[:, :, :3].astype(np.int32)
    # Nearest palette color, working in RGB. Deliberately retain no antialias.
    ds = ((rgb[:, :, None, :] - palette[None, None, :, :]) ** 2).sum(axis=3)
    a[:, :, :3] = palette[ds.argmin(axis=2)]
    a[:, :, 3] = np.where(a[:, :, 3] >= 128, 255, 0)
    return Image.fromarray(a, 'RGBA')


def fit_generated(filename, source_bbox, name, target_bbox):
    base = canvas(name)
    src = candidate(filename, source_bbox)
    x0, y0, x1, y1 = target_bbox
    src = src.resize((x1-x0, y1-y0), Image.Resampling.NEAREST)
    src = snap(src)
    base.paste(src, (x0, y0), src)
    return base


def clip_to_old(name, im):
    # Keep every asset within the original painted footprint plus four pixels.
    old_alpha = orig(name).getchannel('A')
    allowed = old_alpha.point(lambda p: 255 if p else 0).filter(ImageFilter.MaxFilter(9))
    a = np.asarray(im.getchannel('A'))
    m = np.asarray(allowed)
    clipped = Image.fromarray(np.where(m > 0, a, 0).astype('uint8'), 'L')
    im.putalpha(clipped)
    return im


def save(name, im):
    assert im.size == orig(name).size, name
    im = clip_to_old(name, im.convert('RGBA'))
    im.save(OUT / (name + '.png'))


def line(d, xy, color, width=1):
    d.line(xy, fill=color, width=width, joint='curve')


def make_head():
    im = fit_generated('head_candidate_b.png', (35, 49, 1220, 1058),
                       'tut_robot', (7, 8, 249, 217))
    d = ImageDraw.Draw(im)
    # Restore the exact empty screen position required by RobotSpeaker's overlays.
    # Its right edge preserves the generated brass/copper bezel and glass glint.
    panel = [(64, 84), (191, 84), (199, 137), (187, 176),
             (70, 176), (57, 137)]
    d.polygon(panel, fill=GLASS[0])
    line(d, panel + [panel[0]], GLASS[2], 2)
    line(d, [(64, 85), (190, 85)], CYAN[0], 1)
    line(d, [(178, 87), (190, 87), (197, 137)], GLASS[3], 1)
    # Soldered antenna, visually registered to the existing red lamp overlay.
    d.rectangle((146, 8, 159, 37), fill=INK)
    d.rectangle((149, 10, 156, 36), fill=IRON[2])
    line(d, [(151, 11), (151, 31)], IRON[5], 2)
    d.rectangle((143, 33, 162, 41), fill=INK)
    d.rectangle((145, 34, 160, 38), fill=COPPER[2])
    d.point((153, 36), fill=BRASS[5])
    # The same red stress lamp anchor as the original shell.
    d.rectangle((150, 14, 158, 22), fill=RED[1])
    d.rectangle((152, 15, 157, 20), fill=RED[2])
    d.point((153, 16), fill=RED[4])
    # Steam vent and a compact half gear stay away from the face and overlays.
    for x in (112, 119, 126):
        d.rectangle((x, 47, x+3, 51), fill=INK)
        d.point((x+1, 47), fill=BRASS[4])
    for x, y in ((77, 195), (86, 200), (176, 198), (187, 192)):
        d.point((x, y), fill=BRASS[5])
    # A small deliberate rim light, not a soft blur.
    line(d, [(207, 97), (219, 122), (209, 164)], CYAN[0], 1)
    save('tut_robot', im)


def make_bubble():
    im = fit_generated('bubble_candidate.png', (59, 69, 1206, 1176),
                       'tut_bubble', (8, 8, 188, 188))
    d = ImageDraw.Draw(im)
    # The generated image supplies the riveted brass frame. Make its central
    # text field a quiet panel so nine-slice stretches and small text stay clear.
    d.rectangle((33, 35, 160, 158), fill=GLASS[0])
    d.rectangle((38, 40, 155, 153), fill=GLASS[1])
    # Join to the untouched generated border with a dark inner screen rim.
    line(d, [(34, 157), (34, 38), (39, 33), (157, 33)], GLASS[2], 1)
    save('tut_bubble', im)


def make_eyes():
    src = candidate('eye_candidate.png', (258, 272, 1173, 825))
    for pose, bbox in {'open': (1, 0, 51, 37), 'half': (1, 15, 51, 37)}.items():
        name = 'tut_eye_' + pose
        im = canvas(name)
        x0, y0, x1, y1 = bbox
        d = ImageDraw.Draw(im)
        if pose == 'open':
            d.rounded_rectangle((1, 0, 50, 36), radius=10, fill=INK)
            d.rounded_rectangle((5, 6, 46, 30), radius=7, fill=AMBER[1])
            d.rounded_rectangle((8, 8, 43, 27), radius=6, fill=AMBER[3])
            d.rounded_rectangle((11, 9, 40, 24), radius=5, fill=AMBER[4])
            # A few actual sampled clusters from the generated eye painting.
            sampled=snap(src.resize((24,16),Image.Resampling.NEAREST))
            sw=np.asarray(sampled)
            for yy in range(2,9):
                for xx in range(3,13):
                    if sw[yy,xx,3] and sum(int(v) for v in sw[yy,xx,:3])>500:
                        d.point((xx+10,yy+10),fill=tuple(int(v) for v in sw[yy,xx]))
            d.rectangle((10,12,15,15),fill=AMBER[5])
            line(d,[(7,29),(42,29)],CYAN[1],2)
            d.point((44,28),fill=CYAN[2])
        else:
            d.rounded_rectangle((1, 15, 50, 36), radius=7, fill=INK)
            d.rounded_rectangle((5, 17, 46, 32), radius=5, fill=AMBER[2])
            line(d,[(9,19),(42,19)],AMBER[4],3)
            line(d,[(8,31),(43,31)],CYAN[0],1)
        save(name, im)
    im = canvas('tut_eye_happy'); d = ImageDraw.Draw(im)
    line(d, [(1, 33), (23, 9), (27, 9), (51, 33)], INK, 7)
    line(d, [(2, 32), (23, 9), (27, 9), (50, 32)], AMBER[2], 4)
    line(d, [(8, 26), (24, 10), (27, 10), (45, 27)], AMBER[4], 2)
    d.point((25, 4), fill=AMBER[5]); d.point((26, 4), fill=AMBER[5])
    line(d,[(0,36),(16,36)],INK,1)
    line(d,[(35,36),(51,36)],INK,1)
    d.point((49, 34), fill=CYAN[2])
    save('tut_eye_happy', im)
    im = canvas('tut_eye_shut'); d = ImageDraw.Draw(im)
    d.rectangle((1, 23, 50, 32), fill=INK)
    line(d, [(2, 26), (49, 26)], AMBER[2], 3)
    line(d, [(6, 25), (45, 25)], AMBER[4], 1)
    line(d, [(6, 36), (45, 36)], CYAN[0], 1)
    save('tut_eye_shut', im)


def make_mouths():
    src = candidate('mouth_candidate.png', (462, 309, 1221, 620))
    targets = {
        'a': (10, 5, 62, 37), 'big': (2, 1, 70, 39),
        'e': (4, 9, 68, 31), 'rest': (11, 15, 61, 27)
    }
    for pose, (x0, y0, x1, y1) in targets.items():
        name = 'tut_mouth_' + pose
        im = canvas(name)
        shape = snap(src.resize((x1-x0, y1-y0), Image.Resampling.NEAREST))
        if pose == 'rest':
            # Closed resting mouth: preserve generated amber texture in a line.
            shape = shape.crop((0, 0, shape.width, max(3, shape.height//2))).resize(
                (x1-x0, y1-y0), Image.Resampling.NEAREST)
        im.paste(shape, (x0, y0), shape)
        save(name, im)
    im = canvas('tut_mouth_o'); d = ImageDraw.Draw(im)
    d.ellipse((17, 3, 54, 36), fill=INK)
    d.ellipse((21, 6, 50, 33), fill=AMBER[2])
    d.ellipse((25, 10, 46, 29), fill=GLASS[0])
    d.arc((21, 6, 50, 33), 195, 330, fill=AMBER[4], width=3)
    d.point((28, 6), fill=AMBER[5]); d.point((50, 29), fill=CYAN[1])
    save('tut_mouth_o', im)


def make_jets():
    for pose in ('a', 'b'):
        name='tut_jet_'+pose; im=canvas(name); d=ImageDraw.Draw(im)
        if pose == 'a':
            shape=[(10, 3), (49, 3), (42, 19), (32, 49), (29, 52), (18, 20)]
            core=[(23, 12), (37, 12), (33, 28), (30, 44), (26, 28)]
        else:
            shape=[(5, 2), (54, 2), (43, 17), (31, 40), (29, 40), (16, 17)]
            core=[(19, 10), (41, 10), (34, 23), (30, 34), (25, 23)]
        d.polygon(shape, fill=AMBER[1]);d.polygon(core, fill=AMBER[3])
        d.polygon([(24, 11),(36,11),(31,26),(29,26)], fill=AMBER[5])
        d.rectangle((18, 1, 42, 9), fill=INK)
        d.rectangle((20, 2, 40, 7), fill=BRASS[2])
        line(d, [(21, 3), (38, 3)], BRASS[5], 1)
        d.point((15 if pose=='a' else 9, 12), fill=CYAN[1])
        save(name, im)


def make_lamp():
    im=canvas('tut_lamp');d=ImageDraw.Draw(im)
    d.ellipse((0,0,23,23), fill=INK)
    d.ellipse((2,2,21,21), fill=BRASS[3])
    d.ellipse((4,4,19,19), fill=RED[1])
    d.ellipse((6,6,17,17), fill=RED[2])
    d.rectangle((8,6,13,11), fill=RED[3])
    d.point((9,7), fill=RED[4])
    line(d, [(3,17),(8,21),(17,21)], BRASS[5], 1)
    save('tut_lamp',im)


def make_tail():
    im=canvas('tut_tail');d=ImageDraw.Draw(im)
    d.polygon([(0,20),(41,3),(51,5),(51,35),(41,37)], fill=INK)
    d.polygon([(5,20),(42,7),(48,9),(48,31),(42,33)], fill=BRASS[2])
    d.polygon([(11,20),(42,11),(46,12),(46,28),(42,29)], fill=GLASS[1])
    line(d,[(7,19),(41,6),(47,7)],BRASS[5],2)
    d.point((40,7),fill=AMBER[4]);d.point((40,32),fill=COPPER[3])
    save('tut_tail',im)


def make_button():
    im=canvas('tut_button');d=ImageDraw.Draw(im)
    d.polygon([(7,1),(94,1),(94,71),(86,78),(1,78),(1,9)],fill=INK)
    d.polygon([(9,5),(90,5),(90,68),(84,74),(5,74),(5,11)],fill=BRASS[2])
    d.polygon([(11,9),(87,9),(87,65),(81,70),(9,70),(9,13)],fill=BRASS[4])
    d.polygon([(14,15),(84,15),(84,63),(78,67),(13,67)],fill=IRON[1])
    line(d,[(10,11),(86,11)],BRASS[5],2)
    line(d,[(12,68),(79,68)],COPPER[2],2)
    for x,y in [(9,12),(87,12),(9,67),(82,67)]:
        d.ellipse((x-2,y-2,x+2,y+2),fill=INK);d.point((x-1,y-1),fill=BRASS[5])
    save('tut_button',im)


def make_card():
    im=canvas('tut_card');d=ImageDraw.Draw(im)
    d.polygon([(8,1),(94,1),(94,85),(85,94),(1,94),(1,8)],fill=INK)
    d.polygon([(9,5),(90,5),(90,82),(82,90),(5,90),(5,10)],fill=BRASS[2])
    d.polygon([(12,9),(87,9),(87,79),(79,87),(9,87),(9,12)],fill=IRON[1])
    line(d,[(12,11),(86,11)],BRASS[5],2)
    line(d,[(11,18),(11,77)],CYAN[0],1)
    for x,y in [(7,8),(88,8),(7,86)]:
        d.point((x,y),fill=BRASS[5])
    save('tut_card',im)


def make_arrow():
    im=canvas('tut_arrow');d=ImageDraw.Draw(im)
    for y in (8,41):
        outer=[(5,y+32),(47,y),(90,y+32),(82,y+41),(47,y+14),(13,y+41)]
        d.polygon(outer,fill=INK)
        line(d,[(10,y+31),(47,y+4),(85,y+31)],COPPER[2],5)
        line(d,[(13,y+28),(47,y+5),(82,y+28)],AMBER[3],3)
        d.point((47,y+5),fill=AMBER[5])
    d.point((5,40),fill=INK)
    d.point((90,40),fill=INK)
    d.point((90,50),fill=INK)
    d.point((90,82),fill=INK)
    save('tut_arrow',im)


def make_ring():
    im=canvas('tut_ring');d=ImageDraw.Draw(im)
    outer=[(44,1),(84,1),(126,44),(126,84),(84,126),(44,126),(1,84),(1,44)]
    inner=[(46,8),(82,8),(120,46),(120,82),(82,120),(46,120),(8,82),(8,46)]
    d.polygon(outer,fill=(246,242,224,255))
    d.polygon(inner,fill=(0,0,0,0))
    for x,y in [(64,3),(124,64),(64,124),(3,64)]:
        d.rectangle((x-2,y-2,x+2,y+2),fill=(255,255,255,255))
    save('tut_ring',im)


def make_glow():
    im=canvas('tut_glow');d=ImageDraw.Draw(im)
    # Neutral stepped halo: RobotSpeaker and guides tint it red or teal.
    for radius,alpha in [(32,20),(25,40),(19,73),(13,110),(8,166),(4,225)]:
        d.polygon([(32,32-radius),(32+radius,32),(32,32+radius),(32-radius,32)],
                  fill=(255,248,228,alpha))
    d.point((32,32),fill=(255,255,255,255))
    save('tut_glow',im)


def paste(dst, sprite, xy, scale=2, flip=False):
    im=Image.open(OUT/(sprite+'.png')).convert('RGBA')
    if flip: im=im.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    im=im.resize((im.width*scale,im.height*scale),Image.Resampling.NEAREST)
    dst.alpha_composite(im,xy)


def robot_composite(eye='open', mouth='rest', jet='a'):
    im=Image.new('RGBA',(512,512))
    paste(im,'tut_jet_'+jet,(196,424))
    paste(im,'tut_robot',(0,0))
    paste(im,'tut_lamp',(284,12))
    paste(im,'tut_eye_'+eye,(136,188))
    paste(im,'tut_eye_'+eye,(272,188),flip=True)
    paste(im,'tut_mouth_'+mouth,(184,276))
    return im


def nine_slice(im, size, border=44):
    w,h=size; o=Image.new('RGBA',size)
    xs=[0,border,im.width-border,im.width];ys=[0,border,im.height-border,im.height]
    dx=[0,border,w-border,w];dy=[0,border,h-border,h]
    for yi in range(3):
        for xi in range(3):
            tile=im.crop((xs[xi],ys[yi],xs[xi+1],ys[yi+1]))
            tile=tile.resize((dx[xi+1]-dx[xi],dy[yi+1]-dy[yi]),Image.Resampling.NEAREST)
            o.alpha_composite(tile,(dx[xi],dy[yi]))
    return o


def preview():
    im=Image.new('RGBA',(1600,1100),'#0b0b1a')
    big=robot_composite('open','a','a');im.alpha_composite(big,(45,95))
    # 9-sliced bubble at approximately the in-game aspect ratio, at 2x.
    bubble=nine_slice(Image.open(OUT/'tut_bubble.png').convert('RGBA'),(375,130),44)
    bubble=bubble.resize((750,260),Image.Resampling.NEAREST)
    im.alpha_composite(bubble,(550,190))
    paste(im,'tut_tail',(509,295))
    d=ImageDraw.Draw(im)
    fonts=['/System/Library/Fonts/SFNS.ttf','/System/Library/Fonts/Supplemental/Menlo.ttc',
           '/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf']
    font=next((ImageFont.truetype(f,42) for f in fonts if Path(f).exists()),ImageFont.load_default())
    small=next((ImageFont.truetype(f,21) for f in fonts if Path(f).exists()),ImageFont.load_default())
    d.text((655,252),'You have this, pilot.',font=font,fill='#f4ead4',stroke_width=1,stroke_fill=INK)
    d.text((655,315),'Pause, plan, then fly!',font=font,fill='#f4ead4',stroke_width=1,stroke_fill=INK)
    d.text((45,35),'TUTORIAL MENTOR / 2x SPRITE PREVIEW',font=small,fill='#f3d17b')
    d.text((45,650),'EYES',font=small,fill='#b0b4b6')
    for i,pose in enumerate(('open','half','happy','shut')):
        paste(im,'tut_eye_'+pose,(45+i*140,694));d.text((48+i*140,784),pose,font=small,fill='#b0b4b6')
    d.text((45,831),'MOUTHS',font=small,fill='#b0b4b6')
    for i,pose in enumerate(('rest','a','e','o','big')):
        paste(im,'tut_mouth_'+pose,(45+i*150,866));d.text((48+i*150,960),pose,font=small,fill='#b0b4b6')
    d.text((830,505),'COMPONENTS',font=small,fill='#b0b4b6')
    for name,xy in [('tut_button',(820,550)),('tut_card',(1050,550)),('tut_arrow',(1280,550)),
                    ('tut_ring',(810,760)),('tut_glow',(1110,760)),('tut_jet_a',(1260,780)),
                    ('tut_jet_b',(1390,780))]:
        paste(im,name,xy)
        d.text((xy[0],xy[1]+(256 if name=='tut_ring' else 200 if name in ('tut_card','tut_arrow') else 170)),
               name.removeprefix('tut_'),font=small,fill='#b0b4b6')
    im.convert('RGB').save(OUT/'preview.png')


def main():
    make_head();make_bubble();make_eyes();make_mouths();make_jets()
    make_lamp();make_tail();make_button();make_card();make_arrow();make_ring();make_glow()
    preview()


if __name__=='__main__': main()
