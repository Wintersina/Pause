"""Build the Space pod laser kit from selected generated plasma paintings.

All composition happens at 64 px per 128 px atlas cell. The sole export step is
2x nearest-neighbour. Sources are retained beside this script for repainting.
"""
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
OUT = HERE.parent
RESAMPLE = Image.Resampling.NEAREST
PALETTE = np.array([
    [91, 26, 122], [122, 30, 120], [168, 32, 138],
    [192, 42, 168], [255, 79, 216], [255, 138, 230],
    [255, 224, 248], [255, 244, 251],
], dtype=np.uint8)
STEPS = np.array([0, 20, 42, 72, 112, 160, 215, 255], dtype=np.uint8)


def clean(im, cutoff=30):
    """Remove generated matte pixels and make a deliberately stepped plasma ramp."""
    a = np.asarray(im.convert('RGBA')).copy()
    rgb = a[:, :, :3].astype(np.float32)
    light = .36 * rgb[:, :, 0] + .28 * rgb[:, :, 1] + .36 * rgb[:, :, 2]
    index = np.searchsorted([36, 65, 93, 125, 160, 198, 232], light)
    a[:, :, :3] = PALETTE[index]
    alpha = a[:, :, 3]
    alpha = np.where(alpha < cutoff, 0, alpha)
    a[:, :, 3] = STEPS[np.searchsorted([10, 32, 57, 90, 136, 190, 235], alpha)]
    a[alpha == 0, :3] = 0
    return Image.fromarray(a, 'RGBA')


def source_crop(im, box, size, cutoff=38):
    crop = im.crop(box)
    alpha = crop.getchannel('A').point(lambda v: 255 if v > cutoff else 0)
    bounds = alpha.getbbox()
    if bounds:
        crop = crop.crop(bounds)
    crop.thumbnail((size, size), RESAMPLE)
    if crop.width < size or crop.height < size:
        factor = min(size / crop.width, size / crop.height)
        crop = crop.resize((max(1, round(crop.width*factor)), max(1, round(crop.height*factor))), RESAMPLE)
    return clean(crop, cutoff)


def center(canvas, sprite, x=None, y=None, alpha=1):
    x = (canvas.width - sprite.width)//2 if x is None else x
    y = (canvas.height - sprite.height)//2 if y is None else y
    if alpha != 1:
        sprite = sprite.copy()
        sprite.putalpha(sprite.getchannel('A').point(lambda v: round(v*alpha)))
    canvas.alpha_composite(sprite, (x,y))


