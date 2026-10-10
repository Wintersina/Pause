"""Build Pause's 60 native-grid achievement badges from in-game pixel sprites.

Run from anywhere: python3 Pause/Assets/Art/Achievements/src~/build_pixel_badges.py
Only generated achievement PNGs and the four frame PNGs are written.
"""
from __future__ import annotations

import math
from pathlib import Path
from PIL import Image, ImageDraw

ART = Path(__file__).resolve().parents[2]
SRC = Path(__file__).resolve().parent
OUT = ART / "Resources" / "Achievements"
BG = (7, 7, 15)
N = Image.Resampling.NEAREST

# id, tier, world. Source assets are selected in subject() below.
BADGES = [
 ("meta_first_flight","bronze","general"),("meta_logged_on","bronze","general"),
 ("world_frost_reached","bronze","frost"),("world_verdant_reached","silver","verdant"),
 ("world_ember_reached","silver","ember"),("world_tide_reached","silver","tide"),
 ("loop_1","gold","space"),("loop_2","gold","space"),("loop_5","platinum","space"),
 ("boss_space","silver","space"),("boss_frost","silver","frost"),
 ("boss_verdant","silver","verdant"),("boss_ember","gold","ember"),
 ("boss_tide","gold","tide"),("boss_no_hit","gold","general"),("boss_all","platinum","general"),
 ("elite_first","bronze","general"),("elite_10","silver","general"),
 ("elite_50","gold","general"),("elite_space_all","silver","space"),
 ("elite_frost_all","silver","frost"),("elite_verdant_all","silver","verdant"),
 ("elite_ember_all","gold","ember"),("elite_blink","silver","space"),
 ("kills_100","bronze","general"),("kills_1000","silver","general"),
 ("kills_5000","gold","general"),("rocks_500","bronze","general"),
 ("mines_25","bronze","general"),("chain_10","silver","space"),
 ("mega_domino","silver","space"),("codex_10","bronze","general"),
 ("codex_50","silver","general"),("codex_field_guide","silver","general"),
 ("codex_complete","platinum","general"),("ship_first","bronze","general"),
 ("ship_half","silver","general"),("ship_all","platinum","general"),
 ("skin_first","bronze","general"),("skin_special","silver","general"),
 ("skin_full_set","gold","general"),("secret_power_first","bronze","space"),
 ("stars_150","bronze","general"),("stars_1000","silver","general"),
 ("stars_5000","gold","general"),("dust_spent_10000","silver","general"),
 ("score_10k","bronze","general"),("score_50k","silver","general"),
 ("score_150k","platinum","general"),("speed_flash","silver","space"),
 ("speed_speedster","silver","space"),("speed_super_sonic","platinum","space"),
 ("deaths_10","bronze","general"),("deaths_100","silver","general"),
 ("pause_first","bronze","space"),("pause_blink_kill","bronze","space"),
 ("pause_blink_100","silver","space"),("pause_no_pause_world","silver","space"),
 ("pause_hoarder","silver","space"),("pause_perfect_dodge","gold","space"),
]

TIERS = {
 "bronze": ((65,31,24),(162,81,42),(231,151,75),(255,196,102)),
 "silver": ((43,59,68),(97,139,154),(171,211,219),(233,249,247)),
 "gold": ((74,48,22),(163,110,34),(230,177,58),(255,230,129)),
 "platinum": ((78,73,61),(164,158,125),(231,222,171),(255,252,221)),
}
ACCENTS = {
 "general": ((80,54,18),(213,139,43),(255,218,107)),
 "space": ((52,18,91),(195,35,167),(255,111,222)),
 "frost": ((19,67,85),(41,195,231),(204,252,255)),
 "verdant": ((36,74,16),(128,217,42),(230,255,113)),
 "ember": ((95,34,12),(246,93,28),(255,207,81)),
 "tide": ((13,65,69),(50,208,177),(190,255,226)),
}
INK=(5,10,20,255); STEEL=(66,90,105,255); LIGHT=(160,198,204,255)

def rgba(c): return tuple(c)+(255,)
def polygon(d, xy, fill, outline=INK):
 d.polygon(xy, fill=rgba(fill) if len(fill)==3 else fill)
 if outline: d.line(xy+[xy[0]], fill=outline, width=1, joint="curve")
def line(d, xy, c, width=1): d.line(xy,fill=rgba(c) if len(c)==3 else c,width=width,joint="curve")

