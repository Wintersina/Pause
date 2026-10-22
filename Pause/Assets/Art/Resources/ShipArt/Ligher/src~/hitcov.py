"""Hull hit polygon coverage of the stock silhouette (polygon area vs alpha area, after fitting the polygon bbox
to the alpha bbox of the rest cell)."""
import os, struct
import numpy as np
from PIL import Image, ImageDraw
S = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
d = open(os.path.join(S, 'Ligher_hitbox.bytes'), 'rb').read()
magic, margin, cnt = struct.unpack_from('<4sfi', d, 0)
o = 12
sid, n = struct.unpack_from('<ii', d, o); o += 8
hull = np.array(struct.unpack_from('<%df' % (2 * n), d, o)).reshape(n, 2); o += 8 * n
m, = struct.unpack_from('<i', d, o); o += 4
shield = np.array(struct.unpack_from('<%df' % (2 * m), d, o)).reshape(m, 2)
a = np.array(Image.open(os.path.join(S, 'Ligher.png')).convert('RGBA'))[:256, :256, 3] > 0
ys, xs = np.nonzero(a)
print('hull pts', n, 'shield pts', m, 'margin', margin, 'poly bbox', hull.min(0), hull.max(0))
for name, poly in (('hull', hull), ('shield', shield)):
    mn, mx = poly.min(0), poly.max(0)
    sx = (xs.max() - xs.min() + 1) / (mx[0] - mn[0]); sy = (ys.max() - ys.min() + 1) / (mx[1] - mn[1])
    px = (poly[:, 0] - mn[0]) * sx + xs.min(); py = (mx[1] - poly[:, 1]) * sy + ys.min()
    im = Image.new('L', (256, 256), 0); ImageDraw.Draw(im).polygon(list(zip(px, py)), fill=1)
    p = np.array(im) > 0
    print(name, 'polygon covers %.1f%% of silhouette; polygon area / silhouette %.2f' % ((p & a).sum() / a.sum() * 100, p.sum() / a.sum()))
