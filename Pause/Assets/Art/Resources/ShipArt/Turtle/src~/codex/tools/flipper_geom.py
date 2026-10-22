"""Prints the four flipper blobs of the stock idle cell (silhouette pixels outside the shell, |x-128| > 63)."""
import os
import numpy as np
from PIL import Image

S = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "..")
a = np.array(Image.open(os.path.join(S, "Turtle.png")).convert("RGBA"))[:256, :256, 3] > 0
yy, xx = np.mgrid[0:256, 0:256]
for name, m in (("UL", (xx < 65) & (yy < 128)), ("UR", (xx > 191) & (yy < 128)),
                ("LL", (xx < 65) & (yy >= 128)), ("LR", (xx > 191) & (yy >= 128))):
    mm = a & m
    ys, xs = np.nonzero(mm)
    print(name, "n", len(ys), "x", xs.min(), xs.max(), "y", ys.min(), ys.max(), "centroid", round(xs.mean(), 1), round(ys.mean(), 1))
