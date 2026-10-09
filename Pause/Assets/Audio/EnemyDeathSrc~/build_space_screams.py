#!/usr/bin/env python3
"""Build 27 restrained vocal layers for the Space World death cues. NumPy only.

Run from any directory: python3 build_space_screams.py
The existing death WAVs are read for mix checks and never written.
"""
from __future__ import annotations

import itertools
import math
import zlib
from pathlib import Path

import numpy as np

from build_space_death_sounds import RATE, OUT, label, png_write, read_wav, write_wav, max_xcorr

ROOT = Path(__file__).resolve().parent
GAIN = .50  # -9 dBFS file + this gain = -15 dBFS, 12 dB below a -3 dBFS death cue.
GAP = np.zeros(round(1.2 * RATE))
TARGET_PEAK = 10 ** (-9 / 20)

# key, name, duration, fundamental Hz, vowel trajectory, delay, texture
# Vowels are F1/F2/F3/F4 in Hz. F4 is intentionally low and faint for a dark radio voice.
VOWELS = {
    'ah': (750, 1250, 2400, 3200),
    'eh': (530, 1650, 2350, 3150),
    'oh': (470, 1000, 2250, 3050),
    'uh': (550, 1150, 2250, 3050),
}
SOUNDS = [
    ('space_fighter_1', 'Needle', .22, 340, ('eh', 'ah'), .034, 'startled, high but soft'),
    ('space_fighter_2', 'Steel Claw', .28, 235, ('uh', 'ah'), .039, 'sharp grunt-cry'),
    ('space_fighter_3', 'Twin Claw', .32, 270, ('eh', 'ah'), .043, 'two offset crew voices'),
    ('space_fighter_4', 'Warden', .35, 190, ('oh', 'uh'), .048, 'lower strained shielded shout'),
    ('space_alien', 'Bile Mite', .40, 510, ('eh', 'ah'), .043, 'wet reedy chitter and gurgle'),
    ('space_elite_eventide_bastion', 'Eventide Bastion', .45, 180, ('oh', 'ah'), .052, 'commander over damaged comms'),
    ('space_elite_orbit_reaver', 'Orbit Reaver', .40, 290, ('eh', 'ah'), .036, 'fierce raspy cut-off cry'),
    ('space_elite_rift_lancer', 'Rift Lancer', .40, 250, ('eh', 'uh'), .050, 'electrified yell and interference'),
    ('space_elite_singularity_hauler', 'Singularity Hauler', .55, 145, ('oh', 'uh'), .055, 'deep downward groan-shout'),
]


def spectral_filter(x, low, high):
    """Soft voice band limits, then a strict stop above 4 kHz."""
    f = np.fft.rfftfreq(len(x), 1 / RATE)
    hp = 1 - np.exp(-.5 * (f / low) ** 4)
    lp = 1 / np.sqrt(1 + (f / high) ** 14)
    lp[f >= 4000] = 0
    return np.fft.irfft(np.fft.rfft(x) * hp * lp, n=len(x))


def noise_band(rng, n, low, high):
    x = spectral_filter(rng.standard_normal(n), low, high)
    return x / (np.sqrt(np.mean(x * x)) + 1e-12)


def resonator(x, center, q):
    """Moving two-pole band-pass formant (RBJ constant-peak biquad)."""
    y = np.empty_like(x)
    x1 = x2 = y1 = y2 = 0.0
    for i, (sample, hz) in enumerate(zip(x, center)):
        w = 2 * math.pi * min(hz, 3900) / RATE
        a = math.sin(w) / (2 * q)
        inv = 1 / (1 + a)
        b0 = a * inv
        a1 = -2 * math.cos(w) * inv
        a2 = (1 - a) * inv
        value = b0 * (sample - x2) - a1 * y1 - a2 * y2
        y[i] = value
        x2, x1, y2, y1 = x1, sample, y1, value
    return y


def smooth_random(rng, n, spacing):
    points = rng.standard_normal(math.ceil(n / spacing) + 2)
    return np.interp(np.arange(n), np.arange(len(points)) * spacing, points)


