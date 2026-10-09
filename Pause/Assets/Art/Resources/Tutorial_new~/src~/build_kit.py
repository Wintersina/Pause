"""Rebuild the staged tutorial kit from a generated concept and native pixel drawings.

Run from anywhere with: python3 path/to/build_kit.py
Only writes beside this script, under Tutorial_new~.
"""

from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

SRC = Path(__file__).resolve().parent
OUT = SRC.parent
OLD = OUT.parent / 'Tutorial'
NATIVE = SRC / 'native'
NATIVE.mkdir(exist_ok=True)

# Shared 19-ink palette. The selected generated image supplies the head's
# clustered steel texture; all structural edges and expression frames are drawn
# deliberately on the 64/13/18 pixel grids below.
P = {
    'ink': '#05060c', 'black': '#090d15', 'panel': '#0c1725',
    'shadow': '#16212d', 'steel0': '#202b36', 'steel1': '#34414e',
    'steel2': '#50616d', 'steel3': '#84919a', 'steel4': '#bec5c6',
    'cyan0': '#07516b', 'cyan': '#0bd0f6', 'ice': '#7af6fc',
    'mag0': '#76135a', 'mag': '#ff2e88', 'amber': '#ffb83d',
    'brass0': '#645036', 'brass': '#aa8253', 'red0': '#86121f',
    'red': '#d8232c', 'white': '#ffffff',
}

def rgb(name):
    h = P[name].lstrip('#')
    return tuple(int(h[i:i+2], 16) for i in (0, 2, 4))

def rgba(name, a=255):
    return rgb(name) + (a,)

def canvas(size):
    return Image.new('RGBA', size, (0, 0, 0, 0))

def rect(d, box, color):
    d.rectangle(box, fill=rgba(color))

def line(d, points, color, width=1):
    d.line(points, fill=rgba(color), width=width)

def poly(d, points, color):
    d.polygon(points, fill=rgba(color))

def save(name, im, scale=4):
    im.save(NATIVE / name)
    im.resize((im.width*scale, im.height*scale), Image.Resampling.NEAREST).save(OUT / name)

