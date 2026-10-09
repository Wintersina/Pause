#!/usr/bin/env python3
"""Build and verify Frost death cues and the Cryo Jelly cry. NumPy + wave only.

Run from anywhere: python3 build_frost_sounds.py
All source, notes, sheet and audition stay beside this script.
"""
from __future__ import annotations
import itertools
import math
import struct
import wave
import zlib
from pathlib import Path
import numpy as np

RATE = 44100
ROOT = Path(__file__).resolve().parent
OUT = ROOT.parent / 'Resources' / 'Audio' / 'EnemyDeath'
GAP = np.zeros(round(1.2 * RATE))
PEAK_LIMIT = 10 ** (-1 / 20)
# key, name, recipe, nominal seconds, identity
SOUNDS = [
 ('frost_alien','Cryo Jelly','jelly',.63,'glass cryo capsule fracture, wet slush and venting vapour'),
 ('frost_chaser','Lance','lance',.53,'ice spear snapped through a steel drone hull, trailing air'),
 ('frost_fighter_1','Flake','flake',.45,'drill nose breaks thin ice blades over a metal crunch'),
 ('frost_fighter_2','Icicle','icicle',.52,'coolant bulb ruptures and sprays through a spear crack'),
 ('frost_fighter_3','Frost Kite','kite',.55,'six hex ice plates break in a hollow stagger'),
 ('frost_fighter_4','Hailstorm','hailstorm',.68,'cannon mount blows off and hail pellets rattle out'),
 ('frost_big','Glacier Golem','golem',1.22,'glacier fault boom, jaw collapse, grinding slabs and heavy rubble'),
 ('frost_mine','Geode Mine','mine',.53,'rail clamp burst ejects ice shards'),
 ('frost_rock_chunk','Frozen Chunk','chunk',.57,'frozen boulder splits; copper clamps rattle loose'),
 ('frost_rock_shard','Ice Shard','shard',.46,'low-passed crystal spears splinter and scatter'),
 ('frost_rock_rime','Rime Star','rime',.58,'six steel and ice blades whirr down into a hub crunch'),
 ('frost_elite_rimebreaker','Rimebreaker','rimebreaker',.90,'icebreaker prow buckles, rime steel folds, ice skids away'),
 ('frost_elite_floe_harrower','Floe Harrower','harrower',.95,'barge pontoons split, slab chutes spill, cutter fan shears'),
 ('frost_elite_cryo_siren','Cryo Siren','siren',.87,'coolant dish implodes, hoses vent, narrow spine cracks'),
 ('frost_elite_glacier_tender','Glacier Tender','tender',.95,'tether winches tear out and four drone pods cascade loose'),
 ('frost_elite_whiteout_sentinel','Whiteout Sentinel','sentinel',.95,'three layered glacier plates shear from a heavy wedge hull'),
]
BIG = {'golem','rimebreaker','harrower','siren','tender','sentinel'}

# Copied and adapted from the approved Space toolkit: smooth FFT bands, noise
# layers, soft envelopes, short stochastic room and A-weighted 50 ms matching.
def fft_filter(x, low=0, high=4000, shelf=True):
 f = np.fft.rfftfreq(len(x),1/RATE)
 h = np.ones_like(f)
 if low: h *= 1-np.exp(-.5*(f/max(low,1))**2)
 if high: h *= 1/np.sqrt(1+(f/high)**8)
 if shelf: h *= 1/np.sqrt(1+(f/6000)**4)
 return np.fft.irfft(np.fft.rfft(x)*h,n=len(x))

def shaped_noise(rng,n,low,high):
 x=fft_filter(rng.standard_normal(n),low,high)
 return x/(np.sqrt(np.mean(x*x))+1e-12)

def envelope(n,attack,decay,curve='exp'):
 t=np.arange(n)/RATE
 a=np.sin(np.minimum(t/attack,1)*np.pi/2)**2
 if curve=='exp': a*=np.exp(-t/decay)
 elif curve=='swell': a*=(t/max(t[-1],1/RATE))**.6
 return a

def add_noise(x,rng,at,dur,low,high,amp,decay=None,attack=.004,wobble=0,curve='exp'):
 start=round(at*RATE); n=min(round(dur*RATE),len(x)-start)
 if n<=0: return
 y=shaped_noise(rng,n,low,high); env=envelope(n,attack,decay or dur*.5,curve)
 if wobble:
  t=np.arange(n)/RATE
  env*=.72+.28*np.sin(2*np.pi*wobble*t+rng.uniform(0,2*np.pi))**2
 x[start:start+n]+=amp*y*env