def human_voice(rng, n, base, vowels, variant, kind, phase_offset=0):
    t = np.arange(n) / RATE
    u = np.arange(n) / max(n - 1, 1)
    multipliers = [(.91, 1.13, 1.00, .78), (.99, 1.20, .91, .70),
                   (.94, 1.08, 1.05, .82)][variant]
    if kind == 'hauler':
        multipliers = [(.99, 1.18, .88, .56), (1.03, 1.14, .82, .49),
                       (.95, 1.22, .91, .53)][variant]
    f0 = base * [1, .91, 1.08][variant] * np.interp(
        u, [0, .15, .53, 1], multipliers)
    jitter = smooth_random(rng, n, round(.011 * RATE))
    jitter /= max(np.std(jitter), 1e-6)
    f0 *= 1 + [.016, .023, .019][variant] * jitter
    f0 *= 1 + .009 * np.sin(2 * np.pi * [5.3, 6.6, 5.9][variant] * t + phase_offset)
    phase = 2 * np.pi * np.cumsum(f0) / RATE + phase_offset
    # Additive band-limited glottal saw: harmonics roll off; independent phases
    # prevent a buzzy, phase-locked imitation across the three takes.
    source = np.zeros(n)
    phases = rng.uniform(-.35, .35, 20)
    for k in range(1, 21):
        if k * np.max(f0) < 3800:
            source += np.sin(k * phase + phases[k-1]) / k ** 1.18
    source = np.tanh(1.30 * source)  # mild vocal strain
    aspiration = noise_band(rng, n, 420, 2650)
    source = source + (.20 if kind != 'reaver' else .29) * aspiration

    start = np.array(VOWELS[vowels[0]])
    end = np.array(VOWELS[vowels[1]])
    slide = np.clip((u - .18) / .68, 0, 1)
    slide = slide * slide * (3 - 2 * slide)
    centers = start[:, None] * (1 - slide) + end[:, None] * slide
    centers *= [1, 1.035, .965][variant]
    # Four parallel, moving formants. The upper two are quieter by design.
    voice = sum(weight * resonator(source, c, q)
                for weight, c, q in zip((1, .73, .24, .08), centers,
                                        (2.5, 3.3, 4.5, 5.0)))
    # A hint of breath stays outside the resonators to soften the harmonic edge.
    voice += .08 * aspiration
    if kind in ('bastion', 'lancer'):
        static = noise_band(rng, n, 650, 2500)
        ticks = np.zeros(n)
        for at in ([.07, .22, .34] if kind == 'bastion' else [.09, .16, .29]):
            j = min(round(at * RATE), n - 1)
            end_j = min(j + round(.006 * RATE), n)
            ticks[j:end_j] += .22 * static[j:end_j] * np.hanning(2 * (end_j-j))[:end_j-j]
        voice += .025 * static + ticks
    if kind == 'reaver':
        voice += .08 * noise_band(rng, n, 450, 1800)
    return voice


def alien_voice(rng, n, base, variant):
    t = np.arange(n) / RATE
    u = np.arange(n) / max(n - 1, 1)
    jumps = np.interp(u, [0, .16, .29, .44, .62, .76, 1],
                      [[.78, 1.06, .88, 1.30, .92, 1.18, .72],
                       [.95, 1.42, .87, 1.14, 1.48, .91, .70],
                       [.84, 1.20, 1.51, .90, 1.29, .78, .68]][variant])
    # Smooth steps with a 3 ms kernel to avoid clicklike pitch discontinuities.
    kernel = np.ones(round(.003 * RATE))
    kernel /= kernel.sum()
    jumps = np.convolve(np.pad(jumps, (len(kernel), len(kernel)), mode='edge'),
                        kernel, mode='same')[len(kernel):-len(kernel)]
    f0 = base * [1, 1.09, .94][variant] * jumps
    f0 *= 1 + .022 * smooth_random(rng, n, round(.008 * RATE))
    phase = 2 * np.pi * np.cumsum(f0) / RATE
    duty = .32 + .10 * np.sin(2 * np.pi * 13 * t + variant)
    # Band-limited pulse-width-modulated reed, with a shifting duty cycle.
    source = np.zeros(n)
    for k in range(1, 13):
        if k * np.max(f0) < 3900:
            source += 2 * np.sin(np.pi * k * duty) / (np.pi * k) * np.cos(k * phase)
    wet = noise_band(rng, n, 110, 1450)
    gurgle = wet * (.48 + .52 * np.sin(2 * np.pi * [33, 47, 56][variant] * t) ** 2)
    # Two small varying comb taps give an organic throat/flange, not a whistle.
    for delay_ms, amount in ((1.5, .25), (2.8, -.14)):
        d = np.round((delay_ms + .25 * np.sin(2*np.pi*8*t)) * RATE/1000).astype(int)
        idx = np.arange(n) - d
        source += amount * source[np.clip(idx, 0, n-1)] * (idx >= 0)
    source += .33 * gurgle
    chirp = np.sin(2 * np.pi * (15 + 7 * variant) * t)
    f1 = 650 + 170 * chirp
    f2 = 1230 + 320 * np.sin(2*np.pi*11*t + variant)
    f3 = 2200 + 200 * np.sin(2*np.pi*7*t)
    voice = (resonator(source, f1, 2.2) + .60 * resonator(source, f2, 3.3)
             + .15 * resonator(source, f3, 4.0) + .21 * gurgle)
    return np.tanh(1.4 * voice)


