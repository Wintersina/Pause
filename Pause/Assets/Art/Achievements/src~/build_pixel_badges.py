"""Build 60 native 128 px gear-lens badges from drawn hardware and game sprites."""
from __future__ import annotations

import math
import io
import subprocess
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
NEON={"bronze":((105,56,18),(255,159,50),(255,228,121)),
      "silver":((13,73,92),(47,220,241),(207,255,255)),
      "gold":((104,63,13),(255,193,54),(255,244,142)),
      "platinum":((91,34,83),(255,86,218),(255,241,255))}
GROUPS={"world":"world","loop":"world","boss":"boss","elite":"elite",
        "kills":"enemy","rocks":"enemy","mines":"enemy","chain":"enemy",
        "mega":"enemy","deaths":"enemy","ship":"collection","skin":"collection",
        "codex":"codex","stars":"dust","dust":"dust","score":"skill",
        "speed":"skill","secret":"skill","pause":"pause","meta":"meta"}

def rgba(c): return tuple(c)+(255,)
def polygon(d, xy, fill, outline=INK):
 d.polygon(xy, fill=rgba(fill) if len(fill)==3 else fill)
 if outline: d.line(xy+[xy[0]], fill=outline, width=1, joint="curve")
def line(d, xy, c, width=1): d.line(xy,fill=rgba(c) if len(c)==3 else c,width=width,joint="curve")

