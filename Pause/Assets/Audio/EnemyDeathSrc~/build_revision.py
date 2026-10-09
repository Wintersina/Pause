#!/usr/bin/env python3
"""Selective Verdant/Ember revision. Deterministic NumPy + wave PCM only.

Run beside the original builder. Its synthesis helpers are imported read-only;
this script writes only the selected existing WAV names and its own reports.
"""
from __future__ import annotations

import zlib
from pathlib import Path

import numpy as np

import build_verdant_ember_sounds as b

ROOT = Path(__file__).resolve().parent
OUT = b.OUT
RATE = b.RATE
VOICE_LENGTH = {
    'verdant_fighter_1': .84, 'verdant_fighter_2': .88,
    'verdant_fighter_3': .93, 'verdant_fighter_4': 1.00,
    'verdant_alien': .91, 'verdant_elite_resin_warden': 1.20,
    'ember_fighter_1': .82, 'ember_fighter_2': .89,
    'ember_fighter_3': .91, 'ember_fighter_4': .99,
    'ember_alien': .91, 'ember_elite_ash_wraith': 1.08,
    'ember_elite_brass_vulture': 1.16,
    'ember_elite_coalrunner': 1.03, 'ember_elite_sunstoke': 1.04,
}
CHARACTER_DEATHS = {
    'verdant_fighter_4', 'ember_elite_brass_vulture',
    'ember_fighter_1', 'ember_fighter_2', 'ember_fighter_3',
    'ember_fighter_4',
}
HOT = {
    'ember_elite_cauterizer': (0, 1, 2),
    'ember_elite_kilnback': (0, 1, 2),
    'ember_big': (1,), 'verdant_big': (0,),
    'verdant_elite_resin_warden': (0, 1),
}


def pcm(x):
    return np.round(np.clip(x, -1, 1) * 32767).astype('<i2').astype(float) / 32768


def full_a(x):
    y = b.a_weighted(x)
    return 20 * np.log10(np.sqrt(np.mean(y*y)) + 1e-30)


def db(x):
    return 20 * np.log10(max(x, 1e-30))


def add_character(x, key, variant):
    """Noise-led moving material sounds, masked under the existing blast."""
    rng = np.random.default_rng(zlib.crc32(key.encode()) + variant * 1000003 + 711909)
    layer = np.zeros_like(x)
    n = len(x)
    if key == 'verdant_fighter_4':
        # Three sub-90 ms descending wing pulses, a shell break and wet landing.
        for j in range(3):
            start = .047 + j * .075 + rng.uniform(-.008, .008)
            dur = .077 - j * .008
            i = round(start * RATE); size = min(round(dur * RATE), n-i)
            t = np.arange(size) / RATE
            wing = b.shaped_noise(rng, size, 75, 950 - j*190)
            wing *= (.52 + .48*np.sin(2*np.pi*(45-j*6)*t)**2)
            wing *= np.sin(np.pi*np.arange(size)/size)**2
            layer[i:i+size] += (.25-j*.042)*wing
        b.add_noise(layer,rng,.119,.091,120,1750,.30,.032,attack=.002)
        b.add_noise(layer,rng,.19,.19,55,540,.21,.082,wobble=19)
        b.add_thump(layer,rng,.276,.46,True)
        b.add_noise(layer,rng,.276,.095,40,540,.16,.039,wobble=24)
    elif key == 'ember_elite_brass_vulture':
        # Heavy wing flaps spaced across the blast; breath/gasp remains low.
        for j in range(3):
            at = .045 + j*.112 + rng.uniform(-.007,.007)
            b.add_thump(layer,rng,at,.63-j*.12,True)
            b.add_noise(layer,rng,at,.087,55,670,.31-j*.045,.036,wobble=17)
        b.add_noise(layer,rng,.145,.23,180,1550,.27,.088,wobble=23)
        b.add_noise(layer,rng,.23,.31,85,850,.18,.12,wobble=11)
        b.add_noise(layer,rng,.31,.21,70,620,.12,.075,wobble=29)
        b.add_grains(layer,rng,.25,.62,14+variant*2,'metal',.072)
    else:
        # Fighter craft retain their original blast and receive a moving servo
        # seizure/tearing plate band, distinct from static shrapnel.
        for j in range(4):
            at = .045+j*.042+rng.uniform(-.006,.006)
            high = 1600-j*225
            b.add_noise(layer,rng,at,.077,115,high,.18-j*.02,.033,
                        wobble=26-j*3)
        b.add_noise(layer,rng,.118,.15,160,1700,.13,.055,wobble=17)
    target_delta = -6.8 if key in {'verdant_fighter_4','ember_elite_brass_vulture'} else -10.0
    layer *= 10**((full_a(x)+target_delta-full_a(layer))/20)
    y = b.fft_filter(x+layer,28,3400 if key in {'verdant_fighter_4','ember_elite_brass_vulture'} else 4500)
    edge = np.sin(np.minimum(np.arange(n)[::-1]/(.020*RATE),1)*np.pi/2)**2
    y *= edge
    if key == 'ember_elite_brass_vulture':
        y = .8*np.tanh(y/.8)
    return y * 10**((b.short_a_level(x)-b.short_a_level(y))/20)


