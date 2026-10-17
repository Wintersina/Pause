import sys
sys.path.insert(0, '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/dmg')
from common import *
from hints import HINTS
import os
TARGETS['space'] = ['eventide_bastion', 'orbit_reaver', 'rift_lancer', 'singularity_hauler']
SRC_FLIGHT = {}
os.makedirs(SCR + 'in', exist_ok=True)
COMMON = open(SCR + 'common_prompt.md').read()
for w, n, key in keys():
    if w == 'frost':
        path = ART + f'Enemies/Elite/Frost/{key}.png'   # un-outlined original
    else:
        path = respath(w, key)
    im, h, cs = strip(path)
    fl = defn(key)['cells']['flight'][0]
    c = cs[fl]
    big = c.resize((768, 768), Image.NEAREST)
    bg = Image.new('RGBA', (768, 768), (0, 255, 64, 255)); bg.alpha_composite(big)
    bg.convert('RGB').save(SCR + f'in/{key}.png')
    p = COMMON.replace('@HINT@', HINTS[key]).replace('@OUT@', f'out/{key}.png').replace('@IN@', f'in/{key}.png')
    open(SCR + f'in/{key}.md', 'w').write(p)
print('ok')
