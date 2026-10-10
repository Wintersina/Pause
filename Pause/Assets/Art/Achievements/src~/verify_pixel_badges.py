"""Verify all native and x8 achievement sprites; render the pass-2/pass-3 reference."""
from __future__ import annotations

from collections import Counter
from pathlib import Path
from PIL import Image, ImageDraw
from build_pixel_badges import ART, SRC, OUT, BADGES, TIERS, NEON, GROUPS, BG, source

N=Image.Resampling.NEAREST


def dhash(im):
    gray=im.convert('L').resize((17,16),Image.Resampling.BOX)
    p=gray.load()
    return tuple(p[x,y]>p[x+1,y] for y in range(16) for x in range(16))


def distance(a,b):
    return sum(x!=y for x,y in zip(a,b))


def reference_sheet():
    cases=[
      ('meta_first_flight','Space','Space'),('world_frost_reached','Frost','Frost'),
      ('world_verdant_reached','Verdant','Verdant'),('boss_space','Space','Space'),
      ('boss_frost','Frost','Frost'),('boss_tide','Space','Tide'),
      ('elite_first','Space','Space'),('kills_1000','Ember','Ember'),
      ('codex_complete','Space','Space'),('score_150k','Space','Space'),
      ('speed_flash','Space','Space'),('pause_perfect_dodge','Space','Space')]
    rail_names={'Space':'Space/rail_space_wide_v1.png',
      'Frost':'Frost/rail_frost_wide_v1.png',
      'Verdant':'Verdant/rail_forest_wide_v1.png',
      'Ember':'Ember/rail_ember_wide_v1.png'}
    gap=12; width=4*128+5*gap; row_h=151
    sheet=Image.new('RGB',(width,34+len(cases)*row_h),(12,16,26))
    d=ImageDraw.Draw(sheet)
    for x,label in zip([12,152,292,432],['PASS 2','PASS 3','GAME RAIL','GAME BOSS']):
        d.text((x,10),label,fill=(223,234,232))
    for row,(ident,rail_world,boss_world) in enumerate(cases):
        y=34+row*row_h
        old=Image.open(SRC/'rejected_v2'/'icons_128'/f'{ident}.png').convert('RGBA')
        new=Image.open(OUT/f'{ident}.png').convert('RGBA')
        rail=Image.open(ART/'Resources'/'Worlds'/rail_names[rail_world]).convert('RGBA')
        # The 128px rail patch is taken directly from its original pixel grid.
        rail=rail.crop((165,115+row*117,293,243+row*117))
        boss_path='git:tide' if boss_world=='Tide' else f'Resources/Bosses/{boss_world}.png'
        boss,_=source(boss_path,384,3,64,64)
        boss_tile=Image.new('RGBA',(128,128))
        boss_tile.alpha_composite(boss,((128-boss.width)//2,(128-boss.height)//2))
        for x,tile in zip([12,152,292,432],[old,new,rail,boss_tile]):
            rgb=Image.new('RGB',(128,128),BG)
            rgb.paste(tile,(0,0),tile)
            sheet.paste(rgb,(x,y))
        d.text((12,y+132),ident,fill=(198,212,212))
    sheet.save(SRC/'before_after_12.png')


def verify():
    expected={ident for ident,_,_ in BADGES}
    issues=[]
    if len(expected)!=60:issues.append('badge manifest is not 60 unique IDs')
    if {p.stem for p in OUT.glob('*.png')}!=expected:issues.append('128 icon inventory differs')
    masters={p.name.removesuffix('_1024.png') for p in SRC.glob('*_1024.png') if p.name!='sheet_1024.png'}
    if masters!=expected:issues.append('1024 master inventory differs')
    frame_files=list((SRC/'frames').glob('*.png'))
    if len(frame_files)!=14:issues.append(f'frame inventory is {len(frame_files)}, expected 14')
    counts={};hashes={};tier_counts=Counter();group_counts=Counter()
    for ident,tier,world in BADGES:
        icon=Image.open(OUT/f'{ident}.png')
        master=Image.open(SRC/f'{ident}_1024.png')
        if icon.size!=(128,128) or icon.mode!='RGBA':issues.append(f'{ident}: icon mode/size')
        if master.size!=(1024,1024) or master.mode!='RGB':issues.append(f'{ident}: master mode/size')
        alpha=icon.getchannel('A')
        if {value for _,value in (alpha.getcolors(16384) or [])}!={0,255}:
            issues.append(f'{ident}: alpha is not hard edged')
        outside=[]
        for y in range(128):
            for x in range(128):
                if alpha.getpixel((x,y)) and (x-63.5)**2+(y-63.5)**2>57.6**2:
                    outside.append((x,y))
        if outside:issues.append(f'{ident}: {len(outside)} pixels outside 90% circle; first {outside[0]}')
        composite=Image.new('RGB',(128,128),BG)
        composite.paste(icon,(0,0),icon)
        if master.tobytes()!=composite.resize((1024,1024),N).tobytes():
            issues.append(f'{ident}: 1024 is not exact x8 copy')
        if any(master.getpixel(pt)!=BG for pt in [(0,0),(1023,0),(0,1023),(1023,1023)]):
            issues.append(f'{ident}: 1024 corners are not #07070f')
        count=len(icon.getcolors(1000000) or [])
        counts[ident]=count
        if not 120<=count<=1200:issues.append(f'{ident}: {count} colours outside 120..1200')
        if icon.getpixel((50,18))[:3]!=TIERS[tier][1]:
            issues.append(f'{ident}: tier metal sample is wrong')
        if icon.getpixel((64,15))[:3]!=NEON[tier][2]:
            issues.append(f'{ident}: tier neon sample is wrong')
        hashes[ident]=dhash(composite)
        tier_counts[tier]+=1
        group_counts[GROUPS[ident.split('_')[0]]]+=1
    pairs=sorted((distance(hashes[a],hashes[b]),a,b)
                 for i,a in enumerate(hashes) for b in list(hashes)[i+1:])
    exact=[p for p in pairs if p[0]==0]
    if exact:issues.append(f'duplicate perceptual hashes: {exact}')
    report=[
      'VERIFY TABLE',
      f'badges | {len(BADGES)} / 60',
      f'128 icons | {len(list(OUT.glob("*.png")))} RGBA, hard alpha',
      f'1024 masters | {len(masters)} RGB, exact x8, #07070f corners',
      f'frames | {len(frame_files)} = 4 tiers + 10 accessories',
      f'tiers | {dict(tier_counts)}',
      f'groups | {dict(group_counts)}',
      f'colour range | {min(counts.values())}..{max(counts.values())} per icon',
      f'dHash exact duplicates | {len(exact)}',
      f'failures | {len(issues)}',
      '',
      'NEAREST PERCEPTUAL HASH PAIRS (Hamming distance on 256-bit dHash)',
      *[f'{n} | {a} | {b}' for n,a,b in pairs[:12]],
      '',
      'ID | TIER | COLOURS',
      *[f'{ident} | {tier} | {counts[ident]}' for ident,tier,_ in BADGES],
    ]
    if issues:report.extend(['','ISSUES',*issues])
    (SRC/'verification.txt').write_text('\n'.join(report)+'\n')
    reference_sheet()
    print('\n'.join(report[:10]))
    if issues:raise AssertionError('\n'.join(issues))

if __name__=='__main__':verify()