def make_bursts():
    src = Image.open(HERE/'plasma_bursts_selected.png').convert('RGBA')
    boxes = [(0,0,512,512), (512,0,1024,512), (1024,0,1536,512),
             (0,512,512,1024), (512,512,1024,1024), (1024,512,1536,1024)]
    sheet = Image.new('RGBA', (512,192))
    for j,size in enumerate([6,12,20,27,35,43,48,50]):
        c = Image.new('RGBA',(64,64))
        motif = 0 if j<2 else 1 if j<5 else 2
        center(c,source_crop(src,boxes[motif],size),alpha=.72 if j==0 else 1)
        # A small inset swirl keeps each advancing frame visibly distinct.
        if j in (3,4,6):
            mote = source_crop(src,boxes[5],7)
            center(c,mote,x=44 if j==3 else 9,y=14 if j==4 else 43,alpha=.72)
        sheet.alpha_composite(c,(j*64,0))
    for j in range(4):
        c=Image.new('RGBA',(64,64))
        bloom=source_crop(src,boxes[3 if j%2==0 else 2],40+j%2*3)
        bloom=bloom.rotate(j*90,expand=True,resample=RESAMPLE)
        center(c,bloom)
        sheet.alpha_composite(c,(j*64,64))
    for j,size in enumerate([38,27,16,6]):
        c=Image.new('RGBA',(64,64))
        center(c,source_crop(src,boxes[3 if j<2 else 0],size),alpha=1-j*.17 if j<3 else 1)
        sheet.alpha_composite(c,((j+4)*64,64))
    for j,(size,opacity) in enumerate([(26,.9),(42,1),(55,1),(34,.55)]):
        c=Image.new('RGBA',(64,64))
        impact=source_crop(src,boxes[4],size)
        center(c,impact,y=57-impact.height,alpha=opacity)
        sheet.alpha_composite(c,(j*64,128))
    for j in range(4):
        c=Image.new('RGBA',(64,64))
        flecks=source_crop(src,boxes[5],34)
        flecks=flecks.rotate(j*90,expand=True,resample=RESAMPLE)
        center(c,flecks,x=32-flecks.width//2+(j%2)*2)
        sheet.alpha_composite(c,((j+4)*64,128))
    return sheet


def make_beam():
    src = Image.open(HERE/'beam_candidate_b_selected.png').convert('RGBA')
    # The painting supplies the flowing strands and arc sparks; only its wide
    # generated halo is masked away. We wrap a tall excerpt for tile continuity.
    excerpt = src.crop((280,140,744,1420)).resize((64,128),RESAMPLE)
    base=np.asarray(clean(excerpt,cutoff=42)).copy()
    x=np.arange(64)
    outer=np.clip((26-np.abs(x-31.5))/8,0,1)
    base[:,:,3]=(base[:,:,3].astype(float)*outer[None,:]).astype(np.uint8)
    # Preserve the painted strands, but confine the continuous white core to
    # 20 export pixels and the hot body to about 44 export pixels.
    for px in range(64):
        distance=abs(px-31.5)
        if distance>18:
            cap=2
        elif distance>11:
            cap=4
        elif distance>5:
            cap=5
        else:
            continue
        light=base[:,px,:3].astype(int).sum(axis=1)
        old=np.asarray(PALETTE)[cap].astype(int).sum()
        base[light>old,px,:3]=PALETTE[cap]
    # Mirror the source near its ends to produce a periodic painting, without
    # interpolation; every exported frame is a cyclic shift of these pixels.
    for k in range(8):
        row=base[8+k].copy() if k<4 else base[119+(k-4)].copy()
        base[k]=row
        base[127-k]=row
    base[127]=base[0]
    frames=[]
    for j in range(4):
        f=np.roll(base,j*32,axis=0)
        f[127]=f[0]
        frames.append(Image.fromarray(f,'RGBA'))
    return Image.fromarray(np.concatenate([np.asarray(f) for f in frames],axis=1),'RGBA')


def make_tell():
    src=Image.open(HERE/'reticle_selected.png').convert('RGBA')
    sheet=Image.new('RGBA',(256,64))
    # A generated spark from the dotted sight motif, repeated on a 16 px period.
    dot=source_crop(src,(135,120,224,260),6,cutoff=48)
    for j in range(2):
        c=Image.new('RGBA',(64,64))
        for y in range(8-j*4,60,16):
            if y+dot.height<=61:
                center(c,dot,y=y)
        sheet.alpha_composite(c,(j*64,0))
    # These brackets and cross are cropped from the painted reticle concept.
    corners=[(347,184,558,398),(607,184,814,398),
             (350,435,559,652),(610,435,816,652)]
    cross=(886,255,1184,567)
    for j in range(2):
        c=Image.new('RGBA',(64,64))
        positions=[(8,8),(36,8),(8,36),(36,36)]
        for box,(x,y) in zip(corners,positions):
            glyph=source_crop(src,box,20,cutoff=55)
            center(c,glyph,x=x,y=y,alpha=1 if j else .72)
        glyph=source_crop(src,cross,10 if j else 7,cutoff=55)
        center(c,glyph)
        sheet.alpha_composite(c,((j+2)*64,0))
    return sheet


def export(native,name):
    native.resize((native.width*2,native.height*2),RESAMPLE).save(OUT/name)


if __name__=='__main__':
    export(make_bursts(),'space_attack_laser.png')
    export(make_beam(),'space_attack_laserbody.png')
    export(make_tell(),'space_attack_lasertell.png')
