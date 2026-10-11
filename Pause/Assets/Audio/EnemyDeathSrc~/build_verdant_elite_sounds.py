#!/usr/bin/env python3
"""Build the four new Verdant elite deaths, three variants each.

Run from anywhere with Python, NumPy and the existing local synthesis toolkit.
All times below are seconds. Writes only new Verdant elite WAVs and new source
artifacts; the approved boss/gate material is intentionally left untouched.
"""
from __future__ import annotations

import itertools
import zlib
from pathlib import Path

import numpy as np

import build_verdant_ember_sounds as b

ROOT = Path(__file__).resolve().parent
OUT = ROOT.parent / 'Resources' / 'Audio' / 'EnemyDeath'
RATE = b.RATE
PEAK_DB, RMS_DB = -3.0, -17.0
ORDER = [
    ('verdant_elite_timber_hauler', 1.00, 'Timber Hauler',
     'Heavy wooden and iron rupture; splintering hull, hydraulic vent, rolling logs and a brief brass groan', 900),
    ('verdant_elite_thornlash', .80, 'Thornlash',
     'Wet vine fibre tears and snaps; small brass joints pop beneath a thorn rattle', 1800),
    ('verdant_elite_sporebloom', .90, 'Sporebloom',
     'Soft wet bloom burst; hollow pod pop, petal shutters and falling pollen', 1800),
    ('verdant_elite_leafblade', .70, 'Leafblade',
     'Fast slicing crack; dull serrated brass fragments, short air rush and thorax crunch', 1800),
]


def noise(x, rng, at, dur, lo, hi, amp, decay, wobble=0):
    b.add_noise(x, rng, at, dur, lo, hi, amp, decay, attack=.004, wobble=wobble)


def grains(x, rng, start, end, count, lo, hi, amp, duration=(.009, .027)):
    for _ in range(count):
        at = rng.uniform(start, end)
        d = rng.uniform(*duration)
        noise(x, rng, at, d, lo, hi, amp*rng.uniform(.55, 1.15), d*.48)