def concept_head():
    """Use candidate A's actual painted steel clusters as the armor underpainting.

    The old asset contributes only a binary alpha silhouette, not pixels or colors.
    Rebuilding the visor, frame, antenna and surface markings below keeps the
    resulting design at a true 64x64 game grid.
    """
    source = Image.open(SRC / 'candidates/head_a.png').convert('RGB')
    # Crop generated design to its head silhouette and fit the game's footprint.
    crop = source.crop((32, 41, 1221, 1103)).resize((60, 53), Image.Resampling.BOX)
    old_alpha = Image.open(OLD / 'tut_robot.png').convert('RGBA').getchannel('A')
    old_alpha = old_alpha.resize((64, 64), Image.Resampling.BOX)
    im = canvas((64, 64))
    allowed = ['black','panel','shadow','steel0','steel1','steel2','steel3','steel4',
               'cyan0','cyan','ice','mag0','mag','amber','brass0','brass','red0','red']
    pal = [(k, rgb(k)) for k in allowed]
    for y in range(64):
        for x in range(64):
            if old_alpha.getpixel((x,y)) < 34:
                continue
            px = crop.getpixel((max(0,min(59,x-2)), max(0,min(52,y-2))))
            # Generated background is intentionally subdued; shell silhouette
            # is structural and receives the same clustered material palette.
            key = min(pal, key=lambda kv: sum((px[i]-kv[1][i])**2*(1.0,1.15,1.0)[i] for i in range(3)))[0]
            im.putpixel((x,y), rgba(key))
    d = ImageDraw.Draw(im)
    # Side sockets and gunmetal hull, preserving the existing outer alpha.
    poly(d, [(4,27),(11,25),(14,29),(14,43),(10,46),(4,43)], 'ink')
    poly(d, [(5,29),(11,27),(13,30),(13,42),(10,44),(5,42)], 'steel1')
    poly(d, [(5,31),(9,30),(11,32),(11,38),(6,39)], 'black')
    rect(d,(7,33,9,35),'mag0'); rect(d,(7,33,8,33),'mag')
    poly(d, [(50,28),(58,26),(61,29),(61,42),(57,45),(50,43)], 'ink')
    poly(d, [(51,29),(58,28),(60,30),(60,41),(57,43),(51,42)], 'steel1')
    rect(d,(55,32,59,39),'black'); rect(d,(56,33,58,34),'amber')
    for x in (6,57):
        for y in (40,42): rect(d,(x,y,x+3,y),'steel2')
    # Top housings, cyan cable and old brass pipe.
    poly(d,[(12,17),(17,11),(23,9),(28,12),(34,12),(39,11),(46,13),(52,18)],'steel0')
    line(d,[(13,17),(18,12),(25,10),(38,10),(43,12)],'cyan0',2)
    line(d,[(15,15),(20,11),(25,10),(37,10)],'cyan')
    rect(d,(24,9,26,13),'steel2'); rect(d,(25,10,26,11),'steel4')
    line(d,[(29,12),(35,12),(38,14),(43,14)],'brass0',3)
    line(d,[(29,11),(35,11),(39,13)],'brass')
    rect(d,(46,14,51,18),'black'); rect(d,(47,15,49,16),'amber')
    rect(d,(46,19,51,20),'steel2')
    # Antenna: small square warning beacon, same original spot.
    rect(d,(37,2,41,10),'ink'); rect(d,(38,3,40,8),'steel1')
    rect(d,(38,3,39,5),'red0'); rect(d,(39,3,39,4),'red')
    rect(d,(40,4,40,5),'amber'); rect(d,(38,9,42,10),'brass0')
    # Faceted, tight frame. The face must stay blank where overlays land.
    poly(d,[(16,19),(47,19),(52,23),(53,40),(49,46),(15,46),(11,40),(12,25)],'ink')
    poly(d,[(16,20),(47,20),(51,24),(52,39),(48,45),(16,45),(12,39),(13,25)],'steel2')
    poly(d,[(17,21),(46,21),(50,25),(51,39),(47,44),(17,44),(13,39),(14,26)],'steel0')
    poly(d,[(18,22),(46,22),(49,25),(50,38),(47,43),(18,43),(14,38),(15,26)],'panel')
    # Sparse scanlines, avoiding all three anchor sample pixels.
    for y in (23,25,27,30,34,36,42):
        line(d,[(19,y),(45,y)],'shadow')
    # Restore the anchors to exactly the panel colour, including the midpoint.
    for x,y in ((23,28),(40,28),(32,39),(32,32)):
        rect(d,(x,y,x,y),'panel')
    # Neon seam, slight 1-pixel glitch offsets, and shadow-side rim.
    line(d,[(15,23),(13,27),(13,37),(17,44)],'cyan0')
    line(d,[(17,44),(46,44),(50,40)],'cyan0')
    line(d,[(17,20),(44,20)],'cyan',1)
    rect(d,(17,21,20,21),'ice'); rect(d,(44,21,47,21),'mag')
    rect(d,(15,29,16,29),'mag'); rect(d,(47,37,49,37),'mag0')
    rect(d,(47,38,48,38),'mag')
    # Bolt heads and small panel labels; no round toy features.
    for x,y in ((16,20),(47,20),(16,44),(47,44)):
        rect(d,(x,y,x+1,y+1),'ink'); rect(d,(x,y,x,y),'steel4')
    for x in (7,53):
        rect(d,(x,28,x+3,28),'steel3')
        rect(d,(x,29,x+1,29),'brass')
    for x in (20,22,24): rect(d,(x,47,x,47),'amber')
    poly(d,[(15,46),(49,46),(55,48),(52,54),(13,54),(10,49)],'ink')
    poly(d,[(16,47),(48,47),(53,49),(51,53),(14,53),(12,49)],'steel0')
    line(d,[(16,48),(29,48)],'steel2')
    line(d,[(36,48),(47,48)],'steel2')
    for x in (25,28,31,34,37): rect(d,(x,49,x+1,52),'black')
    rect(d,(43,50,47,51),'brass0'); rect(d,(43,50,45,50),'brass')
    for x in (43,46,49):
        line(d,[(x,48),(x+2,50)],'amber')
    rect(d,(17,51,20,51),'steel3'); rect(d,(17,52,18,52),'steel2')
    rect(d,(13,50,15,50),'mag'); rect(d,(51,49,52,50),'cyan')
    # Hard silhouette ink, except the generated material's lit outer edge.
    return im

