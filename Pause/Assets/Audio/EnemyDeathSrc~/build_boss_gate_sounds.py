#!/usr/bin/env python3
"""Build the 22 specified boss/gate WAVs, audition files, sheet and verification.

Deterministic, mono 44.1 kHz, 16-bit PCM. Requires NumPy only. Run from anywhere.
All times are seconds. Per-cue material recipes live in render_boss/render_gate.
"""
from __future__ import annotations

import itertools
import zlib
from pathlib import Path

import numpy as np

import build_verdant_ember_sounds as b

ROOT = Path(__file__).resolve().parent
DEATH = ROOT.parent / 'Resources' / 'Audio' / 'EnemyDeath'
GATE = ROOT.parent / 'Resources' / 'Audio' / 'Gate'
RATE = b.RATE
ORDER = [
    ('boss_space', 2.20, 'Void Archon reactor overload', 'sub swell, dull boom, plasma crackle, torn plates, falling pressure'),
    ('boss_frost', 2.40, 'Leviathan hull implosion', 'hull groan, burst, ice fissures, pressure hiss, frozen rubble'),
    ('boss_verdant', 2.20, 'Bloom Queen core rupture', 'wet tear, brass plate, seed burst, leaf and petal shower'),
    ('boss_ember', 2.40, 'Cinder Drake molten detonation', 'lava rumble, obsidian fracture, steam, falling rock'),
    ('boss_tide', 2.40, 'Iron Kraken underwater breach', 'deep pressure boom, trapped air, iron groan, water rush'),
    ('boss_frost_scream', 1.20, 'Leviathan breathy cry', 'noise formants, low hull groan, ice rasp, falling breath'),
    ('boss_verdant_scream', 1.20, 'Bloom Queen guttural roar', 'low noise formants, wet rasp, seed rattle, petal flutter'),
    ('gate_rattle', 1.00, 'Strained iron latch', 'low plate shiver, hydraulic strain, loose bolts, dark room'),
    ('gate_step', .50, 'Forced open lurch', 'deep clunk, steam burst, brass bend, recoil rattle'),
    ('gate_crack', .60, 'Broken gate pipe and seam', 'low metal fracture, pipe hiss, short falling shards'),
    ('gate_smash', 1.40, 'Gate torn apart', 'concussive blow, tearing plates, vented pipes, settling debris'),
]


def noise(x, rng, at, dur, lo, hi, amp, decay, wobble=0):
    b.add_noise(x, rng, at, dur, lo, hi, amp, decay, attack=.004, wobble=wobble)


def pulse(x, rng, at, amp=1, deep=True):
    # Existing helper keeps its descending sine under a louder, short noise thump.
    b.add_thump(x, rng, at, amp, deep)


def bubbles(x, rng, start, end, count, amp=.13):
    # Stochastic, short, low-passed pressure pops; no bright sine ping.
    for _ in range(count):
        at = rng.uniform(start, end)
        d = rng.uniform(.017, .055)
        noise(x, rng, at, d, 50, rng.uniform(310, 650), amp*rng.uniform(.45, 1.0), d*.46)


def scatter(x, rng, start, end, count, lo, hi, amp=.10):
    for _ in range(count):
        at = rng.uniform(start, end)
        d = rng.uniform(.008, .029)
        noise(x, rng, at, d, lo, hi, amp*rng.uniform(.4, 1.1), d*.42)


