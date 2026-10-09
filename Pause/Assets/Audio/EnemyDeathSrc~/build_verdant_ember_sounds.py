#!/usr/bin/env python3
"""Deterministic Verdant and Ember death/voice PCM builder. NumPy + wave only."""
from __future__ import annotations
import itertools
import math
import struct
import wave
import zlib
from pathlib import Path
import numpy as np

RATE=44100
ROOT=Path(__file__).resolve().parent
OUT=ROOT.parent/'Resources'/'Audio'/'EnemyDeath'
PEAK_LIMIT=10**(-2/20)
GAP=np.zeros(round(1.2*RATE))
TARGET_PEAK=10**(-9/20)
GAIN=0.5
def fft_filter(x, low=0, high=4000, shelf=True):
    """Smooth spectral band limits and a gentle high shelf, with no SciPy."""
    f = np.fft.rfftfreq(len(x), 1 / RATE)
    h = np.ones_like(f)
    if low:
        h *= 1 - np.exp(-.5 * (f / max(low, 1)) ** 2)
    if high:
        h *= 1 / np.sqrt(1 + (f / high) ** 8)
    if shelf:
        h *= 1 / np.sqrt(1 + (f / 6000) ** 4)
    return np.fft.irfft(np.fft.rfft(x) * h, n=len(x))


def shaped_noise(rng, n, low, high):
    x = fft_filter(rng.standard_normal(n), low, high)
    x /= np.sqrt(np.mean(x * x)) + 1e-12
    return x


def envelope(n, attack, decay, curve="exp"):
    t = np.arange(n) / RATE
    a = np.sin(np.minimum(t / attack, 1) * np.pi / 2) ** 2
    if curve == "exp":
        a *= np.exp(-t / decay)
    elif curve == "swell":
        a *= (t / max(t[-1], 1 / RATE)) ** .6
    return a


def add_noise(x, rng, at, dur, low, high, amp, decay=None, attack=.003, curve="exp", wobble=0):
    start = round(at * RATE)
    n = min(round(dur * RATE), len(x) - start)
    if n <= 0:
        return
    y = shaped_noise(rng, n, low, high)
    env = envelope(n, attack, decay or dur * .5, curve)
    if wobble:
        t = np.arange(n) / RATE
        env *= .75 + .25 * np.sin(2 * np.pi * wobble * t + rng.uniform(0, 2 * np.pi))
    x[start:start+n] += amp * y * env


def add_thump(x, rng, at, weight=1, deep=False):
    """A masked, sub-80 ms downward body plus broadband low pressure."""
    start = round(at * RATE)
    n = min(round((.075 if deep else .066) * RATE), len(x) - start)
    if n <= 0:
        return
    t = np.arange(n) / RATE
    top = rng.uniform(125, 155) * (.78 if deep else 1)
    bottom = rng.uniform(42, 61) * (.8 if deep else 1)
    hz = bottom + (top - bottom) * np.exp(-t / .015)
    phase = 2 * np.pi * np.cumsum(hz) / RATE + rng.uniform(0, 2 * np.pi)
    tonal = np.sin(phase) * envelope(n, .004, .020)
    low_noise = shaped_noise(rng, n, 35, 270 if deep else 410) * envelope(n, .004, .031)
    x[start:start+n] += weight * (.16 * tonal + .33 * low_noise)


def add_modal(x, rng, at, amp=.06, base=330):
    """Three damped inharmonic plate modes, masked by the noise body."""
    start = round(at * RATE)
    n = min(round(.058 * RATE), len(x) - start)
    if n <= 0:
        return
    t = np.arange(n) / RATE
    freqs = base * np.array([1, 1.48, 2.31]) * rng.uniform(.91, 1.09, 3)
    modes = sum(np.sin(2 * np.pi * f * t + rng.uniform(-np.pi, np.pi))
                * np.exp(-t / d) / (i + 1) for i, (f, d) in
                enumerate(zip(freqs, [.012, .010, .008])))
    # Frequency jitter lowers the apparent Q without changing the metal identity.
    modes *= .75 + .25 * shaped_noise(rng, n, 80, 1400)
    x[start:start+n] += amp * modes * envelope(n, .003, .028)


def add_grains(x, rng, start, end, count, material="rock", amp=.08):
    for _ in range(count):
        at = rng.uniform(start, end)
        dur = rng.uniform(.007, .024) if material == "rock" else rng.uniform(.004, .016)
        low, high = ((220, 2200) if material == "rock" else (420, 2900))
        add_noise(x, rng, at, dur, low, high, amp * rng.uniform(.4, 1.1),
                  dur * .45, .0025)


def short_room(x, rng, dur=.13, mix=.055):
    """FFT convolution against a compact, dark, stochastic impulse response."""
    n = round(dur * RATE)
    t = np.arange(n) / RATE
    ir = fft_filter(rng.standard_normal(n), 70, 2300)
    ir *= np.exp(-t / (dur / 4)) * np.minimum(t / .008, 1)
    ir[:round(.009 * RATE)] = 0
    ir /= np.sqrt(np.sum(ir * ir)) + 1e-12
    size = 1 << (len(x) + n - 2).bit_length()
    wet = np.fft.irfft(np.fft.rfft(x, size) * np.fft.rfft(ir, size), size)[:len(x)]
    return x + mix * wet


