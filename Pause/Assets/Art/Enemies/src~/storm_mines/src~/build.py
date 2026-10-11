#!/usr/bin/env python3
"""Assemble the painted Storm Rod Mine atlas and its three-cell death strip.

Only nearest-neighbour resizing is used. Run --draft to inspect before the
one-time final export; existing files are never overwritten.
"""
from pathlib import Path
import argparse
import colorsys
import numpy as np
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
MINES = HERE.parent
DEATH = MINES.parent / 'storm_death'
ATLAS_SOURCE = HERE / 'candidate_a.png'
DEATH_SOURCE = DEATH / 'src~' / 'candidate_b.png'
EDGES = (0, 330, 638, 947, 1254)
SOURCE_EDGES = (0, 520, 1000, 1490, 2172)
CORE_X = (321, 783, 1260, 1780)
CORE_Y = (348, 350, 346, 345)
PIVOT = (192, 157)
BG = (11, 11, 26, 255)


def write_new(im, path):
    if path.exists():
        raise FileExistsError(f'Refusing to overwrite {path}')
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path)


def storm_palette(im):
    """Restrict off-world paint hues without quantising the material ramps."""
    rgba = np.array(im.convert('RGBA'))
    hsv = np.array(im.convert('HSV'))
    h, s, v = hsv[:, :, 0].copy(), hsv[:, :, 1].copy(), hsv[:, :, 2]
    active = rgba[:, :, 3] > 0
    bright = v >= 175
    # The generated source has stray violet/cyan fringe and a few warm rivets.
    yellow = active & bright & (h >= 32) & (h <= 52)
    colored = active & (s > 25) & ~yellow
    hsv[:, :, 0][colored & bright] = 145  # 205 degrees: cold arc blue
    hsv[:, :, 0][colored & ~bright] = 154  # 217 degrees: slate
    hsv[:, :, 1][colored & bright] = np.minimum(s[colored & bright], 120)
    hsv[:, :, 1][colored & ~bright] = np.minimum(s[colored & ~bright], 75)
    hsv[:, :, 0][yellow] = 41  # 58 degrees, pale lightning edge
    hsv[:, :, 1][yellow] = np.minimum(s[yellow], 105)
    rgb = np.array(Image.fromarray(hsv, 'HSV').convert('RGB'))
    rgba[:, :, :3] = rgb
    rgba[rgba[:, :, 3] < 12, 3] = 0
    return Image.fromarray(rgba, 'RGBA')


def edge_fade(im, width=8):
    a = np.array(im)
    h, w = a.shape[:2]
    yy, xx = np.indices((h, w))
    d = np.minimum.reduce((xx, yy, w - 1 - xx, h - 1 - yy))
    factor = np.clip((d - 6) / width, 0, 1)
    a[:, :, 3] = (a[:, :, 3].astype(float) * factor).astype(np.uint8)
    return Image.fromarray(a, 'RGBA')


def painted_pose(source, i, sx=.504, sy=.5):
    x0, x1 = SOURCE_EDGES[i:i+2]
    crop = source.crop((x0, 0, x1, 724))
    crop = crop.resize((round(crop.width * sx), round(crop.height * sy)), Image.Resampling.NEAREST)
    cell = Image.new('RGBA', (EDGES[i+1] - EDGES[i], 314))
    left = round(PIVOT[0] - (CORE_X[i] - x0) * sx)
    top = round(PIVOT[1] - CORE_Y[i] * sy)
    cell.paste(crop, (left, top))
    return storm_palette(cell)


def energy_only(im, threshold=155, outside=0, burst=False):
    a=np.array(im)
    r,g,b=[a[:,:,i].astype(np.int16) for i in range(3)]
    v=np.maximum.reduce((r,g,b))
    yy,xx=np.indices(v.shape)
    dist=np.hypot(xx-PIVOT[0],yy-PIVOT[1])
    if burst:
        energy=(v>threshold)&((b>r+8)|((r>185)&(g>175)&(b>125)))
    else:
        energy=(v>threshold)&(b>r+12)&(dist>outside)
        rod_corridor=(abs(xx-PIVOT[0])<34)&((yy<111)|(yy>213))
        energy &= ~rod_corridor
    a[:,:,3]=np.where(energy,a[:,:,3],0)
    return Image.fromarray(a,'RGBA')


