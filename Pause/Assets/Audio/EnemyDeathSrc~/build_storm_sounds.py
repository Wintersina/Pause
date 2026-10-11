#!/usr/bin/env python3
"""Storm's own sounds: the thunder layer every Storm death wears, and the themed attack cues that nothing else says.

Everything else Storm plays is REUSED (EnemyDeathAudio.DeathAlias: donor clips from other worlds, pitched; Storm strikes fall
back to Tide's thunder, see AttackAudio.Fallback). Authored here is only what is missing: a crack of thunder with a rumble and a
sizzle, a crackling arc, a gust of wind and a rattle of hail. Noise, impacts and grains only: no sustained tones, no chirps,
no sine sweeps, no coin or beep. Same toolkit and checks as build_tide_sounds.py. Mono 44.1 kHz 16-bit.

  storm_crack_0..2   (EnemyDeath/)  thunder crack + low rumble + sizzle, quiet, layered under every aliased Storm death
  arc / gust / hail _0..2  (Resources/Audio/Attack/Storm/)
      arc    lightning arc / ion bolt: a dry crackle (filtered-noise grains), a hard thump, a short rumble
      gust   gust wave: wind that swells and rushes past, soft front, no whistle
      hail   hail volley: a hard-grain clatter of pellets, a low drumming bed
"""
from __future__ import annotations
import itertools, zlib
from pathlib import Path
import numpy as np
import build_verdant_ember_sounds as b
import build_verdant_elite_sounds as e

ROOT = Path(__file__).resolve().parent
RES = ROOT.parent / 'Resources' / 'Audio'
RATE = b.RATE
N = e.noise
G = e.grains


def finish(x, rng, room, cutoff, peak_db, rms_db, low=27, fade=.025, atk=.004):
    x = b.short_room(x, rng, room, .05)
    x = b.fft_filter(x, low, cutoff)
    n = len(x); i = np.arange(n)
    edge = np.sin(np.minimum(i / (RATE * atk), 1) * np.pi / 2) ** 2
    edge *= np.sin(np.minimum(i[::-1] / (RATE * fade), 1) * np.pi / 2) ** 2
    x = (x - np.mean(x)) * edge
    target = 10 ** ((peak_db - rms_db) / 20)
    def shaped(g):
        y = np.sign(x) * np.abs(x) ** g * edge
        return y - np.mean(y) * edge
    lo, hi = .1, 3.0
    for _ in range(36):
        mid = (lo + hi) / 2; y = shaped(mid)
        crest = np.max(np.abs(y)) / (np.sqrt(np.mean(y * y)) + 1e-30)
        if crest < target: lo = mid
        else: hi = mid
    y = shaped((lo + hi) / 2)
    return y * 10 ** (peak_db / 20) / (np.max(np.abs(y)) + 1e-30)


def crackle(x, rng, start, end, count, lo, hi, amp):
    """A dry electrical crackle: many 1-5 ms grains of band-limited noise at random times (never a tone)."""
    for _ in range(count):
        at = rng.uniform(start, end) ** 1.0
        d = rng.uniform(.0015, .005)
        N(x, rng, at, d, lo, hi, amp * rng.uniform(.3, 1.2), d * .4)


# name -> (seconds, peak, rms, centroid limit Hz, HF share limit, check attack)
SPEC = {
    'crack':  (.75, -6, -21, 1500, .10, True),
    'arc':    (.55, -4, -18, 2600, .40, True),
    'gust':   (1.00, -5, -19, 1400, .08, False),
    'hail':   (.70, -4, -18, 2600, .40, True),
}