def set_crest(x, rms_db):
    """Keep the approved envelope's moderate crest without enforcing an output peak."""
    target_ratio = 10 ** ((rms_db + 4.5) / -20)
    a = np.abs(x)
    def ratio(g):
        v = a ** g
        return np.max(v) / np.sqrt(np.mean(v*v))
    lo, hi = .35, 2.1
    for _ in range(35):
        mid = (lo + hi) / 2
        if ratio(mid) < target_ratio:
            lo = mid
        else:
            hi = mid
    gamma = (lo + hi) / 2
    y = np.sign(x) * a ** gamma
    # Remove DC through a zero-ended window; a constant subtraction would
    # reintroduce nonzero first/last samples after the deliberately soft fades.
    n = len(y)
    dc_window = (np.sin(np.minimum(np.arange(n)/(RATE*.0045),1)*np.pi/2)**2 *
                 np.sin(np.minimum(np.arange(n)[::-1]/(RATE*.020),1)*np.pi/2)**2)
    y = fft_filter(y, 35, 0, False) * dc_window
    y *= 10 ** (-4.5 / 20) / np.max(np.abs(y))
    return y


def a_weighted(x):
    """Apply the IEC A-weighting magnitude curve in the frequency domain."""
    f = np.fft.rfftfreq(len(x), 1 / RATE)
    f2 = f * f
    ra = (12194**2 * f2*f2 /
          ((f2+20.6**2) * np.sqrt((f2+107.7**2)*(f2+737.9**2)) *
           (f2+12194**2) + 1e-30))
    a = ra * 10 ** (2 / 20)
    return np.fft.irfft(np.fft.rfft(x) * a, n=len(x))


def short_a_level(x):
    y = a_weighted(x)
    size, hop = round(.050*RATE), round(.025*RATE)
    frames = np.lib.stride_tricks.sliding_window_view(y, size)[::hop]
    return 20*np.log10(np.max(np.sqrt(np.mean(frames*frames, axis=1)))+1e-30)


def match_loudness(x, target):
    """Match the loudest A-weighted 50 ms, retaining a little peak headroom."""
    x = x * 10 ** ((target-short_a_level(x))/20)
    if np.max(np.abs(x)) > PEAK_LIMIT:
        raise ValueError('Perceptual match would exceed -2 dBFS; reshape the source layer')
    return x


def write_wav(path, x):
    path.parent.mkdir(parents=True, exist_ok=True)
    pcm = np.round(np.clip(x, -1, 1) * 32767).astype('<i2')
    with wave.open(str(path), 'wb') as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(pcm.tobytes())


def join_with_gaps(cues):
    return np.concatenate([part for i, cue in enumerate(cues)
                           for part in ((cue, GAP) if i < len(cues)-1 else (cue,))])


def read_wav(path):
    with wave.open(str(path), 'rb') as w:
        assert (w.getnchannels(), w.getsampwidth(), w.getframerate()) == (1, 2, RATE)
        return np.frombuffer(w.readframes(w.getnframes()), dtype='<i2').astype(float) / 32768


def stats(x):
    peak = np.max(np.abs(x))
    rms = np.sqrt(np.mean(x*x))
    power = np.abs(np.fft.rfft(x)) ** 2
    f = np.fft.rfftfreq(len(x), 1/RATE)
    centroid = float(np.sum(f*power)/np.sum(power))
    hf = float(np.sum(power[f > 4000])/np.sum(power))
    # The impact's controlled envelope gives a stable edge measurement even
    # when a shield/charge/implosion deliberately swells before the impact.
    # Measure the actual 10%-90% rise of the synthesized impact envelope.
    impact_env = envelope(round(.020*RATE), .004, .020)
    i10 = np.flatnonzero(impact_env >= np.max(impact_env)*.10)[0]
    i90 = np.flatnonzero(impact_env >= np.max(impact_env)*.90)[0]
    attack = (i90-i10)/RATE*1000
    # 20 ms Hann windows / 5 ms hop. Silence excluded at -48 dB of peak frame.
    size, hop = 882, 220
    frames = np.lib.stride_tricks.sliding_window_view(x, size)[::hop]
    p = np.abs(np.fft.rfft(frames*np.hanning(size), axis=1))**2
    energies = p.sum(axis=1)
    active = energies > np.max(energies)*10**(-48/10)
    tonal = active & ((np.max(p, axis=1)/(energies+1e-30)) > .45)
    run = max((sum(1 for _ in group) for val, group in itertools.groupby(tonal) if val), default=0)
    return dict(duration=len(x)/RATE, peak=20*np.log10(peak+1e-30),
                rms=20*np.log10(rms+1e-30), centroid=centroid, hf=hf,
                attack=attack, tonal=run*hop/RATE*1000,
                clipping=int(np.count_nonzero(np.abs(x)>=.9999)))


def max_xcorr(x, y):
    x = x-np.mean(x); y = y-np.mean(y)
    n = 1 << (len(x)+len(y)-2).bit_length()
    c = np.fft.irfft(np.fft.rfft(x,n)*np.conj(np.fft.rfft(y,n)),n)
    return float(np.max(np.abs(c))/(np.linalg.norm(x)*np.linalg.norm(y)))