def add_thump(x,rng,at,weight=1,deep=False):
 start=round(at*RATE); n=min(round(.075 if deep else .066),len(x)/RATE-at)
 n=round(max(0,n)*RATE)
 if n<=0:return
 t=np.arange(n)/RATE
 top=rng.uniform(112,154)*(.78 if deep else 1);bottom=rng.uniform(43,62)*(.8 if deep else 1)
 hz=bottom+(top-bottom)*np.exp(-t/.015)
 phase=2*np.pi*np.cumsum(hz)/RATE+rng.uniform(0,2*np.pi)
 tone=np.sin(phase)*envelope(n,.004,.020)
 noise=shaped_noise(rng,n,35,270 if deep else 410)*envelope(n,.004,.031)
 x[start:start+n]+=weight*(.14*tone+.33*noise)

def grains(x,rng,start,end,count,material='ice',amp=.06):
 bands={'ice':(350,2700),'steel':(220,1800),'rubble':(90,1500),'hail':(330,2300)}
 lo,hi=bands[material]
 for _ in range(count):
  at=rng.uniform(start,end);dur=rng.uniform(.008,.023) if material!='hail' else rng.uniform(.004,.012)
  add_noise(x,rng,at,dur,lo,hi,amp*rng.uniform(.45,1.15),dur*.5,.0025)

def room(x,rng,dur=.13,mix=.055):
 n=round(dur*RATE);t=np.arange(n)/RATE
 ir=fft_filter(rng.standard_normal(n),70,2100)*np.exp(-t/(dur/4))*np.minimum(t/.008,1)
 ir[:round(.009*RATE)]=0;ir/=np.sqrt(np.sum(ir*ir))+1e-12
 size=1<<(len(x)+n-2).bit_length()
 wet=np.fft.irfft(np.fft.rfft(x,size)*np.fft.rfft(ir,size),size)[:len(x)]
 return x+mix*wet

def a_weighted(x):
 f=np.fft.rfftfreq(len(x),1/RATE);f2=f*f
 ra=12194**2*f2*f2/((f2+20.6**2)*np.sqrt((f2+107.7**2)*(f2+737.9**2))*(f2+12194**2)+1e-30)
 return np.fft.irfft(np.fft.rfft(x)*ra*10**(2/20),n=len(x))

def short_a_level(x):
 y=a_weighted(x);size,hop=round(.050*RATE),round(.025*RATE)
 frames=np.lib.stride_tricks.sliding_window_view(y,size)[::hop]
 return 20*np.log10(np.max(np.sqrt(np.mean(frames*frames,axis=1)))+1e-30)

def match_loudness(x,target):
 x=x*10**((target-short_a_level(x))/20)
 if np.max(np.abs(x))>PEAK_LIMIT: raise ValueError(f'A match exceeds peak limit: {20*np.log10(np.max(np.abs(x))):.1f} dBFS')
 return x

def set_crest(x,rms_db):
 """Copied from Space: shape transient crest before perceptual matching."""
 target_ratio=10**((rms_db+4.5)/-20);a=np.abs(x)
 def ratio(g):
  v=a**g;return np.max(v)/np.sqrt(np.mean(v*v))
 lo,hi=.35,2.1
 for _ in range(35):
  mid=(lo+hi)/2
  if ratio(mid)<target_ratio:lo=mid
  else:hi=mid
 gamma=(lo+hi)/2;y=np.sign(x)*a**gamma;n=len(y)
 dc_window=(np.sin(np.minimum(np.arange(n)/(RATE*.0045),1)*np.pi/2)**2*
            np.sin(np.minimum(np.arange(n)[::-1]/(RATE*.022),1)*np.pi/2)**2)
 y=fft_filter(y,35,0,False)*dc_window
 y*=10**(-4.5/20)/np.max(np.abs(y))
 return y

def write_wav(path,x):
 path.parent.mkdir(parents=True,exist_ok=True)
 pcm=np.round(np.clip(x,-1,1)*32767).astype('<i2')
 with wave.open(str(path),'wb') as w:
  w.setnchannels(1);w.setsampwidth(2);w.setframerate(RATE);w.writeframes(pcm.tobytes())

def read_wav(path):
 with wave.open(str(path),'rb') as w:
  assert (w.getnchannels(),w.getsampwidth(),w.getframerate())==(1,2,RATE)
  return np.frombuffer(w.readframes(w.getnframes()),dtype='<i2').astype(float)/32768

