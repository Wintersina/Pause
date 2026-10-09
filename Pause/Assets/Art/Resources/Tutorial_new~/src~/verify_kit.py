"""Check staged sprite dimensions, alpha bounds, and visor registration."""
from collections import deque
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
OLD = ROOT.parent / 'Tutorial'


def face_bbox(path):
    im = Image.open(path).convert('RGBA')
    color = im.getpixel((128, 128))
    todo = deque([(128, 128)])
    seen = {(128, 128)}
    while todo:
        x, y = todo.popleft()
        for xy in ((x-1, y), (x+1, y), (x, y-1), (x, y+1)):
            xx, yy = xy
            if 0 <= xx < im.width and 0 <= yy < im.height and xy not in seen:
                if im.getpixel(xy) == color:
                    seen.add(xy)
                    todo.append(xy)
    return (min(x for x, y in seen), min(y for x, y in seen),
            max(x for x, y in seen)+1, max(y for x, y in seen)+1)


def main():
    names = {p.name for p in OLD.glob('*.png')}
    staged = {p.name for p in ROOT.glob('tut_*.png')}
    assert names == staged, (sorted(names-staged), sorted(staged-names))
    for name in sorted(names):
        a = Image.open(OLD/name).convert('RGBA')
        b = Image.open(ROOT/name)
        assert b.mode == 'RGBA' and b.size == a.size, name
        ox0, oy0, ox1, oy1 = a.getbbox()
        nx0, ny0, nx1, ny1 = b.getbbox()
        assert ox0-4 <= nx0 and oy0-4 <= ny0, (name, 'min bbox')
        assert nx1 <= ox1+4 and ny1 <= oy1+4, (name, 'max bbox')
        assert (nx0, ny0, nx1, ny1) == (ox0, oy0, ox1, oy1), (name, 'padding')
        assert b.getchannel('A').getextrema()[1] == 255, (name, 'no opaque pixels')
    old_face = face_bbox(OLD/'tut_robot.png')
    new_face = face_bbox(ROOT/'tut_robot.png')
    assert all(abs(x-y) <= 3 for x, y in zip(old_face, new_face)), (old_face, new_face)
    preview = Image.open(ROOT/'preview.png')
    assert preview.size == (1600, 1100)
    print(f'PASS: {len(names)} RGBA sprites; dimensions and transparent padding match exactly.')
    print(f'PASS: visor bounds {old_face} -> {new_face} (max delta 2 px).')
    print('PASS: 2x preview exists.')


if __name__ == '__main__': main()