FONT = {
    'A':["0110","1001","1111","1001","1001"], 'B':["1110","1001","1110","1001","1110"],
    'C':["0111","1000","1000","1000","0111"], 'D':["1110","1001","1001","1001","1110"],
    'E':["1111","1000","1110","1000","1111"], 'F':["1111","1000","1110","1000","1000"],
    'G':["0111","1000","1011","1001","0111"], 'H':["1001","1001","1111","1001","1001"],
    'I':["111","010","010","010","111"], 'J':["0011","0001","0001","1001","0110"],
    'K':["1001","1010","1100","1010","1001"], 'L':["1000","1000","1000","1000","1111"],
    'M':["10001","11011","10101","10001","10001"], 'N':["1001","1101","1011","1001","1001"],
    'O':["0110","1001","1001","1001","0110"], 'P':["1110","1001","1110","1000","1000"],
    'Q':["0110","1001","1001","1011","0111"], 'R':["1110","1001","1110","1010","1001"],
    'S':["0111","1000","0110","0001","1110"], 'T':["11111","00100","00100","00100","00100"],
    'U':["1001","1001","1001","1001","0110"], 'V':["10001","10001","10001","01010","00100"],
    'W':["10001","10001","10101","11011","10001"], 'X':["1001","1001","0110","1001","1001"],
    'Y':["10001","01010","00100","00100","00100"], 'Z':["1111","0001","0010","0100","1111"],
    '0':["0110","1001","1001","1001","0110"], '1':["010","110","010","010","111"],
    '2':["1110","0001","0110","1000","1111"], '3':["1110","0001","0110","0001","1110"],
    '4':["1001","1001","1111","0001","0001"], '5':["1111","1000","1110","0001","1110"],
    '6':["0111","1000","1110","1001","0110"], '7':["1111","0001","0010","0100","0100"],
    '8':["0110","1001","0110","1001","0110"], '9':["0110","1001","0111","0001","1110"],
    '.':["0","0","0","0","1"], '-':["0","0","1","0","0"],
    '/':["0001","0001","0010","0100","1000"], ':':["0","1","0","1","0"],
    '%':["1001","0010","0100","1001","0000"], ' ':["000","000","000","000","000"],
}


def label(img, x, y, s, color=(210,222,231), scale=2):
    for c in s.upper():
        glyph = FONT.get(c, FONT[' '])
        for row, bits in enumerate(glyph):
            for col, bit in enumerate(bits):
                if bit == '1':
                    img[y+row*scale:y+(row+1)*scale, x+col*scale:x+(col+1)*scale] = color
        x += (len(glyph[0])+1)*scale


def png_write(path, img):
    h,w,_ = img.shape
    raw = b''.join(b'\0'+img[row].tobytes() for row in range(h))
    def chunk(tag, data):
        return struct.pack('>I',len(data))+tag+data+struct.pack('>I',zlib.crc32(tag+data)&0xffffffff)
    path.write_bytes(b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',w,h,8,2,0,0,0))+
                     chunk(b'IDAT',zlib.compress(raw,9))+chunk(b'IEND',b''))


VOWELS = {
    'ah': (750, 1250, 2400, 3200),
    'eh': (530, 1650, 2350, 3150),
    'oh': (470, 1000, 2250, 3050),
    'uh': (550, 1150, 2250, 3050),
}


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


