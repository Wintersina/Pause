"""RotSprite-style 45 deg rotation for pixel art: Scale2x x3 (8x, no new colours), nearest rotate, sample block centres."""
import sys, numpy as np
from PIL import Image
def scale2x(a):
    H, W = a.shape[:2]
    p = np.pad(a, ((1, 1), (1, 1), (0, 0)), mode='edge')
    E = p[1:-1, 1:-1]; B = p[:-2, 1:-1]; H_ = p[2:, 1:-1]; D = p[1:-1, :-2]; F = p[1:-1, 2:]
    eq = lambda x, y: (x == y).all(-1)
    c = ~eq(B, H_) & ~eq(D, F)
    out = np.empty((H * 2, W * 2, a.shape[2]), a.dtype)
    out[0::2, 0::2] = np.where((c & eq(D, B))[..., None], D, E)
    out[0::2, 1::2] = np.where((c & eq(B, F))[..., None], F, E)
    out[1::2, 0::2] = np.where((c & eq(D, H_))[..., None], D, E)
    out[1::2, 1::2] = np.where((c & eq(H_, F))[..., None], F, E)
    return out
src, dst, deg, N = sys.argv[1], sys.argv[2], float(sys.argv[3]), int(sys.argv[4])
a = np.array(Image.open(src).convert('RGBA'))
a[a[..., 3] == 0] = 0
pad = (N - a.shape[0]) // 2
a = np.pad(a, ((pad, N - a.shape[0] - pad), (pad, N - a.shape[1] - pad), (0, 0)))
b = a
for _ in range(3): b = scale2x(b)
r = np.array(Image.fromarray(b).rotate(deg, resample=Image.NEAREST))
# each 8x8 block: opaque if most of it is, coloured by its most common opaque colour (cleaner than one sample)
n = N
blk = r.reshape(n, 8, n, 8, 4).transpose(0, 2, 1, 3, 4).reshape(n, n, 64, 4)
o = np.zeros((n, n, 4), np.uint8)
for y in range(n):
    for x in range(n):
        px = blk[y, x]
        op = px[px[:, 3] > 0]
        if len(op) * 2 <= 64:
            continue
        cols, cnt = np.unique(op[:, :3], axis=0, return_counts=True)
        o[y, x, :3] = cols[cnt.argmax()]
        o[y, x, 3] = 255
# smooth the turned outline (3x3 majority on alpha; new edge texels take the nearest painted colour)
from scipy import ndimage
al = o[..., 3] > 0
for _ in range(2):
    al = ndimage.convolve(al.astype(int), np.ones((3, 3), int), mode='constant') >= 5
grow = int(sys.argv[5]) if len(sys.argv) > 5 else 0   # optional outline growth, px (nearest edge colour)
if grow:
    al = ndimage.binary_dilation(al, ndimage.generate_binary_structure(2, 1), iterations=grow)
_, (iy, ix) = ndimage.distance_transform_edt(~(o[..., 3] > 0), return_indices=True)
o = o[iy, ix].copy()
o[..., 3] = np.where(al, 255, 0)
o[~al] = 0
Image.fromarray(o).save(dst)
