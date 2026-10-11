# Tide sounds

Run `python3 build_tide_sounds.py` to rebuild deterministically (NumPy + the Verdant/Ember toolkit). Mono 44.1 kHz 16-bit.

Everything Tide plays that another world already says is REUSED (see docs/tide-sounds.md): enemy and elite deaths are donor clips from other worlds, pitched (`EnemyDeathAudio.DeathAlias`), and `boss_tide` already exists. Authored here is only water: the wet layer under every Tide death and the themed attack cues. No tones, chirps, beeps or coins: noise, impacts and bubble grains only.

| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Max pair xcorr |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `tide_wet_0.wav` | 0.600 | -6.00 | -22.00 | 1216 | 0.46 | 2.0 | 0 | 0 | 0.099 |
| `tide_wet_1.wav` | 0.576 | -6.00 | -22.00 | 1275 | 0.60 | 3.0 | 0 | 0 | 0.099 |
| `tide_wet_2.wav` | 0.624 | -6.00 | -22.00 | 1290 | 0.71 | 2.5 | 0 | 0 | 0.099 |
| `jet_0.wav` | 0.650 | -4.00 | -19.00 | 2736 | 17.66 | 5.5 | 0 | 0 | 0.063 |
| `jet_1.wav` | 0.624 | -4.00 | -19.00 | 2753 | 18.04 | 3.5 | 0 | 0 | 0.063 |
| `jet_2.wav` | 0.676 | -4.00 | -19.00 | 2773 | 17.20 | 1.5 | 0 | 0 | 0.063 |
| `wave_0.wav` | 1.250 | -3.00 | -17.00 | 1019 | 0.72 | 7.0 | 0 | 0 | 0.078 |
| `wave_1.wav` | 1.200 | -3.00 | -17.00 | 1051 | 0.51 | 4.5 | 0 | 0 | 0.078 |
| `wave_2.wav` | 1.300 | -3.00 | -17.00 | 1040 | 0.93 | 7.0 | 0 | 0 | 0.078 |
| `strike_0.wav` | 1.350 | -2.00 | -16.00 | 349 | 0.32 | 2.5 | 15 | 0 | 0.156 |
| `strike_1.wav` | 1.296 | -2.00 | -16.00 | 337 | 0.30 | 5.0 | 10 | 0 | 0.156 |
| `strike_2.wav` | 1.404 | -2.00 | -16.00 | 335 | 0.35 | 3.0 | 20 | 0 | 0.156 |
| `lash_0.wav` | 0.500 | -4.00 | -18.00 | 979 | 0.87 | 2.5 | 0 | 0 | 0.122 |
| `lash_1.wav` | 0.480 | -4.00 | -18.00 | 946 | 1.18 | 2.0 | 0 | 0 | 0.122 |
| `lash_2.wav` | 0.520 | -4.00 | -18.00 | 907 | 1.00 | 2.5 | 0 | 0 | 0.122 |
| `burst_0.wav` | 0.950 | -3.00 | -17.00 | 490 | 0.21 | 3.5 | 5 | 0 | 0.144 |
| `burst_1.wav` | 0.912 | -3.00 | -17.00 | 491 | 0.15 | 3.5 | 5 | 0 | 0.144 |
| `burst_2.wav` | 0.988 | -3.00 | -17.00 | 504 | 0.22 | 1.0 | 10 | 0 | 0.144 |
| `splash_0.wav` | 0.500 | -5.00 | -20.00 | 1251 | 0.96 | 2.0 | 0 | 0 | 0.135 |
| `splash_1.wav` | 0.480 | -5.00 | -20.00 | 1302 | 1.07 | 4.0 | 0 | 0 | 0.135 |
| `splash_2.wav` | 0.520 | -5.00 | -20.00 | 1258 | 0.91 | 4.5 | 0 | 0 | 0.135 |

**Result:** PASS: 21 WAVs, every pair correlation below 0.6, no tonal run over 80 ms, no clipping.

No listening monitor was available: audition `audition_tide.wav` (order: wet, jet, wave, strike, lash, burst, splash, three variants each) and check on a phone speaker.