def render_voice(spec, variant):
    key, _, base, vowels, _, _ = spec
    dur = VOICE_LENGTH[key] * [1,.96,1.04][variant]
    n = round(dur * RATE)
    rng = np.random.default_rng(zlib.crc32(key.encode()) + variant*1000003 + 809321)
    creature = key in {'verdant_alien','ember_alien','ember_elite_brass_vulture',
                       'ember_elite_ash_wraith','verdant_elite_resin_warden'}
    # All voices resolve into a sustained open vowel; the source routine's
    # upward reach, 5-7 Hz vibrato, irregular tremor and falling finish remain.
    lead = 'oh' if base < 200 else 'eh'
    x = b.human_voice(rng,n,base,(lead,'ah'),variant,key.split('_')[-1])
    t = np.arange(n)/RATE
    u = t/dur
    breath = b.noise_band(rng,n,330 if creature else 440,1950)
    breath *= (.05 + .23*np.clip((u-.48)/.52,0,1)**1.3)
    x += breath
    if creature:
        low = b.noise_band(rng,n,90,1050)
        wobble = 18 if key == 'verdant_alien' else 27 if key == 'ember_alien' else 13
        creature_level = .40 if key == 'ember_alien' else .24 if key == 'verdant_alien' else .13
        x += creature_level*low*(.55+.45*np.sin(2*np.pi*wobble*t)**2)
    if key == 'verdant_fighter_4':
        x += .075*b.noise_band(rng,n,180,1050)
    # A fast attack, a broad held middle and the blast's 20 ms cut. Small
    # trembling amplitude is independent of the pitch vibrato.
    env = np.sin(np.minimum(t/.010,1)*np.pi/2)**2
    env *= np.sin(np.minimum((dur-t)/.020,1)*np.pi/2)**2
    env *= (1-.14*u)*(1+.065*np.sin(2*np.pi*(7.1+variant*.4)*t+.8))
    x = b.spectral_filter(x,270 if not creature else 220,2300)
    for ms,g in ((12,.06),(23,.032)):
        d=round(ms*RATE);x[d:]+=g*x[:-d].copy()
    x = np.tanh(1.16*x)/1.16
    x *= env
    x = b.spectral_filter(x,250 if not creature else 200,2500)
    # Shape crest to make the file audible alone at -9 peak while keeping its
    # complete A-weighted average around -22 dBFS.
    def shaped(gamma):
        y = np.sign(x)*np.abs(x)**gamma
        y = b.spectral_filter(y,250 if not creature else 200,2550)
        y *= env
        y -= np.mean(y)*env
        return y * (10**(-9/20)/(np.max(np.abs(y))+1e-30))
    target_full = -23.2 if (key,variant) == ('verdant_fighter_3',0) else -22.8 if (key,variant) in {
        ('verdant_fighter_2',1),('verdant_fighter_3',0),
        ('verdant_fighter_3',1),('verdant_alien',2)} else -22.0
    lo,hi=.65,2.0
    for _ in range(20):
        mid=(lo+hi)/2
        if full_a(shaped(mid)) > target_full:lo=mid
        else:hi=mid
    return shaped((lo+hi)/2)


def hf_stats(x):
    p=np.abs(np.fft.rfft(x))**2
    f=np.fft.rfftfreq(len(x),1/RATE)
    return (float(np.sum(f*p)/(np.sum(p)+1e-30)),
            float(np.sum(p[f>3500])/(np.sum(p)+1e-30)),
            float(np.sum(p[f>=4000])/(np.sum(p)+1e-30)))