def render(key, duration, variant):
    rng = np.random.default_rng(zlib.crc32(key.encode()) + 1000003*variant + 261019)
    length = duration * (1, .965, 1.04)[variant]
    x = np.zeros(round(length*RATE))
    # All cues start with a soft-edged, noisy low impact. The existing thump
    # helper supplies a masked, 75 ms descending 150-to-45 Hz body.
    if key.endswith('timber_hauler'):
        noise(x,rng,0,.29,35,790,.62,.13,7)           # hull/body crunch
        b.add_thump(x,rng,.008,1.20,True)
        grains(x,rng,.035,.27,19+variant*3,140,1450,.11)  # splintered timber
        noise(x,rng,.105,.28,75,1100,.32,.115,11)      # iron flex
        noise(x,rng,.17,.26,95,1450,.21,.090,13)       # hydraulic vent
        for at in (.078,.25): b.add_modal(x,rng,at,.052,220) # masked brass
        for at,weight in ((.29,.63),(.40,.47),(.53,.34)):
            b.add_thump(x,rng,at+rng.uniform(-.013,.013),weight,True)
            noise(x,rng,at,.11,45,660,.20,.040)        # falling logs
        grains(x,rng,.28,.72,14,100,980,.068)
        noise(x,rng,.39,.52,40,620,.19,.23,6)         # low settling tail
        room=.18; cutoff=2400
    elif key.endswith('thornlash'):
        b.add_noise(x,rng,0,.25,55,1080,.51,.086,attack=.007,wobble=19) # fibrous tear
        b.add_thump(x,rng,.009,.78)
        for at in (.056,.105,.169):
            noise(x,rng,at,.091,100,1560,.21,.032,27) # vine snaps
        noise(x,rng,.12,.28,75,880,.26,.10,13)        # wet inner body
        for at in (.12,.23): b.add_modal(x,rng,at,.036,265)
        grains(x,rng,.16,.56,22+variant*3,210,1700,.072,(.006,.018))
        noise(x,rng,.34,.35,85,790,.17,.14,18)
        room=.12; cutoff=2750
    elif key.endswith('sporebloom'):
        noise(x,rng,0,.31,45,760,.47,.12,14)          # wet bloom
        b.add_thump(x,rng,.012,.69)
        noise(x,rng,.073,.12,40,450,.31,.048,12)      # hollow pod pop
        b.add_thump(x,rng,.080,.38)
        for at in (.13,.205,.279):
            noise(x,rng,at,.12,100,920,.20,.047,19)   # fleshy petal shutters
        b.wet_bubbles(x,rng,.13,.42,11+variant*2,.070)
        noise(x,rng,.24,.48,190,1600,.105,.21,9)      # pollen hiss, dark
        grains(x,rng,.27,.67,12,180,1200,.040)
        noise(x,rng,.53,.27,55,500,.10,.12,8)
        room=.15; cutoff=2650
    elif key.endswith('leafblade'):
        noise(x,rng,0,.13,75,1770,.61,.036)           # slicing crack
        b.add_thump(x,rng,.008,.66)
        noise(x,rng,.027,.19,130,1650,.28,.063,17)   # quick air displacement
        for at in (.067,.112): b.add_modal(x,rng,at,.042,300)
        grains(x,rng,.065,.32,19+variant*3,260,1900,.065,(.005,.016))
        noise(x,rng,.145,.17,55,760,.34,.052,15)     # thorax crunch
        b.add_thump(x,rng,.151,.36)
        noise(x,rng,.27,.31,85,850,.13,.12,9)
        room=.10; cutoff=2900
    else:
        raise ValueError(key)
    # Compact FFT-convolution ambience, dark final filter and a 20 ms exit.
    x = b.short_room(x,rng,room,.044)
    x = b.fft_filter(x,27,cutoff)
    n=len(x); i=np.arange(n)
    edge=np.sin(np.minimum(i/(RATE*.005),1)*np.pi/2)**2
    edge*=np.sin(np.minimum(i[::-1]/(RATE*.020),1)*np.pi/2)**2
    x=(x-np.mean(x))*edge
    # A monotonic power transform adjusts crest without clipping or introducing
    # pitched content. This gives both the requested integrated RMS and peak.
    target=10**((PEAK_DB-RMS_DB)/20)
    def shaped(g):
        y=np.sign(x)*np.abs(x)**g
        y*=edge
        y-=np.mean(y)*edge
        return y
    lo,hi=.1,3.0
    for _ in range(36):
        mid=(lo+hi)/2; y=shaped(mid)
        crest=np.max(np.abs(y))/(np.sqrt(np.mean(y*y))+1e-30)
        if crest<target: lo=mid
        else: hi=mid
    y=shaped((lo+hi)/2)
    y*=10**(PEAK_DB/20)/(np.max(np.abs(y))+1e-30)
    return y


def stats(x):
    power=np.abs(np.fft.rfft(x))**2
    freq=np.fft.rfftfreq(len(x),1/RATE)
    size,hop=882,220
    frames=np.lib.stride_tricks.sliding_window_view(x,size)[::hop]
    p=np.abs(np.fft.rfft(frames*np.hanning(size),axis=1))**2
    energy=p.sum(axis=1)
    tonal=(energy>energy.max()*1e-4)&(p.max(axis=1)/(energy+1e-30)>.45)
    run=max((sum(1 for _ in group) for yes,group in itertools.groupby(tonal) if yes),default=0)
    # Actual 10-90% RMS rise of the first 23 ms, 2 ms window/0.5 ms hop.
    onset=np.sqrt(np.mean(np.lib.stride_tricks.sliding_window_view(x[:1014],88)[::22]**2,axis=1))
    top=np.percentile(onset[8:20],80)
    rise=np.maximum.accumulate(onset[:21])
    above10=np.flatnonzero(rise>=top*.1)
    above90=np.flatnonzero(rise>=top*.9)
    attack=(above90[0]-above10[0])*.5 if len(above10) and len(above90) else 0
    return dict(duration=len(x)/RATE,
                peak=20*np.log10(np.max(np.abs(x))+1e-30),
                rms=20*np.log10(np.sqrt(np.mean(x*x))+1e-30),
                centroid=float(np.sum(freq*power)/(np.sum(power)+1e-30)),
                hf=float(np.sum(power[freq>4000])/(np.sum(power)+1e-30)),
                attack=attack, tonal=run*hop/RATE*1000,
                clips=int(np.count_nonzero(np.abs(x)>=.9999)))