def frame(tier):
 im=Image.new("RGBA",(128,128));d=ImageDraw.Draw(im)
 a,b,c,h=TIERS[tier]
 dark,mid,hot=NEON[tier]
 # Sixteen angular teeth break the silhouette into a clockwork gear.
 for i in range(16):
  ang=math.radians(i*22.5)
  x=round(63.5+53*math.cos(ang));y=round(63.5+53*math.sin(ang))
  d.rectangle((x-3,y-3,x+3,y+3),fill=INK)
  d.rectangle((x-2,y-2,x+2,y+2),fill=rgba(b))
 for box,col in [((10,10,117,117),INK),((12,12,115,115),rgba(a)),
                 ((15,15,112,112),rgba(b)),((18,18,109,109),rgba(c)),
                 ((20,20,107,107),INK),((23,23,104,104),(47,48,61,255)),
                 ((27,27,100,100),INK),((29,29,98,98),(10,21,34,255))]:
  d.ellipse(box,fill=col)
 # Tiny stepped warm wear pixels make the copper/steel material read at native size.
 for y in range(11,117):
  for x in range(11,117):
   rr=(x-63.5)**2+(y-63.5)**2
   if 44**2<rr<50**2 and ((x*17+y*29)%13)<7:
    dr=(x*7+y*11)%23-11;dg=(x*13+y*5)%19-9;db=(x*3+y*17)%17-8
    col=tuple(max(0,min(255,v+n)) for v,n in zip(b,(dr,dg,db)))
    d.point((x,y),fill=rgba(col))
 # Side copper pressure pipes, ribbed hoses and bright glass ampules.
 for side in (-1,1):
  x=64+side*43
  d.arc((x-8,34,x+8,94),85 if side<0 else 265,275 if side<0 else 455,fill=INK,width=6)
  d.arc((x-8,34,x+8,94),85 if side<0 else 265,275 if side<0 else 455,fill=(133,79,42,255),width=3)
  for y in range(37,92,6):
   xx=x+(-4 if side<0 else 4)
   d.line((xx-2,y,xx+2,y),fill=(238,160,79,255),width=1)
  tx=19 if side<0 else 103
  d.rectangle((tx-4,48,tx+4,80),fill=INK)
  d.rectangle((tx-3,52,tx+3,76),fill=(52,63,71,255))
  d.rectangle((tx-2,55,tx+2,73),fill=rgba(dark))
  d.rectangle((tx-1,57,tx+1,71),fill=rgba(mid))
  d.line((tx-1,58,tx-1,67),fill=rgba(hot))
  for yy in (48,78):
   d.rectangle((tx-5,yy-2,tx+5,yy+2),fill=INK)
   d.rectangle((tx-4,yy-1,tx+4,yy+1),fill=rgba(c))
 # Gear and bolt motifs, with a crossing screw slot.
 for i in range(12):
  ang=math.radians(i*30)
  x=round(63.5+49*math.cos(ang));y=round(63.5+49*math.sin(ang))
  d.ellipse((x-2,y-2,x+2,y+2),fill=INK)
  d.ellipse((x-1,y-1,x+1,y+1),fill=rgba(h))
  d.point((x,y),fill=INK)
 # Exposed circuit ticks between the metal and the glass.
 for i in range(32):
  ang=math.radians(i*11.25)
  x1=round(63.5+37*math.cos(ang));y1=round(63.5+37*math.sin(ang))
  x2=round(63.5+(40 if i%4==0 else 38)*math.cos(ang))
  y2=round(63.5+(40 if i%4==0 else 38)*math.sin(ang))
  d.line((x1,y1,x2,y2),fill=rgba(mid if i%4==0 else dark))
 # Two pressure gauges, a tiny valve cross, paired cable runs and steam pixels.
 d.ellipse((27,89,38,100),fill=INK,outline=rgba(c),width=2)
 d.arc((29,91,36,98),190,345,fill=rgba(h),width=1)
 d.line((32,95,36,92),fill=rgba(NEON[tier][1]))
 d.ellipse((90,90,101,101),fill=INK,outline=rgba(b),width=2)
 d.line((95,89,95,102),fill=rgba(c),width=1)
 d.line((89,95,102,95),fill=rgba(c),width=1)
 d.ellipse((93,93,97,97),fill=INK)
 for side in (-1,1):
  xx=64+side*43
  for off in (0,3):
   d.arc((xx-6+off,82,xx+6+off,101),0 if side<0 else 180,180 if side<0 else 360,fill=(57+off*11,74+off*8,88+off*5,255),width=1)
 for x,y in ((25,31),(27,27),(102,32),(100,28)):
  d.point((x,y),fill=(177,197,198,255))
 d.arc((29,29,98,98),194,296,fill=(117,211,216,255),width=1)
 d.arc((32,32,95,95),205,276,fill=(229,251,244,255),width=1)
 d.arc((29,29,98,98),20,70,fill=rgba(mid),width=1)
 if tier=="platinum":
  d.arc((25,25,102,102),0,359,fill=rgba(h),width=1)
  for ang in range(15,360,45):
   x=round(63.5+42*math.cos(math.radians(ang)));y=round(63.5+42*math.sin(math.radians(ang)))
   d.rectangle((x-1,y-1,x+1,y+1),fill=rgba(hot))
 # A sealed lamp at twelve o'clock and a riveted blank nameplate.
 d.rectangle((55,12,72,22),fill=INK);d.rectangle((57,14,70,20),fill=rgba(b))
 d.rectangle((59,15,68,18),fill=rgba(mid));d.line((60,15,66,15),fill=rgba(hot))
 d.rectangle((39,103,88,114),fill=INK)
 d.polygon([(41,105),(86,105),(83,112),(44,112)],fill=rgba(b))
 d.line((46,106,81,106),fill=rgba(h))
 for x in (45,82):
  d.ellipse((x-1,107,x+1,109),fill=INK);d.point((x,107),fill=rgba(h))
 pixels=im.load()
 for yy in range(128):
  for xx in range(128):
   if (xx-63.5)**2+(yy-63.5)**2>57.6**2:pixels[xx,yy]=(0,0,0,0)
 return im