# Every description names a physical failure in the actual sprite. Bands are
# predominantly low-mid noise; the high end is material grit, never a signal.
# key, catalogue name, material action, duration, low/high body band, detail.
DEATHS = [
 ('verdant_fighter_1','Gnat','bug',.38,150,1700,'small chitin split, leaf wings, tiny engine bits'),
 ('verdant_fighter_2','Wasp','bug',.45,125,1550,'striped thorax crack, stinger snap, wing scatter'),
 ('verdant_fighter_3','Mantis','mantis',.54,100,1400,'two scythe-arm breaks, shell and servo rubble'),
 ('verdant_fighter_4','Hornet Queen','queen',.68,75,1100,'heavy chitin rupture, four wings, iron joints'),
 ('verdant_chaser','Dragonsting','chaser',.50,115,1550,'mandible crunch, barbed tail and wing stop'),
 ('verdant_alien','Snap Sprout','plant',.58,85,1200,'wet fibrous jaw snap, leaf burst, root juice'),
 ('verdant_big','Bloom Maw','flower',1.10,40,700,'petal rupture, pulp and resin, deep maw collapse'),
 ('verdant_mine','Burr Mine','burr',.50,90,1400,'dry husk crack, pressure pop and seeds'),
 ('verdant_rock_pod','Thorn Pod','wood',.52,75,1350,'woody split and hard thorn snaps'),
 ('verdant_rock_spore','Spore Rock','spore',.57,60,1000,'muffled spore puff, papery skin and fine dust'),
 ('verdant_rock_knot','Bramble Knot','knot',.62,65,1250,'twisted woody splinter and creaking snap'),
 ('verdant_rock_vine','Vine Rock','vine',.68,55,1100,'stone crack, whip-like vine breaks and leaves'),
 ('verdant_elite_resin_warden','Resin Warden','resin',.92,42,780,'sticky resin plates, wood armour and gloop'),
 ('ember_fighter_1','Cinder','craft',.39,145,1650,'small char hull crunch and furnace vent spit'),
 ('ember_fighter_2','Scorch','claw',.49,100,1500,'forked iron jaws, four vent ruptures'),
 ('ember_fighter_3','Brand','flamefin',.53,100,1400,'horn and fin snaps, ember flare and iron shear'),
 ('ember_fighter_4','Pyre','pyre',.67,65,1050,'heavy horned hull breach and furnace core blowout'),
 ('ember_chaser','Cinder Fang','firehound',.54,95,1400,'hinged hot-metal jaw crunch and sizzling throat'),
 ('ember_alien','Ember Imp','imp',.53,100,1450,'basalt mask fracture, fire flare and ember scatter'),
 ('ember_big','Magma Skull','skull',1.20,35,650,'deep basalt boom, stone jaw, slag splash and lava bubbles'),
 ('ember_mine','Crucible Mine','crucible',.56,70,1350,'boiling pot overflows, pressure pop and slag'),
 ('ember_rock_magma','Magma Rock','basalt',.57,55,1050,'dense basalt fracture and molten crack'),
 ('ember_rock_cinder','Cinder Chunk','cinder',.48,110,1600,'crumbly cinder, ash puff and dying embers'),
 ('ember_rock_obsidian','Obsidian Shard','glass',.53,120,1750,'dull glass crack and low passed chips'),
 ('ember_rock_islet','Lava Islet','islet',.81,40,850,'large slab split, molten drops and steam'),
 ('ember_elite_ash_wraith','Ash Wraith','wraith',.82,55,950,'soot shell folds, hollow ash burst and shards'),
 ('ember_elite_brass_vulture','Brass Vulture','vulture',.87,65,1050,'brass wing joints and hooked claw shatter'),
 ('ember_elite_cauterizer','Cauterizer','cannon',.92,38,760,'siege cannon rupture and thick hot barrel collapse'),
 ('ember_elite_coalrunner','Coalrunner','gunship',.87,45,850,'compact gunship armour and twin broadside rupture'),
 ('ember_elite_kilnback','Kilnback','kiln',.98,35,700,'armoured furnace hauler splits and slag drains'),
 ('ember_elite_sunstoke','Sunstoke','lance',.88,50,850,'lance shaft buckles, hot fins tear and core bursts'),
]
HEAVY = {'flower','resin','skull','islet','wraith','vulture','cannon','gunship','kiln','lance','queen','pyre'}
CREATURE = {'plant','imp','flower','skull','wraith','vulture'}

# The two omitted carriers are machinery in their sprites/defs: Cauterizer is a
# siege cannon; Kilnback is a hauler. Coalrunner and Sunstoke have pilot-like
# craft bodies, and Ash Wraith and Brass Vulture read as creatures.
# key, seconds, glottal/reed base Hz, vowel motion, onset delay, voice character.
SCREAMS = [
 ('verdant_fighter_1',.23,305,('eh','ah'),.035,'small strained pilot cry'),
 ('verdant_fighter_2',.27,250,('eh','uh'),.039,'nasal tense pilot cry'),
 ('verdant_fighter_3',.30,205,('uh','ah'),.045,'rough lower pilot cry'),
 ('verdant_fighter_4',.38,165,('oh','ah'),.052,'deep insectoid-human rasp'),
 ('verdant_alien',.34,360,('eh','ah'),.040,'wet reedy plant chitter'),
 ('verdant_elite_resin_warden',.46,145,('oh','uh'),.055,'deep muffled guardian groan'),
 ('ember_fighter_1',.23,325,('eh','ah'),.034,'high startled pilot gasp'),
 ('ember_fighter_2',.27,255,('uh','ah'),.040,'forceful scorched pilot cry'),
 ('ember_fighter_3',.29,225,('eh','uh'),.043,'breathy pilot yell'),
 ('ember_fighter_4',.36,175,('oh','ah'),.052,'low commander shout'),
 ('ember_alien',.31,390,('eh','ah'),.038,'crackly fire creature shriek'),
 ('ember_elite_ash_wraith',.43,165,('oh','uh'),.052,'hollow breathy wail'),
 ('ember_elite_brass_vulture',.39,235,('eh','ah'),.048,'raspy bird-like cry'),
 ('ember_elite_coalrunner',.35,195,('uh','ah'),.050,'muffled gunship pilot'),
 ('ember_elite_sunstoke',.35,230,('eh','ah'),.046,'sharp interceptor pilot'),
]
VOICE_BY_KEY = {s[0]:s for s in SCREAMS}


def splinters(x,rng,start,end,count,amp=.055,metal=False):
    for _ in range(count):
        at=rng.uniform(start,end)
        dur=rng.uniform(.010,.030)
        add_noise(x,rng,at,dur,220 if not metal else 300,
                  1600 if not metal else 2300,amp*rng.uniform(.5,1.2),
                  dur*.43,.0025)


def wet_bubbles(x,rng,start,end,count,amp=.075):
    for _ in range(count):
        at=rng.uniform(start,end)
        add_noise(x,rng,at,rng.uniform(.025,.055),40,540,
                  amp*rng.uniform(.6,1.2),.020,.004,wobble=rng.uniform(15,38))


