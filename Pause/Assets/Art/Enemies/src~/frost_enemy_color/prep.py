#!/usr/bin/env python3
"""prep.py <key>: cut the 7 original cells (src~/ship_scale_border~/original) into src/<key>_c<i>_in.png at 4x; hero = src/<key>_new.png if no _hero."""
import sys, shutil
from pathlib import Path
from PIL import Image
H=Path(__file__).resolve().parent; k=sys.argv[1]
im=Image.open(H/'../ship_scale_border~/original'/f'{k}.png').convert('RGBA'); cs=im.height
for i in range(im.width//cs):
    im.crop((i*cs,0,(i+1)*cs,cs)).resize((cs*4,cs*4),Image.NEAREST).save(H/'src'/f'{k}_c{i}_in.png')
(H/'src'/f'{k}_in.png').write_bytes((H/'src'/f'{k}_c0_in.png').read_bytes())
if not (H/'src'/f'{k}_hero.png').exists(): shutil.copy(H/'src'/f'{k}_new.png',H/'src'/f'{k}_hero.png')