def render(kind, variant):
    dur = SPEC[kind][0]
    rng = np.random.default_rng(zlib.crc32(('storm_' + kind).encode()) + 7919 * variant + 616161)
    x = np.zeros(round(dur * (1, .96, 1.04)[variant] * RATE))
    v = variant
    atk = .004; fade = .025
    if kind == 'crack':
        N(x, rng, 0, .07, 300, 4200, .85, .016)                           # the crack
        b.add_thump(x, rng, .003, 1.2, True)
        N(x, rng, .01, .22, 800, 4200, .22, .06, 29)                      # sizzle
        crackle(x, rng, .02, .30, 18 + v * 3, 700, 3800, .10)
        N(x, rng, .04, .62, 30, 240, .55, .32, 6)                         # rumble
        N(x, rng, .12, .50, 60, 480, .22, .26, 9)
        b.add_thump(x, rng, .30 + .03 * v, .30, True)                      # the far rolling echo
        room, cut = .20, 3400
    elif kind == 'arc':
        b.add_thump(x, rng, .002, .70, True)                              # the discharge punch
        N(x, rng, 0, .05, 700, 5000, 1.0, .011)                           # snap
        crackle(x, rng, .004, .30, 30 + v * 4, 900, 5000, .55)            # crackle
        N(x, rng, .01, .30, 120, 1100, .34, .075, 17)                     # body
        N(x, rng, .06, .42, 35, 280, .34, .15, 7)                         # rumble tail
        room, cut = .08, 4200
    elif kind == 'gust':
        n = len(x)
        t = np.arange(n) / RATE
        env = np.sin(np.pi * np.clip(t / (n / RATE), 0, 1) ** 1.35) ** 1.6   # swells and rushes past
        y = b.shaped_noise(rng, n, 120, 1300) * env
        y2 = b.shaped_noise(rng, n, 380, 2200) * env ** 2 * (.5 + .5 * np.sin(2 * np.pi * (1.7 + .3 * v) * t + rng.uniform(0, 6)))
        x += .42 * y + .16 * y2
        N(x, rng, .12, .70, 40, 420, .26, .35, 3)                         # low body of the rush
        G(x, rng, .30, .80, 10 + v * 2, 300, 1800, .04, (.012, .04))     # buffeting
        room, cut, atk, fade = .14, 2400, .06, .12
    elif kind == 'hail':
        N(x, rng, 0, .11, 120, 900, .28, .05, 11)                          # the volley's body
        G(x, rng, .005, .55, 38 + v * 5, 1500, 4200, .42, (.003, .010))   # hard pellet hits
        G(x, rng, .02, .60, 22 + v * 3, 700, 2600, .22, (.006, .018))     # bounce clatter
        N(x, rng, .02, .45, 60, 420, .22, .17, 5)                          # drumming bed
        room, cut = .08, 4200
    _, peak, rms, _, _, _ = SPEC[kind]
    return finish(x, rng, room, cut, peak, rms, fade=fade, atk=atk)


def out_path(kind, v):
    return (ROOT.parent / 'Resources' / 'Audio' / 'EnemyDeath' / f'storm_crack_{v}.wav') if kind == 'crack' \
        else RES / 'Attack' / 'Storm' / f'{kind}_{v}.wav'