def eye(state):
    im=canvas((13,10)); d=ImageDraw.Draw(im)
    if state=='open':
        poly(d,[(0,3),(2,2),(8,3),(12,5),(11,7),(7,6),(2,5),(0,5)],'ink')
        poly(d,[(1,3),(7,3),(11,5),(8,5),(3,4),(1,4)],'cyan')
        line(d,[(2,3),(6,3)],'ice')
        rect(d,(9,6,10,6),'mag0')
        rect(d,(3,0,5,0),'cyan0')
        rect(d,(9,8,10,8),'mag0')
    elif state=='half':
        poly(d,[(0,4),(3,4),(9,5),(12,6),(11,7),(7,6),(2,5)],'ink')
        line(d,[(2,4),(7,5),(10,6)],'cyan')
        rect(d,(2,4,4,4),'ice')
        rect(d,(9,8,10,8),'mag0')
    elif state=='happy':
        # Softened upper chevron, a small sympathetic lift without a smile arc.
        poly(d,[(0,5),(3,3),(7,4),(12,6),(11,7),(7,5),(3,4),(1,6)],'ink')
        line(d,[(2,5),(4,4),(8,5),(10,6)],'cyan')
        rect(d,(4,4,5,4),'ice')
        rect(d,(4,1,5,1),'cyan0')
        rect(d,(9,8,10,8),'mag0')
    else:
        line(d,[(1,6),(10,7)],'ink',2)
        line(d,[(2,6),(9,7)],'cyan0')
        rect(d,(10,7,11,7),'mag0')
    return im

