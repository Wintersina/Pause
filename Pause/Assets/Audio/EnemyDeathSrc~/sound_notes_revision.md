# Verdant / Ember death and scream revision

Deterministic mono 44.1 kHz, 16-bit PCM. `build_revision.py` imports the original builder read-only and writes only these selected existing WAV names. `audition_revision.wav` contains a before/after pair for **every** changed file, in the table order below: 0.25 s within each pair and 0.65 s between pairs.

## Changes

- Rebuilt all 45 Verdant/Ember screams as held open-vowel cries. Pilots are 0.82–1.00 s nominal, Brass Vulture 1.16 s, Resin Warden 1.20 s, and the other creature/elites 0.91–1.08 s. All have a 10 ms attack, 5–7 Hz pitch vibrato, irregular fear tremor, a falling finish, growing breath, radio band limit, and a 20 ms blast cut. Voice file peak is -9 dBFS and full-clip A-weighted RMS targets -22 dBFS.
- Brass Vulture deaths: three heavy wing whumps, tearing brass, low rasping bird gasp and feathery metal grains.
- Hornet Queen deaths: three descending noise wing stutters (each under 90 ms), chitin crack, hollow husk, and wet thud.
- Ember fighter 1–4 deaths: moving servo seizure and metal tear under existing vent/hull blasts; their original static debris alone gave these pilot craft a weaker identity.
- Existing Verdant fighter 1–3 buzz/wing layers and other elites’ ash, cannon, broadside, kiln and lance layers were already specific in the source and were left untouched.
- Reduced the requested hot heavy variants by 1.5 dB. The delivered hot cues had full-clip A-weighted RMS near -18.5 dBFS already; Space bigs/elites measure roughly -20 dBFS, so the 1.5 dB reduction brings these closer to Space and their loudest A-weighted 50 ms to -15.5 dBFS.

The runtime delay is 34–55 ms and `ScreamVolume` is 0.5. The mix-gap column measures that actual fixed runtime gain against each matching death cue.

## Verification of every changed delivered WAV

