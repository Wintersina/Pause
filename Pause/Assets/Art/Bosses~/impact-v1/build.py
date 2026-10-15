#!/usr/bin/env python3
"""Rebuild Pause boss impact concepts from the shipped Resources atlases.

Requires Pillow and NumPy. Writes only alongside this script.
"""
from pathlib import Path
import math
import random
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
SOURCE = HERE.parents[1] / "Resources" / "Bosses"
OUT = HERE / "attack_damage"
BOSSES = ("Space", "Frost", "Verdant", "Ember", "Tide", "Storm", "Desert")
SIZE = 384
SAME_HULL = 56   # max per-channel difference for "the pose did not move this pixel"


def cell(atlas, index, columns=5):
    x, y = index % columns * SIZE, index // columns * SIZE
    return atlas.crop((x, y, x + SIZE, y + SIZE))


def attacks(expanded):
    if expanded:
        return [(f"tell{pose}_{step}", 26 + pose * 3 + step)
                for pose in range(3) for step in range(3)]
    return [(f"tell{pose}_{step}", 5 + pose * 2 + step if pose < 2 else 10 + step)
            for pose in range(3) for step in range(2)] + [("fire", 9)]


def damage_attack(base, damaged, attack):
    """Transfer registered hull changes while retaining the attack silhouette.

    Damage atlases use the same 384 square and hull registration as the body
    atlas. Transfer darkening as a bounded RGB delta, and copy bright fracture
    pixels where both silhouettes overlap. Alpha and outside sparks stay from
    the original attack pose.
    """
    b = np.asarray(base, dtype=np.int16)
    d = np.asarray(damaged, dtype=np.int16)
    a = np.asarray(attack, dtype=np.int16).copy()
    shared = (b[:, :, 3] > 80) & (d[:, :, 3] > 80) & (a[:, :, 3] > 80)
    # Only transfer where the pose still shows the idle hull's own pixels. The damaged idle
    # cell is drawn in the IDLE pose: copying its pixels (the bright fracture copy above all)
    # onto parts that moved in this pose stamped a second, offset hull over the attack frame, so a
    # damaged boss firing read as several frames overlaid. Moved parts keep the pose's drawing.
    shared &= np.max(np.abs(a[:, :, :3] - b[:, :, :3]), axis=2) <= SAME_HULL
    delta = d[:, :, :3] - b[:, :, :3]
    intensity = np.max(np.abs(delta), axis=2)
    changed = shared & (intensity > 22)
    # Broad tint/dents inherit the source pose's shading.
    a[:, :, :3] = np.where(changed[:, :, None],
                            np.clip(a[:, :, :3] + np.clip(delta, -105, 65) * .75, 0, 255),
                            a[:, :, :3])
    # High value fracture lines are the readable, pixel exact damage cues.
    bright = changed & (np.max(d[:, :, :3], axis=2) > 165) & (np.max(delta, axis=2) > 35)
    a[:, :, :3] = np.where(bright[:, :, None], d[:, :, :3], a[:, :, :3])
    return Image.fromarray(a.astype(np.uint8), "RGBA")


def build_attack_damage():
    OUT.mkdir(exist_ok=True)
    records = []
    samples = []
    for boss in BOSSES:
        body = Image.open(SOURCE / f"{boss}.png").convert("RGBA")
        damage = Image.open(SOURCE / f"{boss}_damage.png").convert("RGBA")
        expanded = body.height == 2688
        baseline = cell(body, 20 if expanded else 0)
        poses = attacks(expanded)
        for name, index in poses:
            pose = cell(body, index)
            for stage in range(1, 5):
                damaged = cell(damage, (stage - 1) * 2, 2)
                result = damage_attack(baseline, damaged, pose)
                path = OUT / f"{boss.lower()}_{name}_stage{stage}.png"
                result.save(path, optimize=True)
                if name == ("tell0_2" if expanded else "tell0_1"):
                    samples.append((f"{boss}  stage {stage}", result))
        records.append((boss, expanded, poses))
    return records, samples