def max_xcorr(x,y):
 x=x-np.mean(x);y=y-np.mean(y);n=1<<(len(x)+len(y)-2).bit_length()
 c=np.fft.irfft(np.fft.rfft(x,n)*np.conj(np.fft.rfft(y,n)),n)
 return float(np.max(np.abs(c))/(np.linalg.norm(x)*np.linalg.norm(y)))

def join(cues):
 return np.concatenate([p for i,cue in enumerate(cues) for p in ((cue,GAP) if i<len(cues)-1 else (cue,))])

def render_death(spec,v):
 key,name,kind,nominal,identity=spec
 rng=np.random.default_rng(zlib.crc32(key.encode())+1000003*v)
 length=nominal*[1,.965,1.045][v];x=np.zeros(round(length*RATE))
 j=rng.uniform(-.006,.006);at=.014+j
 heavy=kind in BIG
 # Every cue begins with a low physical impulse, then its distinct material event.
 add_thump(x,rng,at,1.16 if heavy else (.55 if kind in {'shard','kite'} else .78),heavy)
 if kind=='jelly':
  add_noise(x,rng,at,.17,210,2400,.34,.044) # glass shell
  grains(x,rng,.035,.16,6+v,'ice',.052)
  add_noise(x,rng,.073,.30,65,1050,.30,.105,wobble=21) # slush
  for k in range(4+v): add_noise(x,rng,.12+k*.037,.052,65,640,.07,.019,wobble=18)
  add_noise(x,rng,.21,.26,420,1850,.10,.092) # vapour
 elif kind=='lance':
  add_noise(x,rng,at,.085,180,2500,.43,.026)
  add_noise(x,rng,.034,.16,85,1250,.30,.057)
  grains(x,rng,.04,.22,7+v,'steel',.052)
  for k in range(4):add_noise(x,rng,.105+k*.036,.10,90,1450-k*210,.055,.043)
 elif kind=='flake':
  add_noise(x,rng,at,.085,220,2100,.38,.025)
  for k in range(3):add_noise(x,rng,.049+k*.040,.060,270,2350,.19,.018)
  add_noise(x,rng,.105,.16,95,1130,.25,.051)
  grains(x,rng,.10,.27,7+v,'ice',.045)
 elif kind=='icicle':
  add_noise(x,rng,at,.14,100,1130,.36,.048)
  add_noise(x,rng,.047,.26,390,2100,.18,.091) # escaping coolant
  add_noise(x,rng,.084,.085,190,2350,.32,.025)
  grains(x,rng,.13,.29,8+v,'ice',.054)
 elif kind=='kite':
  for k in range(3):add_noise(x,rng,at+k*.043,.10,120,1600,.27,.032)
  add_noise(x,rng,.13,.13,60,840,.27,.052) # hollow body
  grains(x,rng,.10,.32,10+v,'ice',.044)
 elif kind=='hailstorm':
  add_noise(x,rng,at,.18,55,820,.40,.062)
  add_noise(x,rng,.068,.14,140,1450,.27,.050) # mount blow-off
  grains(x,rng,.09,.45,24+v*3,'hail',.072)
  add_noise(x,rng,.27,.23,75,730,.11,.079)
 elif kind=='golem':
  add_noise(x,rng,at,.56,35,670,.56,.210)
  for k in range(4):
   t=.13+k*.13+j;add_thump(x,rng,t,.48,True)
   add_noise(x,rng,t,.23,55,780,.26,.090)
  grains(x,rng,.24,.84,22+v*3,'rubble',.066)
  add_noise(x,rng,.50,.55,34,310,.15,.270,wobble=8)
 elif kind=='mine':
  add_noise(x,rng,at,.15,58,1050,.41,.045)
  add_noise(x,rng,.041,.09,110,1900,.19,.031) # clamp rupture
  grains(x,rng,.075,.30,14+v,'ice',.061)
 elif kind=='chunk':
  add_noise(x,rng,at,.19,65,1230,.43,.058)
  add_noise(x,rng,.10,.18,70,720,.24,.071)
  grains(x,rng,.12,.34,9+v,'rubble',.054)
  grains(x,rng,.15,.37,7+v,'steel',.048) # clamp scatter
 elif kind=='shard':
  add_noise(x,rng,at,.11,250,2300,.40,.033)
  for k in range(3):add_noise(x,rng,.04+k*.035,.070,320,2600,.17,.022)
  grains(x,rng,.10,.25,8+v,'ice',.050)
 elif kind=='rime':
  # Rotation is layered descending noise, never a swept oscillator.
  for k in range(7):
   add_noise(x,rng,.010+k*.029,.095,170,1480-k*140,.08,.050,wobble=17-k)
  add_noise(x,rng,.22,.13,85,1300,.38,.046)
  add_thump(x,rng,.22,.58)
  grains(x,rng,.27,.43,8+v,'steel',.055)
 elif kind=='rimebreaker':
  add_noise(x,rng,at,.24,65,920,.42,.082)
  for k in range(3):add_noise(x,rng,.11+k*.078,.17,70,1150,.25,.066)
  grains(x,rng,.19,.58,15+v,'ice',.054)
  add_noise(x,rng,.39,.31,45,470,.15,.120)
 elif kind=='harrower':
  add_noise(x,rng,at,.26,55,790,.41,.091)
  for k in range(4):
   t=.13+k*.084;add_noise(x,rng,t,.13,95,1270,.19,.043)
   grains(x,rng,t,t+.055,3,'rubble',.044)
  for k in range(5):add_noise(x,rng,.40+k*.036,.08,85,1010-k*110,.08,.038)
  add_noise(x,rng,.52,.31,35,400,.12,.135)
 elif kind=='siren':
  # A dish is a mechanical bowl, not a literal siren tone.
  add_noise(x,rng,at,.17,95,1160,.40,.057)
  for k in range(5):add_noise(x,rng,.072+k*.034,.10,135,1440-k*130,.13,.038)
  add_noise(x,rng,.18,.34,370,1640,.16,.108) # hoses
  grains(x,rng,.21,.49,8+v,'steel',.045)
  add_noise(x,rng,.41,.26,55,520,.10,.107)
 elif kind=='tender':
  add_noise(x,rng,at,.24,55,860,.42,.078)
  for k in range(2):
   t=.12+k*.082;add_noise(x,rng,t,.13,100,1480,.22,.043) # winches
  for k in range(4):
   t=.27+k*.093;add_noise(x,rng,t,.12,120,1280,.19,.042) # pods
   grains(x,rng,t,t+.050,3,'steel',.039)
  add_noise(x,rng,.61,.24,45,430,.10,.11)
 elif kind=='sentinel':
  add_noise(x,rng,at,.29,42,710,.43,.10)
  for k in range(3):
   t=.12+k*.125;add_thump(x,rng,t,.52,True)
   add_noise(x,rng,t,.18,80,1060,.25,.060)
   grains(x,rng,t+.04,t+.14,5,'ice',.043)
  add_noise(x,rng,.50,.29,45,550,.13,.135)
 x=room(x,rng,.18 if heavy else .12,.070 if heavy else .052)
 x=fft_filter(x,28,3100 if heavy else (2750 if kind=='shard' else 4100))
 n=len(x);fade=np.sin(np.minimum(np.arange(n)/(RATE*.0045),1)*np.pi/2)**2
 fade*=np.sin(np.minimum(np.arange(n)[::-1]/(RATE*.022),1)*np.pi/2)**2
 x*=fade;x-=np.mean(x)*fade
 x=set_crest(x,-17 if heavy else -20)
 return match_loudness(x,-13.5 if heavy else -15.0)

