"""Put the J1d bank paintings into cells 3 / 4 of hull_redo_state{0,1,2}.png.
Convention of the existing sheets (NeonComet): column 6 (cell 3, 'bank left') has the larger LEFT half,
column 7 (cell 4, 'bank right') the larger RIGHT half. Codex's file names were handed out the other way
round, so the side is chosen by measured area, not by file name."""
import os
import numpy as np
from PIL import Image

D = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def is_bg(a):
    v = a.astype(int)
    r, g, b = v[..., 0], v[..., 1], v[..., 2]
    return (g > 100) & (g > r * 1.55) & (g > b * 1.4) & (g - r > 65) & (g - b > 65)


def left_bigger(im):
    m = ~is_bg(np.array(im.convert("RGB")))
    return m[:, :512].sum() > m[:, 512:].sum()


for s in range(3):
    f = {side: Image.open(os.path.join(D, f"bank_state{s}_{side}.png")).convert("RGB") for side in ("left", "right")}
    big_left = [side for side in f if left_bigger(f[side])]
    assert len(big_left) == 1, (s, big_left)
    cell3 = f[big_left[0]]
    cell4 = f["right" if big_left[0] == "left" else "left"]
    sheet = Image.open(os.path.join(D, f"hull_redo_state{s}.png")).convert("RGB")
    sheet.paste(cell3, (0, 1024))
    sheet.paste(cell4, (1024, 1024))
    sheet.save(os.path.join(D, f"hull_redo_state{s}.png"))
    print(s, "cell3 (bank left) from", big_left[0])
