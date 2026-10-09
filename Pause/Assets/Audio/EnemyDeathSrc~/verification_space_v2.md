# Space death cue corrective pass

Delivered PCM: 44.1 kHz, mono, 16-bit. A level is the loudest 50 ms RMS with 25 ms hop after frequency-domain IEC A-weighting. Peak and A level are dBFS. Centroid is Hz; HF is the energy share above 4 kHz. Tonal is the longest run of 20 ms Hann frames (5 ms hop) with one bin above 45% of frame energy. Narrowband prominence uses 40 ms Hann frames (5 ms hop), the strongest 800-3500 Hz bin versus the median of its +/-250 Hz neighbourhood excluding +/-50 Hz; only active frames above -40 dB of maximum energy count. The prominence-run column is the longest run above 14 dB.

| File | A before | A after | Peak before | Peak after | Centroid before | Centroid after | HF before % | HF after % | Tonal before ms | Tonal after ms | Max prominence before dB | Max prominence after dB | >14 dB run after ms |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| space_fighter_1_0.wav | -14.18 | -15.50 | -3.00 | -5.51 | 1104 | 1182 | 0.71 | 1.34 | 0.0 | 0.0 | 17.9 | 17.7 | 15.0 |
| space_fighter_1_1.wav | -13.98 | -15.50 | -3.00 | -5.68 | 1126 | 1203 | 0.58 | 1.19 | 0.0 | 0.0 | 14.2 | 16.3 | 15.0 |
| space_fighter_1_2.wav | -13.67 | -15.50 | -3.00 | -5.83 | 1110 | 1159 | 0.29 | 0.75 | 0.0 | 0.0 | 13.8 | 15.8 | 10.0 |
<!-- space_fighter_1: maximum variant cross-correlation 0.182 -->
| space_fighter_2_0.wav | -13.84 | -15.50 | -3.00 | -5.91 | 993 | 1082 | 0.66 | 1.16 | 0.0 | 0.0 | 16.1 | 14.9 | 10.0 |
| space_fighter_2_1.wav | -13.32 | -15.50 | -3.00 | -6.13 | 978 | 1031 | 0.16 | 0.50 | 0.0 | 0.0 | 13.0 | 16.0 | 15.0 |
| space_fighter_2_2.wav | -13.42 | -15.50 | -3.00 | -6.29 | 1019 | 1075 | 0.52 | 0.97 | 5.0 | 5.0 | 16.6 | 16.5 | 24.9 |
<!-- space_fighter_2: maximum variant cross-correlation 0.155 -->
| space_fighter_3_0.wav | -17.17 | -15.50 | -3.00 | -2.80 | 687 | 723 | 0.01 | 0.04 | 0.0 | 0.0 | 14.4 | 14.3 | 5.0 |
| space_fighter_3_1.wav | -17.25 | -15.50 | -3.00 | -2.74 | 696 | 726 | 0.01 | 0.05 | 0.0 | 0.0 | 16.8 | 17.9 | 15.0 |
| space_fighter_3_2.wav | -16.49 | -15.50 | -3.00 | -3.32 | 771 | 829 | 0.05 | 0.20 | 0.0 | 0.0 | 14.1 | 14.0 | 5.0 |
<!-- space_fighter_3: maximum variant cross-correlation 0.188 -->
| space_fighter_4_0.wav | -17.13 | -15.50 | -3.00 | -2.55 | 439 | 451 | 0.01 | 0.03 | 5.0 | 10.0 | 13.1 | 13.8 | 0.0 |
| space_fighter_4_1.wav | -16.87 | -15.50 | -3.00 | -2.48 | 466 | 477 | 0.02 | 0.05 | 5.0 | 10.0 | 13.0 | 14.1 | 5.0 |
| space_fighter_4_2.wav | -16.11 | -15.50 | -3.00 | -3.73 | 413 | 423 | 0.00 | 0.01 | 5.0 | 5.0 | 13.4 | 15.4 | 5.0 |
<!-- space_fighter_4: maximum variant cross-correlation 0.203 -->
| space_chaser_0.wav | -15.68 | -15.50 | -3.00 | -3.57 | 643 | 678 | 0.11 | 0.24 | 0.0 | 0.0 | 17.5 | 16.4 | 15.0 |
| space_chaser_1.wav | -15.70 | -15.50 | -3.00 | -3.81 | 590 | 626 | 0.04 | 0.12 | 0.0 | 0.0 | 14.5 | 14.7 | 5.0 |
| space_chaser_2.wav | -16.25 | -15.50 | -3.00 | -2.88 | 639 | 676 | 0.16 | 0.30 | 5.0 | 5.0 | 20.0 | 19.0 | 29.9 |
<!-- space_chaser: maximum variant cross-correlation 0.165 -->
| space_alien_0.wav | -15.42 | -15.50 | -3.00 | -4.29 | 654 | 662 | 0.01 | 0.07 | 0.0 | 0.0 | 16.5 | 14.5 | 5.0 |
| space_alien_1.wav | -16.63 | -15.50 | -3.00 | -3.09 | 620 | 627 | 0.00 | 0.02 | 0.0 | 0.0 | 12.4 | 14.0 | 5.0 |
| space_alien_2.wav | -16.46 | -15.50 | -3.00 | -3.20 | 606 | 617 | 0.04 | 0.11 | 0.0 | 0.0 | 14.5 | 14.1 | 5.0 |
<!-- space_alien: maximum variant cross-correlation 0.174 -->
| space_big_0.wav | -14.50 | -14.00 | -3.00 | -3.40 | 456 | 485 | 0.08 | 0.21 | 10.0 | 10.0 | 15.0 | 17.8 | 10.0 |
| space_big_1.wav | -14.76 | -14.00 | -3.00 | -3.21 | 469 | 502 | 0.15 | 0.30 | 29.9 | 15.0 | 16.9 | 14.3 | 5.0 |
| space_big_2.wav | -14.50 | -14.00 | -3.00 | -3.47 | 479 | 503 | 0.10 | 0.23 | 15.0 | 29.9 | 15.8 | 15.9 | 10.0 |
<!-- space_big: maximum variant cross-correlation 0.113 -->
| space_mine_0.wav | -15.84 | -15.50 | -3.00 | -3.63 | 753 | 849 | 0.59 | 1.01 | 5.0 | 0.0 | 19.3 | 18.6 | 15.0 |
| space_mine_1.wav | -15.62 | -15.50 | -3.00 | -4.07 | 675 | 758 | 0.20 | 0.48 | 0.0 | 0.0 | 14.8 | 16.7 | 20.0 |
| space_mine_2.wav | -14.54 | -15.50 | -3.00 | -5.37 | 648 | 731 | 0.05 | 0.22 | 5.0 | 5.0 | 13.2 | 13.1 | 0.0 |
<!-- space_mine: maximum variant cross-correlation 0.208 -->
| space_rock_crater_0.wav | -14.05 | -15.50 | -3.00 | -5.49 | 734 | 762 | 0.18 | 0.38 | 5.0 | 5.0 | 14.0 | 16.2 | 10.0 |
| space_rock_crater_1.wav | -13.53 | -15.50 | -3.00 | -5.73 | 778 | 800 | 0.17 | 0.37 | 0.0 | 0.0 | 14.8 | 15.4 | 10.0 |
| space_rock_crater_2.wav | -13.64 | -15.50 | -3.00 | -5.75 | 751 | 775 | 0.19 | 0.40 | 5.0 | 5.0 | 16.4 | 16.9 | 15.0 |
<!-- space_rock_crater: maximum variant cross-correlation 0.146 -->
| space_rock_cluster_0.wav | -14.78 | -15.50 | -3.00 | -4.64 | 873 | 904 | 0.32 | 0.58 | 0.0 | 0.0 | 15.5 | 16.8 | 5.0 |
| space_rock_cluster_1.wav | -14.53 | -15.50 | -3.00 | -5.15 | 868 | 892 | 0.13 | 0.33 | 5.0 | 5.0 | 14.5 | 15.1 | 5.0 |
| space_rock_cluster_2.wav | -13.99 | -15.50 | -3.00 | -5.38 | 940 | 960 | 0.35 | 0.63 | 0.0 | 0.0 | 15.8 | 15.6 | 24.9 |
<!-- space_rock_cluster: maximum variant cross-correlation 0.120 -->
| space_rock_dark_0.wav | -16.97 | -15.50 | -3.00 | -2.41 | 479 | 497 | 0.04 | 0.10 | 5.0 | 5.0 | 14.9 | 15.8 | 15.0 |
| space_rock_dark_1.wav | -17.37 | -15.50 | -3.00 | -2.75 | 473 | 492 | 0.01 | 0.04 | 5.0 | 5.0 | 14.6 | 13.9 | 0.0 |
| space_rock_dark_2.wav | -16.36 | -15.50 | -3.00 | -3.52 | 513 | 530 | 0.03 | 0.09 | 0.0 | 0.0 | 15.9 | 16.1 | 5.0 |
<!-- space_rock_dark: maximum variant cross-correlation 0.162 -->
| space_elite_eventide_bastion_0.wav | -14.14 | -14.00 | -3.00 | -3.90 | 493 | 525 | 0.10 | 0.22 | 0.0 | 5.0 | 13.7 | 16.8 | 10.0 |
| space_elite_eventide_bastion_1.wav | -14.28 | -14.00 | -3.00 | -3.03 | 509 | 537 | 0.21 | 0.35 | 0.0 | 0.0 | 14.3 | 14.3 | 5.0 |
| space_elite_eventide_bastion_2.wav | -13.49 | -14.00 | -3.00 | -4.84 | 511 | 537 | 0.15 | 0.26 | 5.0 | 5.0 | 15.0 | 15.0 | 10.0 |
<!-- space_elite_eventide_bastion: maximum variant cross-correlation 0.143 -->
| space_elite_orbit_reaver_0.wav | -11.72 | -14.00 | -3.00 | -6.18 | 754 | 743 | 0.36 | 0.58 | 0.0 | 0.0 | 17.0 | 16.0 | 10.0 |
| space_elite_orbit_reaver_1.wav | -12.70 | -14.00 | -3.00 | -4.60 | 803 | 799 | 0.64 | 0.89 | 0.0 | 0.0 | 16.8 | 17.2 | 10.0 |
| space_elite_orbit_reaver_2.wav | -12.61 | -14.00 | -3.00 | -4.36 | 801 | 835 | 0.76 | 1.16 | 0.0 | 5.0 | 17.7 | 16.4 | 10.0 |
<!-- space_elite_orbit_reaver: maximum variant cross-correlation 0.139 -->
| space_elite_rift_lancer_0.wav | -11.42 | -14.00 | -3.00 | -5.40 | 805 | 905 | 0.71 | 1.42 | 0.0 | 5.0 | 15.3 | 14.9 | 5.0 |
| space_elite_rift_lancer_1.wav | -11.47 | -14.00 | -3.00 | -5.93 | 852 | 998 | 0.79 | 1.77 | 0.0 | 5.0 | 15.6 | 18.9 | 15.0 |
| space_elite_rift_lancer_2.wav | -11.60 | -14.00 | -3.00 | -5.65 | 827 | 1012 | 0.75 | 1.88 | 5.0 | 5.0 | 15.0 | 14.5 | 10.0 |
<!-- space_elite_rift_lancer: maximum variant cross-correlation 0.108 -->
| space_elite_singularity_hauler_0.wav | -14.25 | -14.00 | -3.00 | -3.71 | 373 | 390 | 0.04 | 0.11 | 10.0 | 20.0 | 15.6 | 15.9 | 10.0 |
| space_elite_singularity_hauler_1.wav | -15.11 | -14.00 | -3.00 | -3.03 | 350 | 368 | 0.05 | 0.11 | 15.0 | 24.9 | 15.4 | 15.4 | 5.0 |
| space_elite_singularity_hauler_2.wav | -15.22 | -14.00 | -3.00 | -3.12 | 368 | 388 | 0.13 | 0.23 | 24.9 | 24.9 | 18.6 | 18.6 | 29.9 |
<!-- space_elite_singularity_hauler: maximum variant cross-correlation 0.153 -->