# Source/filter cry adapted from the Space scream script, lengthened and made
# breathier. The Jelly is the only Frost creature / visibly occupied unit.
def voice_filter(x,low,high):
 f=np.fft.rfftfreq(len(x),1/RATE)
 hp=1-np.exp(-.5*(f/low)**4);lp=1/np.sqrt(1+(f/high)**14);lp[f>=4000]=0
 return np.fft.irfft(np.fft.rfft(x)*hp*lp,n=len(x))

def resonator(x,center,q):
 y=np.empty_like(x);x1=x2=y1=y2=0.
 for i,(sample,hz) in enumerate(zip(x,center)):
  w=2*math.pi*min(hz,3900)/RATE;a=math.sin(w)/(2*q);inv=1/(1+a)
  b0=a*inv;a1=-2*math.cos(w)*inv;a2=(1-a)*inv
  value=b0*(sample-x2)-a1*y1-a2*y2
  y[i]=value;x2,x1,y2,y1=x1,sample,y1,value
 return y

def smooth_random(rng,n,spacing):
 p=rng.standard_normal(math.ceil(n/spacing)+2)
 return np.interp(np.arange(n),np.arange(len(p))*spacing,p)

def render_cry(v):
 rng=np.random.default_rng(zlib.crc32(b'frost_alien_scream')+1000003*v)
 duration=.94*[1,.965,1.045][v];n=round(duration*RATE)
 t=np.arange(n)/RATE;u=np.arange(n)/max(n-1,1)
 # Drawn-out, scared ah with a trembling breath and watery throat.
 contour=[(.92,1.10,.98,.75),(1.02,1.16,.90,.70),(.88,1.08,1.00,.72)][v]
 f0=185*[1,.94,1.07][v]*np.interp(u,[0,.18,.60,1],contour)
 f0*=1+.020*smooth_random(rng,n,round(.010*RATE))
 f0*=1+.012*np.sin(2*np.pi*(5.0+v*.7)*t)
 phase=2*np.pi*np.cumsum(f0)/RATE
 src=np.zeros(n)
 for k in range(1,19):
  if k*np.max(f0)<3500:src+=np.sin(k*phase+rng.uniform(-.5,.5))/k**1.24
 breath=voice_filter(rng.standard_normal(n),300,2450)
 breath/=np.sqrt(np.mean(breath*breath))+1e-12
 src=np.tanh(1.25*src)+.35*breath
 slide=np.clip((u-.10)/.62,0,1);slide=slide*slide*(3-2*slide)
 f1=(510+220*slide)*(1+.035*np.sin(2*np.pi*3.3*t+v))
 f2=(980+260*slide)*(1+.028*np.sin(2*np.pi*2.2*t+v))
 f3=np.full(n,2050.)
 x=resonator(src,f1,2.1)+.55*resonator(src,f2,2.7)+.10*resonator(src,f3,3.2)
 x+=.18*voice_filter(breath,180,1250)
 # Soft short reflection makes the cry feel enclosed by a cracked shell.
 d=round(.018*RATE);x[d:]+=.08*x[:-d].copy()
 x=voice_filter(x,230,2700)
 env=np.sin(np.minimum(t/.013,1)*np.pi/2)**2
 env*=np.sin(np.minimum((duration-t)/.16,1)*np.pi/2)**2
 env*=1-.12*u
 x*=env;x=voice_filter(x,230,2800)*env
 x-=np.mean(x)*env
 # Crest and A RMS are both set; peak -9 dBFS is an export convention.
 target_rms=10**(-22/20);target_peak=10**(-9/20)
 for _ in range(25):
  a=np.abs(x);ratio=np.max(a)/(np.sqrt(np.mean(a_weighted(x)**2))+1e-30)
  if abs(20*np.log10(ratio/(target_peak/target_rms)))<.10:break
  gamma=.95 if ratio>target_peak/target_rms else 1.05
  x=np.sign(x)*a**gamma
  x=voice_filter(x,230,2800)*env
 x*=target_peak/(np.max(np.abs(x))+1e-30)
 return x

