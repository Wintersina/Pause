import sys
from PIL import Image
for n in sys.argv[1:]:
    im=Image.open(f'work_{n}/B_{n}.png').convert('RGBA'); 
    bg=Image.new('RGBA',im.size,(20,22,42,255)); bg.alpha_composite(im)
    bg.convert('RGB').resize((2048,128),Image.NEAREST).save(f'/private/tmp/claude-501/s/v_{n}.png')
