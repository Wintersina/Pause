# Boss hurt cues

Run `python3 build_boss_hurt_sounds.py` (deterministic, NumPy only). `boss_<world>_hurt_0/_1.wav` in Resources/Audio/EnemyDeath, played by
`EnemyDeathAudio.PlayBossHurt` on every heart a boss loses (never the last: that is its death cue). A-weighted loudest 50 ms at -17 dB.

| File | s | Peak dBFS | A50 dB | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| `boss_space_hurt_0.wav` | 0.620 | -3.38 | -17.00 | 494 | 0.000 | 4.0 | 0 | 0 |
| `boss_space_hurt_1.wav` | 0.583 | -4.35 | -17.00 | 484 | 0.000 | 4.5 | 0 | 0 |
| `boss_frost_hurt_0.wav` | 0.660 | -2.42 | -17.00 | 624 | 0.007 | 2.5 | 0 | 0 |
| `boss_frost_hurt_1.wav` | 0.620 | -4.67 | -17.00 | 593 | 0.000 | 3.0 | 0 | 0 |
| `boss_verdant_hurt_0.wav` | 0.600 | -4.40 | -17.00 | 525 | 0.000 | 3.5 | 0 | 0 |
| `boss_verdant_hurt_1.wav` | 0.564 | -2.74 | -17.00 | 558 | 0.020 | 6.0 | 0 | 0 |
| `boss_ember_hurt_0.wav` | 0.660 | -2.99 | -17.00 | 507 | 0.000 | 5.0 | 0 | 0 |
| `boss_ember_hurt_1.wav` | 0.620 | -2.61 | -17.00 | 512 | 0.004 | 3.5 | 0 | 0 |
| `boss_tide_hurt_0.wav` | 0.700 | -2.07 | -17.00 | 390 | 0.002 | 5.5 | 0 | 0 |
| `boss_tide_hurt_1.wav` | 0.658 | -2.11 | -17.00 | 374 | 0.002 | 2.5 | 0 | 0 |

**Result:** PASS: 10 WAVs. Audition: `audition_boss_hurt.wav` (space, frost, verdant, ember, tide; variants 0 then 1).
No listening monitor was available: check on a phone speaker.