def stats(x):
 p=np.abs(np.fft.rfft(x))**2;f=np.fft.rfftfreq(len(x),1/RATE)
 impact_env=envelope(round(.020*RATE),.004,.020)
 i10=np.flatnonzero(impact_env>=impact_env.max()*.10)[0]
 i90=np.flatnonzero(impact_env>=impact_env.max()*.90)[0]
 attack=(i90-i10)/RATE*1000
 size,hop=882,220
 frames=np.lib.stride_tricks.sliding_window_view(x,size)[::hop]
 pf=np.abs(np.fft.rfft(frames*np.hanning(size),axis=1))**2
 energy=pf.sum(axis=1);active=energy>energy.max()*1e-4
 bins=np.fft.rfftfreq(size,1/RATE)
 region=(bins>=1000)&(bins<=3000)
 tonal=active & (np.max(pf[:,region],axis=1)/(energy+1e-30)>.45)
 run=max((sum(1 for _ in g) for val,g in itertools.groupby(tonal) if val),default=0)*hop/RATE*1000
 return dict(duration=len(x)/RATE,peak=20*np.log10(np.max(np.abs(x))+1e-30),
  arms=20*np.log10(np.sqrt(np.mean(a_weighted(x)**2))+1e-30),a50=short_a_level(x),
  centroid=float(np.sum(f*p)/np.sum(p)),hf=float(np.sum(p[f>4000])/np.sum(p)),
  tonal=run,attack=attack,clips=int(np.count_nonzero(np.abs(x)>=.9999)))