def source(path, cell, divisor, x=64,y=68, limit=96, magnify=1):
 """Copy source cell 0 with exact nearest-neighbour integer reduction."""
 if path=="git:tide":
  cached=SRC/"tide_boss_cell0.png"
  if cached.exists():im=Image.open(cached).convert("RGBA")
  else:
   data=subprocess.check_output(["git","show","art/tide-boss:Pause/Assets/Art/Bosses/Tide~/Tide.png"],cwd=SRC)
   im=Image.open(io.BytesIO(data)).convert("RGBA").crop((0,0,384,384))
   im.save(cached)
 else:
  p=Path(path) if Path(path).is_absolute() else ART/path
  im=Image.open(p).convert("RGBA")
 im=im.crop((0,0,cell,cell))
 im=im.resize((cell//divisor,cell//divisor),N)
 # Pixel alpha is made binary for the native transparent sprite; RGB is untouched.
 im.putalpha(im.getchannel("A").point(lambda a: 255 if a>=128 else 0))
 box=im.getbbox()
 if box: im=im.crop(box)
 if magnify!=1: im=im.resize((im.width*magnify,im.height*magnify),N)
 return im,(round(x-im.width/2),round(y-im.height/2))

def paste_asset(im,path,cell,divisor,x=64,y=68,limit=96,magnify=1):
 s,xy=source(path,cell,divisor,x,y,limit,magnify);im.alpha_composite(s,xy)

def glow(d,world):
 dark,mid,hi=ACCENTS[world]
 d.ellipse((31,31,96,96),fill=rgba(dark))
 d.ellipse((36,36,91,91),fill=(12,18,36,255),outline=rgba(mid),width=2)
 d.arc((39,39,88,88),190,345,fill=rgba(hi),width=1)
 for x,y in [(36,56),(89,43),(98,78),(47,92)]:
  d.rectangle((x,y,x+2,y+2),fill=rgba(dark));d.point((x+1,y),fill=rgba(hi))

def accessory(group):
 """Group-specific detachable rim hardware, drawn at the icon's native grid."""
 im=Image.new("RGBA",(128,128));d=ImageDraw.Draw(im)
 copper=(194,111,52);gold=(246,194,86);cyan=(92,230,231)
 if group=="world":
  d.ellipse((17,22,36,41),fill=INK,outline=rgba(copper),width=3)
  d.ellipse((21,26,32,37),fill=(37,86,102,255),outline=rgba(cyan))
  d.arc((13,29,40,38),5,175,fill=rgba(gold),width=2)
  for x,y in [(16,28),(27,19),(37,34)]:d.rectangle((x,y,x+2,y+2),fill=rgba(gold))
 elif group=="boss":
  polygon(d,[(53,16),(58,23),(70,23),(75,16),(78,27),(69,33),(59,33),(50,27)],copper)
  d.rectangle((58,24,61,27),fill=INK);d.rectangle((67,24,70,27),fill=INK)
  for x in (46,80):polygon(d,[(x-2,29),(x,18),(x+3,31)],gold)
 elif group=="elite":
  for flip in (-1,1):
   x=64+flip*43
   polygon(d,[(x,43),(x+flip*13,35),(x+flip*9,47),(x,53)],copper)
   line(d,[(x,44),(x+flip*9,40)],gold)
 elif group=="enemy":
  for i in range(5):
   x=39+i*12;y=104+(abs(2-i)*2)
   d.rectangle((x,y,x+8,y+6),fill=INK)
   d.rectangle((x+1,y+1,x+7,y+5),fill=rgba(copper))
   d.line((x+2,y+2,x+6,y+2),fill=rgba(gold))
 elif group=="collection":
  d.rectangle((52,101,76,115),fill=INK)
  d.rectangle((54,103,74,113),fill=rgba(copper))
  d.rectangle((60,101,68,108),fill=(50,59,63,255),outline=INK)
  d.point((64,105),fill=rgba(gold))
 elif group=="pause":
  d.ellipse((18,20,38,40),fill=INK,outline=rgba(copper),width=3)
  for x in (25,30):d.rectangle((x,26,x+2,34),fill=rgba(cyan))
  for a in range(0,360,60):
   x=round(28+13*math.cos(math.radians(a)));y=round(30+13*math.sin(math.radians(a)))
   d.point((x,y),fill=rgba(gold))
 elif group=="codex":
  polygon(d,[(50,107),(61,108),(64,111),(67,108),(78,107),(78,116),(65,116),(63,118),(50,116)],copper)
  line(d,[(64,110),(64,116)],gold)
 elif group=="dust":
  d.rectangle((103,47,114,78),fill=INK)
  d.rectangle((105,51,112,75),fill=(55,93,95,255))
  d.rectangle((106,60,111,73),fill=rgba(gold))
  d.rectangle((104,47,113,52),fill=rgba(copper))
  d.rectangle((104,75,113,79),fill=rgba(copper))
 elif group=="skill":
  d.arc((17,20,40,43),185,350,fill=rgba(copper),width=4)
  line(d,[(28,33),(35,24)],gold,2)
  d.ellipse((26,31,30,35),fill=rgba(cyan))
 elif group=="meta":
  d.rectangle((91,24,108,38),fill=INK)
  d.rectangle((93,26,106,36),fill=rgba(copper))
  d.rectangle((96,28,100,34),fill=INK)
  d.line((103,22,103,28),fill=rgba(gold),width=2)
  d.point((99,31),fill=(92,255,115,255))
 else:raise ValueError(group)
 return im

def glass(im):
 """Hard-pixel reflection, reticle ticks, and two scan strokes over the subject."""
 d=ImageDraw.Draw(im)
 d.arc((34,33,93,92),195,284,fill=(140,239,244,255),width=1)
 d.arc((38,37,89,88),198,245,fill=(245,251,238,255),width=1)
 for x,y in [(44,40),(47,39),(50,38),(40,44),(84,45),(86,48)]:
  d.point((x,y),fill=(207,248,242,255))
 for yy in (60,74):
  for xx in range(43,86,6):
   if (xx+yy)%3==0:d.point((xx,yy),fill=(75,139,153,255))
 for x,y in [(64,35),(91,64),(64,93),(36,64)]:
  d.line((x-2,y,x+2,y),fill=(106,212,224,255))

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
 if variant=="codex_field_guide":
  polygon(d,[(42,43),(84,43),(91,51),(91,93),(47,93),(38,84),(38,50)],(64,122,117))
  d.rectangle((44,49,84,86),fill=(204,177,104,255),outline=INK,width=2)
  d.rectangle((47,52,80,82),fill=(45,102,108,255))
  d.ellipse((55,58,73,76),fill=(38,166,153,255),outline=(233,227,149,255),width=2)
  d.arc((52,64,76,71),10,170,fill=(255,224,136,255),width=2)
  polygon(d,[(76,41),(83,41),(83,64),(79,60),(76,64)],(71,240,215))
  return
 polygon(d,[(32,45),(59,51),(64,56),(69,51),(96,45),(96,88),(69,91),(64,96),(59,91),(32,88)],(114,79,38))
 polygon(d,[(36,49),(60,55),(63,60),(63,89),(59,86),(36,83)],(230,199,120))
 polygon(d,[(65,60),(69,55),(92,49),(92,83),(69,86),(65,89)],(215,180,102))
 line(d,[(64,57),(64,92)],(44,30,23),2)
 for yy in (64,70,76):
  line(d,[(41,yy),(56,yy+3)],(87,70,55));line(d,[(72,yy+3),(86,yy)],(87,70,55))
 if variant=="codex_10":
  ring(d,83,71,11,(83,210,225),2);line(d,[(90,79),(99,89)],(223,166,78),4)
 elif variant=="codex_50":
  polygon(d,[(67,56),(88,50),(88,61),(68,66)],(24,92,126))
  star(d,79,56,(173,248,246))
  line(d,[(70,76),(79,68),(88,72)],(49,177,201),2)
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
  paste_asset(im,f"{B}{w.title()}/Planetfall/{w}_planet.png",1024,32 if w=="tide" else 16,64,69)
  d=ImageDraw.Draw(im)
  if w=="frost": star(d,64,67,(214,253,255))
  elif w=="verdant": polygon(d,[(62,48),(75,56),(69,74),(55,79),(52,65)],(158,248,60))
  elif w=="ember": polygon(d,[(64,49),(73,68),(68,80),(56,79),(53,68)],(255,148,46))
  else:
   ring(d,64,69,23,(52,208,177),2)
   line(d,[(43,76),(52,70),(61,76),(70,70),(81,76)],(125,255,223),3)
   line(d,[(64,53),(64,85)],(219,252,220),2)
   d.arc((54,72,74,88),0,180,fill=(219,252,220,255),width=2)
 elif ident.startswith("boss_") and ident in ("boss_space","boss_frost","boss_verdant","boss_ember"):
  w=ident.split('_')[1];paste_asset(im,f"{R}Bosses/{w.title()}.png",384,12,64,68,magnify=2)
 elif ident=="boss_tide":
  paste_asset(im,"git:tide",384,12,64,68,magnify=2)
 elif ident=="boss_all":
  for w,x,y in [("Space",44,48),("Frost",83,48),("Verdant",44,87),("Ember",83,87)]:
   paste_asset(im,f"{R}Bosses/{w}.png",384,24,x,y)
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
  paste_asset(im,f"{R}Elites/{elite[0]}/{elite[1]}.png",192,4,64,68)
  d=ImageDraw.Draw(im)
  if ident=="elite_blink": line(d,[(43,96),(59,71),(54,70),(80,40)],(250,65,217),4)
  else:
   for x in (39,64,89): star(d,x,94,hi,True)
 elif ident.startswith("ship_") or ident in ("skin_special","speed_super_sonic","deaths_10","pause_blink_100","pause_perfect_dodge"):
  ships={"ship_first":"GoldWarden","ship_half":"NeonComet","ship_all":"GoldWarden",
         "skin_special":"JadePhantom","speed_super_sonic":"NeonComet","deaths_10":"GoldWarden",
         "pause_blink_100":"Ninja","pause_perfect_dodge":"Ninja"}
  if ident in ("ship_half","ship_all"):
   formation=([(64,51,"GoldWarden"),(45,77,"NeonComet"),(83,77,"VoltViper")]
              if ident=="ship_half" else
              [(64,44,"GoldWarden"),(44,59,"NeonComet"),(84,59,"VoltViper"),(51,83,"JadePhantom"),(77,83,"Ninja")])
   for x,y,name in formation:
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
  if ident=="rocks_500":paste_asset(im,f"{R}Enemies/space_rock_crater.png",192,4,64,67)
  else:paste_asset(im,f"{R}Enemies/Mines/rail_mines_neon.png",310,10,64,67)
  d=ImageDraw.Draw(im)
  if ident=="rocks_500":line(d,[(43,90),(77,47)],(253,222,130),4)
  else:
   polygon(d,[(74,68),(92,63),(96,82),(78,88)],(216,183,104))
   line(d,[(79,76),(84,80),(91,69)],(58,208,187),3)
   d.ellipse((45,48,83,86),outline=(244,193,87,255),width=2)
 elif ident.startswith("stars_") or ident=="dust_spent_10000":
  d=ImageDraw.Draw(im)
  if ident=="dust_spent_10000":
   d.arc((44,39,84,75),180,360,fill=(255,210,112,255),width=5)
   polygon(d,[(42,57),(85,57),(92,69),(84,92),(45,92),(36,72)],(111,66,36))
   polygon(d,[(45,61),(82,61),(87,70),(80,87),(48,87),(41,72)],(214,138,62))
   d.ellipse((51,61,77,87),fill=(91,55,32,255),outline=(254,218,116,255),width=3)
   for a in range(0,360,45):
    xx=round(64+15*math.cos(math.radians(a)));yy=round(74+15*math.sin(math.radians(a)))
    d.rectangle((xx-1,yy-1,xx+1,yy+1),fill=(255,213,109,255))
   d.ellipse((58,68,70,80),fill=(227,179,75,255),outline=INK,width=2)
  else:
   polygon(d,[(44,59),(54,50),(74,50),(84,59),(80,89),(70,96),(53,96),(45,87)],(142,91,43))
   polygon(d,[(48,60),(54,55),(75,55),(80,62),(75,85),(52,85)],(222,151,63))
   line(d,[(51,64),(76,64)],(91,53,26),3)
   for x,y in [(64,46),(50,49),(78,49),(57,38),(73,38)][: 1 if ident=="stars_150" else 3 if ident=="stars_1000" else 5]:
    paste_asset(im,f"{R}Pickups/Atoms/dust_idle_0.png",180,9,x,y,16)
   d=ImageDraw.Draw(im)
   if ident=="stars_5000":ring(d,64,73,17,(255,227,99),2)
 elif ident.startswith("pause_"):
  d=ImageDraw.Draw(im)
  if ident=="pause_hoarder":
   for y in (79,67,55):ring(d,64,y,16,(222,87,188),2)
   pause(d,64,64,9,hi)
  else:
   ring(d,64,67,28,mid,3)
   pause(d,50 if ident=="pause_blink_kill" else 64,67,14 if ident=="pause_blink_kill" else 17,hi)
   if ident=="pause_blink_kill":
    line(d,[(34,88),(58,74),(73,64)],(255,99,216),3)
    d.ellipse((70,49,94,73),outline=rgba(hi),width=3)
    d.ellipse((77,56,87,66),outline=(255,232,143,255),width=2)
    star(d,82,61,(255,244,164),True)
   elif ident=="pause_no_pause_world":
    line(d,[(37,91),(91,39)],(255,206,118),5);d.ellipse((86,81,95,90),outline=rgba(hi),width=2)
 elif ident.startswith("codex_"):
  d=ImageDraw.Draw(im);book(d,ident)
 elif ident.startswith("kills_"):
  if ident=="kills_100":
   d=ImageDraw.Draw(im)
   for x,y in [(44,61),(64,50),(84,61)]:
    polygon(d,[(x-6,y+23),(x-6,y),(x-3,y-8),(x+3,y-8),(x+6,y),(x+6,y+23)],(184,108,44))
    d.rectangle((x-5,y+17,x+5,y+22),fill=(250,196,91,255),outline=INK)
    d.line((x-2,y+2,x-2,y+15),fill=(255,223,134,255))
  elif ident=="kills_1000":
   d=ImageDraw.Draw(im);skull(d)
   ring(d,64,66,30,(173,197,204),2)
  else:
   d=ImageDraw.Draw(im);skull(d,(236,215,155))
   polygon(d,[(45,43),(45,32),(55,37),(64,27),(73,37),(83,32),(83,43)],(241,190,65))
 elif ident in ("elite_first","elite_10","elite_50"):
  # Actual elite hulls for the hunt medals; the chevron wings live on the rim.
  names=["space_elite_rift_lancer","space_elite_orbit_reaver","space_elite_eventide_bastion"]
  idx=["elite_first","elite_10","elite_50"].index(ident)
  paste_asset(im,f"{R}Elites/Space/{names[idx]}.png",192,4,64,66)
  d=ImageDraw.Draw(im)
  for i in range(idx+1):
   y=86+i*5
   line(d,[(51,y),(64,y+5),(77,y)],(255,202,90),2)
  d.ellipse((59,57,69,67),outline=rgba(hi),width=1)
 elif ident.startswith("score_"):
  d=ImageDraw.Draw(im)
  # Three geared odometer drums, with a different count of engaged stops.
  d.rectangle((35,47,93,83),fill=INK)
  d.rectangle((38,50,90,80),fill=(104,70,39,255),outline=(238,177,75,255),width=2)
  n={"score_10k":1,"score_50k":2,"score_150k":3}[ident]
  slots=([(48,28)] if n==1 else [(43,19),(67,19)] if n==2 else [(43,11),(59,11),(75,11)])
  for j,(x,w) in enumerate(slots):
   d.rectangle((x,54,x+w,75),fill=(21,35,47,255),outline=(201,164,82,255))
   d.line((x+3,59,x+w-3,59),fill=(119,229,224,255),width=2)
   d.line((x+3,69,x+w-3,69),fill=(119,229,224,255),width=2)
   for yy in range(62,67):d.point((x+4+(j+yy)%(w-7),yy),fill=(255,226,129,255))
  for i in range(n):
   star(d,round(64+(i-(n-1)/2)*13),43,(255,220,110),True)
  if n==3:polygon(d,[(47,42),(47,33),(57,37),(64,29),(71,37),(81,33),(81,42)],(245,201,76))
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
 groups={group:accessory(group) for group in set(GROUPS.values())}
 for group,im in groups.items():im.save(SRC/"frames"/f"{group}_accessory_128.png")
 cells=[]
 for ident,tier,world in BADGES:
  im=frames[tier].copy();subject(im,ident,world);glass(im)
  im.alpha_composite(groups[GROUPS[ident.split('_')[0]]])
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
 print(f"built {len(cells)} badges, four tier frames, and {len(groups)} group accessories")

if __name__=="__main__":build()
