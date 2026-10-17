import sys, os
import numpy as np
from PIL import Image
# check_raw.py raw.png Key pose stage : delete the raw if it is a copy of an existing repo file
raw, key, pose, stage = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4]
W = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w/'
im = Image.open(raw).convert('RGBA').resize((384, 384), Image.LANCZOS)
a = np.asarray(im).astype(int)
bad = False
for n in range(1, 5):
    p = W + 'orig/attack/%s_%s_stage%d.png' % (key.lower(), pose, n)
    if os.path.exists(p):
        b = np.asarray(Image.open(p).convert('RGBA')).astype(int)
        d = np.abs(a - b)[:, :, :3].mean()
        print('diff vs old stage', n, round(d, 2))
        if d < 4.0:
            bad = True
if bad:
    print('COPY DETECTED -> deleting', raw)
    os.remove(raw)
    sys.exit(2)
print('raw ok')
