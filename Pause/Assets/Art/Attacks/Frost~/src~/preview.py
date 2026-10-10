"""Make the dark 2x sheet, game-size backdrop check, and a GIF of all loops."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parent.parent
ART = ROOT.parent.parent
N = Image.Resampling.NEAREST
INK = '#0b0b1a'
LASER = Image.open(ROOT/'frost_attack_laser.png').convert('RGBA')
BODY = Image.open(ROOT/'frost_attack_laserbody.png').convert('RGBA')
TELL = Image.open(ROOT/'frost_attack_lasertell.png').convert('RGBA')
FONT = ImageFont.load_default()


def label(im,xy,s): ImageDraw.Draw(im).text(xy,s,font=FONT,fill='#eadbed')


def cell(sheet,x,y=0,h=128): return sheet.crop((x*128,y*128,(x+1)*128,y*128+h))


def make_preview():
    out=Image.new('RGBA',(1520,920),INK)
    label(out,(16,11),'FROST LEVIATHAN  |  laser ultimate  |  native 64 px, exported 2x nearest')
    out.alpha_composite(LASER,(16,36))
    label(out,(16,425),'WINDUP / MUZZLE + FADE / IMPACT + SPARK')
    out.alpha_composite(BODY,(16,450))
    label(out,(16,714),'BEAM  |  four downward-rolling loop frames')
    out.alpha_composite(TELL,(16,740))
    label(out,(16,877),'AIM DOTS / LOCK BRACKETS')

    dark=Image.open(ART/'Backgrounds/Resources/Worlds/Frost/Backdrop3/v4/mid.png').convert('RGBA')
    bright=Image.open(ART/'Backgrounds/Resources/Worlds/Frost/Planetfall/frost_cloud_deck.png').convert('RGBA')
    tiles=[dark.crop((120,100,344,610)),bright.crop((1000,250,1224,760))]
    for j,bg in enumerate(tiles):
        px=1048+j*236
        out.alpha_composite(bg,(px,55))
        # Game-size: these are literally the atlas assets reduced to 64 px cells.
        beam=cell(BODY,0,0,256).resize((64,128),N)
        muzzle=cell(LASER,0,1).resize((64,64),N)
        hit=cell(LASER,2,2).resize((64,64),N)
        eye=cell(LASER,6,0).resize((64,64),N)
        x=px+80
        out.alpha_composite(eye,(x,65))
        out.alpha_composite(muzzle,(x,115))
        for y in (145,273,401):out.alpha_composite(beam,(x,y))
        out.alpha_composite(hit,(x,490))
        label(out,(px,580),('DARK TILE' if j==0 else 'BRIGHT TILE')+'  |  game size')
    out.convert('RGB').save(ROOT/'preview.png')


def make_gif():
    frames=[]
    for i in range(4):
        im=Image.new('RGBA',(590,330),INK)
        label(im,(8,8),f'FROST  |  all loop states  |  frame {i+1}/4')
        label(im,(10,32),'MUZZLE')
        label(im,(150,32),'SPARK')
        label(im,(290,32),'BEAM')
        label(im,(430,32),'SIGHT / LOCK')
        im.alpha_composite(cell(LASER,i,1),(10,62))
        im.alpha_composite(cell(LASER,i+4,2),(150,62))
        im.alpha_composite(cell(BODY,i,0,256),(290,62))
        im.alpha_composite(cell(TELL,i%2),(430,62))
        im.alpha_composite(cell(TELL,2+i%2),(430,194))
        frames.append(im.convert('RGB'))
    frames[0].save(ROOT/'preview.gif',save_all=True,append_images=frames[1:],duration=83,loop=0,disposal=2,optimize=False)


if __name__=='__main__':
    make_preview();make_gif()
