"""Prints the damage blobs (key_state1 vs 0, key_state2 vs 1) as ellipses (cx, cy, rx, ry) in the 256 px cell
(same scale/anchor as build_restyle.build_sheet) and the % of hull covered by the union."""
import os
import sys
import numpy as np
from PIL import Image


class ndi:
    @staticmethod
    def binary_dilation(m, iterations=1):
        for _ in range(iterations):
            n = m.copy()
            n[1:] |= m[:-1]; n[:-1] |= m[1:]; n[:, 1:] |= m[:, :-1]; n[:, :-1] |= m[:, 1:]
            m = n
        return m

    @staticmethod
    def label(m):
        lab = np.zeros(m.shape, int)
        n = 0
        for y, x in zip(*np.nonzero(m)):
            if lab[y, x]:
                continue
            n += 1
            st = [(y, x)]
            lab[y, x] = n
            while st:
                a, b = st.pop()
                for da, db in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                    p, q = a + da, b + db
                    if 0 <= p < m.shape[0] and 0 <= q < m.shape[1] and m[p, q] and not lab[p, q]:
                        lab[p, q] = n
                        st.append((p, q))
        return lab, n

d = os.path.dirname(os.path.abspath(__file__)) + "/.."
k = [np.array(Image.open(f"{d}/key_state{i}.png").convert("RGB")).astype(int) for i in range(3)]
green = (k[0][..., 1] > 200) & (k[0][..., 0] < 60) & (k[0][..., 2] < 60)
hull = ~green
ys, xs = np.nonzero(hull)
bw = xs.max() - xs.min() + 1
scale = 174 / bw
ax, ay = (xs.min() + xs.max()) / 2, (ys.min() + ys.max()) / 2
print("scale", scale)


def blobs(a, b, name, thr=60, grow=10):
    m = (np.abs(a - b).sum(2) > thr) & hull
    m = ndi.binary_dilation(m, iterations=grow)
    lab, n = ndi.label(m)
    out = []
    for i in range(1, n + 1):
        yy, xx = np.nonzero(lab == i)
        if len(yy) < 900:
            continue
        cx = 128 + (xx.mean() - ax) * scale
        cy = 128 + (yy.mean() - ay) * scale
        rx = (xx.max() - xx.min()) / 2 * scale + 2
        ry = (yy.max() - yy.min()) / 2 * scale + 2
        out.append((round(cx), round(cy), round(rx), round(ry), len(yy)))
    print(name, out)


blobs(k[0], k[1], "s1")
blobs(k[1], k[2], "s2 (extra over s1)")