def build_boss(key, dur, rng, variant):
    x = np.zeros(round(dur * RATE))
    v = variant
    if key == 'boss_space':
        noise(x,rng,0,.22,35,330,.43,.12,11)
        pulse(x,rng,.14,.95)
        noise(x,rng,.14,1.20,45,1050,.73,.47,8)
        noise(x,rng,.24,.70,160,1800,.28,.25,21)
        scatter(x,rng,.23,1.15,34+v*5,110,1450,.13)
        bubbles(x,rng,.22,.90,12+v*2,.11)
        for at in (.21,.35,.52,.72): b.add_modal(x,rng,at,.065,260)
        noise(x,rng,.78,1.25,55,850,.26,.55,5)
    elif key == 'boss_frost':
        noise(x,rng,0,.27,45,430,.40,.19,7)
        pulse(x,rng,.20,1.10)
        noise(x,rng,.19,1.2,50,1000,.76,.49,7)
        noise(x,rng,.26,.48,140,1900,.31,.19)
        scatter(x,rng,.25,1.56,48+v*7,130,2200,.12)
        noise(x,rng,.55,1.18,360,1850,.18,.54)
        for at in (.25,.48,.77): b.add_modal(x,rng,at,.050,190)
        noise(x,rng,1.05,1.15,65,570,.24,.51,9)
    elif key == 'boss_verdant':
        noise(x,rng,0,.30,50,610,.38,.18,14)
        pulse(x,rng,.13,1.0)
        noise(x,rng,.13,.90,65,1050,.76,.35,19)
        bubbles(x,rng,.18,.83,22+v*3,.16)
        noise(x,rng,.38,.20,75,1100,.42,.07)
        for at in (.19,.31,.49): b.add_modal(x,rng,at,.057,225)
        scatter(x,rng,.36,1.68,56+v*5,130,1700,.092)
        noise(x,rng,.70,1.3,100,1150,.22,.60,17)
    elif key == 'boss_ember':
        noise(x,rng,0,.25,35,400,.46,.17,10)
        pulse(x,rng,.12,1.12)
        noise(x,rng,.12,1.24,40,940,.80,.54,12)
        bubbles(x,rng,.20,1.30,22+v*3,.15)
        scatter(x,rng,.30,1.7,50+v*6,80,1500,.12)
        noise(x,rng,.58,1.1,200,1850,.19,.45)
        noise(x,rng,1.00,1.2,45,570,.25,.58,7)
    elif key == 'boss_tide':
        noise(x,rng,0,.27,30,300,.46,.19,9)
        pulse(x,rng,.16,1.13)
        noise(x,rng,.16,1.35,35,750,.80,.55,6)
        bubbles(x,rng,.22,1.65,40+v*5,.13)
        for at in (.24,.53,.90): b.add_modal(x,rng,at,.058,180)
        noise(x,rng,.65,1.35,90,1180,.29,.70,11)
        scatter(x,rng,.50,1.82,25+v*4,80,1100,.075)
        noise(x,rng,1.83,.57,75,1050,.13,.35,13)  # mask narrow late sub tail
    else:
        raise ValueError(key)
    return x


def build_scream(key, dur, rng, variant):
    n=round(dur*RATE); t=np.arange(n)/RATE; u=t/dur
    # Moving formants sculpt noise, with irregular vibrato and no sustained oscillator.
    source=b.shaped_noise(rng,n,45,1150)
    wobble=b.smooth_random(rng,n,round(.019*RATE))
    wobble=np.clip(wobble,-2,2)
    center=(330 if key=='boss_frost_scream' else 245)*(1-.56*u)
    center*=1+.05*wobble+.018*np.sin(2*np.pi*(5.1+variant*.8)*t)
    q=1.3 if key=='boss_frost_scream' else 1.0
    form=b.resonator(source,center,q)
    form+=.70*b.resonator(source,center*1.73,1.15)
    form+=.28*b.resonator(source,center*2.42,1.0)
    form/=np.sqrt(np.mean(form*form))+1e-12
    x=.52*form
    noise(x,rng,0,dur,65,860,.34,.72,7 if key=='boss_frost_scream' else 16)
    if key=='boss_frost_scream':
        noise(x,rng,.05,.83,70,460,.30,.38,6)
        scatter(x,rng,.21,.97,22+variant*3,120,1550,.035)
        noise(x,rng,.57,.58,140,1250,.16,.30)
    else:
        noise(x,rng,.03,.80,90,1070,.34,.36,23)
        bubbles(x,rng,.14,.68,13+variant*2,.095)
        scatter(x,rng,.47,1.02,29+variant*4,150,1650,.048)
        noise(x,rng,.71,.43,170,1300,.13,.20,26)
    env=np.sin(np.minimum(t/.006,1)*np.pi/2)**2
    env*=np.sin(np.minimum((dur-t)/.024,1)*np.pi/2)**2
    env*=np.minimum(1,(1-u)*2.4)**.35
    return x*env


