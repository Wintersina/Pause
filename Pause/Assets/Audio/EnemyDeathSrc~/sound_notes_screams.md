# Space World light scream layers

27 deterministic 44.1 kHz mono 16-bit PCM WAVs. Existing death WAVs were read only. Each scream file peaks at -9 dBFS. In the audition mix it begins 34-55 ms after the death cue and plays at 0.50 volume, putting its peak 12 dB below the death WAV peak of -3 dBFS. A playback gain of 0.8 would make this an 8 dB difference; use 0.50 to satisfy the requested 10-14 dB range.

Human cries use a jittered, vibrato-modulated harmonic glottal source through four moving vowel formants, breath, mild saturation, 350-2800 Hz radio shaping, a trace of hiss and two early reflections. The creature uses a pulse-width reed source, shifting throat formants, small moving comb taps and wet modulated noise. All have a 9-10 ms attack. All but the Hauler cut into a 15 ms fade; the Hauler bends down into a 120 ms fade. The spectrum is steeply attenuated and stopped above 4 kHz. Quantized WAV spectra are checked to keep every bin above 4 kHz at least 60 dB below the strongest bin.

| Enemy | Character | Delay ms |
|---|---|---:|
| Needle | startled, high but soft | 34 |
| Steel Claw | sharp grunt-cry | 39 |
| Twin Claw | two offset crew voices | 43 |
| Warden | lower strained shielded shout | 48 |
| Bile Mite | wet reedy chitter and gurgle | 43 |
| Eventide Bastion | commander over damaged comms | 52 |
| Orbit Reaver | fierce raspy cut-off cry | 36 |
| Rift Lancer | electrified yell and interference | 50 |
| Singularity Hauler | deep downward groan-shout | 55 |

## Delivered PCM verification

Centroid is spectral centroid. HF is the energy share above 3.5 kHz. Tonal is the longest contiguous run of active 20 ms Hann frames, 5 ms apart, whose strongest frequency bin has over 45% of frame energy. Mix measurements use each corresponding death variant and the delay above. Xcorr is the maximum normalized full-lag correlation among that enemy’s three scream variants.

