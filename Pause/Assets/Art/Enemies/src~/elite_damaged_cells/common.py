import json, glob, os
import numpy as np
from PIL import Image
WT = '/Users/sina/Developer/Pause/.claude/worktrees/agent-ac13024fc36f9c0ec/'
ART = WT + 'Pause/Assets/Art/'
RES = ART + 'Resources/Elites/'
SCR = '/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/dmg/'
TARGETS = {
 'tide': ['bathyscaphe', 'pearl_warden', 'riptide', 'thunderfin', 'trawler'],
 'storm': ['anvil_cloud', 'refinery_tender', 'rod_dancer', 'skyhook_harrier', 'static_bastion'],
 'desert': ['caravan_hauler', 'dune_stalker', 'glass_cannon', 'lens_matriarch', 'mirror_bastion', 'scorpion_walker'],
 'frost': ['floe_harrower', 'glacier_tender', 'whiteout_sentinel'],
}
def keys():
    for w, ns in TARGETS.items():
        for n in ns:
            yield w, n, f'{w}_elite_{n}'
def defn(key):
    return json.load(open(RES + 'Defs/' + key + '.json'))
def strip(path):
    im = Image.open(path).convert('RGBA'); h = im.size[1]
    return im, h, [im.crop((i * h, 0, i * h + h, h)) for i in range(im.size[0] // h)]
def respath(w, key):
    return RES + w.capitalize() + '/' + key + '.png'
def mask(c, t=96):
    return np.asarray(c)[..., 3] >= t