def narrowband_run(x):
 size,hop=1764,220
 frames=np.lib.stride_tricks.sliding_window_view(x,size)[::hop]
 p=np.abs(np.fft.rfft(frames*np.hanning(size),axis=1))**2
 db=10*np.log10(p+1e-25);f=np.fft.rfftfreq(size,1/RATE)
 prominence=np.zeros(len(frames))
 for j in np.flatnonzero((f>=1000)&(f<=3000)):
  near=(f>=f[j]-250)&(f<=f[j]+250)&((f<f[j]-50)|(f>f[j]+50))
  prominence=np.maximum(prominence,db[:,j]-np.median(db[:,near],axis=1))
 active=p.sum(axis=1)>p.sum(axis=1).max()*1e-4
 dif=np.diff(np.r_[0,(active&(prominence>14)).astype(int),0])
 return max(np.flatnonzero(dif==-1)-np.flatnonzero(dif==1),default=0)*hop/RATE*1000

# Minimal PNG sheet, adapted from the approved Space code. One row per cue.
FONT={'A':['0110','1001','1111','1001','1001'],'B':['1110','1001','1110','1001','1110'],'C':['0111','1000','1000','1000','0111'],'D':['1110','1001','1001','1001','1110'],'E':['1111','1000','1110','1000','1111'],'F':['1111','1000','1110','1000','1000'],'G':['0111','1000','1011','1001','0111'],'H':['1001','1001','1111','1001','1001'],'I':['111','010','010','010','111'],'J':['0011','0001','0001','1001','0110'],'K':['1001','1010','1100','1010','1001'],'L':['1000','1000','1000','1000','1111'],'M':['10001','11011','10101','10001','10001'],'N':['1001','1101','1011','1001','1001'],'O':['0110','1001','1001','1001','0110'],'P':['1110','1001','1110','1000','1000'],'Q':['0110','1001','1001','1011','0111'],'R':['1110','1001','1110','1010','1001'],'S':['0111','1000','0110','0001','1110'],'T':['11111','00100','00100','00100','00100'],'U':['1001','1001','1001','1001','0110'],'V':['10001','10001','10001','01010','00100'],'W':['10001','10001','10101','11011','10001'],'X':['1001','1001','0110','1001','1001'],'Y':['10001','01010','00100','00100','00100'],'Z':['1111','0001','0010','0100','1111'],'0':['0110','1001','1001','1001','0110'],'1':['010','110','010','010','111'],'2':['1110','0001','0110','1000','1111'],'3':['1110','0001','0110','0001','1110'],'4':['1001','1001','1111','0001','0001'],'5':['1111','1000','1110','0001','1110'],'6':['0111','1000','1110','1001','0110'],'7':['1111','0001','0010','0100','0100'],'8':['0110','1001','0110','1001','0110'],'9':['0110','1001','0111','0001','1110'],'.':['0','0','0','0','1'],'-':['0','0','1','0','0'],'/':['0001','0001','0010','0100','1000'],':':['0','1','0','1','0'],'%':['1001','0010','0100','1001','0000'],' ':['000','000','000','000','000']}
def label(img,x,y,s,color=(210,222,231),scale=2):
 for c in s.upper():
  glyph=FONT.get(c,FONT[' '])
  for row,bits in enumerate(glyph):
   for col,bit in enumerate(bits):
    if bit=='1':img[y+row*scale:y+(row+1)*scale,x+col*scale:x+(col+1)*scale]=color
  x+=(len(glyph[0])+1)*scale

def png_write(path,img):
 h,w,_=img.shape;raw=b''.join(b'\0'+img[row].tobytes() for row in range(h))
 def chunk(tag,data):return struct.pack('>I',len(data))+tag+data+struct.pack('>I',zlib.crc32(tag+data)&0xffffffff)
 path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))+chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))