def build_gate(key,dur,rng,variant):
    x=np.zeros(round(dur*RATE));v=variant
    if key=='gate_rattle':
        noise(x,rng,0,dur,55,760,.34,.65,22)
        for i in range(21+v*4):
            at=.025+i*(.038 if v==0 else .032)+rng.uniform(-.012,.012)
            if at<dur-.03: noise(x,rng,at,.038,95,1450,.08+.18*at/dur,.016)
        scatter(x,rng,.10,.88,21+v*3,140,1900,.065)
        noise(x,rng,.38,.57,120,1100,.27,.35,8)
        pulse(x,rng,.72,.55)
    elif key=='gate_step':
        pulse(x,rng,0,.86)
        noise(x,rng,0,.28,50,1050,.56,.11)
        noise(x,rng,.03,.25,220,1900,.25,.090)
        b.add_modal(x,rng,.027,.065,235)
        scatter(x,rng,.11,.39,16+v*3,135,1750,.082)
        noise(x,rng,.12,.31,90,760,.20,.13,13)
    elif key=='gate_crack':
        pulse(x,rng,0,.68)
        noise(x,rng,0,.17,75,1300,.53,.064)
        noise(x,rng,.025,.29,280,2050,.28,.105)
        b.add_modal(x,rng,.035,.065,285)
        scatter(x,rng,.06,.48,27+v*4,220,2100,.07)
        noise(x,rng,.22,.30,70,630,.13,.12)
    elif key=='gate_smash':
        pulse(x,rng,0,1.15)
        noise(x,rng,0,.92,45,1150,.78,.33)
        noise(x,rng,.04,.57,130,1750,.32,.21)
        for at in (.08,.17,.30,.47): b.add_modal(x,rng,at,.060,215)
        scatter(x,rng,.06,1.15,62+v*7,110,1950,.11)
        noise(x,rng,.30,.80,190,1800,.23,.35)
        noise(x,rng,.59,.72,60,710,.22,.34,10)
    else: raise ValueError(key)
    return x


def finish(x,rng,key,target_rms,target_peak):
    # Short, dark convolution and final anti-beep band limit use existing helpers.
    x=b.short_room(x,rng,.18 if key.startswith('boss_') else .11,.043)
    x=b.fft_filter(x,27,2350 if key.startswith('boss_') else 2750)
    n=len(x);t=np.arange(n)/RATE
    edge=np.sin(np.minimum(t/.005,1)*np.pi/2)**2
    edge*=np.sin(np.minimum((n-1-np.arange(n))/(RATE*.020),1)*np.pi/2)**2
    x*=edge
    # Full-clip RMS and sample peak are both controlled. Power shaping changes
    # crest while preserving the sign and the natural time envelope.
    x-=np.mean(x)*edge
    ratio=10**((target_peak-target_rms)/20)
    def shaped(g):
        y=np.sign(x)*np.abs(x)**g
        y*=edge
        y-=np.mean(y)*edge
        return y
    low,high=.10,3.0
    for _ in range(34):
        mid=(low+high)/2;y=shaped(mid)
        crest=np.max(np.abs(y))/(np.sqrt(np.mean(y*y))+1e-30)
        if crest<ratio: low=mid
        else: high=mid
    y=shaped((low+high)/2)
    y*=10**(target_peak/20)/(np.max(np.abs(y))+1e-30)
    return y