def frame(tier):
 im=Image.new("RGBA",(128,128));d=ImageDraw.Draw(im)
 a,b,c,h=TIERS[tier]
 # Hard silhouette, four metal steps, and a dark instrument face.
 for box,col in [((8,8,119,119),INK),((10,10,117,117),rgba(a)),
                 ((13,13,114,114),rgba(b)),((16,16,111,111),INK),
                 ((19,19,108,108),(37,55,65,255)),((23,23,104,104),(10,20,31,255))]:
  d.ellipse(box,fill=col)
 d.arc((11,11,116,116),188,328,fill=rgba(h),width=2)
 d.arc((16,16,111,111),12,165,fill=rgba(c),width=2)
 d.arc((21,21,106,106),180,355,fill=(65,95,105,255),width=1)
 # Eight rail bolts with crisp specular pixels.
 for ang in range(0,360,45):
  x=round(64+49*math.cos(math.radians(ang)));y=round(64+49*math.sin(math.radians(ang)))
  d.rectangle((x-2,y-2,x+2,y+2),fill=INK)
  d.rectangle((x-1,y-1,x+1,y+1),fill=rgba(b))
  d.point((x-1,y-1),fill=rgba(h))
 # Copper side clamps, patterned after rail plates.
 for x,flip in [(12,1),(107,-1)]:
  d.rectangle((x,49,x+8,77),fill=INK)
  d.rectangle((x+1,51,x+7,75),fill=(87,49,29,255))
  d.rectangle((x+2,53,x+5,72),fill=(181,103,46,255))
  d.rectangle((x+2,55,x+3,67),fill=(229,156,76,255))
  for y in (56,70): d.point((x+4,y),fill=(22,30,38,255))
 # Small status tube is the tier signifier.
 d.rectangle((53,13,74,24),fill=INK)
 d.rectangle((56,15,71,22),fill=(58,72,78,255))
 d.rectangle((58,16,69,20),fill=rgba(c))
 d.rectangle((60,16,67,18),fill=rgba(h))
 d.point((60,16),fill=(255,255,255,255))
 # Several stepped metal tones make a readable plate at 48 px.
 for x,y,v in [(27,43,59),(31,35,72),(39,28,81),(87,28,69),(96,36,66),(101,47,54),
               (29,88,61),(35,97,69),(91,97,54),(99,85,53),(42,104,57),(85,103,59)]:
  d.point((x,y),fill=(v,v+17,v+22,255))
 # Hand-placed rail finish pixels: small warm and cool wear marks, each solid.
 for k in range(20):
  ang=math.radians(14+k*17)
  x=round(64+43*math.cos(ang));y=round(64+43*math.sin(ang))
  col=(min(255,b[0]+(k%5)*3),min(255,b[1]+(k%4)*4),min(255,b[2]+(k%6)*2),255)
  d.point((x,y),fill=col)
 return im

