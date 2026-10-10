"""Verify every generated badge and make the six-row before/after reference sheet."""
from __future__ import annotations

from collections import Counter
from pathlib import Path
from PIL import Image, ImageDraw
from build_pixel_badges import ART, SRC, OUT, BADGES, TIERS, BG, source

N=Image.Resampling.NEAREST

def dhash(im):
 gray=im.convert("RGB").convert("L").resize((17,16),Image.Resampling.BOX)
 p=gray.load();return tuple(p[x,y]>p[x+1,y] for y in range(16) for x in range(16))

def comparison():
 cases=[
  ("world_frost_reached","Frost planet","Backgrounds/Resources/Worlds/Frost/Planetfall/frost_planet.png",1024,16),
  ("boss_space","Space boss","Resources/Bosses/Space.png",384,6),
  ("boss_ember","Ember boss","Resources/Bosses/Ember.png",384,6),
  ("elite_frost_all","Frost elite","Resources/Elites/Frost/frost_elite_floe_harrower.png",192,2),
  ("rocks_500","Space rock","Resources/Enemies/space_rock_crater.png",192,3),
  ("mines_25","Mine atlas","Resources/Enemies/Mines/rail_mines_neon.png",310,5),
 ]
 w,h=600,6*162+39
 sheet=Image.new("RGB",(w,h),(12,17,27));d=ImageDraw.Draw(sheet)
 for x,label in [(19,"REJECTED V1"),(214,"NEW PIXEL BADGE"),(410,"GAME SPRITE")]:
  d.text((x,12),label,fill=(225,232,230))
 for row,(ident,label,path,cell,divisor) in enumerate(cases):
  y=39+row*162
  old=Image.open(SRC/"rejected_v1"/f"{ident}_1024.png").convert("RGB").resize((128,128),Image.Resampling.BOX)
  new=Image.open(OUT/f"{ident}.png").convert("RGBA")
  newtile=Image.new("RGB",(128,128),BG);newtile.paste(new,(0,0),new)
  game=Image.new("RGBA",(128,128),(7,7,15,255));sprite,xy=source(path,cell,divisor,64,64,112)
  game.alpha_composite(sprite,xy)
  for x,tile in [(12,old),(208,newtile),(404,game.convert("RGB"))]:sheet.paste(tile,(x,y))
  d.text((13,y+132),f"{ident} / {label}",fill=(206,214,214))
 sheet.save(SRC/"before_after_6.png")

def verify():
 expected={b[0] for b in BADGES}
 assert len(expected)==60
 assert {p.stem for p in OUT.glob("*.png")}==expected,"128 icon inventory differs"
 masters=[p for p in SRC.glob("*_1024.png") if p.name!="sheet_1024.png"]
 assert {p.name.removesuffix("_1024.png") for p in masters}==expected,"master inventory differs"
 issues=[];counts={};hashes={};tiers=Counter()
 for ident,tier,world in BADGES:
  icon=Image.open(OUT/f"{ident}.png")
  master=Image.open(SRC/f"{ident}_1024.png")
  if icon.size!=(128,128) or icon.mode!="RGBA":issues.append(f"{ident}: 128 size/mode")
  if master.size!=(1024,1024) or master.mode!="RGB":issues.append(f"{ident}: 1024 size/mode")
  a=icon.getchannel("A")
  if {value for _,value in (a.getcolors(16384) or [])}!={0,255}:
   issues.append(f"{ident}: alpha is not hard edged")
  # 90% centered circle: every painted native pixel must be within radius 57.6.
  for y in range(128):
   for x in range(128):
    if a.getpixel((x,y)) and (x-63.5)**2+(y-63.5)**2>57.6**2:
     issues.append(f"{ident}: paint outside circle at {x},{y}");break
   if issues and issues[-1].startswith(f"{ident}: paint outside"):break
  composite=Image.new("RGB",(128,128),BG);composite.paste(icon,(0,0),icon)
  if master.tobytes()!=composite.resize((1024,1024),N).tobytes():
   issues.append(f"{ident}: master is not exact x8 nearest copy of native grid")
  for pt in [(0,0),(1023,0),(0,1023),(1023,1023)]:
   if master.getpixel(pt)!=BG:issues.append(f"{ident}: non-background corner")
  count=len(icon.getcolors(1000000) or [])
  counts[ident]=count
  if not 40<=count<=900:issues.append(f"{ident}: {count} colours")
  # A clear, fixed metal sample in the upper left rail rim.
  sample=icon.getpixel((49,15))[:3]
  if sample!=TIERS[tier][1]:issues.append(f"{ident}: wrong tier rim {sample}")
  tiers[tier]+=1
  h=dhash(composite)
  if h in hashes:issues.append(f"{ident}: perceptual hash duplicates {hashes[h]}")
  hashes[h]=ident
 report=[f"badges: {len(BADGES)}",f"masters: {len(masters)} RGB 1024x1024",
         f"icons: {len(list(OUT.glob('*.png')))} RGBA 128x128",
         f"tiers: {dict(tiers)}",f"colour range: {min(counts.values())}-{max(counts.values())}",
         f"unique 16x16 dHashes: {len(hashes)}",f"failures: {len(issues)}","",
         "id,tier,colour_count"]
 report.extend(f"{ident},{tier},{counts[ident]}" for ident,tier,_ in BADGES)
 if issues:report.extend(["","ISSUES:",*issues])
 (SRC/"verification.txt").write_text("\n".join(report)+"\n")
 comparison()
 print("\n".join(report[:7]))
 if issues:raise AssertionError("\n".join(issues))

if __name__=="__main__":verify()
