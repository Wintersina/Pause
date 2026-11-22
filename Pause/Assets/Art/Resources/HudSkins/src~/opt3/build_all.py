import sys; sys.path.insert(0,'hud-final')
from PIL import Image
from recolor import recolor, refill, W as CFG, BASE
from panel import build_panel
OUT='hud-final/out/'
import os; os.makedirs(OUT,exist_ok=True)
base=Image.open(BASE).convert('RGBA')
PANEL_FILL={'Desert':(39,24,13,255),'Frost':(14,31,55,255),'Verdant':(14,36,21,255),'Ember':(35,21,15,255),'Storm':(17,27,44,255)}
boards={}
boards['Desert']=Image.open('hud-boards-opt3/hud_board_Desert.png').convert('RGBA')
WORM_BOTTOM, WORM_LEFT = 44, 29
for w in ['Frost','Verdant','Ember','Storm']:
    b=refill(recolor(base,CFG[w]),w)
    c=Image.open(f'hud-creatures/creature_{w}.png').convert('RGBA'); bb=c.getbbox(); c=c.crop(bb)
    b.alpha_composite(c,(WORM_LEFT, WORM_BOTTOM-c.height))
    boards[w]=b
for w,b in boards.items():
    b.save(OUT+f'hud_board_{w}.png')
    build_panel(b,PANEL_FILL[w]).save(OUT+f'dp_panel_{w}.png')
# preview
H='/Users/sina/Developer/Pause/Pause/Assets/Art/Resources/HudSkins/'
BG={'Desert':'desert-hud/ref/new_desert_night_grid.png'}
for w in ['Frost','Verdant','Ember','Storm']: BG[w]=f'hud-boards/ref/backdrop_{w}.png'
rows=[]
for w in ['Desert','Frost','Verdant','Ember','Storm']:
    bg=Image.open(BG[w]).convert('RGBA'); bg=bg.resize((1080,int(bg.height*1080/bg.width)))
    canvas=bg.crop((0,200,1080,200+520)).copy()
    old=Image.open(H+w+'/hud_board.png').convert('RGBA').resize((351,155),Image.NEAREST)
    new=Image.open(OUT+f'hud_board_{w}.png').resize((351,155),Image.NEAREST)
    canvas.alpha_composite(old,(20,20)); canvas.alpha_composite(new,(20,200))
    oldp=Image.open(H+w+'/dp_panel.png').convert('RGBA').resize((300,450),Image.NEAREST)
    newp=Image.open(OUT+f'dp_panel_{w}.png').resize((300,450),Image.NEAREST)
    canvas.alpha_composite(oldp,(420,40)); canvas.alpha_composite(newp,(750,40))
    rows.append(canvas)
sheet=Image.new('RGBA',(1080,520*len(rows)+10*len(rows)),(10,10,16,255))
for i,r in enumerate(rows): sheet.alpha_composite(r,(0,i*530))
sheet.convert('RGB').save(OUT+'preview.png')
