import sys, os
from PIL import Image
R = '/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/Resources/'
OUT = '/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/Bosses~/low-health-fx/src/'
os.makedirs(OUT, exist_ok=True)
C = 384
# prep_cx.py Key pose frame stage
k, pose, frame, stage = sys.argv[1], sys.argv[2], int(sys.argv[3]), int(sys.argv[4])
body = Image.open(R + 'Bosses/%s.png' % k).convert('RGBA')
cols = 5
x, y = (frame % cols) * C, (frame // cols) * C
clean = body.crop((x, y, x + C, y + C))
dm = Image.open(R + 'Bosses/%s_damage.png' % k).convert('RGBA')
hull = dm.crop((0, (stage - 1) * C, C, stage * C))
def up(im):
    bg = Image.new('RGBA', (1024, 1024), (0, 0, 0, 0))
    bg.alpha_composite(im.resize((1024, 1024), Image.NEAREST))
    return bg
def onbg(im):

    return im
onbg(up(clean)).save(OUT + '%s_%s_clean.png' % (k.lower(), pose))
onbg(up(hull)).save(OUT + '%s_wreck_stage%d.png' % (k.lower(), stage))
print(OUT)
