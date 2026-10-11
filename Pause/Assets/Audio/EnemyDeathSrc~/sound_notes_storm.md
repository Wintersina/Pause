# Storm sounds

Run `python3 build_storm_sounds.py` to rebuild deterministically (NumPy + the Verdant/Ember toolkit). Mono 44.1 kHz 16-bit.

Everything Storm plays that another world already says is REUSED (docs/storm-sounds.md): enemy and elite deaths are donor clips from other worlds, pitched (`EnemyDeathAudio.DeathAlias`), screams are borrowed (`ScreamBorrow`), the boss death is `boss_space` re-pitched, and a Storm strike plays Tide's thunder (`AttackAudio.Fallback`). Authored here is only the thunder layer under every Storm death and the arc, gust and hail attack cues. No tones, chirps, beeps or coins: noise, impacts and grains only.

Why they cannot beep: every layer is band-limited noise (grains 1.5-5 ms for crackle, 3-18 ms for hail), the only pitched element is the 75 ms downward thump body of the shared toolkit (masked by 35-410 Hz noise), the wind is two noise bands with a slow amplitude swell (no resonant filter, no sweep), no oscillator runs over 80 ms, and a dark room tail and a 4 kHz-ish low-pass round the top. The generator measures the longest tonal run and fails a clip over 80 ms.

| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Max pair xcorr |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `storm_crack_0.wav` | 0.750 | -6.00 | -21.00 | 345 | 0.20 | 2.0 | 15 | 0 | 0.222 |
| `storm_crack_1.wav` | 0.720 | -6.00 | -21.00 | 315 | 0.11 | 3.5 | 20 | 0 | 0.222 |
| `storm_crack_2.wav` | 0.780 | -6.00 | -21.00 | 357 | 0.12 | 2.5 | 20 | 0 | 0.222 |
| `arc_0.wav` | 0.550 | -4.00 | -18.00 | 668 | 1.61 | 3.0 | 20 | 0 | 0.217 |
| `arc_1.wav` | 0.528 | -4.00 | -18.00 | 624 | 1.23 | 2.0 | 15 | 0 | 0.217 |
| `arc_2.wav` | 0.572 | -4.00 | -18.00 | 584 | 0.99 | 2.0 | 5 | 0 | 0.217 |
| `gust_0.wav` | 1.000 | -5.00 | -19.00 | 758 | 0.02 | 1.5 | 0 | 0 | 0.114 |
| `gust_1.wav` | 0.960 | -5.00 | -19.00 | 756 | 0.07 | 0.5 | 0 | 0 | 0.114 |
| `gust_2.wav` | 1.040 | -5.00 | -19.00 | 743 | 0.00 | 0.0 | 0 | 0 | 0.114 |
| `hail_0.wav` | 0.700 | -4.00 | -18.00 | 1274 | 2.53 | 3.0 | 5 | 0 | 0.122 |
| `hail_1.wav` | 0.672 | -4.00 | -18.00 | 1320 | 2.61 | 2.5 | 0 | 0 | 0.122 |
| `hail_2.wav` | 0.728 | -4.00 | -18.00 | 1409 | 2.65 | 5.5 | 0 | 0 | 0.122 |

**Result:** PASS: 12 WAVs, every pair correlation below 0.6, no tonal run over 80 ms, no clipping.

No listening monitor was available: audition `audition_storm.wav` (variant 0 of crack, arc, gust, hail) and check on a phone speaker. Most likely to need ear-tuning: the crack layer level under the donors (`WetVolume`/`LayerVolume`), and the gust (is it wind or a hiss?).