def render(spec, variant):
    key, name, duration, base, vowels, delay, texture = spec
    rng = np.random.default_rng(zlib.crc32(key.encode()) + 1000003 * variant)
    duration *= [1, .96, 1.04][variant]
    n = round(duration * RATE)
    kind = key.split('_')[-1]
    if key == 'space_alien':
        x = alien_voice(rng, n, base, variant)
    elif key == 'space_fighter_3':
        x = human_voice(rng, n, base, vowels, variant, kind)
        offset = round((.045 + variant * .009) * RATE)
        crew = human_voice(rng, n-offset, base * .76, ('oh', 'ah'),
                           (variant+1) % 3, kind, phase_offset=1.3)
        x[offset:] += .56 * crew
    else:
        x = human_voice(rng, n, base, vowels, variant, kind)

    x = spectral_filter(x, 350 if key != 'space_alien' else 240,
                        2600 if key != 'space_alien' else 2700)
    # Very short dark early reflections make these feel like transmissions.
    if key != 'space_alien':
        for ms, gain in ((12, .065), (23, .035)):
            d = round(ms * RATE)
            x[d:] += gain * x[:-d].copy()
    x = np.tanh(1.15 * x) / 1.15  # slight compression
    attack = .009 if key != 'space_alien' else .010
    end_fade = .120 if kind == 'hauler' else .015
    t = np.arange(n) / RATE
    env = np.sin(np.minimum(t / attack, 1) * np.pi / 2) ** 2
    # Hold the cry, then let the blast cut it in 15 ms. The hauler bends down
    # through its longer 120 ms release.
    env *= np.sin(np.minimum((duration - t) / end_fade, 1) * np.pi / 2) ** 2
    env *= (1 - .18 * t / duration)
    if key == 'space_alien':
        # The creature's dense reed/gurgle would otherwise carry too much
        # average level alongside the much more transient wet death splat.
        env *= np.exp(-t / .19)
    x *= env
    # A final steep spectral stop is followed by an edge window to keep WAV
    # endpoints quiet. This is far below the audible band above 4 kHz.
    x = spectral_filter(x, 300 if key != 'space_alien' else 210, 2800)
    x *= env
    x -= np.mean(x) * env
    x *= TARGET_PEAK / (np.max(np.abs(x)) + 1e-30)
    return x


def stats(x, alien=False):
    f = np.fft.rfftfreq(len(x), 1/RATE)
    p = np.abs(np.fft.rfft(x)) ** 2
    centroid = float(np.sum(f*p) / (np.sum(p)+1e-30))
    hf = float(np.sum(p[f > 3500]) / (np.sum(p)+1e-30))
    top4 = float(np.sum(p[f >= 4000]) / (np.sum(p)+1e-30))
    max4_db = float(20*np.log10((np.sqrt(p[f >= 4000]).max()+1e-30) /
                                (np.sqrt(p).max()+1e-30)))
    size, hop = 882, 220
    frames = np.lib.stride_tricks.sliding_window_view(x, size)[::hop]
    pf = np.abs(np.fft.rfft(frames*np.hanning(size), axis=1))**2
    energy = pf.sum(axis=1)
    active = energy > max(energy)*10**(-48/10)
    tonal = active & (pf.max(axis=1)/(energy+1e-30) > .45)
    run = max((sum(1 for _ in g) for val,g in itertools.groupby(tonal) if val), default=0)
    return dict(duration=len(x)/RATE, peak=20*np.log10(max(np.abs(x))+1e-30),
                centroid=centroid, hf=hf, top4=top4, max4_db=max4_db,
                tonal=run*hop/RATE*1000,
                clips=int(np.count_nonzero(np.abs(x) >= .9999)))