def main():
    changed=[]; pairs=[]; original={}; revised={}
    for spec in b.DEATHS:
        key,_,kind,nominal,*_=spec
        if key not in CHARACTER_DEATHS and key not in HOT:continue
        for v in range(3):
            if key not in CHARACTER_DEATHS and v not in HOT[key]:continue
            name=f'{key}_{v}.wav'; path=OUT/name
            before=pcm(b.render_death(spec,v))
            x=add_character(before,key,v) if key in CHARACTER_DEATHS else before.copy()
            if key in HOT and v in HOT[key]:x*=10**(-1.5/20)
            b.write_wav(path,x);after=b.read_wav(path)
            original[name]=before;revised[name]=after;changed.append(name)
            pairs.append((before,after))
    for spec in b.SCREAMS:
        key=spec[0]
        for v in range(3):
            name=f'{key}_scream_{v}.wav';path=OUT/name
            before=pcm(b.render_scream(spec,v))
            b.write_wav(path,render_voice(spec,v));after=b.read_wav(path)
            original[name]=before;revised[name]=after;changed.append(name)
            pairs.append((before,after))
    audition=[]
    for before,after in pairs:
        audition.extend((before,np.zeros(round(.25*RATE)),after,
                         np.zeros(round(.65*RATE))))
    b.write_wav(ROOT/'audition_revision.wav',np.concatenate(audition))
    lines=['# Verdant / Ember death and scream revision','',
           'Deterministic mono 44.1 kHz, 16-bit PCM. `build_revision.py` imports the original builder read-only and writes only these selected existing WAV names. `audition_revision.wav` contains a before/after pair for **every** changed file, in the table order below: 0.25 s within each pair and 0.65 s between pairs.','',
           '## Changes','',
           '- Rebuilt all 45 Verdant/Ember screams as held open-vowel cries. Pilots are 0.82–1.00 s nominal, Brass Vulture 1.16 s, Resin Warden 1.20 s, and the other creature/elites 0.91–1.08 s. All have a 10 ms attack, 5–7 Hz pitch vibrato, irregular fear tremor, a falling finish, growing breath, radio band limit, and a 20 ms blast cut. Voice file peak is -9 dBFS and full-clip A-weighted RMS targets -22 dBFS.','- Brass Vulture deaths: three heavy wing whumps, tearing brass, low rasping bird gasp and feathery metal grains.','- Hornet Queen deaths: three descending noise wing stutters (each under 90 ms), chitin crack, hollow husk, and wet thud.','- Ember fighter 1–4 deaths: moving servo seizure and metal tear under existing vent/hull blasts; their original static debris alone gave these pilot craft a weaker identity.','- Existing Verdant fighter 1–3 buzz/wing layers and other elites’ ash, cannon, broadside, kiln and lance layers were already specific in the source and were left untouched.','- Reduced the requested hot heavy variants by 1.5 dB. The delivered hot cues had full-clip A-weighted RMS near -18.5 dBFS already; Space bigs/elites measure roughly -20 dBFS, so the 1.5 dB reduction brings these closer to Space and their loudest A-weighted 50 ms to -15.5 dBFS.','',
           'The runtime delay is 34–55 ms and `ScreamVolume` is 0.5. The mix-gap column measures that actual fixed runtime gain against each matching death cue.','',
           '## Verification of every changed delivered WAV','',
           '| File | s | Δ length % | Peak dBFS | A50 dBFS | A full dBFS | Centroid Hz | >3.5 kHz % | >4 kHz % | 1–3 kHz ring ms | Tonal ms | Clips | Max xcorr | Mix gap dB | Runtime gain |',
           '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    failures=[]
    for name in changed:
        x=revised[name];before=original[name];voice='_scream_' in name
        key=name.split('_scream_')[0] if voice else name.rsplit('_',1)[0]
        v=int(name[-5]); group=[revised.get(f'{key}{"_scream" if voice else ""}_{i}.wav',
                              b.read_wav(OUT/f'{key}{"_scream" if voice else ""}_{i}.wav')) for i in range(3)]
        corr=max(b.max_xcorr(group[a],group[c]) for a,c in ((0,1),(0,2),(1,2)))
        m=b.pcm_stats(x,voice);ring=b.resonance_run(x);centroid,hf35,hf4=hf_stats(x)
        nominal=VOICE_LENGTH[key] if voice else next(s[3] for s in b.DEATHS if s[0]==key)
        delta=(len(x)/RATE/nominal-1)*100
        gap=gain=None
        if voice:
            death=b.read_wav(OUT/f'{key}_{v}.wav')
            gain=.5
            gap=b.short_a_level(death)-b.short_a_level(x*gain)
            ok=(.7<=len(x)/RATE<=1.31 and abs(delta)<=20 and abs(m['peak']+9)<.1
                and -23.5<=full_a(x)<=-20.5 and centroid<1500 and hf35<.08
                and hf4<.002 and m['clipping']==0 and ring<=100 and
                m['tonal']<=100 and corr<.6 and 10<=gap<=14)
        else:
            target=-15.5 if key in HOT and v in HOT[key] else -14 if key in CHARACTER_DEATHS and key in {'verdant_fighter_4','ember_elite_brass_vulture'} else -15.5
            ok=(abs(delta)<=20 and abs(m['a']+15)<=1.5 and m['peak']<=-2 and m['clipping']==0
                and ring<=100 and m['tonal']<=100 and corr<.6 and
                abs(m['a']-target)<1.5)
        if not ok:failures.append(name)
        lines.append(f'| `{name}` | {len(x)/RATE:.3f} | {delta:+.1f} | {m["peak"]:.2f} | {m["a"]:.2f} | {full_a(x):.2f} | {centroid:.0f} | {hf35*100:.2f} | {hf4*100:.3f} | {ring:.0f} | {m["tonal"]:.0f} | {m["clipping"]} | {corr:.3f} | {gap if gap is None else f"{gap:.1f}"} | {gain if gain is None else f"{gain:.3f}"} |')
    lines+=['','**Result:** '+ ('FAIL: '+', '.join(failures) if failures else f'PASS: {len(changed)} changed WAVs.'),
            'No listening monitor was available. The noise-led identity layers, moving/damped pitch, low formants, steep voice cutoff below 4 kHz, and delivered-PCM spectral/ring checks protect against beepiness and shrillness; phone playback still needs an ear pass.','']
    (ROOT/'sound_notes_revision.md').write_text('\n'.join(lines))
    print(lines[-2]);print('changed',len(changed))
    if failures:raise SystemExit(1)


if __name__=='__main__':main()
