import sys
from PIL import Image
S='/private/tmp/claude-501/-Users-sina-Developer-Pause/eec2cb2b-f8a3-415f-a3f7-066e3d4d29ca/scratchpad/'
k=sys.argv[1];n=Image.open(f'out/{k}.png').convert('RGBA');cs=n.height
bg=Image.new('RGBA',n.size,(40,46,72,255));bg.alpha_composite(n)
sh=Image.new('RGB',(cs*7*1,cs+cs*3*2),(40,46,72));sh.paste(bg.convert('RGB'),(0,0))
big=bg.convert('RGB').crop((0,0,cs*7,cs)).resize((cs*7*3,cs*3),Image.NEAREST)
sh=Image.new('RGB',(cs*7*3//2,cs+cs*3//2*1+0),(40,46,72))
sh.paste(bg.convert('RGB'),(0,0)); sh.paste(big.resize((cs*7*3//2,cs*3//2),Image.NEAREST),(0,cs))
sh.save(S+f'{k}_review.png'); print(S+f'{k}_review.png')
