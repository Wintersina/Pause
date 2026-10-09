# Frost World death audio

48 death WAVs (16 keys × 3) and 3 Cryo Jelly cries. Deterministic 44.1 kHz mono 16-bit PCM. The loudest 50 ms A-weighted window is matched to -15.0 dBFS for ordinary cues and -13.5 dBFS for the Golem and elites. A weighting and 25 ms hop follow the approved Space toolkit. No peak normalization is used on death cues. All impacts have a 4.5 ms soft onset, noise-based material body, transient detail and a dark 120 or 180 ms room.

## Identity and scream decisions

| Unit | Death identity | Vocal choice |
|---|---|---|
| Cryo Jelly (`frost_alien`) | glass cryo capsule fracture, wet slush and venting vapour; 0.63 s nominal | Creature cry: Cryo Jelly is alive inside the cryo shell. |
| Lance (`frost_chaser`) | ice spear snapped through a steel drone hull, trailing air; 0.53 s nominal | No cry: no living occupant visible in sprite or roster. |
| Flake (`frost_fighter_1`) | drill nose breaks thin ice blades over a metal crunch; 0.45 s nominal | No cry: roster and sprite show a crystal drone with no occupant. |
| Icicle (`frost_fighter_2`) | coolant bulb ruptures and sprays through a spear crack; 0.52 s nominal | No cry: roster and sprite show a crystal drone with no occupant. |
| Frost Kite (`frost_fighter_3`) | six hex ice plates break in a hollow stagger; 0.55 s nominal | No cry: roster and sprite show a crystal drone with no occupant. |
| Hailstorm (`frost_fighter_4`) | cannon mount blows off and hail pellets rattle out; 0.68 s nominal | No cry: roster and sprite show a crystal drone with no occupant. |
| Glacier Golem (`frost_big`) | glacier fault boom, jaw collapse, grinding slabs and heavy rubble; 1.22 s nominal | No cry: no living occupant visible in sprite or roster. |
| Geode Mine (`frost_mine`) | rail clamp burst ejects ice shards; 0.53 s nominal | No cry: no living occupant visible in sprite or roster. |
| Frozen Chunk (`frost_rock_chunk`) | frozen boulder splits; copper clamps rattle loose; 0.57 s nominal | No cry: no living occupant visible in sprite or roster. |
| Ice Shard (`frost_rock_shard`) | low-passed crystal spears splinter and scatter; 0.46 s nominal | No cry: no living occupant visible in sprite or roster. |
| Rime Star (`frost_rock_rime`) | six steel and ice blades whirr down into a hub crunch; 0.58 s nominal | No cry: no living occupant visible in sprite or roster. |
| Rimebreaker (`frost_elite_rimebreaker`) | icebreaker prow buckles, rime steel folds, ice skids away; 0.90 s nominal | No cry: sprite and definition show machinery, armor and payloads; no visible pilot or crew. |
| Floe Harrower (`frost_elite_floe_harrower`) | barge pontoons split, slab chutes spill, cutter fan shears; 0.95 s nominal | No cry: sprite and definition show machinery, armor and payloads; no visible pilot or crew. |
| Cryo Siren (`frost_elite_cryo_siren`) | coolant dish implodes, hoses vent, narrow spine cracks; 0.87 s nominal | No cry: sprite and definition show machinery, armor and payloads; no visible pilot or crew. |
| Glacier Tender (`frost_elite_glacier_tender`) | tether winches tear out and four drone pods cascade loose; 0.95 s nominal | No cry: sprite and definition show machinery, armor and payloads; no visible pilot or crew. |
| Whiteout Sentinel (`frost_elite_whiteout_sentinel`) | three layered glacier plates shear from a heavy wedge hull; 0.95 s nominal | No cry: sprite and definition show machinery, armor and payloads; no visible pilot or crew. |

`frost_mine` exists in EnemyRoster as `hazard_frost_mine`; it has no dedicated sprite or death strip, so its identity follows the geode/rail clamp roster description. The five elite definitions and all available flight/death strips were inspected. No elite has a clear pilot or crew in its sprite or definition.

