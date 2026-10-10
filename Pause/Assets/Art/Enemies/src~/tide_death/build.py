#!/usr/bin/env python3
"""Assemble image-painted Tide death keys at the approved idle scale.

Painted rupture and spray sources remain beside the exports. Resampling is
nearest-neighbour; this script only recells, masks off-world colours, and adds
the concentrated first-frame flash.
"""
from pathlib import Path
import math
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
ENEMIES = HERE.parent / "tide_enemies"
MINES = HERE.parent / "tide_mines"
NAMES = (
    "tide_alien", "tide_chaser", "tide_big", "tide_fighter_1",
    "tide_fighter_2", "tide_fighter_3", "tide_fighter_4",
    "tide_rock_brain", "tide_rock_staghorn", "tide_rock_urchin",
    "tide_rock_islet", "tide_mine",
)
CORE = {
    "tide_alien": (.50, .39), "tide_chaser": (.50, .69),
    "tide_big": (.50, .75), "tide_fighter_1": (.50, .68),
    "tide_fighter_2": (.50, .68), "tide_fighter_3": (.76, .29),
    "tide_fighter_4": (.50, .68), "tide_rock_brain": (.57, .69),
    "tide_rock_staghorn": (.48, .67), "tide_rock_urchin": (.50, .50),
    "tide_rock_islet": (.50, .62), "tide_mine": (.65, .50),
}


def world_palette(im):
    a = np.array(im.convert("RGBA"))
    hsv = np.array(im.convert("HSV"))
    h, s = hsv[:, :, 0], hsv[:, :, 1]
    active = a[:, :, 3] > 16
    red = active & (s > 115) & ((h < 11) | (h >= 244))
    lime = active & (s > 115) & (h >= 50) & (h < 93)
    cyan = active & (s > 120) & (h >= 129) & (h < 151)
    hsv[red, 0] = 18; hsv[red, 1] = np.minimum(hsv[red, 1], 110)
    hsv[lime, 0] = 108
    hsv[cyan, 0] = 111
    a[:, :, :3] = np.array(Image.fromarray(hsv, "HSV").convert("RGB"))
    a[a[:, :, 3] < 16, 3] = 0
    return Image.fromarray(a, "RGBA")


def bbox_alpha(im, threshold=48):
    a = np.array(im.getchannel("A"))
    yy, xx = np.where(a > threshold)
    return (int(xx.min()), int(yy.min()), int(xx.max()+1), int(yy.max()+1))


def fit_painted(im, cell, maxdim, center=None):
    box = bbox_alpha(im)
    src = im.crop(box)
    scale = maxdim / max(src.size)
    size = (max(1, round(src.width*scale)), max(1, round(src.height*scale)))
    src = src.resize(size, Image.Resampling.NEAREST)
    dst = Image.new("RGBA", (cell, cell))
    cx, cy = center or (cell/2, cell/2)
    dst.paste(src, (round(cx-size[0]/2), round(cy-size[1]/2)))
    return dst


def mine_idle():
    atlas = Image.open(MINES / "rail_mines_neon_tide.png").convert("RGBA")
    pose = atlas.crop((0, 0, 313, 314))
    x0,y0,x1,y1 = bbox_alpha(pose)
    src = pose.crop((x0,y0,x1,y1))
    size = (round(src.width*170/max(src.size)), round(src.height*170/max(src.size)))
    src = src.resize(size, Image.Resampling.NEAREST)
    frame = Image.new("RGBA", (192,192))
    frame.paste(src, (8, (192-size[1])//2))
    return frame


def idle(name, cell):
    if name == "tide_mine":
        return mine_idle()
    strip = Image.open(ENEMIES / f"{name}.png").convert("RGBA")
    return strip.crop((3*cell, 0, 4*cell, cell))


def flash(im, name):
    out = im.copy()
    cell = out.width
    x = round(CORE[name][0]*cell); y = round(CORE[name][1]*cell)
    d = ImageDraw.Draw(out)
    radius = 10 if cell == 192 else 14
    white = (249,255,250,255); pale = (200,255,228,255)
    d.line((x-radius,y,x+radius,y),fill=pale,width=1)
    d.line((x,y-radius,x,y+radius),fill=pale,width=1)
    d.line((x-6,y-6,x+6,y+6),fill=pale,width=1)
    d.line((x-6,y+6,x+6,y-6),fill=pale,width=1)
    d.ellipse((x-3,y-3,x+3,y+3),fill=white)
    d.point((x-1,y-1),fill=(255,255,255,255))
    return out


def sparse_afterimage(im, name):
    """Retain painted chips and spray, break continuous fluid into drifting wisps."""
    a = np.array(im)
    h,w = a.shape[:2]
    yy,xx = np.indices((h,w))
    cx,cy = (w-1)/2,(h-1)/2
    r = np.hypot((xx-cx)/(w/2),(yy-cy)/(h/2))
    theta = np.arctan2(yy-cy,xx-cx)
    # This irregular angular breakup prevents an intact source silhouette in cell 2.
    phase = (theta*5.0 + .56*np.sin(r*14 + len(name)))
    open_gap = np.cos(phase) > .08
    inner = r > .15
    # Keep authored bright glints; thin broad opaque water fields.
    rgb = a[:,:,:3].astype(np.float32)
    value = rgb.max(2)
    bright = value > 210
    keep = (open_gap & inner) | (bright & (r > .22))
    a[:,:,3] = np.where(keep,a[:,:,3],0)
    return Image.fromarray(a,"RGBA")


def death(name):
    cell = 256 if name == "tide_big" else 192
    first = flash(idle(name, cell), name)
    source = Image.open(HERE / f"{name}_painted_death_source.png").convert("RGBA")
    left = world_palette(source.crop((0,0,887,887)))
    right = world_palette(source.crop((887,0,1774,887)))
    second = fit_painted(left, cell, cell-22)
    third = fit_painted(sparse_afterimage(right,name), cell, round(cell*.77))
    frames = [first,second,third]
    strip = Image.new("RGBA",(3*cell,cell))
    for i,fr in enumerate(frames):
        strip.alpha_composite(fr,(i*cell,0))
    strip.save(HERE / f"{name}.png")
    return frames


def previews(poses):
    # Four columns per enemy: exact idle, flash, rupture, lingering debris.
    width = 4*512 + 3*8
    height = len(NAMES)*520
    board = Image.new("RGB",(width,height),(7,20,25))
    for j,name in enumerate(NAMES):
        cell=256 if name=="tide_big" else 192
        plain=idle(name,cell)
        row=[plain]+poses[name]
        for i,fr in enumerate(row):
            scaled=fr.resize((cell*2,cell*2),Image.Resampling.NEAREST)
            board.paste(scaled,(i*520+(512-scaled.width)//2,j*520+(512-scaled.height)//2),scaled)
    board.save(HERE / "preview.png")
    reps=("tide_alien","tide_big","tide_fighter_3","tide_rock_urchin")
    animation=[]; durations=[]
    for name in reps:
        for fr,ms in zip(poses[name],(80,110,200)):
            canvas=Image.new("RGBA",(256,256),(7,20,25,255))
            canvas.alpha_composite(fr,((256-fr.width)//2,(256-fr.height)//2))
            animation.append(canvas.convert("RGB"));durations.append(ms)
    animation[0].save(HERE / "preview.gif",save_all=True,append_images=animation[1:],duration=durations,loop=0,disposal=2)


if __name__ == "__main__":
    poses={name:death(name) for name in NAMES}
    previews(poses)
    print(f"wrote {len(poses)} strips and previews")
