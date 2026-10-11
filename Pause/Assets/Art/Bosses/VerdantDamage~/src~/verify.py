"""Check the Verdant damage deliverables against the atlas contract."""

from pathlib import Path
import colorsys
import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parent.parent
CELL = 384


def tiles(im, cols, rows):
    for y in range(rows):
        for x in range(cols):
            yield im.crop((x * CELL, y * CELL, (x + 1) * CELL, (y + 1) * CELL))


def red_share(im):
    rgba = np.array(im, dtype=np.uint8)
    active = rgba[:, :, 3] > 0
    colors = rgba[:, :, :3][active]
    red = 0
    for r, g, b in colors:
        hue, sat, _ = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
        red += sat > 0.5 and (hue * 360 >= 345 or hue * 360 <= 15)
    return red / max(len(colors), 1)


def main():
    damage = Image.open(OUT / "Verdant_damage.png")
    fx = Image.open(OUT / "Verdant_damage_fx.png")
    assert (damage.size, damage.mode) == ((768, 1536), "RGBA")
    assert (fx.size, fx.mode) == ((2304, 768), "RGBA")
    body = list(tiles(damage, 2, 4))
    effects = list(tiles(fx, 6, 2))
    for i, tile in enumerate(body + effects):
        alpha = np.array(tile.getchannel("A"))
        assert not (alpha[0].any() or alpha[-1].any() or alpha[:, 0].any() or alpha[:, -1].any()), i
        assert red_share(tile) == 0, i
        if i < 8:
            bbox = tile.getchannel("A").point(lambda v: 255 if v >= 16 else 0).getbbox()
            assert bbox and bbox[0] >= 18 and bbox[1] >= 35 and bbox[2] <= 362 and bbox[3] <= 382, (i, bbox)
    small = [body[2 * i].resize((190, 190), Image.Resampling.NEAREST) for i in range(4)]
    diffs = []
    for a, b in zip(small, small[1:]):
        delta = np.abs(np.array(a, dtype=np.int16) - np.array(b, dtype=np.int16))
        diffs.append(float(np.mean(np.any(delta > 24, axis=2))))
    assert all(d > 0.15 for d in diffs), diffs
    assert all(np.any(np.array(body[2 * i]) != np.array(body[2 * i + 1])) for i in range(4))
    assert all(np.any(np.array(effects[i]) != np.array(effects[(i + 1) % 6])) for i in range(6))
    assert all(np.any(np.array(effects[6 + i]) != np.array(effects[6 + (i + 1) % 6])) for i in range(6))
    preview = Image.open(OUT / "preview.png")
    assert preview.size == (3840, 828)
    gif = Image.open(OUT / "preview.gif")
    assert gif.n_frames == 6
    print("PASS: dimensions, RGBA, alpha bounds/borders, zero red-band pixels, distinct stages and loops")
    print("190 px stage-difference fractions:", ", ".join(f"{d:.3f}" for d in diffs))


if __name__ == "__main__":
    main()