Ordinary 30-file A-level spread: 0.00 dB (limit 2.0).  Big/elite 15-file range: -14.00 to -14.00 dBFS (target -14.0 +/-0.7). The brief per-frame prominence spikes shown above never persist longer than 30 ms. Orbit Reaver averaged-spectrum 1-3 kHz partial prominence: 4.8 dB (limit 12). Maximum variant cross-correlation: 0.208 (limit 0.6).

## Scream and death mixes

Existing scream WAVs are read only. The scream starts at its authored delay. Gain sets its A-weighted loudest 50 ms to 12 dB below the matching death WAV. Variant-zero mixes are rendered in `audition_space_scream_mixes.wav` with 1.2 s gaps.

| Enemy | Variant | Scream gain | Death A dBFS | Mix A dBFS | Added dB | Mix peak dBFS |
|---|---:|---:|---:|---:|---:|---:|
| Needle | 0 | 0.377 | -15.50 | -15.43 | +0.07 | -5.51 |
| Needle | 1 | 0.386 | -15.50 | -15.50 | +0.00 | -5.68 |
| Needle | 2 | 0.369 | -15.50 | -15.56 | -0.06 | -5.83 |
| Steel Claw | 0 | 0.364 | -15.50 | -15.51 | -0.01 | -5.91 |
| Steel Claw | 1 | 0.389 | -15.50 | -15.45 | +0.05 | -6.13 |
| Steel Claw | 2 | 0.386 | -15.50 | -15.33 | +0.17 | -6.29 |
| Twin Claw | 0 | 0.373 | -15.50 | -15.17 | +0.33 | -2.49 |
| Twin Claw | 1 | 0.343 | -15.50 | -15.38 | +0.12 | -2.74 |
| Twin Claw | 2 | 0.359 | -15.50 | -15.42 | +0.08 | -3.32 |
| Warden | 0 | 0.399 | -15.50 | -15.00 | +0.50 | -3.46 |
| Warden | 1 | 0.407 | -15.50 | -15.42 | +0.08 | -2.52 |
| Warden | 2 | 0.404 | -15.50 | -15.34 | +0.16 | -3.50 |
| Bile Mite | 0 | 0.343 | -15.50 | -15.31 | +0.19 | -3.45 |
| Bile Mite | 1 | 0.295 | -15.50 | -15.25 | +0.25 | -3.09 |
| Bile Mite | 2 | 0.333 | -15.50 | -15.42 | +0.08 | -3.84 |
| Eventide Bastion | 0 | 0.561 | -14.00 | -13.78 | +0.22 | -3.26 |
| Eventide Bastion | 1 | 0.437 | -14.00 | -13.67 | +0.33 | -2.97 |
| Eventide Bastion | 2 | 0.485 | -14.00 | -13.61 | +0.39 | -3.39 |
| Orbit Reaver | 0 | 0.435 | -14.00 | -13.74 | +0.26 | -6.18 |
| Orbit Reaver | 1 | 0.427 | -14.00 | -13.86 | +0.14 | -4.60 |
| Orbit Reaver | 2 | 0.416 | -14.00 | -13.92 | +0.08 | -4.36 |
| Rift Lancer | 0 | 0.416 | -14.00 | -13.77 | +0.23 | -5.10 |
| Rift Lancer | 1 | 0.385 | -14.00 | -13.78 | +0.22 | -5.14 |
| Rift Lancer | 2 | 0.426 | -14.00 | -13.76 | +0.24 | -5.14 |
| Singularity Hauler | 0 | 0.473 | -14.00 | -13.64 | +0.36 | -2.92 |
| Singularity Hauler | 1 | 0.479 | -14.00 | -13.74 | +0.26 | -2.16 |
| Singularity Hauler | 2 | 0.457 | -14.00 | -13.86 | +0.14 | -2.41 |

The death cues retain their 20 ms end fades, seeded variants, and authored timing. The original 45 WAVs are in `original_v1/`. A listening pass remains necessary because these checks cannot judge the actual perceived character.
