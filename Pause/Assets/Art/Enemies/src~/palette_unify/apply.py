import sys,glob,os,shutil,numpy as np
from PIL import Image
import remap
ROOT='/Users/sina/Developer/Pause/.claude/worktrees/enemy-palettes/Pause/Assets/Art/'
w=sys.argv[1]; W=w.capitalize()
files=sorted(glob.glob(ROOT+f'Resources/Enemies/{w}_*.png')+glob.glob(ROOT+f'Resources/Enemies/Death/{w}_*.png')
  +[p for p in glob.glob(ROOT+f'Enemies/Elite/{W}/{w}_elite_*.png') if 'concept' not in p]
  +[ROOT+f'Resources/Enemies/Mines/rail_mines_neon_{w}.png'])
store=ROOT+'Enemies/src~/palette_unify/original/'
for p in files:
    rel=os.path.relpath(p,ROOT);d=store+rel
    if not os.path.exists(d):
        os.makedirs(os.path.dirname(d),exist_ok=True);shutil.copy(p,d)
    remap.remap_file(d,p,w)
print(w,len(files),'files remapped')