def mix(death, scream, delay):
    start = round(delay * RATE)
    n = max(len(death), start + len(scream))
    y = np.zeros(n)
    y[:len(death)] = death
    y[start:start+len(scream)] += GAIN * scream
    return y


def join(cues):
    return np.concatenate([part for i, cue in enumerate(cues)
                           for part in ((cue, GAP) if i < len(cues)-1 else (cue,))])


def sheet(rows):
    width, rh = 1440, 122
    img = np.zeros((rh*len(rows)+72,width,3), dtype=np.uint8)
    img[:] = (16,23,29)
    label(img, 18, 16, 'SPACE WORLD / LIGHT SCREAMS / VARIANT 0', (236,228,195), 3)
    label(img, 18, 46, 'WAVEFORM                    SPECTROGRAM 0-4 KHZ / DARKER = QUIETER', (150,170,184), 2)
    for i,(spec,x,m) in enumerate(rows):
        y0 = 72+i*rh
        img[y0:y0+1] = (51,67,77)
        label(img, 18, y0+11, spec[1], (235,226,196), 2)
        label(img, 18, y0+34, f"{m['duration']:.2f}S  PK {m['peak']:.1f}DB  C {m['centroid']:.0f}HZ", scale=2)
        label(img, 18, y0+55, f"HF {m['hf']*100:.2f}%  TONAL {m['tonal']:.0f}MS", scale=2)
        x0,wv = 450,360
        img[y0+12:y0+105,x0:x0+wv] = (24,34,41)
        for j,b in enumerate(np.array_split(x,wv)):
            a = int(np.clip(59-np.max(b)*115,13,104))
            btm = int(np.clip(59-np.min(b)*115,13,104))
            img[y0+a:y0+btm+1,x0+j] = (96,189,177)
        x1,sw,sh = 831,590,93
        nfft = 1024
        hop = max(1,(len(x)-nfft)//sw)
        frames = np.lib.stride_tricks.sliding_window_view(x,nfft)[::hop][:sw]
        p = np.abs(np.fft.rfft(frames*np.hanning(nfft),axis=1))
        db = 20*np.log10(p+1e-9)
        db -= np.max(db)
        indices = np.clip(np.round(np.linspace(0,4000,sh)*nfft/RATE).astype(int),0,p.shape[1]-1)
        intensity = np.clip((db[:,indices]+65)/65,0,1).T[::-1]
        colors = np.empty((sh,len(frames),3),dtype=np.uint8)
        colors[:,:,0] = (25+210*intensity).astype('u1')
        colors[:,:,1] = (32+160*intensity**.7).astype('u1')
        colors[:,:,2] = (40+80*intensity**.5).astype('u1')
        img[y0+12:y0+12+sh,x1:x1+len(frames)] = colors
    png_write(ROOT/'sheet_screams.png',img)


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    rows, first_cues, all_cues, failures, notes_rows = [], [], [], [], []
    print('file dur_s peak_db centroid_hz HF_gt3.5k_pct tonal_ms clips mix_peak_db mix_centroid_hz max_variant_xcorr')
    for spec in SOUNDS:
        key,name,nominal,base,vowels,delay,texture = spec
        variants = []
        death_files = [OUT/f'{key}_{v}.wav' for v in range(3)]
        if not all(p.is_file() for p in death_files):
            raise FileNotFoundError(f'Missing reference death WAV for {key}')
        for v in range(3):
            path = OUT/f'{key}_scream_{v}.wav'
            write_wav(path,render(spec,v))
            variants.append(read_wav(path))  # measure final PCM
        maxcor = max(max_xcorr(variants[a],variants[b]) for a,b in ((0,1),(0,2),(1,2)))
        for v,x in enumerate(variants):
            death = read_wav(death_files[v])
            combined = mix(death,x,delay)
            m = stats(x, key == 'space_alien')
            mm = stats(combined)
            print(f"{key+'_scream_'+str(v)+'.wav':45} {m['duration']:.3f} {m['peak']:.2f} "
                  f"{m['centroid']:.0f} {m['hf']*100:.3f} {m['tonal']:.1f} {m['clips']} "
                  f"{mm['peak']:.2f} {mm['centroid']:.0f} {maxcor:.3f}")
            limits = [abs(m['duration']/nominal-1)<.06, abs(m['peak']+9)<.05,
                      m['centroid']<(1700 if key == 'space_alien' else 1500),
                      m['hf']<.08, m['top4']<.0001, m['max4_db'] < -60,
                      m['tonal']<=200,
                      m['clips']==0, mm['peak']<-.01, maxcor<.6]
            if not all(limits):
                failures.append((key,v,limits))
            if v == 0:
                rows.append((spec,x,m))
                first_cues.extend((x,combined))
            all_cues.append(x)
            notes_rows.append((key,name,v,m,mm,maxcor))
    write_wav(ROOT/'audition_screams.wav',join(first_cues))
    write_wav(ROOT/'audition_screams_all.wav',join(all_cues))
    sheet(rows)
    notes = ['# Space World light scream layers', '',
             '27 deterministic 44.1 kHz mono 16-bit PCM WAVs. Existing death WAVs were read only. '
             'Each scream file peaks at -9 dBFS. In the audition mix it begins 34-55 ms after '
             'the death cue and plays at 0.50 volume, putting its peak 12 dB below the '
             'death WAV peak of -3 dBFS. A playback gain of 0.8 would make this an 8 dB '
             'difference; use 0.50 to satisfy the requested 10-14 dB range.','',
             'Human cries use a jittered, vibrato-modulated harmonic glottal source through '
             'four moving vowel formants, breath, mild saturation, 350-2800 Hz radio shaping, '
             'a trace of hiss and two early reflections. The creature uses a pulse-width reed '
             'source, shifting throat formants, small moving comb taps and wet modulated noise. '
             'All have a 9-10 ms attack. All but the Hauler cut into a 15 ms fade; the Hauler '
             'bends down into a 120 ms fade. The spectrum is steeply attenuated and stopped '
             'above 4 kHz. Quantized WAV spectra are checked to keep every bin above 4 kHz '
             'at least 60 dB below the strongest bin.','',
             '| Enemy | Character | Delay ms |', '|---|---|---:|']
    for key,name,nominal,base,vowels,delay,texture in SOUNDS:
        notes.append(f'| {name} | {texture} | {delay*1000:.0f} |')
    notes += ['', '## Delivered PCM verification', '',
              'Centroid is spectral centroid. HF is the energy share above 3.5 kHz. '
              'Tonal is the longest contiguous run of active 20 ms Hann frames, 5 ms apart, '
              'whose strongest frequency bin has over 45% of frame energy. Mix measurements '
              'use each corresponding death variant and the delay above. Xcorr is the maximum '
              'normalized full-lag correlation among that enemy’s three scream variants.','',
              '| File | s | Peak dBFS | Centroid Hz | HF % | Tonal ms | Clips | Mix peak dBFS | Mix centroid Hz | Max xcorr |',
              '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    for key,name,v,m,mm,cor in notes_rows:
        notes.append(f"| `{key}_scream_{v}.wav` | {m['duration']:.3f} | {m['peak']:.2f} | "
                     f"{m['centroid']:.0f} | {m['hf']*100:.3f} | {m['tonal']:.1f} | "
                     f"{m['clips']} | {mm['peak']:.2f} | {mm['centroid']:.0f} | {cor:.3f} |")
    notes += ['', '## Audition and integration', '',
              '`audition_screams.wav` plays variant 0 alone and then over its matching death '
              'variant, for all nine enemies in the order above. Each item is followed by '
              '1.2 s of silence. `audition_screams_all.wav` plays all 27 screams in enemy and '
              'variant order, with the same gaps. `sheet_screams.png` shows variant 0 for each.','',
              'A later game-code change must load `*_scream_0/1/2.wav`, select a variant and '
              'play it probabilistically at 0.50 volume after the specified delay. No C# or '
              '.meta files were edited here. Probability is intentionally left for game tuning.','',
              'These are synthesized approximations of distant vocal cries, not recorded '
              'performances. Ear-tune Bile Mite first for an organic rather than electronic '
              'edge, then Needle for pitch, and Twin Claw for two voices remaining distinct '
              'beneath the double impact.']
    (ROOT/'sound_notes_screams.md').write_text('\n'.join(notes)+'\n')
    if failures:
        raise SystemExit(f'FAILED checks: {failures}')
    print('PASS: 27 scream WAVs, no clips, limits and mix checks satisfied')


if __name__ == '__main__':
    main()
