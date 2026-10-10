"""Produce the review still and a montage GIF of every sustained loop."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT=Path(__file__).resolve().parent.parent
ART=ROOT.parent.parent
LASER=Image.open(ROOT/'ember_attack_laser.png').convert('RGBA')
BODY=Image.open(ROOT/'ember_attack_laserbody.png').convert('RGBA')
TELL=Image.open(ROOT/'ember_attack_lasertell.png').convert('RGBA')
BG=Path(__file__).resolve().parents[3]/'Backgrounds/Resources/Worlds/Ember'

def crop(im,x,y,w=128,h=128):return im.crop((x*w,y*h,(x+1)*w,(y+1)*h))
def title(im,xy,s):ImageDraw.Draw(im).text(xy,s,fill=(242,221,236),font=ImageFont.load_default())

canvas=Image.new('RGB',(1080,1050),(11,11,26))
title(canvas,(16,10),'EMBER BOSS FIRE LASER   |   2x native pixel sheets on #0b0b1a')
canvas.paste(LASER,(16,30),LASER)
title(canvas,(16,425),'BEAM LOOP  |  128 x 256 frame, 12 fps')
canvas.paste(BODY,(16,444),BODY)
title(canvas,(548,425),'SIGHT a-b  |  LOCK a-b')
canvas.paste(TELL,(548,444),TELL)
title(canvas,(16,725),'GAME SIZE  |  40 px beam / 60 px flare over Ember backdrop tiles')
sky=Image.open(BG/'Backdrop3/v4/sky.png').convert('RGB').crop((120,230,376,486))
bright=Image.open(BG/'Planetfall/ember_cloud_deck.png').convert('RGB').crop((800,350,1056,606))
for x,back,label in [(16,sky,'dark tile'),(300,bright,'bright tile')]:
    canvas.paste(back,(x,750))
    beam=crop(BODY,0,0,128,256).resize((40,170),Image.Resampling.NEAREST)
    flare=crop(LASER,0,1).resize((60,60),Image.Resampling.NEAREST)
    impact=crop(LASER,2,2).resize((60,60),Image.Resampling.NEAREST)
    canvas.paste(beam,(x+108,805),beam)
    canvas.paste(flare,(x+98,778),flare)
    canvas.paste(impact,(x+98,927),impact)
    title(canvas,(x,1011),label)
canvas.save(ROOT/'preview.png')

frames=[]
for i in range(4):
    f=Image.new('RGB',(640,280),(11,11,26))
    for xx,label in [(0,'MUZZLE'),(128,'SPARK'),(256,'BEAM'),(384,'SIGHT'),(512,'LOCK')]:title(f,(xx+5,5),label)
    m=crop(LASER,i,1);s=crop(LASER,i+4,2)
    b=crop(BODY,i,0,128,256)
    sight=crop(TELL,i%2,0)
    lock=crop(TELL,2+i%2,0)
    for im,xy in [(m,(0,72)),(s,(128,72)),(b,(256,22)),(sight,(384,72)),(lock,(512,72))]:f.paste(im,xy,im)
    frames.append(f)
frames[0].save(ROOT/'preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,optimize=False)
print('Wrote preview.png and preview.gif')