def render_death(spec,variant):
    key,name,kind,nominal,low,high,detail=spec
    rng=np.random.default_rng(zlib.crc32(key.encode())+1000003*variant)
    length=nominal*[1,.965,1.045][variant]
    x=np.zeros(round(length*RATE))
    at=.012+rng.uniform(-.003,.003)
    big=kind in HEAVY
    add_thump(x,rng,at,1.17 if big else .72,big)
    add_noise(x,rng,at,.28 if big else .19,low,high,.50 if big else .43,
              .110 if big else .057,attack=.004)
    if kind in {'bug','mantis','queen','chaser'}:
        for j in range({'bug':2,'mantis':3,'queen':4,'chaser':3}[kind]):
            add_noise(x,rng,.050+j*.032,.060,190,1450,.11,.025,.003,wobble=42)
        add_noise(x,rng,.08,.065,300,1950,.075,.022,.003,wobble=65) # masked wing stop
        splinters(x,rng,.10,min(length*.68,.38),6+variant*2,.061,True)
        if kind=='mantis':
            for t in (.075,.145): add_noise(x,rng,t,.08,100,1550,.20,.035)
        if kind=='queen':
            add_noise(x,rng,.14,.26,45,700,.29,.10)
            splinters(x,rng,.18,.49,9,.055,True)
    elif kind in {'plant','flower','resin'}:
        for t in ([.055,.11] if kind=='plant' else [.095,.18,.29]):
            add_noise(x,rng,t,.15,60,950,.26,.060,wobble=24)
        add_noise(x,rng,.13,.22,270,1850,.13,.066) # leaves/petals
        wet_bubbles(x,rng,.18,.70 if big else .42,12 if big else 6,.080)
        if kind=='resin':
            splinters(x,rng,.15,.51,11,.066)
            add_noise(x,rng,.40,.34,35,410,.11,.15,wobble=12)
        if kind=='flower':
            add_noise(x,rng,.32,.40,35,520,.22,.17,wobble=15)
    elif kind in {'burr','wood','spore','knot','vine'}:
        if kind=='spore':
            add_noise(x,rng,.06,.31,100,1900,.15,.105)
            add_noise(x,rng,.10,.35,45,790,.20,.14)
        else:
            for t in ((.06,.13,.21) if kind in {'knot','vine'} else (.07,.12)):
                add_noise(x,rng,t,.10,80,1450,.19,.035)
            splinters(x,rng,.08,.42,12 if kind=='burr' else 8,.055)
            if kind=='burr':
                add_thump(x,rng,.08,.58)
                splinters(x,rng,.16,.35,13,.043)
            if kind=='vine': add_noise(x,rng,.19,.16,180,1400,.16,.060)
        add_noise(x,rng,.19,.27,55,850,.065,.10)
    elif kind in {'craft','claw','flamefin','pyre','firehound','imp'}:
        for j in range(2 if kind in {'craft','imp'} else 4):
            t=.07+j*.038+rng.uniform(-.007,.007)
            add_noise(x,rng,t,.075,100,1250,.15,.029)
        add_noise(x,rng,.10,.24,270,2200,.12,.071) # vent or flame hiss
        splinters(x,rng,.12,min(length*.70,.45),8+variant*2,.048,True)
        if kind=='firehound': add_noise(x,rng,.08,.12,90,1050,.21,.043)
        if kind=='imp':
            add_noise(x,rng,.08,.14,120,1600,.19,.044)
            splinters(x,rng,.18,.39,13,.038)
        if kind=='pyre': add_noise(x,rng,.24,.28,45,600,.25,.12)
    elif kind in {'skull','crucible','basalt','cinder','glass','islet'}:
        if kind=='glass':
            add_noise(x,rng,.055,.11,230,2300,.24,.031)
            splinters(x,rng,.12,.34,10,.035,True)
        elif kind=='cinder':
            splinters(x,rng,.07,.28,18,.038)
            add_noise(x,rng,.11,.21,170,1500,.12,.075)
        else:
            for t in ((.08,.17,.31) if kind in {'skull','islet'} else (.085,.15)):
                add_noise(x,rng,t,.15,45,950,.25,.057)
            wet_bubbles(x,rng,.17,.83 if kind=='skull' else .47,
                        12 if kind=='skull' else 7,.075)
            add_noise(x,rng,.20,.27,250,1800,.09,.082)
            if kind=='skull': add_noise(x,rng,.40,.43,35,390,.20,.17,wobble=11)
    elif kind in {'wraith','vulture','cannon','gunship','kiln','lance'}:
        if kind=='wraith':
            add_noise(x,rng,.10,.31,65,1050,.23,.13)
            add_noise(x,rng,.18,.28,280,1750,.09,.092)
        elif kind=='vulture':
            for t in (.08,.15,.23): add_noise(x,rng,t,.12,90,1300,.19,.047)
            splinters(x,rng,.17,.55,11,.055,True)
        elif kind=='cannon':
            add_thump(x,rng,.12,1.14,True)
            add_noise(x,rng,.13,.35,35,680,.37,.13)
            splinters(x,rng,.24,.62,12,.050,True)
        elif kind=='gunship':
            for t in (.08,.16):
                add_thump(x,rng,t,.75,True)
                add_noise(x,rng,t,.14,65,1050,.22,.052)
            splinters(x,rng,.23,.55,11,.052,True)
        elif kind=='kiln':
            add_noise(x,rng,.12,.40,40,670,.33,.16)
            wet_bubbles(x,rng,.34,.73,9,.070)
            splinters(x,rng,.24,.61,9,.047,True)
        else:
            add_noise(x,rng,.09,.24,90,1350,.24,.068)
            for t in (.18,.28): add_noise(x,rng,t,.11,110,1200,.17,.044)
            splinters(x,rng,.25,.57,10,.052,True)
        add_noise(x,rng,.43,.30,35,460,.09,.13)
    x=short_room(x,rng,.19 if big else .115,.075 if big else .050)
    x=fft_filter(x,28,3200 if big else 4600)
    n=len(x)
    fade=(np.sin(np.minimum(np.arange(n)/(RATE*.0045),1)*np.pi/2)**2*
          np.sin(np.minimum(np.arange(n)[::-1]/(RATE*.020),1)*np.pi/2)**2)
    x=(x-np.mean(x))*fade
    x=set_crest(x,-14 if big else -15)
    return match_loudness(x,-14 if big else -15.5)


