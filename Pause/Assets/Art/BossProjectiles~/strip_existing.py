import sys, numpy as np
from PIL import Image
sys.path.insert(0, '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/bp')
import stripmod
R = '/Users/sina/Developer/Pause/.claude/worktrees/agent-a5b9f3f319d40b387/Pause/Assets/Art/Resources/Attacks/Verdant/verdant_attack_'
for n, wrap in (('laserbody', True), ('laser', False)):
    a = np.array(Image.open(R + n + '.png').convert('RGBA')).astype(np.float32)
    cw, ch = (128, 256) if n == 'laserbody' else (128, 128)
    o = stripmod.strip_sheet(a, cw, ch, wrap)
    if n == 'laser': o[256:] = a[256:]   # (Verdant's impact / spark row keeps its animation: stripping flattens its frames)
    Image.fromarray(np.clip(o + .5, 0, 255).astype(np.uint8), 'RGBA').save(R + n + '.png')
    print(n, 'stripped')
cols = set()
a = np.array(Image.open(R + 'laserbody.png').convert('RGBA'))
for f in range(4):
    c = np.where(a[:, f * 128:(f + 1) * 128, 3].max(0) > 0)[0]
    cols |= set(c.tolist())
print('verdant bodyOpaquePx', max(cols) - min(cols) + 1)