RIM = (24, 39, 53, 235)
GLASS = (146, 199, 219, 235)
WHITE = (249, 253, 255, 255)


def fracture(draw, origin, angle, length, progress, seed, width=7):
    rng = random.Random(seed)
    ox, oy = origin
    points = [(ox, oy)]
    count = max(3, int(length / 43))
    for i in range(1, count + 1):
        dist = length * i / count
        offset = rng.uniform(-21, 21) * (i / count) ** .5
        points.append((round(ox + math.cos(angle) * dist - math.sin(angle) * offset),
                       round(oy + math.sin(angle) * dist + math.cos(angle) * offset)))
    points = points[:max(2, round(count * progress) + 1)]
    draw.line(points, fill=RIM, width=width + 8, joint="curve")
    draw.line(points, fill=GLASS, width=width + 3, joint="curve")
    draw.line(points, fill=WHITE, width=2 if width < 7 else 3, joint="curve")
    for j in range(1, len(points) - 1):
        x, y = points[j]
        for side in (-1, 1):
            if rng.random() > .78: continue
            direction = angle + side * rng.uniform(.42, 1.14)
            reach = rng.uniform(35, 112) * (1 - .3 * j / count)
            mid = (round(x + math.cos(direction) * reach * .55), round(y + math.sin(direction) * reach * .55))
            tip = (round(x + math.cos(direction) * reach), round(y + math.sin(direction) * reach))
            kink = (mid[0] + rng.randrange(-10, 11), mid[1] + rng.randrange(-10, 11))
            draw.line([(x,y), kink, tip], fill=RIM, width=4)
            draw.line([(x,y), kink, tip], fill=WHITE, width=1)
            if rng.random() < .55:
                twig = (tip[0] + round(math.cos(direction + side*.8)*reach*.35), tip[1] + round(math.sin(direction + side*.8)*reach*.35))
                draw.line([kink, twig], fill=GLASS, width=1)


def glass_ring(draw, origin, radius, seed, progress=1):
    rng = random.Random(seed)
    count = 16
    points = []
    for i in range(count + 1):
        a = i * math.tau / count
        r = radius * rng.uniform(.85, 1.13)
        points.append((round(origin[0]+math.cos(a)*r), round(origin[1]+math.sin(a)*r)))
    segments = max(1, round(count * progress))
    for i in range(segments):
        if i % 7 == 5: continue
        draw.line(points[i:i+2], fill=RIM, width=8)
        draw.line(points[i:i+2], fill=GLASS, width=4)
        draw.line(points[i:i+2], fill=WHITE, width=1)


def glass_chips(draw, origin, radius, seed, count):
    rng = random.Random(seed)
    for _ in range(count):
        a = rng.random()*math.tau
        r = rng.uniform(radius*.25, radius)
        x, y = origin[0]+math.cos(a)*r, origin[1]+math.sin(a)*r
        w = rng.randint(8, 25)
        p = [(round(x),round(y)), (round(x+w),round(y-rng.randint(3,14))),
             (round(x+w+rng.randint(1,8)),round(y+rng.randint(7,23)))]
        draw.polygon(p, fill=(85, 135, 155, 100), outline=GLASS)
        draw.line(p[:2], fill=WHITE, width=2)