| File | s | Peak dBFS | Centroid Hz | HF % | Tonal ms | Clips | Mix peak dBFS | Mix centroid Hz | Max xcorr |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `space_fighter_1_scream_0.wav` | 0.220 | -9.00 | 1050 | 0.000 | 0.0 | 0 | -3.00 | 1081 | 0.199 |
| `space_fighter_1_scream_1.wav` | 0.211 | -9.00 | 1097 | 0.001 | 0.0 | 0 | -3.00 | 1115 | 0.199 |
| `space_fighter_1_scream_2.wav` | 0.229 | -9.00 | 989 | 0.001 | 10.0 | 0 | -3.00 | 1081 | 0.199 |
| `space_fighter_2_scream_0.wav` | 0.280 | -9.00 | 989 | 0.000 | 0.0 | 0 | -3.00 | 987 | 0.182 |
| `space_fighter_2_scream_1.wav` | 0.269 | -9.00 | 1024 | 0.000 | 0.0 | 0 | -3.00 | 974 | 0.182 |
| `space_fighter_2_scream_2.wav` | 0.291 | -9.00 | 962 | 0.000 | 0.0 | 0 | -3.00 | 1005 | 0.182 |
| `space_fighter_3_scream_0.wav` | 0.320 | -9.00 | 1040 | 0.000 | 0.0 | 0 | -2.58 | 757 | 0.130 |
| `space_fighter_3_scream_1.wav` | 0.307 | -9.00 | 1065 | 0.000 | 0.0 | 0 | -3.00 | 767 | 0.130 |
| `space_fighter_3_scream_2.wav` | 0.333 | -9.00 | 1086 | 0.000 | 0.0 | 0 | -3.00 | 825 | 0.130 |
| `space_fighter_4_scream_0.wav` | 0.350 | -9.00 | 1009 | 0.000 | 0.0 | 0 | -4.22 | 504 | 0.177 |
| `space_fighter_4_scream_1.wav` | 0.336 | -9.00 | 993 | 0.000 | 0.0 | 0 | -3.03 | 524 | 0.177 |
| `space_fighter_4_scream_2.wav` | 0.364 | -9.00 | 969 | 0.000 | 0.0 | 0 | -3.05 | 485 | 0.177 |
| `space_alien_scream_0.wav` | 0.400 | -9.00 | 753 | 0.001 | 15.0 | 0 | -1.95 | 660 | 0.242 |
| `space_alien_scream_1.wav` | 0.384 | -9.00 | 812 | 0.000 | 24.9 | 0 | -3.00 | 637 | 0.242 |
| `space_alien_scream_2.wav` | 0.416 | -9.00 | 763 | 0.000 | 24.9 | 0 | -3.92 | 612 | 0.242 |
| `space_elite_eventide_bastion_scream_0.wav` | 0.450 | -9.00 | 1032 | 0.000 | 0.0 | 0 | -2.50 | 517 | 0.106 |
| `space_elite_eventide_bastion_scream_1.wav` | 0.432 | -9.00 | 1036 | 0.000 | 0.0 | 0 | -2.99 | 539 | 0.106 |
| `space_elite_eventide_bastion_scream_2.wav` | 0.468 | -9.00 | 1022 | 0.000 | 0.0 | 0 | -2.01 | 540 | 0.106 |
| `space_elite_orbit_reaver_scream_0.wav` | 0.400 | -9.00 | 1140 | 0.001 | 0.0 | 0 | -3.00 | 779 | 0.120 |
| `space_elite_orbit_reaver_scream_1.wav` | 0.384 | -9.00 | 1118 | 0.001 | 0.0 | 0 | -3.00 | 829 | 0.120 |
| `space_elite_orbit_reaver_scream_2.wav` | 0.416 | -9.00 | 1180 | 0.001 | 0.0 | 0 | -3.00 | 827 | 0.120 |
| `space_elite_rift_lancer_scream_0.wav` | 0.400 | -9.00 | 1002 | 0.000 | 0.0 | 0 | -2.41 | 814 | 0.153 |
| `space_elite_rift_lancer_scream_1.wav` | 0.384 | -9.00 | 1066 | 0.000 | 0.0 | 0 | -2.16 | 857 | 0.153 |
| `space_elite_rift_lancer_scream_2.wav` | 0.416 | -9.00 | 1025 | 0.000 | 0.0 | 0 | -3.13 | 838 | 0.153 |
| `space_elite_singularity_hauler_scream_0.wav` | 0.550 | -9.00 | 1010 | 0.000 | 0.0 | 0 | -2.30 | 410 | 0.129 |
| `space_elite_singularity_hauler_scream_1.wav` | 0.528 | -9.00 | 1036 | 0.000 | 0.0 | 0 | -2.31 | 383 | 0.129 |
| `space_elite_singularity_hauler_scream_2.wav` | 0.572 | -9.00 | 1040 | 0.000 | 0.0 | 0 | -2.24 | 405 | 0.129 |

## Audition and integration

`audition_screams.wav` plays variant 0 alone and then over its matching death variant, for all nine enemies in the order above. Each item is followed by 1.2 s of silence. `audition_screams_all.wav` plays all 27 screams in enemy and variant order, with the same gaps. `sheet_screams.png` shows variant 0 for each.

A later game-code change must load `*_scream_0/1/2.wav`, select a variant and play it probabilistically at 0.50 volume after the specified delay. No C# or .meta files were edited here. Probability is intentionally left for game tuning.

These are synthesized approximations of distant vocal cries, not recorded performances. Ear-tune Bile Mite first for an organic rather than electronic edge, then Needle for pitch, and Twin Claw for two voices remaining distinct beneath the double impact.

## Trimmed (scream-borrow)

Shipped scream layers were cut to 2 variants per key (`_0`, `_1`), and Needle, Steel Claw, Twin Claw, Bile Mite, Cryo Jelly and Gnat lost theirs: they borrow a donor's scream pitched up (`EnemyDeathAudio.BorrowTable`). The master files for the deleted variants can be regenerated with `build_space_screams.py` and the verdant/ember/frost build scripts.
