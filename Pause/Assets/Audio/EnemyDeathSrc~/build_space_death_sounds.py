#!/usr/bin/env python3
"""Build and verify the 45 Space World death cues. Requires NumPy only.

Run from anywhere: python3 build_space_death_sounds.py
All source parameters, diagnostics and audition files stay beside this script.
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
OUT = ROOT.parent / "Resources" / "Audio" / "EnemyDeath"
GAP = np.zeros(round(1.2 * RATE), dtype=np.float64)
PEAK = 10 ** (-3 / 20)

# key, character, material, nominal seconds, spectral character, layers/structure.
# lo/hi are the main noise band's -3 dB region; the global 6 kHz shelf is below.
SOUNDS = [
    ("space_fighter_1", "Needle", "needle", .30, 220, 1800, "single dry snap, air fizz"),
    ("space_fighter_2", "Steel Claw", "steel", .45, 140, 1750, "metal crunch, short clang, shrapnel"),
    ("space_fighter_3", "Twin Claw", "twin", .50, 95, 1500, "two 60 ms spaced crunches, steel tear"),
    ("space_fighter_4", "Warden", "shield", .60, 75, 1100, "low shield whump, muffled hull, energy sigh"),
    ("space_chaser", "Steel Hound", "servo", .55, 95, 1500, "servo wind down, clunk, sparks"),
    ("space_alien", "Bile Mite", "alien", .55, 85, 1300, "wet splat, low bubbles, slimy tail"),
    ("space_big", "Bastion", "big", 1.10, 45, 740, "sub thump, dull boom, tearing plates, reactor thud"),
    ("space_mine", "Rail Mine", "mine", .50, 80, 1450, "bass thoomp, arc, tiny shrapnel"),
    ("space_rock_crater", "Beacon Rock", "crater", .50, 75, 1400, "one crack, a few chunks, dust"),
    ("space_rock_cluster", "Cluster Rock", "cluster", .65, 100, 1600, "staggered cracks, many stones, dust"),
    ("space_rock_dark", "Coal Rock", "coal", .50, 48, 950, "dense thud, coal crumble"),
    ("space_elite_eventide_bastion", "Eventide Bastion", "eventide", .90, 45, 860, "shield failure, large hull explosion"),
    ("space_elite_orbit_reaver", "Orbit Reaver", "reaver", .70, 85, 1150, "tearing metal, quick whoosh burst"),
    ("space_elite_rift_lancer", "Rift Lancer", "rift", .80, 80, 1250, "rising noise charge, crack, ion crackle"),
    ("space_elite_singularity_hauler", "Singularity Hauler", "hauler", 1.00, 40, 670, "inward noise swell, boom, warbling low drone"),
]
BIG = {"big", "eventide", "reaver", "rift", "hauler"}
LAYER_NOTES = {
    "needle": "Small low pressure pop; short dry mid-band snap; softened airy noise fizz; two or three minute fragments.",
    "steel": "Low punch; dense gunmetal noise crunch; three brief inharmonic plate modes; sparse shrapnel rattle.",
    "twin": "Two separately textured low crunches about 60 ms apart; deeper noise body; short torn plate resonance; metal fragments.",
    "shield": "Low electrical noise whump; delayed low thump and muffled hull burst; progressively darker noise sigh; minor metal scatter.",
    "servo": "Eight overlapping descending noise bands with slow modulation; late mechanical clunk; a few low-level sparks.",
    "alien": "Low organic thump; two wobbly filtered wet-noise splats; small soft bubble bursts; darker slimy burble tail.",
    "big": "Deep low thump; long dark boom; three tearing metal-plate bursts with brief modes; settling fragments; reactor thud-hum noise.",
    "mine": "Compact bass thoomp; band-limited arc burst; scattered short crackles; tiny metal fragments.",
    "crater": "One low split crack; five or six larger mineral fragments; lightly filtered dust tail.",
    "cluster": "Three staggered rock fractures; many small stone grains; a longer dusty tail.",
    "coal": "Dense low coal thud; dark crumbly crackle; small close-spaced mineral bits; subdued dust.",
    "eventide": "Shield-pressure failure in dark noise; delayed heavy low hull detonation; brief torn plate mode; settling metal.",
    "reaver": "Fast low punch; sharp filtered metal tear; four overlapping descending whoosh-noise pieces; a short plate ring and fragments.",
    "rift": "Five ascending filtered-noise charge bands; low discharge and broad crack; short ion crackles; dim low fade.",
    "hauler": "Five inward swelling dark-noise layers; delayed heavy boom; modulated low pressure drone; sparse debris and dying hum.",
}


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
    """Three masked inharmonic plate modes, each ending before 80 ms."""
    start = round(at * RATE)
    n = min(round(.072 * RATE), len(x) - start)
    if n <= 0:
        return
    t = np.arange(n) / RATE
    freqs = base * np.array([1, 1.48, 2.31]) * rng.uniform(.91, 1.09, 3)
    modes = sum(np.sin(2 * np.pi * f * t + rng.uniform(-np.pi, np.pi))
                * np.exp(-t / d) / (i + 1) for i, (f, d) in
                enumerate(zip(freqs, [.022, .016, .010])))
    x[start:start+n] += amp * modes * envelope(n, .003, .060)


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


def render(spec, variant):
    key, name, kind, nominal, low, high, description = spec
    # Separate reproducible seeds and different timings make each round-robin real.
    seed = zlib.crc32(key.encode()) + 1000003 * variant
    rng = np.random.default_rng(seed)
    length = nominal * [1, .965, 1.045][variant]
    n = round(length * RATE)
    x = np.zeros(n)
    jitter = rng.uniform(-.006, .006)
    impact = .012 + jitter

    if kind == "needle":
        add_thump(x, rng, impact, .46)
        add_noise(x, rng, impact, .075, low, high, .39, .020)
        add_noise(x, rng, impact+.019, .115, 450, 2500, .105, .036)
        add_grains(x, rng, .038, .095, 2+variant, "metal", .038)
    elif kind == "steel":
        add_thump(x, rng, impact, .84)
        add_noise(x, rng, impact, .19, low, high, .43, .048)
        add_modal(x, rng, impact+.012, .065, 350)
        add_grains(x, rng, .075, .23, 6+variant, "metal", .075)
    elif kind == "twin":
        for at, w in ((impact, .86), (impact+.060+rng.uniform(-.004,.004), .70)):
            add_thump(x, rng, at, w, True)
            add_noise(x, rng, at, .13, low, high, .37*w, .046)
        add_noise(x, rng, impact+.105, .16, 200, 1500, .22, .057)
        add_modal(x, rng, impact+.073, .052, 260)
        add_grains(x, rng, .12, .30, 8+variant, "metal", .065)
    elif kind == "shield":
        add_noise(x, rng, .010, .20, 38, 450, .31, .085, .005)
        add_thump(x, rng, impact+.075, 1.0, True)
        add_noise(x, rng, impact+.078, .22, 75, 1000, .43, .080)
        # A falling low-pass noise sigh, not a tonal sweep.
        for j in range(5):
            add_noise(x, rng, .22+j*.040, .080, 70, 920-j*145, .065, .045)
        add_grains(x, rng, .14, .30, 5, "metal", .055)
    elif kind == "servo":
        # Repeated moving noise bands imply rotation without a sustained pitch.
        for j in range(8):
            at = .012+j*.034
            center = 1030 * np.exp(-j*.18)
            add_noise(x, rng, at, .085, center*.48, center*1.32,
                      .095*(1-j/11), .047, wobble=24-j*1.3)
        add_thump(x, rng, .275+jitter, .56)
        add_noise(x, rng, .278+jitter, .095, 100, 1000, .24, .033)
        add_grains(x, rng, .31, .43, 5+variant, "metal", .040)
    elif kind == "alien":
        add_thump(x, rng, impact, .45, True)
        add_noise(x, rng, impact, .20, 60, 1000, .34, .073, wobble=31)
        add_noise(x, rng, impact+.055, .19, 90, 1450, .29, .064, wobble=22)
        for j in range(4+variant):
            at = rng.uniform(.10, .32)
            add_noise(x, rng, at, .045, 85, rng.uniform(420, 850), .085, .014)
        add_noise(x, rng, .23, .20, 55, 680, .065, .085, wobble=14)
    elif kind == "big":
        add_thump(x, rng, impact, 1.28, True)
        add_noise(x, rng, impact, .49, 38, 680, .54, .185)
        add_noise(x, rng, .091, .32, 45, 950, .31, .115)
        for at in (.17, .28, .41):
            add_noise(x, rng, at+rng.uniform(-.025,.025), .16, 75, 1100, .17, .062)
            add_modal(x, rng, at, .043, 190)
        add_grains(x, rng, .27, .73, 17+variant*2, "metal", .060)
        add_noise(x, rng, .46, .40, 35, 240, .10, .145, wobble=12)
    elif kind == "mine":
        add_thump(x, rng, impact, 1.06, True)
        add_noise(x, rng, impact, .16, 60, 1250, .38, .045)
        for j in range(6+variant):
            at = rng.uniform(.044, .24)
            add_noise(x, rng, at, .025, 350, 2600, .075, .009)
        add_grains(x, rng, .13, .29, 4, "metal", .047)
    elif kind in {"crater", "cluster", "coal"}:
        dark = kind == "coal"
        add_thump(x, rng, impact, .83 if dark else .68, dark)
        cracks = [impact] if kind != "cluster" else [impact, .075, .133]
        for j, at in enumerate(cracks):
            add_noise(x, rng, at, .12, low, high, (.45 if j == 0 else .27), .036)
        if kind == "coal":
            add_noise(x, rng, .09, .21, 50, 720, .21, .082)
            add_grains(x, rng, .07, .33, 10+variant, "rock", .035)
        elif kind == "crater":
            add_grains(x, rng, .07, .25, 5+variant, "rock", .080)
        else:
            add_grains(x, rng, .10, .45, 20+variant*2, "rock", .058)
        add_noise(x, rng, .16, .22 if kind != "cluster" else .34, 100, 850,
                  .065, .095 if kind != "cluster" else .14)
    elif kind == "eventide":
        add_noise(x, rng, .010, .19, 40, 420, .27, .075, .005)
        add_noise(x, rng, .085, .14, 90, 1350, .28, .046)
        add_thump(x, rng, .135, 1.18, True)
        add_noise(x, rng, .14, .39, 45, 850, .52, .143)
        add_modal(x, rng, .185, .047, 220)
        add_grains(x, rng, .25, .57, 13+variant, "metal", .060)
        add_noise(x, rng, .39, .30, 45, 450, .09, .113)
    elif kind == "reaver":
        add_thump(x, rng, impact, .76)
        add_noise(x, rng, impact, .15, 90, 1350, .42, .050)
        for j in range(4):
            add_noise(x, rng, .065+j*.027, .095, 100, 1250-j*130, .16, .036)
        add_modal(x, rng, .064, .065, 360)
        add_noise(x, rng, .16, .19, 100, 1050, .16, .062)
        add_grains(x, rng, .17, .39, 10+variant, "metal", .054)
    elif kind == "rift":
        # Rising charge is a changing filtered-noise texture, never a chirp.
        for j in range(5):
            add_noise(x, rng, .011+j*.027, .074, 100+j*82, 500+j*155,
                      .066+j*.016, .045, .004)
        add_thump(x, rng, .147+jitter, .83)
        add_noise(x, rng, .150+jitter, .13, 100, 1850, .43, .038)
        for j in range(9+variant):
            at = rng.uniform(.18, .48)
            add_noise(x, rng, at, .021, 320, 2200, .057, .008)
        add_noise(x, rng, .31, .29, 55, 850, .09, .106)
    elif kind == "hauler":
        for j in range(5):
            add_noise(x, rng, .012+j*.028, .14, 35, 400-j*45,
                      .050+j*.018, .105, .006, "swell")
        add_thump(x, rng, .174, 1.28, True)
        add_noise(x, rng, .179, .39, 38, 650, .54, .151)
        add_noise(x, rng, .26, .43, 35, 430, .18, .180, wobble=12)
        add_grains(x, rng, .34, .60, 8+variant, "metal", .041)
        add_noise(x, rng, .50, .32, 35, 220, .075, .145, wobble=9)

    x = short_room(x, rng, .18 if kind in BIG else .105,
                   .075 if kind in BIG else .052)
    # A final gentle 6 kHz shelf and 2-5 kHz restraint after convolution.
    x = fft_filter(x, 28, 4800 if kind not in BIG else 3400)
    x -= np.mean(x)
    fade_in = np.sin(np.minimum(np.arange(n) / (RATE * .0045), 1) * np.pi/2) ** 2
    fade_out = np.sin(np.minimum(np.arange(n)[::-1] / (RATE * .020), 1) * np.pi/2) ** 2
    x *= fade_in * fade_out
    x = set_crest(x, -17 if kind in BIG else -20)
    return x


def set_crest(x, rms_db):
    """Small memoryless dynamic curve sets both requested peak and RMS."""
    target_ratio = PEAK / 10 ** (rms_db / 20)
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
    y -= np.mean(y) * dc_window / np.mean(dc_window)
    y *= PEAK / np.max(np.abs(y))
    return y


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


def make_sheet(rows):
    width, rh = 1480, 120
    img = np.zeros((rh*15+70,width,3),dtype=np.uint8)
    img[:] = (16,23,29)
    label(img, 18, 16, 'SPACE WORLD DEATH SOUNDS / VARIANT 0', (236,228,195), 3)
    label(img, 18, 45, 'WAVEFORM                    SPECTROGRAM 0-6 KHZ / DARKER = QUIETER', (150,170,184), 2)
    for i,(spec,x,m) in enumerate(rows):
        y0 = 70+i*rh
        img[y0:y0+1,:,:] = (51,67,77)
        label(img, 18, y0+10, spec[1], (235,226,196), 2)
        label(img, 18, y0+31, f"{m['duration']:.2f}S  PK {m['peak']:.1f}  RMS {m['rms']:.1f} DB", scale=2)
        label(img, 18, y0+51, f"C {m['centroid']:.0f}HZ  HF {m['hf']*100:.1f}%", scale=2)
        # Waveform: min/max per horizontal pixel preserves short transients.
        x0,wv = 445, 365
        img[y0+12:y0+104,x0:x0+wv] = (24,34,41)
        blocks = np.array_split(x,wv)
        for j,b in enumerate(blocks):
            a = int(np.clip(58-np.max(b)*41,15,101))
            btm = int(np.clip(58-np.min(b)*41,15,101))
            img[y0+a:y0+btm+1,x0+j] = (96,189,177)
        # NumPy STFT; log intensity at 0-6 kHz with fixed dB window per row.
        x1,sw,sh = 837,620,92
        nfft,hop = 1024,max(1,(len(x)-1024)//sw)
        frames = np.lib.stride_tricks.sliding_window_view(x, nfft)[::hop][:sw]
        power = np.abs(np.fft.rfft(frames*np.hanning(nfft),axis=1))
        db = 20*np.log10(power+1e-9)
        db -= np.max(db)
        bins = np.linspace(0,6000,sh)
        indices = np.clip(np.round(bins*nfft/RATE).astype(int),0,power.shape[1]-1)
        intensity = np.clip((db[:,indices]+67)/67,0,1).T[::-1]
        colors = np.empty((sh,len(frames),3),dtype=np.uint8)
        colors[:,:,0] = (25+210*intensity).astype('u1')
        colors[:,:,1] = (32+160*intensity**.7).astype('u1')
        colors[:,:,2] = (40+80*intensity**.5).astype('u1')
        img[y0+12:y0+12+sh,x1:x1+len(frames)] = colors
    png_write(ROOT/'sheet_space.png',img)


def main():
    OUT.mkdir(parents=True,exist_ok=True)
    rows = []
    all_cues = []
    first_cues = []
    failures = []
    print('key variant dur_s peak_db rms_db centroid_hz HF_gt4k_pct attack_ms tonal_ms clips max_pair_xcorr')
    for spec in SOUNDS:
        key,name,kind,nominal,low,high,description=spec
        variants = []
        measures = []
        for v in range(3):
            path = OUT/f'{key}_{v}.wav'
            write_wav(path,render(spec,v))
            x = read_wav(path)  # verify delivered PCM, not intermediate floats
            m = stats(x)
            variants.append(x); measures.append(m); all_cues.append(x)
            if v == 0:
                first_cues.append(x); rows.append((spec,x,m))
        cors = [max_xcorr(variants[a],variants[b]) for a,b in ((0,1),(0,2),(1,2))]
        maxcor = max(cors)
        for v,m in enumerate(measures):
            print(f"{key:34} {v} {m['duration']:.3f} {m['peak']:.2f} {m['rms']:.2f} "
                  f"{m['centroid']:.0f} {m['hf']*100:.2f} {m['attack']:.2f} {m['tonal']:.1f} "
                  f"{m['clipping']} {maxcor:.3f}")
            limits = [abs(m['duration']/nominal-1)<.12, abs(m['peak']+3)<.15,
                      abs(m['rms']-(-17 if kind in BIG else -20))<.7,
                      m['centroid']<(900 if kind in BIG else 1800), m['hf']<.12,
                      2<=m['attack']<=8, m['tonal']<=80,
                      m['clipping']==0, maxcor<.6]
            if not all(limits):
                failures.append((key,v,limits))
        if max(len(a) for a in variants)/min(len(a) for a in variants)>1.2:
            failures.append((key,'length spread'))
    write_wav(ROOT/'audition_space.wav',join_with_gaps(first_cues))
    write_wav(ROOT/'audition_space_all.wav',join_with_gaps(all_cues))
    make_sheet(rows)
    notes = ['# Space World enemy death sounds','',
             '44.1 kHz, mono, 16-bit PCM. Three deterministic round-robin variants per key. '
             'The measured figures below are read back from the final integer PCM WAVs. '
             'Peak and RMS are dBFS; centroid is Hz; HF is energy above 4 kHz. '
             'Attack is the 10%-90% rise of the controlled primary impact envelope; '
             'pre-impact swells on shield, ion and gravity cues are excluded. '
             'Tonal is the longest run of 20 ms Hann frames (5 ms hop) whose strongest FFT bin '
             'exceeds 45% of active-frame energy. Cross-correlation is the maximum normalized '
             'full-lag value for any variant pair.\n',
             'All cues have a masked short low thump, filtered mid-band material body, '
             'specific debris/detail and a low-level 105-180 ms FFT-convolved dark room tail. '
             'All final samples receive a 20 ms fade and DC removal.\n']
    for spec,_,_ in rows:
        key,name,kind,nominal,low,high,description=spec
        notes += [f'## {name} — `{key}`','',f'Evokes {description}. '
                  f'Main body noise band: {low}-{high} Hz. '
                  f'Nominal duration {nominal:.2f} s. '+
                  ('Heavy/elite RMS target -17 dBFS.' if kind in BIG else 'Standard RMS target -20 dBFS.'),'',
                  f'Layers: {LAYER_NOTES[kind]}','',
                  '| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |',
                  '|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
        for v in range(3):
            m=stats(read_wav(OUT/f'{key}_{v}.wav'))
            notes.append(f"| {v} | {m['duration']:.3f} | {m['peak']:.2f} | {m['rms']:.2f} | "
                         f"{m['centroid']:.0f} | {m['hf']*100:.2f} | {m['attack']:.2f} | "
                         f"{m['tonal']:.1f} | {m['clipping']} |")
        cors=[max_xcorr(read_wav(OUT/f'{key}_{a}.wav'),read_wav(OUT/f'{key}_{b}.wav'))
              for a,b in ((0,1),(0,2),(1,2))]
        notes += ['',f'Maximum variant cross-correlation: {max(cors):.3f}.','']
    notes += ['## Audition and integration','',
              '`audition_space.wav` plays variant 0 in roster order. '
              '`audition_space_all.wav` plays all three variants per key in roster order. '
              'Adjacent cues are separated by exactly 1.2 s of silence. '
              'These files and the sheet remain in `EnemyDeathSrc~`, which Unity ignores.','',
              'The current `EnemyDeathAudio.cs` still synthesizes cues at runtime. '
              'Per the asset-only scope, it was not edited; the game will need a later '
              'integration change to load these Resources WAVs.','']
    (ROOT/'sound_notes_space.md').write_text('\n'.join(notes))
    if failures:
        raise SystemExit(f'FAILED checks: {failures}')
    print('PASS: 45 WAVs, all spectral/loudness/attack/tonality/variant checks')


if __name__ == '__main__':
    main()
