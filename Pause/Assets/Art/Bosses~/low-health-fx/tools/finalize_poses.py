import sys, os, json
import numpy as np
from PIL import Image
from register import register
R = '/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/'
SRC = R + 'Bosses~/low-health-fx/src/'
DST = R + 'Resources/BossImpact/attack/'
DST2 = R + 'Bosses~/impact-v1/attack_damage/'
W = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w/'
C = 384
POSES = {"tell0_0": 5, "tell0_1": 6, "tell1_0": 7, "tell1_1": 8, "tell2_0": 10, "tell2_1": 11, "fire": 9}
stage = int(sys.argv[1])
for k in sys.argv[2:]:
    body = Image.open(R + 'Resources/Bosses/%s.png' % k).convert('RGBA')
    for pose, f in POSES.items():
        raw = SRC + '%s_%s_stage%d_raw.png' % (k.lower(), pose, stage)
        if not os.path.exists(raw):
            continue
        clean = body.crop(((f % 5) * C, (f // 5) * C, (f % 5) * C + C, (f // 5) * C + C))
        cell, info = register(raw, clean)
        a = np.asarray(cell).copy()
        a[a[:, :, 3] < 10] = 0
        if k == 'Desert':
            # the Desert hull (clean poses and the wrecked idle hull) carries the thin pale readability outline
            from metrics import erode
            solid = a[:, :, 3] >= 128
            ring = solid & ~erode(solid, 1)
            a[:, :, 3] = np.where(solid, 255, 0)
            a[ring, :3] = (230, 220, 192)
        out = Image.fromarray(a, 'RGBA')
        assert out.size == (C, C)
        out.save(DST + '%s_%s_stage%d.png' % (k.lower(), pose, stage), optimize=True)
        out.save(DST2 + '%s_%s_stage%d.png' % (k.lower(), pose, stage), optimize=True)
        print(k, pose, stage, info)