def cracks():
    folder = HERE / "crack_overlays"
    folder.mkdir(exist_ok=True)
    points = [(270, 620), (845, 1130), (500, 1860), (785, 450)]
    previews = []
    for variant, point in enumerate(points):
        im = Image.new("RGBA", (1080, 2400))
        dr = ImageDraw.Draw(im)
        rng = random.Random(100 + variant)
        for arm in range(15):
            angle = arm * math.tau / 15 + rng.uniform(-.12, .12)
            fracture(dr, point, angle, rng.randrange(580, 1320), 1, variant * 101 + arm, 6 if arm % 3 else 9)
        for ring, radius in enumerate((65, 135, 225, 345)):
            glass_ring(dr, point, radius, 700+variant*10+ring)
        glass_chips(dr, point, 295, 900+variant, 65)
        x, y = point
        dr.polygon([(x-29,y-13),(x-11,y-31),(x+18,y-24),(x+32,y+4),(x+15,y+29),(x-21,y+22)], fill=RIM, outline=WHITE)
        im.save(folder / f"crack_{variant + 1:02d}.png", optimize=True)
        previews.append((f"crack {variant + 1}", im))
    strip = Image.new("RGBA", (4096, 512))
    for frame in range(8):
        im = Image.new("RGBA", (512, 512))
        dr = ImageDraw.Draw(im)
        progress = (frame + 1) / 8
        for arm in range(14):
            if progress < .35 and arm % 2: continue
            fracture(dr, (256, 256), arm * math.tau / 14 + .08, 235,
                     progress, 700 + arm,
                     max(4, round(4 + frame*.85)))
        for ring, radius in enumerate((35, 80, 145, 215)):
            if progress > ring*.18:
                glass_ring(dr, (256,256), radius*progress, 1300+ring, min(1, progress*1.6))
        glass_chips(dr, (256,256), 180*progress, 1500, frame*5+3)
        strip.alpha_composite(im, (frame * 512, 0))
    strip.save(folder / "crack_growth.png", optimize=True)
    return previews, strip


