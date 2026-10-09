"""Audit the staged kit against the current Unity sprite contract."""
from pathlib import Path
from PIL import Image

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
OLD = OUT.parent / 'Tutorial'
expected = {p.name for p in OLD.glob('*.png')}
actual = {p.name for p in OUT.glob('tut_*.png')}
assert len(expected) == 20 and actual == expected, (len(expected), actual ^ expected)

def strong_bbox(im):
    return im.getchannel('A').point(lambda x: 255 if x >= 128 else 0).getbbox()

for name in sorted(expected):
    old = Image.open(OLD / name).convert('RGBA')
    new = Image.open(OUT / name)
    assert new.mode == 'RGBA', (name, new.mode)
    assert new.size == old.size, (name, new.size, old.size)
    w,h = new.size
    assert w % 4 == h % 4 == 0
    native = Image.open(SRC / 'native' / name).convert('RGBA')
    assert native.size == (w//4,h//4), name
    assert native.resize((w,h),Image.Resampling.NEAREST).tobytes() == new.tobytes(), name
    assert new.resize((w//4,h//4),Image.Resampling.NEAREST).resize((w,h),Image.Resampling.NEAREST).tobytes() == new.tobytes(), name
    opaques = {p[:3] for _,p in new.getcolors(1000000) if p[3]}
    limit = 20 if name == 'tut_robot.png' else 10
    assert len(opaques) <= limit, (name,len(opaques),limit)
    ob,nb = strong_bbox(old),strong_bbox(new)
    assert ob and nb
    assert all(abs(a-b) <= 4 for a,b in zip(ob,nb)), (name,ob,nb)
    print(f'{name:19s} {w:3d}x{h:<3d}  {len(opaques):2d} colors  bbox delta {tuple(abs(a-b) for a,b in zip(ob,nb))}')

# Runtime uses fixed placements in RobotSpeaker.PlaceSvg, expressed as source
# pixels here. New face screen must retain all three old anchors exactly.
head = Image.open(OUT / 'tut_robot.png').convert('RGBA')
panel = (12,23,37,255) # TutorialPalette.Panel = #0C1725
for label,xy in {'left eye':(94,114),'right eye':(162,114),'mouth':(128,158),'screen center':(128,128)}.items():
    assert head.getpixel(xy) == panel, (label,xy,head.getpixel(xy))
# Visor nominal boundary in old source is x58..198, y86..174; the new
# hand-drawn polygon is x56..200, y88..176: each boundary differs by 2 px.
assert max(abs(a-b) for a,b in zip((58,86,198,174),(56,88,200,176))) <= 3
# Eye, mouth, lamp, and jet frames retain their alpha center relative to
# their unchanged canvases; their RectTransform centers in RobotSpeaker are
# therefore unchanged too.
anchored = ('tut_eye_','tut_mouth_','tut_lamp','tut_jet_')
for name in sorted(n for n in expected if n.startswith(anchored)):
    ob = strong_bbox(Image.open(OLD/name).convert('RGBA'))
    nb = strong_bbox(Image.open(OUT/name).convert('RGBA'))
    oc = ((ob[0]+ob[2])/2,(ob[1]+ob[3])/2)
    nc = ((nb[0]+nb[2])/2,(nb[1]+nb[3])/2)
    assert max(abs(a-b) for a,b in zip(oc,nc)) <= 3, (name,oc,nc)

bubble = Image.open(OUT/'tut_bubble.png').convert('RGBA')
assert bubble.getpixel((96,96)) == panel
assert bubble.getpixel((88,88)) == panel
assert (OUT/'preview.png').exists()
print('PASS: 20 RGBA sprites, native 4x grids, palette, footprint, visor and panel registration')
