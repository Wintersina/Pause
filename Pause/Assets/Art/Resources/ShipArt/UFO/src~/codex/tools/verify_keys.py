import os
import numpy as np
from PIL import Image
C = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
ims = [np.array(Image.open(os.path.join(C, f"key_state{i}.png")).convert("RGB")).astype(int) for i in range(3)]
def hull(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    return ~((g > 100) & (g > r * 1.55) & (g > b * 1.4))
m = [hull(a) for a in ims]
area = m[0].sum()
for s in (1, 2):
    sil = (m[s] != m[0]).sum()
    ch = (np.abs(ims[s] - ims[0]).sum(-1) > 60) & m[0]
    print(f"state {s}: silhouette diff px {sil}, hull changed {100*ch.sum()/area:.1f}%")
    ys, xs = np.nonzero(ch)
    print("   changed bbox", xs.min(), ys.min(), xs.max(), ys.max())
# pink check: r and b high, g low
a = ims[2]
pink = (a[..., 0] > 180) & (a[..., 2] > 140) & (a[..., 1] < 110) & m[0]
print("pink-ish px", int(pink.sum()))
row = np.concatenate([np.where(m[i][..., None], ims[i], (14, 20, 36)) for i in range(3)], axis=1).astype(np.uint8)
Image.fromarray(row).resize((1536, 512), Image.NEAREST).save(os.path.join(C, "keys_dark.png"))