| File | s | Δ length % | Peak dBFS | A50 dBFS | A full dBFS | Centroid Hz | >3.5 kHz % | >4 kHz % | 1–3 kHz ring ms | Tonal ms | Clips | Max xcorr | Mix gap dB | Runtime gain |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `verdant_fighter_4_0.wav` | 0.680 | +0.0 | -2.67 | -14.00 | -19.20 | 637 | 0.10 | 0.030 | 10 | 0 | 0 | 0.120 | None | None |
| `verdant_fighter_4_1.wav` | 0.656 | -3.5 | -2.45 | -14.00 | -19.17 | 629 | 0.08 | 0.025 | 0 | 0 | 0 | 0.120 | None | None |
| `verdant_fighter_4_2.wav` | 0.711 | +4.5 | -3.54 | -14.00 | -18.79 | 621 | 0.09 | 0.028 | 0 | 0 | 0 | 0.120 | None | None |
| `verdant_big_0.wav` | 1.100 | +0.0 | -6.59 | -15.50 | -20.39 | 521 | 0.63 | 0.492 | 0 | 5 | 0 | 0.108 | None | None |
| `verdant_elite_resin_warden_0.wav` | 0.920 | +0.0 | -6.15 | -15.50 | -19.95 | 564 | 0.83 | 0.655 | 0 | 10 | 0 | 0.098 | None | None |
| `verdant_elite_resin_warden_1.wav` | 0.888 | -3.5 | -5.10 | -15.50 | -20.13 | 568 | 0.84 | 0.675 | 0 | 10 | 0 | 0.098 | None | None |
| `ember_fighter_1_0.wav` | 0.390 | +0.0 | -7.79 | -15.50 | -19.56 | 1057 | 0.61 | 0.270 | 0 | 0 | 0 | 0.131 | None | None |
| `ember_fighter_1_1.wav` | 0.376 | -3.5 | -7.61 | -15.50 | -19.50 | 1070 | 0.73 | 0.344 | 0 | 0 | 0 | 0.131 | None | None |
| `ember_fighter_1_2.wav` | 0.408 | +4.5 | -8.38 | -15.50 | -19.61 | 1036 | 0.67 | 0.314 | 0 | 0 | 0 | 0.131 | None | None |
| `ember_fighter_2_0.wav` | 0.490 | +0.0 | -7.11 | -15.50 | -20.43 | 994 | 0.76 | 0.325 | 0 | 0 | 0 | 0.125 | None | None |
| `ember_fighter_2_1.wav` | 0.473 | -3.5 | -7.17 | -15.50 | -19.74 | 984 | 0.57 | 0.254 | 0 | 0 | 0 | 0.125 | None | None |
| `ember_fighter_2_2.wav` | 0.512 | +4.5 | -6.29 | -15.50 | -19.94 | 960 | 0.58 | 0.280 | 0 | 0 | 0 | 0.125 | None | None |
| `ember_fighter_3_0.wav` | 0.530 | +0.0 | -8.04 | -15.50 | -20.01 | 1002 | 0.69 | 0.342 | 0 | 0 | 0 | 0.143 | None | None |
| `ember_fighter_3_1.wav` | 0.511 | -3.5 | -7.44 | -15.50 | -19.89 | 938 | 0.59 | 0.258 | 0 | 0 | 0 | 0.143 | None | None |
| `ember_fighter_3_2.wav` | 0.554 | +4.5 | -6.99 | -15.50 | -20.53 | 972 | 0.65 | 0.294 | 0 | 0 | 0 | 0.143 | None | None |
| `ember_fighter_4_0.wav` | 0.670 | +0.0 | -3.03 | -14.00 | -18.61 | 615 | 0.28 | 0.125 | 0 | 5 | 0 | 0.119 | None | None |
| `ember_fighter_4_1.wav` | 0.647 | -3.5 | -3.96 | -14.00 | -18.94 | 624 | 0.21 | 0.085 | 0 | 0 | 0 | 0.119 | None | None |
| `ember_fighter_4_2.wav` | 0.700 | +4.5 | -4.87 | -14.00 | -18.93 | 654 | 0.29 | 0.139 | 0 | 0 | 0 | 0.119 | None | None |
| `ember_big_1.wav` | 1.158 | -3.5 | -6.48 | -15.50 | -20.42 | 513 | 0.72 | 0.574 | 0 | 20 | 0 | 0.112 | None | None |
| `ember_elite_brass_vulture_0.wav` | 0.870 | +0.0 | -3.21 | -14.00 | -18.80 | 613 | 0.10 | 0.033 | 0 | 5 | 0 | 0.097 | None | None |
| `ember_elite_brass_vulture_1.wav` | 0.840 | -3.5 | -3.65 | -14.00 | -18.97 | 625 | 0.10 | 0.034 | 0 | 0 | 0 | 0.097 | None | None |
| `ember_elite_brass_vulture_2.wav` | 0.909 | +4.5 | -3.11 | -14.00 | -18.99 | 669 | 0.13 | 0.042 | 0 | 10 | 0 | 0.097 | None | None |
| `ember_elite_cauterizer_0.wav` | 0.920 | +0.0 | -6.30 | -15.50 | -20.26 | 511 | 0.72 | 0.562 | 0 | 20 | 0 | 0.140 | None | None |
| `ember_elite_cauterizer_1.wav` | 0.888 | -3.5 | -5.32 | -15.50 | -19.95 | 477 | 0.61 | 0.485 | 0 | 5 | 0 | 0.140 | None | None |
| `ember_elite_cauterizer_2.wav` | 0.961 | +4.5 | -6.05 | -15.50 | -20.36 | 508 | 0.70 | 0.555 | 0 | 15 | 0 | 0.140 | None | None |
| `ember_elite_kilnback_0.wav` | 0.980 | +0.0 | -5.80 | -15.50 | -20.35 | 511 | 0.68 | 0.531 | 0 | 0 | 0 | 0.123 | None | None |
| `ember_elite_kilnback_1.wav` | 0.946 | -3.5 | -6.12 | -15.50 | -20.20 | 484 | 0.63 | 0.490 | 0 | 5 | 0 | 0.123 | None | None |
| `ember_elite_kilnback_2.wav` | 1.024 | +4.5 | -6.09 | -15.50 | -20.48 | 475 | 0.59 | 0.465 | 0 | 5 | 0 | 0.123 | None | None |
| `verdant_fighter_1_scream_0.wav` | 0.840 | +0.0 | -9.00 | -20.49 | -22.00 | 1027 | 0.00 | 0.000 | 10 | 0 | 0 | 0.133 | 11.0 | 0.500 |
| `verdant_fighter_1_scream_1.wav` | 0.806 | -4.0 | -9.00 | -20.62 | -22.00 | 1001 | 0.00 | 0.000 | 0 | 0 | 0 | 0.133 | 11.1 | 0.500 |
| `verdant_fighter_1_scream_2.wav` | 0.874 | +4.0 | -9.00 | -20.21 | -22.00 | 946 | 0.00 | 0.000 | 0 | 0 | 0 | 0.133 | 10.7 | 0.500 |
| `verdant_fighter_2_scream_0.wav` | 0.880 | +0.0 | -9.00 | -19.92 | -22.00 | 989 | 0.00 | 0.000 | 0 | 0 | 0 | 0.113 | 10.4 | 0.500 |
| `verdant_fighter_2_scream_1.wav` | 0.845 | -4.0 | -9.00 | -19.98 | -22.80 | 1018 | 0.00 | 0.000 | 0 | 0 | 0 | 0.113 | 10.5 | 0.500 |
| `verdant_fighter_2_scream_2.wav` | 0.915 | +4.0 | -9.00 | -19.79 | -22.00 | 1018 | 0.00 | 0.000 | 0 | 0 | 0 | 0.113 | 10.3 | 0.500 |
| `verdant_fighter_3_scream_0.wav` | 0.930 | +0.0 | -9.00 | -19.61 | -23.20 | 1027 | 0.00 | 0.000 | 0 | 0 | 0 | 0.078 | 10.1 | 0.500 |
| `verdant_fighter_3_scream_1.wav` | 0.893 | -4.0 | -9.00 | -19.58 | -22.80 | 1045 | 0.00 | 0.000 | 0 | 0 | 0 | 0.078 | 10.1 | 0.500 |
| `verdant_fighter_3_scream_2.wav` | 0.967 | +4.0 | -9.00 | -19.92 | -22.00 | 967 | 0.00 | 0.000 | 0 | 0 | 0 | 0.078 | 10.4 | 0.500 |
| `verdant_fighter_4_scream_0.wav` | 1.000 | +0.0 | -9.00 | -20.08 | -22.00 | 921 | 0.00 | 0.000 | 0 | 0 | 0 | 0.085 | 12.1 | 0.500 |
| `verdant_fighter_4_scream_1.wav` | 0.960 | -4.0 | -9.00 | -20.11 | -22.00 | 970 | 0.00 | 0.000 | 10 | 0 | 0 | 0.085 | 12.1 | 0.500 |
| `verdant_fighter_4_scream_2.wav` | 1.040 | +4.0 | -9.00 | -20.22 | -22.00 | 933 | 0.00 | 0.000 | 0 | 0 | 0 | 0.085 | 12.2 | 0.500 |
| `verdant_alien_scream_0.wav` | 0.910 | +0.0 | -9.00 | -19.99 | -22.00 | 704 | 0.00 | 0.000 | 10 | 40 | 0 | 0.128 | 10.5 | 0.500 |
| `verdant_alien_scream_1.wav` | 0.874 | -4.0 | -9.00 | -20.26 | -22.00 | 768 | 0.00 | 0.000 | 0 | 5 | 0 | 0.128 | 10.8 | 0.500 |
| `verdant_alien_scream_2.wav` | 0.946 | +4.0 | -9.00 | -19.65 | -22.80 | 664 | 0.00 | 0.000 | 0 | 25 | 0 | 0.128 | 10.2 | 0.500 |
| `verdant_elite_resin_warden_scream_0.wav` | 1.200 | +0.0 | -9.00 | -19.65 | -22.00 | 887 | 0.00 | 0.000 | 0 | 0 | 0 | 0.073 | 10.2 | 0.500 |
| `verdant_elite_resin_warden_scream_1.wav` | 1.152 | -4.0 | -9.00 | -19.63 | -22.00 | 916 | 0.00 | 0.000 | 0 | 0 | 0 | 0.073 | 10.1 | 0.500 |
| `verdant_elite_resin_warden_scream_2.wav` | 1.248 | +4.0 | -9.00 | -19.90 | -22.00 | 877 | 0.00 | 0.000 | 0 | 0 | 0 | 0.073 | 11.9 | 0.500 |
| `ember_fighter_1_scream_0.wav` | 0.820 | +0.0 | -9.00 | -19.59 | -22.00 | 953 | 0.00 | 0.000 | 10 | 0 | 0 | 0.117 | 10.1 | 0.500 |
| `ember_fighter_1_scream_1.wav` | 0.787 | -4.0 | -9.00 | -20.04 | -22.00 | 1013 | 0.00 | 0.000 | 0 | 0 | 0 | 0.117 | 10.6 | 0.500 |
| `ember_fighter_1_scream_2.wav` | 0.853 | +4.0 | -9.00 | -19.67 | -22.00 | 841 | 0.00 | 0.000 | 0 | 15 | 0 | 0.117 | 10.2 | 0.500 |
| `ember_fighter_2_scream_0.wav` | 0.890 | +0.0 | -9.00 | -20.04 | -22.00 | 941 | 0.00 | 0.000 | 0 | 15 | 0 | 0.130 | 10.6 | 0.500 |
| `ember_fighter_2_scream_1.wav` | 0.854 | -4.0 | -9.00 | -20.14 | -22.00 | 1024 | 0.00 | 0.000 | 0 | 0 | 0 | 0.130 | 10.7 | 0.500 |
| `ember_fighter_2_scream_2.wav` | 0.926 | +4.0 | -9.00 | -19.72 | -22.00 | 992 | 0.00 | 0.000 | 0 | 0 | 0 | 0.130 | 10.2 | 0.500 |
| `ember_fighter_3_scream_0.wav` | 0.910 | +0.0 | -9.00 | -19.63 | -22.00 | 968 | 0.00 | 0.000 | 0 | 0 | 0 | 0.085 | 10.2 | 0.500 |
| `ember_fighter_3_scream_1.wav` | 0.874 | -4.0 | -9.00 | -19.97 | -22.00 | 1033 | 0.00 | 0.000 | 0 | 0 | 0 | 0.085 | 10.5 | 0.500 |
| `ember_fighter_3_scream_2.wav` | 0.946 | +4.0 | -9.00 | -19.85 | -22.00 | 940 | 0.00 | 0.000 | 0 | 0 | 0 | 0.085 | 10.4 | 0.500 |
| `ember_fighter_4_scream_0.wav` | 0.990 | +0.0 | -9.00 | -19.70 | -22.00 | 959 | 0.00 | 0.000 | 0 | 0 | 0 | 0.081 | 11.7 | 0.500 |
| `ember_fighter_4_scream_1.wav` | 0.950 | -4.0 | -9.00 | -19.01 | -22.00 | 995 | 0.00 | 0.000 | 0 | 0 | 0 | 0.081 | 11.0 | 0.500 |
| `ember_fighter_4_scream_2.wav` | 1.030 | +4.0 | -9.00 | -19.46 | -22.00 | 948 | 0.00 | 0.000 | 0 | 0 | 0 | 0.081 | 11.5 | 0.500 |
| `ember_alien_scream_0.wav` | 0.910 | +0.0 | -9.00 | -19.60 | -22.00 | 682 | 0.00 | 0.000 | 10 | 15 | 0 | 0.155 | 10.1 | 0.500 |
| `ember_alien_scream_1.wav` | 0.874 | -4.0 | -9.00 | -19.62 | -22.00 | 724 | 0.00 | 0.000 | 0 | 25 | 0 | 0.155 | 10.1 | 0.500 |
| `ember_alien_scream_2.wav` | 0.946 | +4.0 | -9.00 | -20.01 | -22.00 | 632 | 0.00 | 0.000 | 0 | 85 | 0 | 0.155 | 10.5 | 0.500 |
| `ember_elite_ash_wraith_scream_0.wav` | 1.080 | +0.0 | -9.00 | -20.07 | -22.00 | 886 | 0.00 | 0.000 | 0 | 0 | 0 | 0.086 | 12.1 | 0.500 |
| `ember_elite_ash_wraith_scream_1.wav` | 1.037 | -4.0 | -9.00 | -20.08 | -22.00 | 898 | 0.00 | 0.000 | 0 | 0 | 0 | 0.086 | 12.1 | 0.500 |
| `ember_elite_ash_wraith_scream_2.wav` | 1.123 | +4.0 | -9.00 | -19.30 | -22.00 | 884 | 0.00 | 0.000 | 0 | 0 | 0 | 0.086 | 11.3 | 0.500 |
| `ember_elite_brass_vulture_scream_0.wav` | 1.160 | +0.0 | -9.00 | -20.18 | -22.00 | 941 | 0.00 | 0.000 | 0 | 0 | 0 | 0.081 | 12.2 | 0.500 |
| `ember_elite_brass_vulture_scream_1.wav` | 1.114 | -4.0 | -9.00 | -19.89 | -22.00 | 968 | 0.00 | 0.000 | 0 | 0 | 0 | 0.081 | 11.9 | 0.500 |
| `ember_elite_brass_vulture_scream_2.wav` | 1.206 | +4.0 | -9.00 | -19.77 | -22.00 | 896 | 0.00 | 0.000 | 0 | 0 | 0 | 0.081 | 11.8 | 0.500 |
| `ember_elite_coalrunner_scream_0.wav` | 1.030 | +0.0 | -9.00 | -20.04 | -22.00 | 931 | 0.00 | 0.000 | 0 | 0 | 0 | 0.098 | 12.1 | 0.500 |
| `ember_elite_coalrunner_scream_1.wav` | 0.989 | -4.0 | -9.00 | -19.20 | -22.00 | 967 | 0.00 | 0.000 | 0 | 0 | 0 | 0.098 | 11.2 | 0.500 |
| `ember_elite_coalrunner_scream_2.wav` | 1.071 | +4.0 | -9.00 | -19.44 | -22.00 | 907 | 0.00 | 0.000 | 0 | 0 | 0 | 0.098 | 11.5 | 0.500 |
| `ember_elite_sunstoke_scream_0.wav` | 1.040 | +0.0 | -9.00 | -19.52 | -22.00 | 991 | 0.00 | 0.000 | 0 | 5 | 0 | 0.091 | 11.5 | 0.500 |
| `ember_elite_sunstoke_scream_1.wav` | 0.998 | -4.0 | -9.00 | -19.48 | -22.00 | 1036 | 0.00 | 0.000 | 0 | 0 | 0 | 0.091 | 11.5 | 0.500 |
| `ember_elite_sunstoke_scream_2.wav` | 1.082 | +4.0 | -9.00 | -19.71 | -22.00 | 979 | 0.00 | 0.000 | 0 | 5 | 0 | 0.091 | 11.7 | 0.500 |

**Result:** PASS: 73 changed WAVs.
No listening monitor was available. The noise-led identity layers, moving/damped pitch, low formants, steep voice cutoff below 4 kHz, and delivered-PCM spectral/ring checks protect against beepiness and shrillness; phone playback still needs an ear pass.