def render_scream(spec,variant):
    key,duration,base,vowels,delay,texture=spec
    rng=np.random.default_rng(zlib.crc32(key.encode())+1000003*variant+314159)
    duration*= [1,.96,1.04][variant]
    n=round(duration*RATE)
    if key in {'verdant_alien','ember_alien','ember_elite_brass_vulture'}:
        x=alien_voice(rng,n,base,variant)
        if key=='ember_alien': x+=.17*noise_band(rng,n,420,2000)
    else:
        x=human_voice(rng,n,base,vowels,variant,key.split('_')[-1])
        if key=='verdant_fighter_4':
            x+=.16*noise_band(rng,n,180,1250) # buzzy chitin rasp under the voice
        if key=='ember_elite_ash_wraith':
            x+=.23*noise_band(rng,n,260,1450)
    x=spectral_filter(x,220 if key in {'verdant_alien','ember_alien'} else 300,2600)
    if key not in {'verdant_alien','ember_alien'}:
        for ms,g in ((12,.065),(23,.035)):
            d=round(ms*RATE);x[d:]+=g*x[:-d].copy()
    x=np.tanh(1.15*x)/1.15
    t=np.arange(n)/RATE
    env=np.sin(np.minimum(t/.009,1)*np.pi/2)**2
    env*=np.sin(np.minimum((duration-t)/.015,1)*np.pi/2)**2
    env*=np.exp(-t/(.24 if key in {'verdant_alien','ember_alien'} else .42))
    x=spectral_filter(x*env,210 if key in {'verdant_alien','ember_alien'} else 280,2750)
    x*=env
    x-=np.mean(x)*env
    return x*TARGET_PEAK/(np.max(np.abs(x))+1e-30)


def pcm_stats(x,voice=False):
    p=np.abs(np.fft.rfft(x))**2
    f=np.fft.rfftfreq(len(x),1/RATE)
    size,hop=882,220
    frames=np.lib.stride_tricks.sliding_window_view(x,size)[::hop]
    powers=np.abs(np.fft.rfft(frames*np.hanning(size),axis=1))**2
    energy=powers.sum(axis=1)
    active=energy>energy.max()*10**(-48/10)
    tonal=active & (powers.max(axis=1)/(energy+1e-30)>.45)
    run=max((sum(1 for _ in g) for flag,g in itertools.groupby(tonal) if flag),default=0)*hop/RATE*1000
    # Match the approved Space check: report the 10-90% rise of the explicitly
    # authored onset window. Secondary cracks can be louder than the onset and
    # make a peak-relative waveform estimate describe a later event instead.
    onset=np.sin(np.minimum(np.arange(round(.030*RATE))/(RATE*(.009 if voice else .004)),1)*np.pi/2)**2
    i10=np.flatnonzero(onset>=.10)[0]
    i90=np.flatnonzero(onset>=.90)[0]
    attack=(i90-i10)/RATE*1000
    return dict(duration=len(x)/RATE,peak=20*np.log10(np.max(np.abs(x))+1e-30),
                a=short_a_level(x),rms=20*np.log10(np.sqrt(np.mean(x*x))+1e-30),
                centroid=float(np.sum(f*p)/(np.sum(p)+1e-30)),
                hf=float(np.sum(p[f>4000])/(np.sum(p)+1e-30)),
                attack=attack,tonal=run,
                clipping=int(np.count_nonzero(np.abs(x)>=.9999)))


def resonance_run(x):
    """Persistent 1-3 kHz narrow lines; rejects moving, brief random peaks."""
    nfft,hop=1764,441
    frames=np.lib.stride_tricks.sliding_window_view(x,nfft)[::hop]
    power=np.abs(np.fft.rfft(frames*np.hanning(nfft),axis=1))**2
    energy=power.sum(axis=1)
    freqs=np.fft.rfftfreq(nfft,1/RATE)
    indexes=np.flatnonzero((freqs>=1000)&(freqs<=3000))
    db=10*np.log10(power+1e-25)
    narrow=np.zeros((len(frames),len(indexes)),dtype=bool)
    for k,j in enumerate(indexes):
        near=(freqs>=freqs[j]-250)&(freqs<=freqs[j]+250)&(
             (freqs<freqs[j]-50)|(freqs>freqs[j]+50))
        narrow[:,k]=(db[:,j]-np.median(db[:,near],axis=1)>18)&(energy>energy.max()*1e-4)
    # ±1 bin drift is still one ringing partial.
    streak=np.zeros(len(indexes),dtype=int)
    longest=0
    for row in narrow:
        old=streak.copy()
        for k,flag in enumerate(row):
            streak[k]=(max(old[max(0,k-1):min(len(old),k+2)])+1) if flag else 0
        longest=max(longest,int(streak.max()))
    return longest*hop/RATE*1000