def source(path, cell, divisor, x=64,y=68, limit=96):
 """Copy sprite cell 0 at an exact integer reduction, preserving source pixels."""
 p=ART/path;im=Image.open(p).convert("RGBA").crop((0,0,cell,cell))
 im=im.resize((cell//divisor,cell//divisor),N)
 # Source effects can contain thousands of nearly identical alpha colours.
 # Resolve alpha to hard pixels, then use a small palette like the game's UI.
 alpha=im.getchannel("A").point(lambda a: 255 if a>=96 else 0)
 rgb=im.convert("RGB")
 if len(rgb.getcolors(1000000) or [])>limit:
  rgb=rgb.quantize(colors=limit,method=Image.Quantize.FASTOCTREE,dither=Image.Dither.NONE).convert("RGB")
 im=rgb.convert("RGBA");im.putalpha(alpha)
 box=im.getbbox()
 if box: im=im.crop(box)
 return im,(round(x-im.width/2),round(y-im.height/2))

def paste_asset(im,path,cell,divisor,x=64,y=68,limit=96):
 s,xy=source(path,cell,divisor,x,y,limit);im.alpha_composite(s,xy)

def glow(d,world):
 dark,mid,hi=ACCENTS[world]
 d.ellipse((30,31,97,98),outline=rgba(dark),width=5)
 d.arc((34,35,93,94),205,333,fill=rgba(mid),width=2)
 for x,y in [(36,56),(89,43),(98,78),(47,92)]:
  d.rectangle((x,y,x+2,y+2),fill=rgba(dark));d.point((x+1,y),fill=rgba(hi))

def star(d,x,y,col,small=False):
 r=3 if small else 6
 line(d,[(x-r,y),(x+r,y)],col,1);line(d,[(x,y-r),(x,y+r)],col,1)
 d.point((x,y),fill=(255,255,237,255))

def ring(d,x=64,y=67,r=27,col=(215,143,52),thick=4):
 d.ellipse((x-r-1,y-r-1,x+r+1,y+r+1),outline=INK,width=thick+2)
 d.ellipse((x-r,y-r,x+r,y+r),outline=rgba(col),width=thick)
 d.arc((x-r,y-r,x+r,y+r),200,315,fill=(255,236,158,255),width=1)

def chevron(d,y,metal=(217,168,80)):
 polygon(d,[(37,y),(64,y+16),(91,y),(91,y+9),(64,y+26),(37,y+9)],metal)
 line(d,[(43,y+3),(64,y+16),(85,y+3)],(255,226,141),2)

def book(d,variant):
 polygon(d,[(32,45),(59,51),(64,56),(69,51),(96,45),(96,88),(69,91),(64,96),(59,91),(32,88)],(114,79,38))
 polygon(d,[(36,49),(60,55),(63,60),(63,89),(59,86),(36,83)],(230,199,120))
 polygon(d,[(65,60),(69,55),(92,49),(92,83),(69,86),(65,89)],(215,180,102))
 line(d,[(64,57),(64,92)],(44,30,23),2)
 for yy in (64,70,76):
  line(d,[(41,yy),(56,yy+3)],(87,70,55));line(d,[(72,yy+3),(86,yy)],(87,70,55))
 if variant=="codex_10":
  ring(d,83,71,11,(83,210,225),2);line(d,[(90,79),(99,89)],(223,166,78),4)
 elif variant=="codex_50":
  star(d,77,67,(98,218,232));line(d,[(69,75),(87,75)],(43,102,128),2)
 elif variant=="codex_field_guide":
  d.rectangle((73,50,80,74),fill=(51,177,171,255));polygon(d,[(73,73),(76,69),(80,73)],(174,255,230))
 else:
  d.rectangle((57,46,70,55),fill=(97,58,24,255));star(d,64,49,(255,222,91),True)

def pause(d,x=64,y=67,size=19,col=(250,100,216)):
 w=max(5,size//3)
 for xx in (x-size//2-w//2,x+size//2-w//2):
  d.rectangle((xx-2,y-size-2,xx+w+2,y+size+2),fill=INK)
  d.rectangle((xx,y-size,xx+w,y+size),fill=rgba(col))
  d.rectangle((xx+1,y-size+1,xx+2,y+size-1),fill=(255,190,239,255))

def skull(d,c=(200,211,202)):
 polygon(d,[(43,47),(50,39),(78,39),(86,48),(83,73),(74,81),(74,90),(54,90),(54,81),(45,74)],c)
 d.rectangle((48,57,59,68),fill=INK);d.rectangle((69,57,80,68),fill=INK)
 d.rectangle((60,70,67,75),fill=(45,57,61,255))
 for x in (57,63,69): d.rectangle((x,82,x+3,89),fill=INK)
 line(d,[(46,49),(52,43),(76,43)],(255,244,212),2)

def subject(im,ident,world):
 d=ImageDraw.Draw(im);lo,mid,hi=ACCENTS[world]
 glow(d,world)
 R="Resources/"
 B="Backgrounds/Resources/Worlds/"
 # Direct game sprite subjects, always cell 0 and nearest-neighbour integer reduction.
 if ident.startswith("world_"):
  w=ident.split('_')[1]
  paste_asset(im,f"{B}{w.title()}/Planetfall/{w}_planet.png",1024,16,64,69,110)
  d=ImageDraw.Draw(im)
  if w=="frost": star(d,64,67,(214,253,255))
  elif w=="verdant": polygon(d,[(62,48),(75,56),(69,74),(55,79),(52,65)],(158,248,60))
  elif w=="ember": polygon(d,[(64,49),(73,68),(68,80),(56,79),(53,68)],(255,148,46))
  else: line(d,[(43,72),(52,66),(61,72),(70,66),(81,72)],(125,255,223),3)
 elif ident.startswith("boss_") and ident in ("boss_space","boss_frost","boss_verdant","boss_ember"):
  w=ident.split('_')[1];paste_asset(im,f"{R}Bosses/{w.title()}.png",384,6,64,68,112)
 elif ident=="boss_tide":
  # Tide has a planet but no boss sprite in this worktree.
  paste_asset(im,f"{B}Tide/Planetfall/tide_planet.png",1024,32,64,66,64)
  d=ImageDraw.Draw(im);ring(d,64,66,20,(48,211,190),3);d.ellipse((57,58,71,72),fill=(9,42,49,255),outline=rgba(hi),width=2)
 elif ident=="boss_all":
  for w,x,y in [("Space",44,48),("Frost",83,48),("Verdant",44,87),("Ember",83,87)]:
   paste_asset(im,f"{R}Bosses/{w}.png",384,12,x,y,60)
  d=ImageDraw.Draw(im);star(d,64,67,(255,227,132),True)
 elif ident=="boss_no_hit":
  paste_asset(im,f"{R}Pickups/Atoms/shield_idle_0.png",180,4,64,67,80)
  d=ImageDraw.Draw(im);polygon(d,[(64,79),(54,68),(55,63),(60,61),(64,65),(68,61),(73,63),(74,68)],(250,181,90))
 elif ident.startswith("elite_") and ident not in ("elite_first","elite_10","elite_50"):
  elite={"elite_space_all":("Space","space_elite_eventide_bastion"),
         "elite_frost_all":("Frost","frost_elite_floe_harrower"),
         "elite_verdant_all":("Verdant","verdant_elite_resin_warden"),
         "elite_ember_all":("Ember","ember_elite_kilnback"),
         "elite_blink":("Space","space_elite_rift_lancer")}[ident]
  paste_asset(im,f"{R}Elites/{elite[0]}/{elite[1]}.png",192,2,64,68,100)
  d=ImageDraw.Draw(im)
  if ident=="elite_blink": line(d,[(43,96),(59,71),(54,70),(80,40)],(250,65,217),4)
  else:
   for x in (39,64,89): star(d,x,94,hi,True)
 elif ident.startswith("ship_") or ident in ("skin_special","speed_super_sonic","deaths_10","pause_blink_100","pause_perfect_dodge"):
  ships={"ship_first":"GoldWarden","ship_half":"NeonComet","ship_all":"GoldWarden",
         "skin_special":"JadePhantom","speed_super_sonic":"NeonComet","deaths_10":"GoldWarden",
         "pause_blink_100":"Ninja","pause_perfect_dodge":"Ninja"}
  if ident in ("ship_half","ship_all"):
   for x,y,name in [(64,51,"GoldWarden"),(45,77,"NeonComet"),(83,77,"VoltViper")]:
    paste_asset(im,f"{R}ShipArt/Hulls/{name}.png",256,8,x,y,60)
  elif ident=="pause_blink_100":
   for x,y in [(44,84),(58,70),(75,53)]:paste_asset(im,f"{R}ShipArt/Hulls/Ninja.png",256,8,x,y,55)
  else:paste_asset(im,f"{R}ShipArt/Hulls/{ships[ident]}.png",256,4,64,67,110)
  d=ImageDraw.Draw(im)
  if ident=="ship_first": d.rectangle((39,97,89,103),fill=(149,100,48,255),outline=INK)
  elif ident=="ship_all": star(d,64,99,(255,236,151))
  elif ident=="skin_special": star(d,91,44,(255,237,152))
  elif ident=="speed_super_sonic":
   for r in (36,42):d.arc((64-r,66-r,64+r,66+r),-70,70,fill=rgba(hi),width=2)
  elif ident=="deaths_10":line(d,[(48,59),(79,82)],(244,221,168),5)
  elif ident=="pause_perfect_dodge":
   d.rectangle((88,58,99,63),fill=(251,185,57,255));line(d,[(83,55),(83,68)],hi,2)
 elif ident in ("rocks_500","mines_25"):
  if ident=="rocks_500":paste_asset(im,f"{R}Enemies/space_rock_crater.png",192,3,64,67,90)
  else:paste_asset(im,f"{R}Enemies/Mines/rail_mines_neon.png",310,5,64,67,90)
  d=ImageDraw.Draw(im)
  if ident=="rocks_500":line(d,[(43,90),(77,47)],(253,222,130),4)
  else:line(d,[(39,94),(90,43)],(252,224,135),4)
 elif ident.startswith("stars_") or ident=="dust_spent_10000":
  d=ImageDraw.Draw(im)
  polygon(d,[(44,59),(54,50),(74,50),(84,59),(80,89),(70,96),(53,96),(45,87)],(142,91,43))
  polygon(d,[(48,60),(54,55),(75,55),(80,62),(75,85),(52,85)],(222,151,63))
  line(d,[(51,64),(76,64)],(91,53,26),3)
  for x,y in [(64,46),(50,49),(78,49),(57,38),(73,38)][: 1 if ident=="stars_150" else 3 if ident=="stars_1000" else 5]:
   paste_asset(im,f"{R}Pickups/Atoms/dust_idle_0.png",180,9,x,y,16)
  d=ImageDraw.Draw(im)
  if ident=="stars_5000":ring(d,64,73,17,(255,227,99),2)
  if ident=="dust_spent_10000":d.ellipse((56,69,72,85),fill=(113,70,30,255),outline=(252,225,116,255),width=2)
 elif ident.startswith("pause_"):
  d=ImageDraw.Draw(im)
  if ident=="pause_hoarder":
   for y in (79,67,55):ring(d,64,y,16,(222,87,188),2)
   pause(d,64,64,9,hi)
  else:
   ring(d,64,67,28,mid,3);pause(d,64,67,17,hi)
   if ident=="pause_blink_kill":
    line(d,[(33,87),(55,73)],(255,99,216),3);star(d,88,49,hi)
   elif ident=="pause_no_pause_world":
    line(d,[(37,91),(91,39)],(255,206,118),5);d.ellipse((86,81,95,90),outline=rgba(hi),width=2)
 elif ident.startswith("codex_"):
  d=ImageDraw.Draw(im);book(d,ident)
 elif ident.startswith("kills_"):
  d=ImageDraw.Draw(im);skull(d)
  if ident=="kills_100":
   for x in (40,64,88): d.rectangle((x-3,91,x+3,101),fill=(186,112,47,255),outline=INK)
  elif ident=="kills_1000":
   ring(d,64,66,30,(173,197,204),2)
  else:
   polygon(d,[(45,43),(45,32),(55,37),(64,27),(73,37),(83,32),(83,43)],(241,190,65))
 elif ident.startswith("score_") or ident.startswith("elite_"):
  d=ImageDraw.Draw(im)
  n={"score_10k":1,"score_50k":2,"score_150k":3,"elite_first":1,"elite_10":2,"elite_50":3}[ident]
  for i in range(n):chevron(d,44+i*17,(190,123,55) if n==1 else (178,207,207) if n==2 else (244,193,76))
  if ident.startswith("elite_"):
   d.ellipse((57,39,71,53),fill=(11,21,31,255),outline=rgba(hi),width=2);star(d,64,46,hi,True)
  elif n>1:
   for x in (43,85):star(d,x,97,hi,True)
 elif ident.startswith("loop_"):
  d=ImageDraw.Draw(im);ring(d,64,67,30,mid,4)
  if ident=="loop_1":
   polygon(d,[(88,43),(96,52),(85,55)],hi);paste_asset(im,f"{B}Frost/Planetfall/frost_planet.png",1024,32,64,68,55)
  elif ident=="loop_2":
   ring(d,64,67,20,hi,3)
   polygon(d,[(87,43),(96,51),(85,55)],hi);polygon(d,[(40,91),(32,82),(44,79)],hi)
  else:
   for a in range(0,360,72):
    xx=64+int(20*math.cos(math.radians(a)));yy=67+int(20*math.sin(math.radians(a)))
    star(d,xx,yy,hi,True)
 elif ident in ("meta_first_flight","meta_logged_on","secret_power_first","chain_10","mega_domino",
               "skin_first","skin_full_set","speed_flash","speed_speedster","deaths_100"):
  d=ImageDraw.Draw(im)
  if ident=="meta_first_flight":
   ring(d,64,72,18,(73,153,237),3)
   polygon(d,[(61,65),(47,54),(34,50),(43,65),(56,70)],(231,172,74))
   polygon(d,[(67,65),(81,54),(94,50),(85,65),(72,70)],(231,172,74))
   star(d,64,72,(160,239,255),True)
  elif ident=="meta_logged_on":
   d.rectangle((39,48,61,82),fill=(153,98,43,255),outline=INK)
   d.rectangle((66,48,88,82),fill=(150,97,43,255),outline=INK)
   for y in (55,73):d.rectangle((59,y,72,y+4),fill=(238,190,93,255))
   d.rectangle((76,58,82,65),fill=(83,247,106,255))
  elif ident=="secret_power_first":
   ring(d,64,67,29,mid,5);line(d,[(59,37),(69,50),(60,62),(75,75)],hi,5)
  elif ident=="chain_10":
   for x,y in [(51,60),(77,74)]:
    d.ellipse((x-18,y-12,x+18,y+12),outline=INK,width=8)
    d.ellipse((x-18,y-12,x+18,y+12),outline=rgba(mid),width=5)
   line(d,[(57,67),(71,67)],hi,3)
  elif ident=="mega_domino":
   for x,y in [(39,62),(57,66),(76,72)]:
    polygon(d,[(x,y-16),(x+12,y-14),(x+9,y+17),(x-3,y+15)],(176,206,216))
    d.point((x+4,y),fill=INK)
   star(d,91,79,hi)
  elif ident=="skin_first":
   polygon(d,[(46,48),(82,48),(87,84),(42,84)],(181,114,53))
   d.rectangle((49,55,80,66),fill=(239,185,73,255))
   line(d,[(80,66),(80,92)],(92,246,222),4)
  elif ident=="skin_full_set":
   for i,c in enumerate([(89,215,223),(117,225,77),(242,112,45),(214,62,184),(250,215,91)]):
    x=37+i*11;y=48+i*3;d.rectangle((x,y,x+25,y+39),fill=rgba(c),outline=INK,width=2)
  elif ident=="speed_flash":
   ring(d,64,67,29,mid,3);polygon(d,[(70,33),(49,67),(62,67),(54,99),(82,58),(69,59)],(255,227,96))
  elif ident=="speed_speedster":
   ring(d,64,68,30,mid,3);line(d,[(64,69),(88,45)],(255,92,182),4)
   d.ellipse((59,63,69,73),fill=(235,235,231,255),outline=INK)
   for a in (200,240,280,320):
    x=64+int(25*math.cos(math.radians(a)));y=68+int(25*math.sin(math.radians(a)))
    d.point((x,y),fill=rgba(hi))
  elif ident=="deaths_100":
   polygon(d,[(64,39),(79,50),(75,67),(89,62),(74,87),(61,97),(52,78),(43,61),(55,67)],(214,123,63))
   line(d,[(64,89),(65,48)],(255,213,107),3)
 else:
  # Deliberately fail during build if a new badge has no subject.
  raise ValueError(f"No subject for {ident}")

def build():
 assert len(BADGES)==60 and len({b[0] for b in BADGES})==60
 OUT.mkdir(parents=True,exist_ok=True);(SRC/"frames").mkdir(exist_ok=True)
 frames={tier:frame(tier) for tier in TIERS}
 for tier,im in frames.items():im.save(SRC/"frames"/f"{tier}_frame_128.png")
 cells=[]
 for ident,tier,world in BADGES:
  im=frames[tier].copy();subject(im,ident,world)
  # Preserve the round hard alpha edge even for large imported effects.
  mask=frames[tier].getchannel("A")
  a=im.getchannel("A");im.putalpha(Image.composite(a,Image.new("L",(128,128)),mask))
  im.save(OUT/f"{ident}.png")
  master=Image.new("RGB",(128,128),BG);master.paste(im,(0,0),im)
  master.resize((1024,1024),N).save(SRC/f"{ident}_1024.png")
  cells.append((ident,im))
 for size,filename,cols in [(128,"sheet_128.png",10),(48,"sheet_48.png",10),(1024,"sheet_1024.png",10)]:
  # The large sheet is intentionally a 10-column index, suitable for zooming.
  gap=8 if size<1024 else 20
  row_height=size+gap+(18 if size==128 else 0)
  sheet=Image.new("RGB",((size+gap)*cols+gap,row_height*6+gap),(12,16,24))
  draw=ImageDraw.Draw(sheet)
  for i,(ident,im) in enumerate(cells):
   tile=Image.new("RGB",(128,128),BG);tile.paste(im,(0,0),im)
   tile=tile.resize((size,size),N if size!=48 else Image.Resampling.BOX)
   x=gap+(i%cols)*(size+gap);y=gap+(i//cols)*row_height
   sheet.paste(tile,(x,y))
   if size==128:draw.text((x+2,y+129),ident[:20],fill=(225,231,230))
  sheet.save(SRC/filename)
 print(f"built {len(cells)} badges and four frames")

if __name__=="__main__":build()