## PCM verification

A50 is the loudest A-weighted 50 ms window in dBFS. A RMS is weighted over the entire file. HF is energy above 4 kHz. Attack is the 10–90% rise of the synthesized impact envelope. Tonal is the longest run of 20 ms Hann frames (5 ms hop) in which a 1–3 kHz bin holds over 45% of frame energy; NB is the longest 40 ms narrowband prominence run above 14 dB. Xcorr is the maximum full-lag normalized cross-correlation among three variants.

| File | s | A50 | A RMS | Peak | Centroid Hz | HF % | Attack ms | Tonal ms | NB ms | Clips | Max xcorr |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `frost_alien_0.wav` | 0.630 | -15.00 | -20.95 | -4.36 | 977 | 0.38 | 2.3 | 0 | 15 | 0 | 0.112 |
| `frost_alien_1.wav` | 0.608 | -15.00 | -21.07 | -4.48 | 985 | 0.31 | 2.3 | 0 | 10 | 0 | 0.112 |
| `frost_alien_2.wav` | 0.658 | -15.00 | -21.02 | -4.59 | 1003 | 0.45 | 2.3 | 0 | 5 | 0 | 0.112 |
| `frost_chaser_0.wav` | 0.530 | -15.00 | -22.07 | -5.53 | 1024 | 0.74 | 2.3 | 0 | 5 | 0 | 0.133 |
| `frost_chaser_1.wav` | 0.511 | -15.00 | -22.53 | -6.30 | 1045 | 0.56 | 2.3 | 0 | 0 | 0 | 0.133 |
| `frost_chaser_2.wav` | 0.554 | -15.00 | -22.54 | -6.14 | 1054 | 0.53 | 2.3 | 0 | 0 | 0 | 0.133 |
| `frost_fighter_1_0.wav` | 0.450 | -15.00 | -20.00 | -3.97 | 1151 | 0.56 | 2.3 | 0 | 20 | 0 | 0.135 |
| `frost_fighter_1_1.wav` | 0.434 | -15.00 | -20.24 | -4.03 | 1094 | 0.15 | 2.3 | 0 | 15 | 0 | 0.135 |
| `frost_fighter_1_2.wav` | 0.470 | -15.00 | -20.68 | -4.36 | 1036 | 0.10 | 2.3 | 0 | 0 | 0 | 0.135 |
| `frost_fighter_2_0.wav` | 0.520 | -15.00 | -21.66 | -5.35 | 1049 | 0.26 | 2.3 | 0 | 10 | 0 | 0.160 |
| `frost_fighter_2_1.wav` | 0.502 | -15.00 | -21.00 | -4.95 | 1106 | 0.50 | 2.3 | 0 | 5 | 0 | 0.160 |
| `frost_fighter_2_2.wav` | 0.543 | -15.00 | -21.67 | -5.55 | 1109 | 0.35 | 2.3 | 0 | 15 | 0 | 0.160 |
| `frost_fighter_3_0.wav` | 0.550 | -15.00 | -20.87 | -3.83 | 799 | 0.02 | 2.3 | 0 | 0 | 0 | 0.141 |
| `frost_fighter_3_1.wav` | 0.531 | -15.00 | -21.50 | -4.58 | 841 | 0.01 | 2.3 | 0 | 5 | 0 | 0.141 |
| `frost_fighter_3_2.wav` | 0.575 | -15.00 | -20.47 | -3.57 | 852 | 0.16 | 2.3 | 0 | 0 | 0 | 0.141 |
| `frost_fighter_4_0.wav` | 0.680 | -15.00 | -21.72 | -3.87 | 651 | 0.13 | 2.3 | 0 | 5 | 0 | 0.170 |
| `frost_fighter_4_1.wav` | 0.656 | -15.00 | -21.54 | -3.48 | 624 | 0.09 | 2.3 | 0 | 5 | 0 | 0.170 |
| `frost_fighter_4_2.wav` | 0.711 | -15.00 | -22.22 | -4.34 | 661 | 0.10 | 2.3 | 0 | 10 | 0 | 0.170 |
| `frost_big_0.wav` | 1.220 | -13.50 | -19.62 | -2.54 | 423 | 0.07 | 2.3 | 0 | 10 | 0 | 0.106 |
| `frost_big_1.wav` | 1.177 | -13.50 | -19.15 | -2.20 | 425 | 0.04 | 2.3 | 0 | 5 | 0 | 0.106 |
| `frost_big_2.wav` | 1.275 | -13.50 | -19.69 | -2.24 | 426 | 0.08 | 2.3 | 0 | 25 | 0 | 0.106 |
| `frost_mine_0.wav` | 0.530 | -15.00 | -22.74 | -5.92 | 892 | 0.77 | 2.3 | 0 | 10 | 0 | 0.203 |
| `frost_mine_1.wav` | 0.511 | -15.00 | -22.23 | -5.63 | 911 | 0.45 | 2.3 | 0 | 5 | 0 | 0.203 |
| `frost_mine_2.wav` | 0.554 | -15.00 | -22.03 | -4.73 | 883 | 0.61 | 2.3 | 0 | 35 | 0 | 0.203 |
| `frost_rock_chunk_0.wav` | 0.570 | -15.00 | -21.85 | -4.10 | 687 | 0.17 | 2.3 | 0 | 10 | 0 | 0.190 |
| `frost_rock_chunk_1.wav` | 0.550 | -15.00 | -22.10 | -4.32 | 670 | 0.14 | 2.3 | 0 | 10 | 0 | 0.190 |
| `frost_rock_chunk_2.wav` | 0.596 | -15.00 | -22.52 | -4.79 | 683 | 0.17 | 2.3 | 0 | 5 | 0 | 0.190 |
| `frost_rock_shard_0.wav` | 0.460 | -15.00 | -21.60 | -6.39 | 1534 | 1.81 | 2.3 | 0 | 65 | 0 | 0.159 |
| `frost_rock_shard_1.wav` | 0.444 | -15.00 | -21.66 | -6.50 | 1501 | 0.83 | 2.3 | 0 | 50 | 0 | 0.159 |
| `frost_rock_shard_2.wav` | 0.481 | -15.00 | -21.68 | -6.43 | 1435 | 0.85 | 2.3 | 0 | 5 | 0 | 0.159 |
| `frost_rock_rime_0.wav` | 0.580 | -15.00 | -22.07 | -5.30 | 835 | 0.34 | 2.3 | 0 | 50 | 0 | 0.151 |
| `frost_rock_rime_1.wav` | 0.560 | -15.00 | -21.67 | -4.60 | 792 | 0.24 | 2.3 | 0 | 10 | 0 | 0.151 |
| `frost_rock_rime_2.wav` | 0.606 | -15.00 | -22.05 | -5.03 | 802 | 0.25 | 2.3 | 0 | 15 | 0 | 0.151 |
| `frost_elite_rimebreaker_0.wav` | 0.900 | -13.50 | -18.34 | -2.93 | 592 | 0.16 | 2.3 | 0 | 0 | 0 | 0.115 |
| `frost_elite_rimebreaker_1.wav` | 0.869 | -13.50 | -18.10 | -2.36 | 605 | 0.20 | 2.3 | 0 | 5 | 0 | 0.115 |
| `frost_elite_rimebreaker_2.wav` | 0.940 | -13.50 | -19.36 | -3.94 | 610 | 0.24 | 2.3 | 0 | 5 | 0 | 0.115 |
| `frost_elite_floe_harrower_0.wav` | 0.950 | -13.50 | -18.93 | -3.69 | 611 | 0.30 | 2.3 | 0 | 5 | 0 | 0.109 |
| `frost_elite_floe_harrower_1.wav` | 0.917 | -13.50 | -19.18 | -3.57 | 568 | 0.19 | 2.3 | 0 | 0 | 0 | 0.109 |
| `frost_elite_floe_harrower_2.wav` | 0.993 | -13.50 | -19.30 | -3.76 | 578 | 0.19 | 2.3 | 0 | 5 | 0 | 0.109 |
| `frost_elite_cryo_siren_0.wav` | 0.870 | -13.50 | -18.74 | -5.09 | 869 | 0.83 | 2.3 | 0 | 10 | 0 | 0.127 |
| `frost_elite_cryo_siren_1.wav` | 0.840 | -13.50 | -19.29 | -4.96 | 845 | 0.61 | 2.3 | 0 | 5 | 0 | 0.127 |
| `frost_elite_cryo_siren_2.wav` | 0.909 | -13.50 | -18.47 | -4.00 | 860 | 0.93 | 2.3 | 0 | 5 | 0 | 0.127 |
| `frost_elite_glacier_tender_0.wav` | 0.950 | -13.50 | -18.60 | -3.84 | 702 | 0.23 | 2.3 | 0 | 15 | 0 | 0.107 |
| `frost_elite_glacier_tender_1.wav` | 0.917 | -13.50 | -17.95 | -2.93 | 727 | 0.43 | 2.3 | 0 | 10 | 0 | 0.107 |
| `frost_elite_glacier_tender_2.wav` | 0.993 | -13.50 | -18.93 | -3.98 | 683 | 0.22 | 2.3 | 0 | 5 | 0 | 0.107 |
| `frost_elite_whiteout_sentinel_0.wav` | 0.950 | -13.50 | -18.84 | -2.59 | 520 | 0.08 | 2.3 | 0 | 5 | 0 | 0.105 |
| `frost_elite_whiteout_sentinel_1.wav` | 0.917 | -13.50 | -18.46 | -2.52 | 518 | 0.06 | 2.3 | 0 | 10 | 0 | 0.105 |
| `frost_elite_whiteout_sentinel_2.wav` | 0.993 | -13.50 | -18.44 | -2.37 | 566 | 0.27 | 2.3 | 0 | 5 | 0 | 0.105 |