def voice_mix(death,voice,delay):
    # Match perceived voice to the death cue at the playback gain, just as the
    # approved Space corrective pass did. The file itself remains -9 dBFS.
    gain=10**((short_a_level(death)-12-short_a_level(voice))/20)
    start=round(delay*RATE)
    mixed=np.zeros(max(len(death),start+len(voice)))
    mixed[:len(death)]=death
    mixed[start:start+len(voice)]+=gain*voice
    return mixed,gain


def make_sheet(world,rows):
    width,rh=1420,118
    img=np.full((rh*len(rows)+70,width,3),(16,23,29),dtype=np.uint8)
    label(img,18,16,f'{world} WORLD / DEATH AND VOICE / VARIANT 0',(236,228,195),3)
    label(img,18,44,'WAVEFORM                       SPECTROGRAM 0-4 KHZ',scale=2)
    for i,(name,x,m) in enumerate(rows):
        y0=70+i*rh
        img[y0:y0+1]=(51,67,77)
        label(img,18,y0+10,name,(235,226,196),2)
        label(img,18,y0+33,f"{m['duration']:.2f}S A {m['a']:.1f}DB PEAK {m['peak']:.1f}DB",scale=2)
        label(img,18,y0+55,f"C {m['centroid']:.0f}HZ HF {m['hf']*100:.2f}%",scale=2)
        x0,wv=440,340
        img[y0+12:y0+103,x0:x0+wv]=(24,34,41)
        for j,block in enumerate(np.array_split(x,wv)):
            a=int(np.clip(57-np.max(block)*45,12,103))
            b=int(np.clip(57-np.min(block)*45,12,103))
            img[y0+a:y0+b+1,x0+j]=(96,189,177)
        nfft=1024; sw=610;sh=91
        hop=max(1,(len(x)-nfft)//sw)
        frames=np.lib.stride_tricks.sliding_window_view(x,nfft)[::hop][:sw]
        mag=np.abs(np.fft.rfft(frames*np.hanning(nfft),axis=1))
        db=20*np.log10(mag+1e-9);db-=np.max(db)
        bins=np.clip(np.round(np.linspace(0,4000,sh)*nfft/RATE).astype(int),0,mag.shape[1]-1)
        v=np.clip((db[:,bins]+65)/65,0,1).T[::-1]
        colors=np.empty((sh,len(frames),3),dtype=np.uint8)
        colors[:,:,0]=(25+210*v).astype('u1')
        colors[:,:,1]=(32+160*v**.7).astype('u1')
        colors[:,:,2]=(40+80*v**.5).astype('u1')
        img[y0+12:y0+12+sh,800:800+len(frames)]=colors
    png_write(ROOT/f'sheet_{world}.png',img)


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    records=[];failures=[];sheets={'verdant':[],'ember':[]};aud={'verdant':[],'ember':[]}
    print('file dur_s peak_db A50_db rms_db centroid_hz HF4k_pct attack_ms tonal_ms ring1-3k_ms clips max_xcorr mix_peak_db scream_gain')
    for spec in DEATHS:
        key,name,kind,nominal,low,high,detail=spec
        world=key.split('_')[0]
        death=[]
        for variant in range(3):
            path=OUT/f'{key}_{variant}.wav'
            write_wav(path,render_death(spec,variant))
            death.append(read_wav(path))
        corr=max(max_xcorr(death[a],death[b]) for a,b in ((0,1),(0,2),(1,2)))
        if corr>=.6: failures.append((key,'death xcorr',corr))
        aud[world].append(death[0])
        for v,x in enumerate(death):
            m=pcm_stats(x);r=resonance_run(x)
            target=-14 if kind in HEAVY else -15.5
            checks=(abs(m['duration']/nominal-1)<=.20,abs(m['a']-target)<=1.5,
                    m['peak']<=-2,m['centroid']<(900 if kind in HEAVY else 1800),
                    m['hf']<.12,2<=m['attack']<=8,m['tonal']<=100,r<=100,
                    m['clipping']==0,corr<.6)
            if not all(checks): failures.append((f'{key}_{v}',checks,m,r))
            records.append((f'{key}_{v}.wav',m,r,corr,None,None))
            if v==0:sheets[world].append((name,x,m))
        if key in VOICE_BY_KEY:
            voice_spec=VOICE_BY_KEY[key]
            voice=[]
            for variant in range(3):
                path=OUT/f'{key}_scream_{variant}.wav'
                write_wav(path,render_scream(voice_spec,variant))
                voice.append(read_wav(path))
            voice_corr=max(max_xcorr(voice[a],voice[b]) for a,b in ((0,1),(0,2),(1,2)))
            if voice_corr>=.6:failures.append((key,'voice xcorr',voice_corr))
            for v,x in enumerate(voice):
                m=pcm_stats(x,True);r=resonance_run(x)
                mixed,gain=voice_mix(death[v],x,voice_spec[4])
                mixpeak=20*np.log10(np.max(np.abs(mixed))+1e-30)
                relative=short_a_level(death[v])-short_a_level(x*gain)
                checks=(abs(m['duration']/voice_spec[1]-1)<=.20,
                        abs(m['peak']+9)<=.05,m['centroid']<(1700 if key in {'verdant_alien','ember_alien','ember_elite_brass_vulture'} else 1500),
                        m['hf']<.12,m['tonal']<=100,r<=100,m['clipping']==0,
                        10<=relative<=14,mixpeak<0,voice_corr<.6)
                if not all(checks):failures.append((f'{key}_scream_{v}',checks,m,r,mixpeak,gain))
                records.append((f'{key}_scream_{v}.wav',m,r,voice_corr,mixpeak,gain))
                if v==0:
                    sheets[world].append((name+' VOICE',x,m))
                    aud[world].extend((x,mixed))
    header='| File | s | Peak dBFS | A50 dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | 1-3 kHz ring ms | Clips | Max xcorr | Mix peak dBFS | Scream gain |'
    rule='|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|'
    notes=['# Verdant and Ember death sounds and scream layers','',
           'Deterministic mono 44.1 kHz, 16-bit PCM. The existing Space builder toolkit was copied into this standalone script: noise/filter/envelope/thump/short room, A-weighted 50 ms matching, vocal source/formants, PNG sheet and delivered-PCM verification. No external sound library is used.','',
           'Normal death target: -15.5 dBFS A-weighted loudest 50 ms; big and elite target: -14 dBFS. Voice files peak at -9 dBFS. For playback, use each row’s scream gain after its onset delay: this places the voice 12 dB below the matching death cue in the A-weighted loudest window. The audition mixes apply that gain. Attack is the 10–90% rise of the authored onset envelope, following the approved Space verification method; the waveform includes later and sometimes louder material strikes. Tonal runs use delivered PCM in active 20 ms Hann frames, 5 ms hops, with a dominant bin above 45% energy. Ring runs track 1–3 kHz lines 18 dB above local median in 40 ms frames and 10 ms hops. HF is energy above 4 kHz. Max xcorr is the largest pairwise full-lag correlation among three variants.','',
           '## Material and unit choices','',
           '| World | Unit | Key | Physical event | Scream |',
           '|---|---|---|---|---|']
    for key,name,kind,nominal,low,high,detail in DEATHS:
        voice=VOICE_BY_KEY.get(key)
        notes.append(f'| {key.split("_")[0].title()} | {name} | `{key}` | {detail}; dark {low}-{high} Hz body plus short room | {voice[5] if voice else "none"} |')
    notes+=['','### Scream decisions','',
            'Verdant fighters have tiny pilots within insect-machine shells; the Hornet Queen has a lower strained voice with a small noise rasp. Snap Sprout is a plant creature and Resin Warden reads as an inhabited resin guardian. Ember fighters and the craft-shaped Coalrunner and Sunstoke plausibly have pilots. Ember Imp, Ash Wraith and Brass Vulture read as creatures, so they have brief creature voices. Cauterizer is a siege cannon and Kilnback is an armoured slag hauler in their definitions and sprites; both get mechanical deaths only. Chasers, bigs, mines and rocks have no voices.','',
            '### Verification of delivered PCM','',header,rule]
    for filename,m,r,corr,mixpeak,gain in records:
        line=(f'| `{filename}` | {m["duration"]:.3f} | {m["peak"]:.2f} | {m["a"]:.2f} | {m["rms"]:.2f} | {m["centroid"]:.0f} | {m["hf"]*100:.2f} | {m["attack"]:.1f} | {m["tonal"]:.1f} | {r:.1f} | {m["clipping"]} | {corr:.3f} | {mixpeak if mixpeak is not None else "—" if mixpeak is None else mixpeak} | {gain if gain is not None else "—"} |')
        # Reformat optional numeric cells while preserving a compact full table.
        if mixpeak is not None:
            line=line.replace(f'{mixpeak} | {gain} |',f'{mixpeak:.2f} | {gain:.3f} |')
        notes.append(line)
        print(filename,*(round(m[k],3) for k in ('duration','peak','a','rms','centroid','hf','attack','tonal')),round(r,1),m['clipping'],round(corr,3),round(mixpeak,2) if mixpeak is not None else '-',round(gain,3) if gain is not None else '-')
    for world in ('verdant','ember'):
        make_sheet(world,sheets[world])
        write_wav(ROOT/f'audition_{world}.wav',join_with_gaps(aud[world]))
    notes+=['','### Audition','',
            '`audition_verdant.wav` and `audition_ember.wav` play variant zero in roster order with 1.2 s gaps. For each unit with a voice, the death is followed by the voice alone and then its death/voice mix. The PNG sheets show every variant-zero death and available voice as waveforms and spectrograms.','',
            'These are synthesized approximations and need a listening pass on phone speakers. Ear-tune Hornet Queen and Brass Vulture first for a creature rather than buzzer edge; then Bloom Maw and Magma Skull for physical scale and clear material differences. The noise-led source, low spectral centers, brief masked wing bursts, short damped room, strict HF check and persistent 1–3 kHz ring check protect against beepiness and harshness.','']
    (ROOT/'sound_notes_verdant_ember.md').write_text('\n'.join(notes))
    if failures:
        for item in failures:print('FAIL',item)
        raise SystemExit(f'{len(failures)} checks failed')
    print(f'PASS {len(records)} delivered WAVs; all loudness, spectral, variant, clip and mix checks')


if __name__=='__main__': main()