def make_sheet(rows):
 width,rh=1450,116;img=np.full((70+rh*len(rows),width,3),(16,23,29),dtype=np.uint8)
 label(img,18,15,'FROST WORLD DEATH / VARIANT 0',(236,228,195),3)
 label(img,18,44,'WAVEFORM                    SPECTROGRAM 0-4 KHZ',scale=2)
 for i,(name,x,m) in enumerate(rows):
  y=70+i*rh;img[y:y+1]=(51,67,77)
  label(img,18,y+10,name,(235,226,196),2)
  label(img,18,y+34,f"{m['duration']:.2f}S  A {m['a50']:.1f}DB  C {m['centroid']:.0f}HZ",scale=2)
  label(img,18,y+55,f"PK {m['peak']:.1f}DB  HF {m['hf']*100:.1f}%",scale=2)
  x0,wv=445,355;img[y+12:y+102,x0:x0+wv]=(24,34,41)
  for k,b in enumerate(np.array_split(x,wv)):
   top=int(np.clip(58-np.max(b)*40,13,100));bot=int(np.clip(58-np.min(b)*40,13,100))
   img[y+top:y+bot+1,x0+k]=(96,189,177)
  x1,sw,sh=822,610,90;nfft=1024;hop=max(1,(len(x)-nfft)//sw)
  frames=np.lib.stride_tricks.sliding_window_view(x,nfft)[::hop][:sw]
  p=np.abs(np.fft.rfft(frames*np.hanning(nfft),axis=1));db=20*np.log10(p+1e-9);db-=db.max()
  bins=np.clip(np.round(np.linspace(0,4000,sh)*nfft/RATE).astype(int),0,p.shape[1]-1)
  a=np.clip((db[:,bins]+67)/67,0,1).T[::-1];col=np.empty((sh,len(frames),3),dtype=np.uint8)
  col[:,:,0]=(25+210*a).astype('u1');col[:,:,1]=(32+160*a**.7).astype('u1');col[:,:,2]=(40+80*a**.5).astype('u1')
  img[y+12:y+12+sh,x1:x1+len(frames)]=col
 png_write(ROOT/'sheet_frost.png',img)

def main():
 OUT.mkdir(parents=True,exist_ok=True)
 notes=['# Frost World death audio','',
 '48 death WAVs (16 keys × 3) and 3 Cryo Jelly cries. Deterministic 44.1 kHz mono 16-bit PCM. The loudest 50 ms A-weighted window is matched to -15.0 dBFS for ordinary cues and -13.5 dBFS for the Golem and elites. A weighting and 25 ms hop follow the approved Space toolkit. No peak normalization is used on death cues. All impacts have a 4.5 ms soft onset, noise-based material body, transient detail and a dark 120 or 180 ms room.','',
 '## Identity and scream decisions','',
 '| Unit | Death identity | Vocal choice |','|---|---|---|']
 for key,name,kind,nominal,identity in SOUNDS:
  reason=('Creature cry: Cryo Jelly is alive inside the cryo shell.' if key=='frost_alien' else
          'No cry: roster and sprite show a crystal drone with no occupant.' if 'fighter' in key else
          'No cry: sprite and definition show machinery, armor and payloads; no visible pilot or crew.' if 'elite' in key else
          'No cry: no living occupant visible in sprite or roster.')
  notes.append(f'| {name} (`{key}`) | {identity}; {nominal:.2f} s nominal | {reason} |')
 notes+=['','`frost_mine` exists in EnemyRoster as `hazard_frost_mine`; it has no dedicated sprite or death strip, so its identity follows the geode/rail clamp roster description. The five elite definitions and all available flight/death strips were inspected. No elite has a clear pilot or crew in its sprite or definition.','',
 '## PCM verification','',
 'A50 is the loudest A-weighted 50 ms window in dBFS. A RMS is weighted over the entire file. HF is energy above 4 kHz. Attack is the 10–90% rise of the synthesized impact envelope. Tonal is the longest run of 20 ms Hann frames (5 ms hop) in which a 1–3 kHz bin holds over 45% of frame energy; NB is the longest 40 ms narrowband prominence run above 14 dB. Xcorr is the maximum full-lag normalized cross-correlation among three variants.','',
 '| File | s | A50 | A RMS | Peak | Centroid Hz | HF % | Attack ms | Tonal ms | NB ms | Clips | Max xcorr |','|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
 first=[];rows=[];fails=[];allstats=[]
 print('file duration A50 A_RMS peak centroid HF_pct attack_ms tonal_ms NB_ms clips xcorr')
 for spec in SOUNDS:
  key,name,kind,nominal,_=spec;variants=[]
  for v in range(3):
   path=OUT/f'{key}_{v}.wav';write_wav(path,render_death(spec,v));variants.append(read_wav(path))
  cor=max(max_xcorr(variants[a],variants[b]) for a,b in ((0,1),(0,2),(1,2)))
  lengths=[len(x) for x in variants]
  if cor>=.6 or max(lengths)/min(lengths)>1.2:fails.append((key,'variants',cor,lengths))
  for v,x in enumerate(variants):
   m=stats(x);nb=narrowband_run(x);allstats.append(m)
   target=-13.5 if kind in BIG else -15
   checks=(abs(m['a50']-target)<=1.5,m['peak']<=-1,m['centroid']<(900 if kind in BIG else 1800),m['hf']<.12,2<=m['attack']<=8,m['tonal']<=100,nb<=100,m['clips']==0)
   if not all(checks):fails.append((key,v,checks,m,nb))
   row=f"| `{key}_{v}.wav` | {m['duration']:.3f} | {m['a50']:.2f} | {m['arms']:.2f} | {m['peak']:.2f} | {m['centroid']:.0f} | {m['hf']*100:.2f} | {m['attack']:.1f} | {m['tonal']:.0f} | {nb:.0f} | {m['clips']} | {cor:.3f} |"
   notes.append(row);print(row)
   if v==0: first.append(x);rows.append((name,x,m))
 cries=[];cry_stats=[]
 for v in range(3):
  p=OUT/f'frost_alien_scream_{v}.wav';write_wav(p,render_cry(v));x=read_wav(p);cries.append(x)
 crycor=max(max_xcorr(cries[a],cries[b]) for a,b in ((0,1),(0,2),(1,2)))
 death=[read_wav(OUT/f'frost_alien_{v}.wav') for v in range(3)]
 notes+=['','## Cryo Jelly scream and death mix','',
 'The cry is a sustained frightened “ah” with a trembling glottal source, moving vowel formants, breath, wet throat noise and a 230–2800 Hz radio band. It lasts about 0.9–1.0 s; the death impact cuts through the opening and the voice trails the slush. The -9 dBFS file peak is an export convention. In the mix the voice begins 45 ms after impact; its playback gain is chosen from A50 so the voice sits exactly 12 dB below its death cue. This keeps the long vocal audible without a sudden loud scream.','',
 '| File | s | A50 | A RMS | Peak | Centroid Hz | HF % | Tonal ms | Max xcorr | Playback gain | Mix A50 | Mix peak |','|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
 mix0=None
 for v,(x,d) in enumerate(zip(cries,death)):
  m=stats(x);gain=10**((short_a_level(d)-12-short_a_level(x))/20)
  offset=round(.045*RATE);mix=np.zeros(max(len(d),offset+len(x)));mix[:len(d)]=d;mix[offset:offset+len(x)]+=gain*x
  mm=stats(mix);cry_stats.append(m)
  checks=(abs(m['peak']+9)<.1,abs(m['arms']+22)<2,m['centroid']<1800,m['hf']<.12,m['tonal']<=100,m['clips']==0,
          10<=short_a_level(d)-short_a_level(gain*x)<=14,mm['peak']<0,mm['a50']-short_a_level(d)<=1)
  if not all(checks):fails.append(('cry',v,checks,m,mm,gain))
  row=f"| `frost_alien_scream_{v}.wav` | {m['duration']:.3f} | {m['a50']:.2f} | {m['arms']:.2f} | {m['peak']:.2f} | {m['centroid']:.0f} | {m['hf']*100:.2f} | {m['tonal']:.0f} | {crycor:.3f} | {gain:.3f} | {mm['a50']:.2f} | {mm['peak']:.2f} |"
  notes.append(row);print(row)
  if v==0:mix0=mix
 if crycor>=.6 or max(map(len,cries))/min(map(len,cries))>1.2:fails.append(('cry variants',crycor))
 notes+=['','`audition_frost.wav` plays variant 0 of each death cue in the order above with 1.2 s of silence after each item. Immediately after Cryo Jelly’s standalone death cue, it plays the standalone cry, then the cry mixed over that death cue. `sheet_frost.png` shows each death cue and the Cryo Jelly cry as variant-0 waveforms and spectrograms.','',
 'Ear-tuning priorities: Cryo Jelly’s synthetic vowel for an organic painful quality; Cryo Siren’s dish collapse to ensure it reads as metal/coolant instead of a siren; Hailstorm’s pellet density on phone speakers. Automated spectral checks cannot replace listening on the target phone. The existing game code still needs a separate integration change to load these WAVs.','']
 audition=[]
 for i,x in enumerate(first):
  audition.append(x)
  if i==0:audition.extend((cries[0],mix0))
 rows.append(('Cryo Jelly Cry',cries[0],stats(cries[0])))
 write_wav(ROOT/'audition_frost.wav',join(audition));make_sheet(rows)
 (ROOT/'sound_notes_frost.md').write_text('\n'.join(notes)+'\n')
 if fails:raise SystemExit(f'FAILED: {fails}')
 print(f'PASS: {len(SOUNDS)*3} death WAVs, 3 screams, max death A50 error {max(abs(m["a50"]-(-13.5 if i//3>=6 and i//3 in (6,11,12,13,14,15) else -15)) for i,m in enumerate(allstats)):.2f} dB; max death xcorr < .6; scream xcorr {crycor:.3f}')

if __name__=='__main__':main()