## Cryo Jelly scream and death mix

The cry is a sustained frightened “ah” with a trembling glottal source, moving vowel formants, breath, wet throat noise and a 230–2800 Hz radio band. It lasts about 0.9–1.0 s; the death impact cuts through the opening and the voice trails the slush. The -9 dBFS file peak is an export convention. In the mix the voice begins 45 ms after impact; its playback gain is chosen from A50 so the voice sits exactly 12 dB below its death cue. This keeps the long vocal audible without a sudden loud scream.

| File | s | A50 | A RMS | Peak | Centroid Hz | HF % | Tonal ms | Max xcorr | Playback gain | Mix A50 | Mix peak |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `frost_alien_scream_0.wav` | 0.940 | -18.58 | -21.96 | -9.00 | 830 | 0.00 | 0 | 0.097 | 0.379 | -14.96 | -4.36 |
| `frost_alien_scream_1.wav` | 0.907 | -18.18 | -22.04 | -9.00 | 862 | 0.00 | 0 | 0.097 | 0.362 | -14.81 | -4.48 |
| `frost_alien_scream_2.wav` | 0.982 | -19.69 | -22.06 | -9.00 | 867 | 0.00 | 0 | 0.097 | 0.431 | -15.00 | -4.59 |

`audition_frost.wav` plays variant 0 of each death cue in the order above with 1.2 s of silence after each item. Immediately after Cryo Jelly’s standalone death cue, it plays the standalone cry, then the cry mixed over that death cue. `sheet_frost.png` shows each death cue and the Cryo Jelly cry as variant-0 waveforms and spectrograms.

Ear-tuning priorities: Cryo Jelly’s synthetic vowel for an organic painful quality; Cryo Siren’s dish collapse to ensure it reads as metal/coolant instead of a siren; Hailstorm’s pellet density on phone speakers. Automated spectral checks cannot replace listening on the target phone. The existing game code still needs a separate integration change to load these WAVs.