def read_stats(x):
    p=np.abs(np.fft.rfft(x))**2; f=np.fft.rfftfreq(len(x),1/RATE)
    peak=20*np.log10(np.max(np.abs(x))+1e-30)
    rms=20*np.log10(np.sqrt(np.mean(x*x))+1e-30)
    size,hop=882,220
    frames=np.lib.stride_tricks.sliding_window_view(x,size)[::hop]
    power=np.abs(np.fft.rfft(frames*np.hanning(size),axis=1))**2
    energy=power.sum(axis=1)
    tonal=(energy>energy.max()*1e-4)&(power.max(axis=1)/(energy+1e-30)>.45)
    run=max((sum(1 for _ in group) for yes,group in itertools.groupby(tonal) if yes),default=0)
    # Measure the local first onset, before the deliberately delayed main boom:
    # 2 ms RMS windows / 0.5 ms hop, 10-90% of the 4-9 ms local plateau.
    onset=np.sqrt(np.mean(np.lib.stride_tricks.sliding_window_view(x[:1000],88)[::22]**2,axis=1))
    top=np.percentile(onset[8:19],80)
    rise=np.maximum.accumulate(onset[:21])
    above10=np.flatnonzero(rise>=top*.1);above90=np.flatnonzero(rise>=top*.9)
    attack=(above90[0]-above10[0])*.5 if len(above10) and len(above90) else 0
    return dict(duration=len(x)/RATE,peak=peak,rms=rms,
        centroid=float(np.sum(f*p)/(np.sum(p)+1e-30)),
        hf=float(np.sum(p[f>4000])/(np.sum(p)+1e-30)),attack=attack,
        tonal=run*hop/RATE*1000,clips=int(np.count_nonzero(np.abs(x)>=.9999)))