def add_halo(cell, radius, opacity):
    glow=Image.new('RGBA',cell.size)
    d=ImageDraw.Draw(glow)
    for r,a in ((radius,opacity//4),(radius-8,opacity//3),(radius-17,opacity//2)):
        d.ellipse((PIVOT[0]-r,PIVOT[1]-r,PIVOT[0]+r,PIVOT[1]+r),fill=(195,218,238,a))
    glow.alpha_composite(cell)
    return glow


def brighten_coil(cell, strength):
    light=Image.new('RGBA',cell.size)
    d=ImageDraw.Draw(light)
    x,y=PIVOT
    d.line((x-14,y-17,x-4,y-6,x+11,y-9),fill=(202,225,246,strength),width=2)
    d.line((x+12,y+3,x+3,y+11,x-10,y+17),fill=(242,246,255,strength),width=2)
    d.ellipse((x-5,y-5,x+5,y+5),fill=(242,246,255,strength//2))
    cell.alpha_composite(light)
    return cell


def lift_core(cell, amount):
    a=np.array(cell)
    yy,xx=np.indices(a.shape[:2]);x,y=PIVOT
    mask=(((xx-x)/22)**2+((yy-y)/24)**2<1)&(a[:,:,3]>100)
    rgb=a[:,:,:3].astype(float)
    rgb[mask]=rgb[mask]*(1-amount)+np.array([242,246,255])*amount
    a[:,:,:3]=rgb.astype(np.uint8)
    return Image.fromarray(a,'RGBA')


def extend_rods(cell, dormant):
    # Stretch the generated steel pixels along the rod axis with nearest sampling.
    top=dormant.crop((185,46,218,100)).resize((33,69),Image.Resampling.NEAREST)
    lower=dormant.crop((185,223,218,284)).resize((33,76),Image.Resampling.NEAREST)
    cell.alpha_composite(top,(185,31))
    cell.alpha_composite(lower,(185,223))
    return cell


def atlas():
    source = Image.open(ATLAS_SOURCE).convert('RGBA')
    if source.size != (2172, 724):
        raise ValueError(f'Unexpected atlas source size: {source.size}')
    dormant=painted_pose(source,0)
    frames=[dormant]
    # Each later key uses the same solid painted hull and grip. The generated
    # key poses supply the changing lightning, rod tips and starburst.
    wake=add_halo(dormant,62,34)
    wake.alpha_composite(energy_only(painted_pose(source,1),158,outside=51))
    wake=brighten_coil(lift_core(wake,.25),190)
    frames.append(wake)
    charge=add_halo(dormant,91,115)
    charge_art=painted_pose(source,2,sx=.40,sy=.44)
    charge.alpha_composite(energy_only(charge_art,130,outside=54))
    charge=extend_rods(charge,dormant)
    charge=brighten_coil(lift_core(charge,.48),240)
    frames.append(charge)
    burst=add_halo(dormant,100,95)
    burst_art=painted_pose(source,3,sx=.30,sy=.44)
    burst.alpha_composite(energy_only(burst_art,126,burst=True))
    frames.append(burst)
    # One physical wall grip, pixel-identical in all frames.
    clamp=np.array(dormant)[:,:155]
    clamp_mask=clamp[:,:,3]>240
    for i in range(1,4):
        a=np.array(frames[i]);a[:,:155][clamp_mask]=clamp[clamp_mask]
        frames[i]=Image.fromarray(a,'RGBA')
    # Fade generated halo/ray fringe before it can touch cell boundaries.
    frames = [edge_fade(f) for f in frames]
    sheet = Image.new('RGBA', (1254, 314))
    for i, frame in enumerate(frames):
        sheet.alpha_composite(frame, (EDGES[i], 0))
    return sheet, frames


def death_strip(dormant):
    source = Image.open(DEATH_SOURCE).convert('RGBA')
    if source.size != (1774, 887):
        raise ValueError(f'Unexpected death source size: {source.size}')
    cells = []
    # Cell 0 is literally the atlas dormant mine, reduced as in Tide's mine death.
    cell = Image.new('RGBA', (192, 192))
    small = dormant.resize((round(dormant.width*.68), round(dormant.height*.68)), Image.Resampling.NEAREST)
    cell.alpha_composite(small, (round(96-PIVOT[0]*.68), round(96-PIVOT[1]*.68)))
    flash = Image.new('RGBA', (192, 192))
    d = ImageDraw.Draw(flash)
    for dx, dy in ((0,24),(0,-24),(24,0),(-24,0),(17,17),(-17,17),(17,-17),(-17,-17)):
        d.line((96,96,96+dx,96+dy), fill=(242,246,255,210), width=2)
    d.ellipse((89,89,103,103), fill=(242,246,255,255))
    d.point((96,96), fill=(255,243,160,255))
    cell.alpha_composite(flash)
    cells.append(storm_palette(cell))
    for x0, x1 in ((590,1260),(1260,1774)):
        crop = source.crop((x0,0,x1,887))
        crop = crop.resize((round(crop.width*.26),round(crop.height*.26)), Image.Resampling.NEAREST)
        alpha = np.array(crop.getchannel('A'))
        ys,xs=np.where(alpha>16)
        xmid=(xs.min()+xs.max())/2; ymid=(ys.min()+ys.max())/2
        cell=Image.new('RGBA',(192,192))
        cell.alpha_composite(crop,(round(96-xmid),round(96-ymid)))
        cells.append(storm_palette(edge_fade(cell)))
    sheet=Image.new('RGBA',(576,192))
    for i,cell in enumerate(cells):sheet.alpha_composite(cell,(i*192,0))
    return sheet,cells


def preview(sheet, path):
    canvas=Image.new('RGBA',sheet.size,BG)
    canvas.alpha_composite(sheet)
    write_new(canvas.resize((sheet.width*2,sheet.height*2),Image.Resampling.NEAREST).convert('RGB'),path)


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--draft',action='store_true');args=ap.parse_args()
    mine,mine_frames=atlas()
    death,_=death_strip(mine_frames[0])
    if args.draft:
        n=1
        while (HERE/f'draft_atlas_v{n}.png').exists():n+=1
        out_m=HERE/f'draft_atlas_v{n}.png';out_d=DEATH/'src~'/f'draft_death_v{n}.png'
        pm=HERE/f'draft_preview_v{n}.png';pd=DEATH/'src~'/f'draft_preview_v{n}.png'
    else:
        out_m=MINES/'rail_mines_neon_storm.png';out_d=DEATH/'storm_mine.png'
        pm=MINES/'preview.png';pd=DEATH/'preview.png'
    write_new(mine,out_m);write_new(death,out_d)
    preview(mine,pm);preview(death,pd)
    print(out_m);print(out_d)


if __name__=='__main__':main()