def burst_frame(size, frame, small=False):
    im = Image.new("RGBA", (size, size))
    dr = ImageDraw.Draw(im)
    rng = random.Random(900)
    c = size // 2
    s = size / 384
    def xy(a, r):
        return (round(c + math.cos(a)*r*s), round(c + math.sin(a)*r*s))
    def poly(points, fill, edge=None):
        dr.polygon(points, fill=fill)
        if edge: dr.line(points+[points[0]], fill=edge, width=max(1,round(2*s)))
    # Shock front leads the expanding flame and fades into the smoke phase.
    if frame < 9:
        radius = 25 + frame*20
        alpha = max(45, 245-frame*23)
        pts = [xy(i*math.tau/32, radius+rng.uniform(-5,5)) for i in range(32)]
        dr.line(pts+[pts[0]], fill=(111,43,16,alpha), width=max(2,round(12*s)))
        dr.line(pts+[pts[0]], fill=(255,179,47,alpha), width=max(1,round(5*s)))
        dr.line(pts+[pts[0]], fill=(255,244,174,alpha), width=max(1,round(2*s)))
    if frame <= 7:
        outer = min(135, 23+frame*17)
        inner = outer*.58
        points=[]
        for k in range(32):
            a=k*math.tau/32
            r=outer*(rng.uniform(.82,1.15) if k%2 else .53)
            points.append(xy(a,r))
        poly(points,(164,48,13,max(80,255-frame*20)),(75,31,22,180))
        points=[]
        for k in range(32):
            a=k*math.tau/32
            r=inner*(rng.uniform(.9,1.18) if k%2 else .65)
            points.append(xy(a,r))
        poly(points,(255,128,19,max(95,255-frame*22)),(255,201,46,220))
        # Radial flame blades have an orange shadow and yellow cutting edge.
        for k in range(14):
            a=k*math.tau/14+rng.uniform(-.1,.1)
            tip=xy(a,outer*rng.uniform(1.12,1.5))
            p1=xy(a-.12,inner*.58); p2=xy(a+.12,inner*.58)
            poly([p1,tip,p2],(210,65,14,max(80,245-frame*22)))
            poly([xy(a-.035,inner*.65),tip,xy(a+.045,inner*.63)],(255,197,36,max(90,250-frame*21)))
        core=max(7,round((35+frame*2)*(1-frame/9)))
        corepts=[xy(k*math.tau/12,core*(1.5 if k%2 else .9)) for k in range(12)]
        poly(corepts,(255,236,94,max(80,255-frame*25)))
        poly([xy(k*math.tau/10,core*.85) for k in range(10)],(255,253,233,max(90,255-frame*28)))
    # Large angular projectiles carry a shadow, hot face and tapered trail.
    for k in range(17):
        a=k*math.tau/17+rng.uniform(-.14,.14)
        speed=rng.uniform(9,16)
        dist=24+frame*speed
        if dist>185: continue
        w=rng.uniform(4,9)*(1+frame*.025)
        tail=xy(a,dist-18-frame*2)
        p1=xy(a-.07,dist); tip=xy(a,dist+w*1.6); p2=xy(a+.07,dist)
        alpha=max(35,255-frame*19)
        poly([tail,p1,tip,p2],(143,52,19,alpha))
        poly([xy(a-.025,dist-8),p1,tip],(255,180,40,alpha))
        dr.line([xy(a,dist-28),xy(a,dist-10)],fill=(255,231,115,alpha),width=max(1,round(3*s)))
    for k in range(12):
        a=k*math.tau/12+.22
        dist=32+frame*(8+k%4*2)
        if dist>182: continue
        x,y=xy(a,dist)
        side=max(2,round((8+frame*.4)*s))
        poly([(x-side,y-side//2),(x+side//2,y-side),(x+side,y+side//2),(x-side//2,y+side)],
             (84,48,34,max(35,215-frame*16)),(255,146,38,max(35,230-frame*21)))
    if frame>=5:
        smoke_alpha=max(22,185-(frame-5)*23)
        for k in range(18):
            a=k*math.tau/18+rng.uniform(-.2,.2)
            dist=(22+(frame-4)*rng.uniform(8,18))
            x,y=xy(a,dist)
            r=round(rng.uniform(8,18)*s)
            poly([(x-r,y),(x-r//2,y-r),(x+r//2,y-r),(x+r,y),(x+r//2,y+r),(x-r//2,y+r)],
                 (53,48,45,smoke_alpha),(107,80,60,smoke_alpha))
    return im


def bursts():
    results = []
    for size, name in [(384, "boss_hit_burst.png"), (192, "boss_hit_burst_small.png")]:
        strip = Image.new("RGBA", (size * 12, size))
        for frame in range(12):
            strip.alpha_composite(burst_frame(size, frame, size == 192), (frame * size, 0))
        strip.save(HERE / name, optimize=True)
        results.append((name, strip, size))
    return results


def contact(items, columns, thumb, path):
    rows = math.ceil(len(items) / columns)
    sheet = Image.new("RGB", (columns * thumb, rows * (thumb + 24)), (12, 21, 31))
    dr = ImageDraw.Draw(sheet)
    for i, (name, im) in enumerate(items):
        x, y = i % columns * thumb, i // columns * (thumb + 24)
        view = im.copy()
        view.thumbnail((thumb - 12, thumb - 12), Image.Resampling.NEAREST)
        sheet.paste(view, (x + (thumb - view.width)//2, y + (thumb - view.height)//2), view)
        dr.text((x + 7, y + thumb), name, fill=(232, 245, 252))
    sheet.save(path, optimize=True)


def main():
    crack_previews, growth = cracks()
    burst_results = bursts()
    fx = crack_previews[:]
    for frame in range(8):
        fx.append((f"growth {frame}", growth.crop((frame*512, 0, (frame+1)*512, 512))))
    for name, strip, size in burst_results:
        for frame in range(12):
            fx.append((f"{'heavy' if size == 384 else 'light'} {frame}",
                       strip.crop((frame*size, 0, (frame+1)*size, size))))
    contact(fx, 6, 160, HERE / "contact_job1.png")
    print("wrote Job 1 impact assets and contact sheet")


if __name__ == "__main__":
    main()