def sheet(rows):
    width,rh=1450,116
    img=np.full((70+rh*len(rows),width,3),(16,23,29),dtype=np.uint8)
    b.label(img,18,15,'BOSS AND GATE CUES / VARIANT 0',(236,228,195),3)
    b.label(img,18,44,'WAVEFORM                    SPECTROGRAM 0-4 KHZ',scale=2)
    for i,(key,x,m) in enumerate(rows):
        y=70+i*rh;img[y:y+1]=(51,67,77)
        b.label(img,18,y+8,key,(235,226,196),2)
        b.label(img,18,y+32,f"{m['duration']:.2f}S  PK {m['peak']:.1f}DB  RMS {m['rms']:.1f}DB",scale=2)
        b.label(img,18,y+53,f"C {m['centroid']:.0f}HZ  HF {m['hf']*100:.2f}%",scale=2)
        x0,wv=445,355;img[y+12:y+102,x0:x0+wv]=(24,34,41)
        for k,chunk in enumerate(np.array_split(x,wv)):
            top=int(np.clip(58-np.max(chunk)*45,13,100));bot=int(np.clip(58-np.min(chunk)*45,13,100))
            img[y+top:y+bot+1,x0+k]=(96,189,177)
        x1,sw,sh=822,610,90;nfft=1024;hop=max(1,(len(x)-nfft)//sw)
        frames=np.lib.stride_tricks.sliding_window_view(x,nfft)[::hop][:sw]
        p=np.abs(np.fft.rfft(frames*np.hanning(nfft),axis=1));db=20*np.log10(p+1e-9);db-=db.max()
        bins=np.clip(np.round(np.linspace(0,4000,sh)*nfft/RATE).astype(int),0,p.shape[1]-1)
        a=np.clip((db[:,bins]+67)/67,0,1).T[::-1]
        col=np.empty((sh,len(frames),3),dtype=np.uint8)
        col[:,:,0]=(25+210*a).astype('u1');col[:,:,1]=(32+160*a**.7).astype('u1');col[:,:,2]=(40+80*a**.5).astype('u1')
        img[y+12:y+12+sh,x1:x1+len(frames)]=col
    b.png_write(ROOT/'sheet_boss_gate.png',img)


def main():
    generated={}; rows=[]; notes=[
        '# Boss deaths and HapticGate sound design','',
        'Run `python3 build_boss_gate_sounds.py` to rebuild deterministically. Mono 44.1 kHz, 16-bit PCM; NumPy and Python standard library only. The existing Verdant/Ember toolkit supplies filtered noise, low thumps, modal plates, compact convolution, WAV and PNG utilities. All output audio is verified after PCM export.','',
        'The brief names 11 cue families and explicitly asks for variants 0 and 1, yielding 22 final WAVs. Its references to 15 variant-0 sounds, 45 files and three variants conflict with that list; the sheet contains all 11 specified variant-0 sounds and the full audition contains all 22 delivered files.','',
        '## Recipes and parameters','',
        '| Cue | Intended image | Layers | Duration target | RMS target | Peak target |','|---|---|---|---:|---:|---:|']
    for key,dur,identity,layers in ORDER:
        voice='_scream' in key;gate=key.startswith('gate_')
        target_rms=-20 if voice else -16 if gate else -15
        target_peak=-9 if voice else -3
        notes.append(f'| `{key}` | {identity} | {layers}; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | {dur:.2f} s | {target_rms} dBFS | {target_peak} dBFS |')
        for v in range(2):
            length=dur*([1,.965][v] if not voice else [1,1.04][v])
            rng=np.random.default_rng(zlib.crc32(key.encode())+v*1000003+18471)
            raw=build_scream(key,length,rng,v) if voice else build_gate(key,length,rng,v) if gate else build_boss(key,length,rng,v)
            x=finish(raw,rng,key,target_rms,target_peak)
            path=(GATE if gate else DEATH)/f'{key}_{v}.wav'
            b.write_wav(path,x)
            delivered=b.read_wav(path);m=read_stats(delivered)
            generated[key,v]=(delivered,m)
            if v==0: rows.append((key,delivered,m))
    notes+=['','## Delivered PCM verification','',
        '| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Pair xcorr |',
        '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    print('File                       s    peak    RMS   centroid   HF%  attack  tonal  clips  xcorr')
    failures=[]
    for key,dur,_,_ in ORDER:
        a,ma=generated[key,0];c,mc=generated[key,1]
        corr=b.max_xcorr(a,c)
        if corr>=.6:failures.append(f'{key}: xcorr {corr:.3f}')
        if abs(len(a)/len(c)-1)>.2:failures.append(f'{key}: length pair')
        for v,(x,m) in enumerate(((a,ma),(c,mc))):
            name=f'{key}_{v}.wav';voice='_scream' in key;gate=key.startswith('gate_')
            target_rms=-20 if voice else -16 if gate else -15
            target_peak=-9 if voice else -3
            good=(abs(m['duration']/dur-1)<=.20 and abs(m['peak']-target_peak)<.10 and
                  abs(m['rms']-target_rms)<.65 and m['centroid']<(1800 if voice or gate else 900) and
                  m['hf']<.12 and 2<=m['attack']<=8 and m['tonal']<80 and m['clips']==0)
            if not good: failures.append(name)
            print(f"{name:<27} {m['duration']:4.2f} {m['peak']:7.2f} {m['rms']:7.2f} {m['centroid']:9.0f} {m['hf']*100:6.2f} {m['attack']:7.1f} {m['tonal']:6.0f} {m['clips']:6d} {corr:6.3f}")
            notes.append(f"| `{name}` | {m['duration']:.3f} | {m['peak']:.2f} | {m['rms']:.2f} | {m['centroid']:.0f} | {m['hf']*100:.3f} | {m['attack']:.1f} | {m['tonal']:.0f} | {m['clips']} | {corr:.3f} |")
    if failures: raise SystemExit('Verification failed: '+', '.join(failures))
    gap=np.zeros(round(1.2*RATE))
    for suffix,sequence in (('',[(key,0) for key,*_ in ORDER]),('_all',[(key,v) for key,*_ in ORDER for v in range(2)])):
        parts=[]
        for i,item in enumerate(sequence):
            parts.append(generated[item][0])
            if i<len(sequence)-1:parts.append(gap)
        b.write_wav(ROOT/f'audition_boss_gate{suffix}.wav',np.concatenate(parts))
    sheet(rows)
    notes+=['',f'**Result:** PASS: {len(generated)} delivered WAVs; 11 pair correlations under 0.6.','',
        'No listening monitor was available. Dark filtering, short masked low thumps, low-level modal fragments, bounded high-frequency share, spectral centroid and tonality checks reduce the risk of beeps and piercing treble. Check on a phone speaker before shipping.','']
    (ROOT/'sound_notes_boss_gate.md').write_text('\n'.join(notes))
    print('PASS: 22 WAVs, two auditions, sheet and notes')


if __name__=='__main__': main()