def sheet(rows):
    width,rh=1440,122
    img=np.full((70+rh*len(rows),width,3),(16,23,29),dtype=np.uint8)
    b.label(img,18,15,'VERDANT ELITE DEATHS / VARIANT 0',(236,228,195),3)
    b.label(img,18,44,'WAVEFORM                              SPECTROGRAM 0-4 KHZ',scale=2)
    for i,(key,x,m) in enumerate(rows):
        y=70+i*rh; img[y:y+1]=(51,67,77)
        b.label(img,18,y+7,key,(235,226,196),2)
        b.label(img,18,y+32,f"{m['duration']:.2f}S  PK {m['peak']:.1f}DB  RMS {m['rms']:.1f}DB",scale=2)
        b.label(img,18,y+55,f"C {m['centroid']:.0f}HZ  HF {m['hf']*100:.2f}%",scale=2)
        x0,wv=450,340; img[y+14:y+106,x0:x0+wv]=(24,34,41)
        for j,part in enumerate(np.array_split(x,wv)):
            a=int(np.clip(60-np.max(part)*47,14,105))
            c=int(np.clip(60-np.min(part)*47,14,105))
            img[y+a:y+c+1,x0+j]=(96,189,177)
        x1,sw,sh=812,610,92; nfft=1024
        hop=max(1,(len(x)-nfft)//sw)
        frames=np.lib.stride_tricks.sliding_window_view(x,nfft)[::hop][:sw]
        mag=np.abs(np.fft.rfft(frames*np.hanning(nfft),axis=1))
        db=20*np.log10(mag+1e-9); db-=db.max()
        bins=np.clip(np.round(np.linspace(0,4000,sh)*nfft/RATE).astype(int),0,mag.shape[1]-1)
        a=np.clip((db[:,bins]+67)/67,0,1).T[::-1]
        col=np.empty((sh,len(frames),3),dtype=np.uint8)
        col[:,:,0]=(25+210*a).astype('u1')
        col[:,:,1]=(32+160*a**.7).astype('u1')
        col[:,:,2]=(40+80*a**.5).astype('u1')
        img[y+14:y+14+sh,x1:x1+len(frames)]=col
    b.png_write(ROOT/'sheet_verdant_elites.png',img)


def main():
    generated={}; rows=[]; failures=[]
    notes=['# New Verdant elite death cues','',
           'Run `python3 build_verdant_elite_sounds.py` to rebuild deterministically. Uses NumPy, Python standard library and the existing Verdant/Ember synthesis helpers. Mono 44.1 kHz, 16-bit PCM. All measurements below are taken from delivered PCM. No new scream layers are made.','',
           'The brief names four elites, yielding 12 files. Existing `boss_gate` source artifacts belong to another completed sound set and are left untouched; the requested 15-family/45-file counts cannot describe these four named elites.','',
           '## Recipes','',
           '| Key | Evokes | Main layers | Length | Peak | RMS |',
           '|---|---|---|---:|---:|---:|']
    layers={
        'verdant_elite_timber_hauler':'35-790 Hz timber crunch, masked 75 ms sub thump, 19-25 splinters, hydraulic noise, two 58 ms modal brass fragments, three falling-log thumps, 180 ms room',
        'verdant_elite_thornlash':'55-1080 Hz tearing noise, masked thump, three vine snaps, wet body, two short brass modes, 22-28 thorn grains, 120 ms room',
        'verdant_elite_sporebloom':'45-760 Hz bloom, low thump and pod pop, three petal slaps, 11-15 wet noise bubbles, low pollen hiss, 150 ms room',
        'verdant_elite_leafblade':'75-1770 Hz slicing crack, masked thump, 130-1650 Hz air rush, two brief brass modes, 19-25 shrapnel grains, thorax crunch, 100 ms room',
    }
    for key,duration,name,identity,limit in ORDER:
        notes.append(f'| `{key}` | {name}: {identity} | {layers[key]}; 4 ms layer attacks, 20 ms exit fade | {duration:.2f} s | {PEAK_DB:.0f} dBFS | {RMS_DB:.0f} dBFS |')
        for variant in range(3):
            path=OUT/f'{key}_{variant}.wav'
            b.write_wav(path,render(key,duration,variant))
            x=b.read_wav(path); m=stats(x)
            generated[key,variant]=(x,m)
            if variant==0: rows.append((key,x,m))
    notes+=['','## Delivered PCM verification','',
            '| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Max pair xcorr |',
            '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    print('File                                    s    peak     RMS  centroid    HF%  attack  tonal  clips  xcorr')
    for key,duration,_,_,limit in ORDER:
        sounds=[generated[key,v][0] for v in range(3)]
        corr=max(b.max_xcorr(sounds[a],sounds[c]) for a,c in itertools.combinations(range(3),2))
        for v in range(3):
            x,m=generated[key,v]; name=f'{key}_{v}.wav'
            good=(abs(m['duration']/duration-1)<=.20 and abs(m['peak']-PEAK_DB)<.10 and
                  abs(m['rms']-RMS_DB)<.65 and m['centroid']<limit and m['hf']<.12 and
                  2<=m['attack']<=8 and m['tonal']<80 and m['clips']==0 and corr<.6)
            if not good: failures.append(name)
            print(f"{name:<40} {m['duration']:4.2f} {m['peak']:7.2f} {m['rms']:7.2f} {m['centroid']:9.0f} {m['hf']*100:6.2f} {m['attack']:7.1f} {m['tonal']:6.0f} {m['clips']:6d} {corr:6.3f}")
            notes.append(f"| `{name}` | {m['duration']:.3f} | {m['peak']:.2f} | {m['rms']:.2f} | {m['centroid']:.0f} | {m['hf']*100:.3f} | {m['attack']:.1f} | {m['tonal']:.0f} | {m['clips']} | {corr:.3f} |")
        if max(len(x) for x in sounds)/min(len(x) for x in sounds)>1.20:
            failures.append(f'{key}: variant lengths')
    if failures: raise SystemExit('Verification failed: '+', '.join(failures))
    gap=np.zeros(round(1.2*RATE))
    for suffix,sequence in (('',[(key,0) for key,*_ in ORDER]),
                            ('_all',[(key,v) for key,*_ in ORDER for v in range(3)])):
        parts=[]
        for i,item in enumerate(sequence):
            parts.append(generated[item][0])
            if i<len(sequence)-1: parts.append(gap)
        b.write_wav(ROOT/f'audition_verdant_elites{suffix}.wav',np.concatenate(parts))
    sheet(rows)
    notes+=['',f'**Result:** PASS: {len(generated)} death WAVs; all pair correlations below 0.6.','',
            'No listening monitor was available. The cues use dark filtering, low-level short brass modes masked by noisy impacts, restrained 2-5 kHz content, a 6 kHz shelf, short rooms and explicit centroid/HF/tonality limits. Check on a phone speaker before shipping.','']
    (ROOT/'sound_notes_verdant_elites.md').write_text('\n'.join(notes))
    print('PASS: 12 WAVs, two auditions, sheet and notes')


if __name__=='__main__': main()