def mouth(state):
    im=canvas((18,10)); d=ImageDraw.Draw(im)
    # Each pose is an LED equalizer pattern. No continuous lower lip or arc.
    patterns={
      'rest': [0,1,1,0,1,1,0],
      'e':    [1,1,2,2,2,1,1],
      'a':    [1,2,4,6,4,2],
      'o':    [0,0,4,6,4,1,0],
      'big':  [2,3,5,6,5,3,2],
    }
    width={'rest':1,'e':2,'a':1,'o':1,'big':2}[state]
    heights=patterns[state]
    gap=1
    total=len(heights)*width+(len(heights)-1)*gap
    left=(18-total)//2
    for i,h in enumerate(heights):
        if h==0: continue
        x=left+i*(width+gap); top=max(1, 5-h//2)
        rect(d,(x-1,top-1,x+width,top+h),'ink')
        rect(d,(x,top,x+width-1,top+h-1),'mag' if i in (2,4) and state!='rest' else 'cyan')
        if h>=3: rect(d,(x,top,x+width-1,top),'ice')
        if h>=5: rect(d,(x,top+h-1,x+width-1,top+h-1),'mag0')
    if state=='rest': rect(d,(14,5,14,5),'cyan0')
    return im

def bubble():
    im=canvas((48,48));d=ImageDraw.Draw(im)
    # Frame is 11 native pixels (44 exported); edge middles are constant for slicing.
    poly(d,[(4,2),(43,2),(46,5),(46,43),(43,46),(4,46),(2,44),(2,5)],'ink')
    poly(d,[(5,3),(42,3),(45,6),(45,42),(42,45),(5,45),(3,43),(3,6)],'steel1')
    poly(d,[(6,4),(41,4),(44,7),(44,41),(41,44),(6,44),(4,42),(4,7)],'panel')
    # Edge lines cross the sliced strips without changing thickness.
    line(d,[(8,4),(40,4)],'cyan0')
    line(d,[(8,5),(40,5)],'cyan')
    line(d,[(4,8),(4,40)],'cyan0')
    line(d,[(5,8),(5,40)],'cyan')
    line(d,[(42,8),(42,40)],'steel2')
    line(d,[(8,43),(40,43)],'steel2')
    # Bracket plates, corner bolts, tiny magenta signal marks.
    for x,y in ((4,4),(40,4),(4,40),(40,40)):
        rect(d,(x,y,x+3,y+3),'brass0')
        rect(d,(x+1,y+1,x+2,y+2),'ink')
        rect(d,(x+1,y+1,x+1,y+1),'steel3')
    for x,y in ((8,7),(38,7),(8,40),(38,40)):
        rect(d,(x,y,x+1,y),'mag0')
    # Scanlines live only inside the fixed corner regions so the nine-slice
    # center retains the exact solid TutorialPalette.Panel colour.
    for y in (9,11,36,38):
        line(d,[(10,y),(13,y)],'shadow')
        line(d,[(34,y),(38,y)],'shadow')
    return im

def button_card(card=False):
    size=(24,24) if card else (24,20)
    im=canvas(size);d=ImageDraw.Draw(im)
    end=23 if card else 19
    poly(d,[(2,0),(21,0),(23,2),(23,end-2),(21,end),(2,end),(0,end-2),(0,2)],'ink')
    poly(d,[(2,1),(21,1),(22,2),(22,end-2),(21,end-1),(2,end-1),(1,end-2),(1,2)],'steel1')
    rect(d,(3,3,20,end-3),'panel')
    line(d,[(3,2),(20,2)],'cyan')
    line(d,[(2,4),(2,end-4)],'cyan0')
    line(d,[(20,end-2),(21,end-3)],'mag')
    for x,y in ((2,2),(20,2),(2,end-2),(20,end-2)):
        rect(d,(x,y,x,y),'brass')
    if card:
        rect(d,(5,6,7,6),'steel2')
        rect(d,(16,6,19,6),'steel2')
        rect(d,(5,18,9,18),'mag0')
    else:
        rect(d,(6,14,16,14),'shadow')
    return im

def arrow():
    im=canvas((24,24));d=ImageDraw.Draw(im)
    # Two upward HUD chevrons, same original bounding region.
    for y in (2,11):
        line(d,[(2,y+7),(11,y),(21,y+7)],'ink',3)
        line(d,[(2,y+6),(11,y+1),(20,y+6)],'cyan',1)
        rect(d,(10,y,12,y),'ice')
        rect(d,(18,y+6,20,y+6),'mag')
    return im

def ring():
    im=canvas((32,32));d=ImageDraw.Draw(im)
    outer=[(10,0),(21,0),(31,10),(31,21),(21,31),(10,31),(0,21),(0,10)]
    inner=[(10,2),(21,2),(29,10),(29,21),(21,29),(10,29),(2,21),(2,10)]
    poly(d,outer,'ink'); poly(d,inner,'white')
    poly(d,[(11,3),(20,3),(28,11),(28,20),(20,28),(11,28),(3,20),(3,11)],'black')
    line(d,[(11,1),(20,1)],'steel4')
    line(d,[(1,11),(1,20)],'steel2')
    return im

def glow():
    im=canvas((16,16));d=ImageDraw.Draw(im)
    for box,key,alpha in [((0,0,15,15),'white',32),((2,2,13,13),'white',64),
                          ((4,4,11,11),'white',96),((6,6,9,9),'white',170)]:
        d.ellipse(box,fill=rgba(key,alpha))
    rect(d,(7,7,8,8),'white')
    return im

def lamp():
    im=canvas((6,6));d=ImageDraw.Draw(im)
    poly(d,[(2,0),(4,0),(5,2),(5,4),(4,5),(1,5),(0,4),(0,2)],'ink')
    rect(d,(1,1,4,4),'red0'); rect(d,(2,1,3,3),'red')
    rect(d,(2,1,2,1),'amber'); rect(d,(4,4,4,4),'steel2')
    return im

def jet(state):
    im=canvas((15,13));d=ImageDraw.Draw(im)
    if state=='a':
        poly(d,[(4,0),(10,0),(12,3),(10,8),(7,12),(4,8),(2,3)],'mag0')
        poly(d,[(5,1),(9,1),(10,4),(8,10),(6,10),(4,4)],'mag')
        poly(d,[(6,2),(8,2),(9,4),(7,8),(5,4)],'cyan')
        line(d,[(6,2),(8,2)],'ice')
    else:
        poly(d,[(3,0),(11,0),(13,3),(9,8),(7,10),(5,8),(1,3)],'mag0')
        poly(d,[(4,1),(10,1),(11,3),(8,7),(6,7),(3,3)],'cyan')
        rect(d,(6,2,8,4),'ice')
        rect(d,(3,5,4,6),'mag')
    line(d,[(3,0),(11,0)],'steel1')
    return im

def tail():
    im=canvas((13,10));d=ImageDraw.Draw(im)
    poly(d,[(0,4),(11,1),(12,2),(12,8),(11,9)],'ink')
    poly(d,[(1,4),(11,2),(11,7),(2,5)],'steel1')
    line(d,[(3,4),(9,3)],'cyan')
    rect(d,(9,3,10,3),'mag')
    rect(d,(10,6,11,7),'brass0')
    return im

def preview():
    W,H=1600,1140
    bg=Image.new('RGB',(W,H),'#0b0b1a')
    d=ImageDraw.Draw(bg)
    try:
        font=ImageFont.truetype('/System/Library/Fonts/Menlo.ttc',22)
        small=ImageFont.truetype('/System/Library/Fonts/Menlo.ttc',16)
    except OSError:
        font=ImageFont.load_default();small=font
    d.text((50,35),'PAUSE  /  TUTORIAL ROBOT  /  CYBERPUNK PIXEL KIT',font=font,fill='#7af6fc')
    d.text((50,72),'Native-grid sprite staging  |  4x nearest-neighbour  |  October 2026',font=small,fill='#84919a')
    def paste(name,x,y,factor=1,tint=None):
        im=Image.open(OUT/name).convert('RGBA')
        if tint:
            tr,tg,tb=tint
            im.putdata([(r*tr//255,g*tg//255,b*tb//255,a) for r,g,b,a in im.get_flattened_data()])
        if factor!=1: im=im.resize((im.width*factor,im.height*factor),Image.Resampling.NEAREST)
        bg.paste(im,(x,y),im)
        return im.size
    def composed(x,y,eye_name='open',mouth_name='rest',factor=2):
        paste('tut_jet_a.png',x+98*factor,y+210*factor,factor)
        paste('tut_robot.png',x,y,factor)
        paste('tut_eye_'+eye_name+'.png',x+68*factor,y+94*factor,factor)
        e=Image.open(OUT/('tut_eye_'+eye_name+'.png')).convert('RGBA').transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        e=e.resize((52*factor,40*factor),Image.Resampling.NEAREST)
        bg.paste(e,(x+136*factor,y+94*factor),e)
        paste('tut_mouth_'+mouth_name+'.png',x+92*factor,y+138*factor,factor)
        paste('tut_lamp.png',x+142*factor,y+6*factor,factor)
    composed(45,135,'open','rest',2)
    d.text((60,675),'FULL COMPOSITION  /  2x GAME SPRITE',font=small,fill='#bec5c6')
    # Bubble sample, kept at export scale so the 44 px edge is visible.
    d.text((610,150),'DIALOGUE HUD',font=font,fill='#7af6fc')
    paste('tut_tail.png',583,271,2)
    paste('tut_bubble.png',675,195,2)
    d.text((755,330),'STAY SHARP, PILOT.',font=font,fill='#bec5c6')
    d.text((755,371),'Watch the rail. Move on my mark.',font=small,fill='#bec5c6')
    d.text((610,630),'LIP-SYNC / EYE POSES',font=font,fill='#7af6fc')
    names=['rest','e','a','o','big']
    for i,n in enumerate(names):
        x=610+i*185
        paste('tut_mouth_'+n+'.png',x,700,2)
        d.text((x+24,795),n.upper(),font=small,fill='#84919a')
    for i,n in enumerate(['open','half','happy','shut']):
        x=610+i*230
        paste('tut_eye_'+n+'.png',x,840,2)
        d.text((x+20,930),n.upper(),font=small,fill='#84919a')
    d.text((60,740),'COMPONENTS',font=font,fill='#7af6fc')
    for i,n in enumerate(['tut_button','tut_card','tut_arrow','tut_ring','tut_jet_a','tut_jet_b','tut_glow']):
        x=45+(i%4)*133;y=785+(i//4)*150
        tint=(255,184,61) if n=='tut_ring' else (216,35,44) if n=='tut_glow' else None
        paste(n+'.png',x,y,1,tint)
        d.text((x,y+104),n[4:],font=small,fill='#84919a')
    bg.save(OUT/'preview.png')

def main():
    save('tut_robot.png',concept_head())
    for n in ('open','half','happy','shut'): save('tut_eye_'+n+'.png',eye(n))
    for n in ('rest','a','e','o','big'): save('tut_mouth_'+n+'.png',mouth(n))
    save('tut_bubble.png',bubble())
    save('tut_button.png',button_card())
    save('tut_card.png',button_card(True))
    save('tut_arrow.png',arrow())
    save('tut_ring.png',ring())
    save('tut_glow.png',glow())
    save('tut_lamp.png',lamp())
    save('tut_jet_a.png',jet('a'))
    save('tut_jet_b.png',jet('b'))
    save('tut_tail.png',tail())
    preview()

if __name__=='__main__': main()
