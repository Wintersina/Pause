#!/usr/bin/env python3
"""Tide's own sounds: the wet layer every Tide death wears, and the themed attack cues.

Everything else Tide plays is REUSED from other worlds (EnemyDeathAudio.DeathAlias: donor clips, pitched), so only what no
existing sound can say is authored here: water. Noise, impacts and bubbles only: no sustained tones, no chirps, no coin or beep.
Same toolkit and checks as build_verdant_elite_sounds.py. Mono 44.1 kHz 16-bit.

  tide_wet_0..2          (EnemyDeath/)       splash + bubble tail layered under a Tide enemy death (quiet)
  jet / wave / strike / lash / burst / splash _0..2   (Resources/Audio/Attack/Tide/)
      jet     pressure water gun / limpet-mine jet: a hiss with a hard front and spray
      wave    surf wave: a swell that builds, then the crash
      strike  thunder: a hard crack, sizzle, long low rumble
      lash    tentacle lash: a wet whip snap
      burst   depth charge: a muffled boom and a column of water, bubbles
      splash  net / pool landing: a slap of water
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


def finish(x, rng, room, cutoff, peak_db, rms_db, low=27):
    x = b.short_room(x, rng, room, .05)
    x = b.fft_filter(x, low, cutoff)
    n = len(x); i = np.arange(n)
    edge = np.sin(np.minimum(i / (RATE * .004), 1) * np.pi / 2) ** 2
    edge *= np.sin(np.minimum(i[::-1] / (RATE * .025), 1) * np.pi / 2) ** 2
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


# name -> (seconds, peak, rms, centroid limit Hz, HF share limit, check attack)
SPEC = {
    'wet':    (.60, -6, -22, 1700, .14, True),
    'jet':    (.65, -4, -19, 3200, .60, True),
    'wave':   (1.25, -3, -17, 1500, .12, False),
    'strike': (1.35, -2, -16, 1500, .35, True),
    'lash':   (.50, -4, -18, 2200, .30, True),
    'burst':  (.95, -3, -17, 900, .06, True),
    'splash': (.50, -5, -20, 1900, .20, True),
}


def render(kind, variant):
    dur = SPEC[kind][0]
    rng = np.random.default_rng(zlib.crc32(kind.encode()) + 7919 * variant + 424242)
    x = np.zeros(round(dur * (1, .96, 1.04)[variant] * RATE))
    v = variant
    if kind == 'wet':
        N(x, rng, 0, .30, 220, 3000, .50, .085)
        b.add_thump(x, rng, .004, .5)
        G(x, rng, .03, .38, 14 + v * 3, 320, 2500, .08)
        N(x, rng, .08, .40, 90, 900, .28, .14, 9)
        b.wet_bubbles(x, rng, .06, .46, 9 + v * 2, .06)
        room, cut = .10, 3600
    elif kind == 'jet':
        N(x, rng, 0, .012 + .004 * v, 700, 5200, .9, .006)               # hard front
        N(x, rng, .004, .58, 1300, 5200, .58, .46, 33)                    # pressure hiss
        N(x, rng, 0, .45, 140, 900, .30, .22, 11)                         # body
        G(x, rng, .08, .58, 16 + v * 3, 700, 4200, .05, (.006, .02))     # spray
        N(x, rng, .45, .18, 250, 2400, .16, .07)                          # splash at the end
        room, cut = .06, 5200
    elif kind == 'wave':
        N(x, rng, 0, .62, 90, 1700, .42, .5, 5, ) if False else None
        sw = int(.55 * RATE)                                              # the swell builds
        y = b.shaped_noise(rng, sw, 70, 1500) * (np.linspace(0, 1, sw) ** 1.6)
        x[:sw] += .42 * y
        N(x, rng, .50, .55, 80, 3300, .85, .15, 4)                        # the crash
        b.add_thump(x, rng, .52, 1.3, True)
        N(x, rng, .55, .62, 40, 700, .34, .22, 7)
        G(x, rng, .55, .95, 22 + v * 3, 300, 2600, .07)                   # foam
        b.wet_bubbles(x, rng, .65, 1.05, 10, .05)
        room, cut = .20, 2800
    elif kind == 'strike':
        N(x, rng, 0, .09, 320, 5200, .95, .018)                           # the crack
        b.add_thump(x, rng, .003, 1.3, True)
        N(x, rng, .01, .30, 900, 5200, .30, .075, 31)                     # sizzle
        N(x, rng, .05, 1.15, 30, 260, .58, .42, 6)                        # rumble
        N(x, rng, .16, .95, 60, 520, .26, .34, 9)
        G(x, rng, .08, .50, 12 + v * 3, 400, 3000, .06)
        for at in (.52 + .03 * v, .83):
            b.add_thump(x, rng, at, .40, True)
        room, cut = .22, 3600
    elif kind == 'lash':
        N(x, rng, 0, .045, 600, 4400, .85, .011)                          # snap
        N(x, rng, .015, .22, 90, 950, .42, .06, 18)                       # wet body
        b.add_thump(x, rng, .006, .55)
        b.wet_bubbles(x, rng, .08, .34, 5 + v, .06)
        N(x, rng, .12, .30, 120, 1400, .14, .09)
        room, cut = .07, 3600
    elif kind == 'burst':
        b.add_thump(x, rng, 0, 1.4, True)
        N(x, rng, 0, .34, 35, 460, .80, .11, 8)                           # muffled boom
        N(x, rng, .05, .52, 150, 2100, .38, .16, 14)                      # the column of water
        b.wet_bubbles(x, rng, .12, .72, 14 + v * 2, .08)
        N(x, rng, .30, .56, 50, 420, .20, .22, 6)
        G(x, rng, .2, .75, 14, 250, 1800, .05)
        room, cut = .14, 2200
    elif kind == 'splash':
        N(x, rng, 0, .22, 260, 2600, .58, .05)
        b.add_thump(x, rng, .004, .45)
        G(x, rng, .02, .30, 12 + v * 2, 320, 2500, .08)
        b.wet_bubbles(x, rng, .05, .34, 6 + v, .05)
        N(x, rng, .08, .30, 100, 900, .20, .10, 10)
        room, cut = .07, 3200
    _, peak, rms, _, _, _ = SPEC[kind]
    return finish(x, rng, room, cut, peak, rms)


def out_path(kind, v):
    return (ROOT.parent / 'Resources' / 'Audio' / 'EnemyDeath' / f'tide_wet_{v}.wav') if kind == 'wet' \
        else RES / 'Attack' / 'Tide' / f'{kind}_{v}.wav'


def main():
    gen = {}; failures = []; notes = ['# Tide sounds', '',
        'Run `python3 build_tide_sounds.py` to rebuild deterministically (NumPy + the Verdant/Ember toolkit). Mono 44.1 kHz 16-bit.', '',
        'Everything Tide plays that another world already says is REUSED (see docs/tide-sounds.md): enemy and elite deaths are donor clips from other worlds, pitched (`EnemyDeathAudio.DeathAlias`), and `boss_tide` already exists. Authored here is only water: the wet layer under every Tide death and the themed attack cues. No tones, chirps, beeps or coins: noise, impacts and bubble grains only.', '',
        '| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Max pair xcorr |', '|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|']
    print('File                       s    peak     RMS  centroid    HF%  attack  tonal  clips  xcorr')
    for kind in SPEC:
        for v in range(3):
            b.write_wav(out_path(kind, v), render(kind, v))
            x = b.read_wav(out_path(kind, v)); gen[kind, v] = (x, e.stats(x))
        sounds = [gen[kind, v][0] for v in range(3)]
        corr = max(b.max_xcorr(sounds[a], sounds[c]) for a, c in itertools.combinations(range(3), 2))
        dur, peak, rms, cen, hf, chk = SPEC[kind]
        for v in range(3):
            x, m = gen[kind, v]; name = out_path(kind, v).name
            good = (abs(m['duration'] / dur - 1) <= .12 and abs(m['peak'] - peak) < .15 and abs(m['rms'] - rms) < 1.2 and
                    m['centroid'] < cen and m['hf'] < hf and (not chk or 1.0 <= m['attack'] <= 12) and m['tonal'] < 80 and
                    m['clips'] == 0 and corr < .6)
            if not good: failures.append(name)
            line = (f"{name:<22} {m['duration']:5.2f} {m['peak']:7.2f} {m['rms']:7.2f} {m['centroid']:9.0f} {m['hf']*100:6.2f} "
                    f"{m['attack']:7.1f} {m['tonal']:6.0f} {m['clips']:6d} {corr:6.3f}" + ('' if good else '  <-- FAIL'))
            print(line)
            notes.append(f"| `{name}` | {m['duration']:.3f} | {m['peak']:.2f} | {m['rms']:.2f} | {m['centroid']:.0f} | {m['hf']*100:.2f} | {m['attack']:.1f} | {m['tonal']:.0f} | {m['clips']} | {corr:.3f} |")
    if failures: raise SystemExit('Verification failed: ' + ', '.join(failures))
    gap = np.zeros(round(.9 * RATE))
    seq = [gen[k, v][0] for k in SPEC for v in range(3)]
    b.write_wav(ROOT / 'audition_tide.wav', np.concatenate([p for i, s in enumerate(seq) for p in ((s, gap) if i < len(seq) - 1 else (s,))]))
    notes += ['', f'**Result:** PASS: {len(gen)} WAVs, every pair correlation below 0.6, no tonal run over 80 ms, no clipping.', '',
              'No listening monitor was available: audition `audition_tide.wav` (order: wet, jet, wave, strike, lash, burst, splash, three variants each) and check on a phone speaker.', '']
    (ROOT / 'sound_notes_tide.md').write_text('\n'.join(notes))
    print('PASS')


if __name__ == '__main__':
    main()