def sheet(rows):
    width, rh = 1440, 122
    img = np.full((70 + rh * len(rows), width, 3), (16, 23, 29), dtype=np.uint8)
    b.label(img, 18, 15, 'STORM SOUNDS / VARIANT 0', (236, 228, 195), 3)
    b.label(img, 18, 44, 'WAVEFORM                              SPECTROGRAM 0-4 KHZ', scale=2)
    for i, (key, x, m) in enumerate(rows):
        y = 70 + i * rh; img[y:y + 1] = (51, 67, 77)
        b.label(img, 18, y + 7, key, (235, 226, 196), 2)
        b.label(img, 18, y + 32, f"{m['duration']:.2f}S  PK {m['peak']:.1f}DB  RMS {m['rms']:.1f}DB", scale=2)
        b.label(img, 18, y + 55, f"C {m['centroid']:.0f}HZ  HF {m['hf']*100:.2f}%", scale=2)
        x0, wv = 450, 340; img[y + 14:y + 106, x0:x0 + wv] = (24, 34, 41)
        for j, part in enumerate(np.array_split(x, wv)):
            a = int(np.clip(60 - np.max(part) * 47, 14, 105)); c = int(np.clip(60 - np.min(part) * 47, 14, 105))
            img[y + a:y + c + 1, x0 + j] = (96, 189, 177)
        x1, sw, sh = 812, 610, 92; nfft = 1024
        hop = max(1, (len(x) - nfft) // sw)
        frames = np.lib.stride_tricks.sliding_window_view(x, nfft)[::hop][:sw]
        mag = np.abs(np.fft.rfft(frames * np.hanning(nfft), axis=1))
        db = 20 * np.log10(mag + 1e-9); db -= db.max()
        bins = np.clip(np.round(np.linspace(0, 4000, sh) * nfft / RATE).astype(int), 0, mag.shape[1] - 1)
        a = np.clip((db[:, bins] + 67) / 67, 0, 1).T[::-1]
        col = np.empty((sh, len(frames), 3), dtype=np.uint8)
        col[:, :, 0] = (25 + 210 * a).astype('u1'); col[:, :, 1] = (32 + 160 * a ** .7).astype('u1'); col[:, :, 2] = (40 + 80 * a ** .5).astype('u1')
        img[y + 14:y + 14 + sh, x1:x1 + len(frames)] = col
    b.png_write(ROOT / 'sheet_storm.png', img)


def main():
    gen = {}; failures = []; rows = []
    notes = ['# Storm sounds', '',
        'Run `python3 build_storm_sounds.py` to rebuild deterministically (NumPy + the Verdant/Ember toolkit). Mono 44.1 kHz 16-bit.', '',
        'Everything Storm plays that another world already says is REUSED (docs/storm-sounds.md): enemy and elite deaths are donor clips from other worlds, pitched (`EnemyDeathAudio.DeathAlias`), screams are borrowed (`ScreamBorrow`), the boss death is `boss_space` re-pitched, and a Storm strike plays Tide\'s thunder (`AttackAudio.Fallback`). Authored here is only the thunder layer under every Storm death and the arc, gust and hail attack cues. No tones, chirps, beeps or coins: noise, impacts and grains only.', '',
        'Why they cannot beep: every layer is band-limited noise (grains 1.5-5 ms for crackle, 3-18 ms for hail), the only pitched element is the 75 ms downward thump body of the shared toolkit (masked by 35-410 Hz noise), the wind is two noise bands with a slow amplitude swell (no resonant filter, no sweep), no oscillator runs over 80 ms, and a dark room tail and a 4 kHz-ish low-pass round the top. The generator measures the longest tonal run and fails a clip over 80 ms.', '',
        '| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Max pair xcorr |', '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    print('File                       s    peak     RMS  centroid    HF%  attack  tonal  clips  xcorr')
    for kind in SPEC:
        for v in range(3):
            b.write_wav(out_path(kind, v), render(kind, v))
            x = b.read_wav(out_path(kind, v)); gen[kind, v] = (x, e.stats(x))
        sounds = [gen[kind, v][0] for v in range(3)]
        corr = max(b.max_xcorr(sounds[a], sounds[c]) for a, c in itertools.combinations(range(3), 2))
        dur, peak, rms, cen, hf, chk = SPEC[kind]
        rows.append((kind, gen[kind, 0][0], gen[kind, 0][1]))
        for v in range(3):
            x, m = gen[kind, v]; name = out_path(kind, v).name
            good = (abs(m['duration'] / dur - 1) <= .12 and abs(m['peak'] - peak) < .15 and abs(m['rms'] - rms) < 1.2 and
                    m['centroid'] < cen and m['hf'] < hf and (not chk or 1.0 <= m['attack'] <= 12) and m['tonal'] < 80 and
                    m['clips'] == 0 and corr < .6)
            if not good: failures.append(name)
            print(f"{name:<22} {m['duration']:5.2f} {m['peak']:7.2f} {m['rms']:7.2f} {m['centroid']:9.0f} {m['hf']*100:6.2f} "
                  f"{m['attack']:7.1f} {m['tonal']:6.0f} {m['clips']:6d} {corr:6.3f}" + ('' if good else '  <-- FAIL'))
            notes.append(f"| `{name}` | {m['duration']:.3f} | {m['peak']:.2f} | {m['rms']:.2f} | {m['centroid']:.0f} | {m['hf']*100:.2f} | {m['attack']:.1f} | {m['tonal']:.0f} | {m['clips']} | {corr:.3f} |")
    if failures: raise SystemExit('Verification failed: ' + ', '.join(failures))
    sheet(rows)
    gap = np.zeros(round(.9 * RATE))
    seq = [gen[k, 0][0] for k in SPEC]
    b.write_wav(ROOT / 'audition_storm.wav', np.concatenate([p for i, s in enumerate(seq) for p in ((s, gap) if i < len(seq) - 1 else (s,))]))
    notes += ['', f'**Result:** PASS: {len(gen)} WAVs, every pair correlation below 0.6, no tonal run over 80 ms, no clipping.', '',
              'No listening monitor was available: audition `audition_storm.wav` (variant 0 of crack, arc, gust, hail) and check on a phone speaker. Most likely to need ear-tuning: the crack layer level under the donors (`WetVolume`/`LayerVolume`), and the gust (is it wind or a hiss?).', '']
    (ROOT / 'sound_notes_storm.md').write_text('\n'.join(notes))
    print('PASS')


if __name__ == '__main__':
    main()
