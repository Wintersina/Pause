from pathlib import Path

from PIL import Image


OUT = Path(__file__).resolve().parent.parent
GREEN = (0, 255, 0)
NAMES = ('key_state0.png', 'key_state1.png', 'key_state2.png')


def load(name):
    image = Image.open(OUT / name).convert('RGB')
    if image.size != (1024, 1024):
        raise SystemExit(f'{name}: expected 1024x1024, got {image.size}')
    return image


def non_green(image):
    return [pixel != GREEN for pixel in image.getdata()]


def bbox(image):
    points = [(x, y) for y in range(1024) for x in range(1024)
              if image.getpixel((x, y)) != GREEN]
    if not points:
        return None
    xs, ys = zip(*points)
    return min(xs), min(ys), max(xs), max(ys)


def changed(a, b):
    return sum(pa != pb for pa, pb in zip(a.getdata(), b.getdata()))


images = [load(name) for name in NAMES]
masks = [non_green(image) for image in images]
print('file           size       non-green bbox')
for name, image in zip(NAMES, images):
    print(f'{name:14} {image.size[0]}x{image.size[1]}   {bbox(image)}')

base_mask = masks[0]
base_count = sum(base_mask)
print(f'base silhouette pixels: {base_count}')
print('comparison     silhouette mask diff   colour pixel diff')
colour_diffs = []
for index in (1, 2):
    silhouette_diff = sum(a != b for a, b in zip(base_mask, masks[index]))
    colour_diff = changed(images[0], images[index])
    colour_diffs.append(colour_diff)
    print(f'state0 vs {index}: {silhouette_diff / base_count * 100:8.4f}%              {colour_diff / (1024 * 1024) * 100:8.4f}%')

checks = []
checks.append(all(image.size == (1024, 1024) for image in images))
checks.append(all(sum(a != b for a, b in zip(base_mask, mask)) / base_count * 100 < 1.5
                  for mask in masks[1:]))
checks.append(3 < colour_diffs[0] / (1024 * 1024) * 100 < 45)
checks.append(8 < colour_diffs[1] / (1024 * 1024) * 100 < 45)
checks.append(colour_diffs[1] > colour_diffs[0] * 1.4)
if not all(checks):
    raise SystemExit('FAIL: one or more J1 constraints did not pass')
print('PASS: dimensions, shared silhouette, damage ranges, and state-2 ratio')
