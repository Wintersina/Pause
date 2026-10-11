# New Verdant elite death cues

Run `python3 build_verdant_elite_sounds.py` to rebuild deterministically. Uses NumPy, Python standard library and the existing Verdant/Ember synthesis helpers. Mono 44.1 kHz, 16-bit PCM. All measurements below are taken from delivered PCM. No new scream layers are made.

The brief names four elites, yielding 12 files. Existing `boss_gate` source artifacts belong to another completed sound set and are left untouched; the requested 15-family/45-file counts cannot describe these four named elites.

## Recipes

| Key | Evokes | Main layers | Length | Peak | RMS |
|---|---|---|---:|---:|---:|
| `verdant_elite_timber_hauler` | Timber Hauler: Heavy wooden and iron rupture; splintering hull, hydraulic vent, rolling logs and a brief brass groan | 35-790 Hz timber crunch, masked 75 ms sub thump, 19-25 splinters, hydraulic noise, two 58 ms modal brass fragments, three falling-log thumps, 180 ms room; 4 ms layer attacks, 20 ms exit fade | 1.00 s | -3 dBFS | -17 dBFS |
| `verdant_elite_thornlash` | Thornlash: Wet vine fibre tears and snaps; small brass joints pop beneath a thorn rattle | 55-1080 Hz tearing noise, masked thump, three vine snaps, wet body, two short brass modes, 22-28 thorn grains, 120 ms room; 4 ms layer attacks, 20 ms exit fade | 0.80 s | -3 dBFS | -17 dBFS |
| `verdant_elite_sporebloom` | Sporebloom: Soft wet bloom burst; hollow pod pop, petal shutters and falling pollen | 45-760 Hz bloom, low thump and pod pop, three petal slaps, 11-15 wet noise bubbles, low pollen hiss, 150 ms room; 4 ms layer attacks, 20 ms exit fade | 0.90 s | -3 dBFS | -17 dBFS |
| `verdant_elite_leafblade` | Leafblade: Fast slicing crack; dull serrated brass fragments, short air rush and thorax crunch | 75-1770 Hz slicing crack, masked thump, 130-1650 Hz air rush, two brief brass modes, 19-25 shrapnel grains, thorax crunch, 100 ms room; 4 ms layer attacks, 20 ms exit fade | 0.70 s | -3 dBFS | -17 dBFS |

## Delivered PCM verification

| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Max pair xcorr |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `verdant_elite_timber_hauler_0.wav` | 1.000 | -3.00 | -17.00 | 471 | 0.038 | 5.0 | 0 | 0 | 0.107 |
| `verdant_elite_timber_hauler_1.wav` | 0.965 | -3.00 | -17.00 | 479 | 0.042 | 6.5 | 0 | 0 | 0.107 |
| `verdant_elite_timber_hauler_2.wav` | 1.040 | -3.00 | -17.00 | 510 | 0.142 | 6.0 | 0 | 0 | 0.107 |
| `verdant_elite_thornlash_0.wav` | 0.800 | -3.00 | -17.00 | 612 | 0.215 | 4.5 | 5 | 0 | 0.128 |
| `verdant_elite_thornlash_1.wav` | 0.772 | -3.00 | -17.00 | 650 | 0.182 | 2.5 | 0 | 0 | 0.128 |
| `verdant_elite_thornlash_2.wav` | 0.832 | -3.00 | -17.00 | 639 | 0.172 | 4.5 | 0 | 0 | 0.128 |
| `verdant_elite_sporebloom_0.wav` | 0.900 | -3.00 | -17.00 | 523 | 0.146 | 4.5 | 5 | 0 | 0.135 |
| `verdant_elite_sporebloom_1.wav` | 0.869 | -3.00 | -17.00 | 551 | 0.175 | 7.0 | 10 | 0 | 0.135 |
| `verdant_elite_sporebloom_2.wav` | 0.936 | -3.00 | -17.00 | 501 | 0.065 | 2.5 | 0 | 0 | 0.135 |
| `verdant_elite_leafblade_0.wav` | 0.700 | -3.00 | -17.00 | 817 | 0.679 | 5.0 | 15 | 0 | 0.113 |
| `verdant_elite_leafblade_1.wav` | 0.676 | -3.00 | -17.00 | 846 | 0.543 | 3.0 | 0 | 0 | 0.113 |
| `verdant_elite_leafblade_2.wav` | 0.728 | -3.00 | -17.00 | 803 | 0.670 | 3.5 | 10 | 0 | 0.113 |

**Result:** PASS: 12 death WAVs; all pair correlations below 0.6.

No listening monitor was available. The cues use dark filtering, low-level short brass modes masked by noisy impacts, restrained 2-5 kHz content, a 6 kHz shelf, short rooms and explicit centroid/HF/tonality limits. Check on a phone speaker before shipping.
