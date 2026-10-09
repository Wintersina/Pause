# Verdant and Ember death sounds and scream layers

Deterministic mono 44.1 kHz, 16-bit PCM. The existing Space builder toolkit was copied into this standalone script: noise/filter/envelope/thump/short room, A-weighted 50 ms matching, vocal source/formants, PNG sheet and delivered-PCM verification. No external sound library is used.

Normal death target: -15.5 dBFS A-weighted loudest 50 ms; big and elite target: -14 dBFS. Voice files peak at -9 dBFS. For playback, use each row’s scream gain after its onset delay: this places the voice 12 dB below the matching death cue in the A-weighted loudest window. The audition mixes apply that gain. Attack is the 10–90% rise of the authored onset envelope, following the approved Space verification method; the waveform includes later and sometimes louder material strikes. Tonal runs use delivered PCM in active 20 ms Hann frames, 5 ms hops, with a dominant bin above 45% energy. Ring runs track 1–3 kHz lines 18 dB above local median in 40 ms frames and 10 ms hops. HF is energy above 4 kHz. Max xcorr is the largest pairwise full-lag correlation among three variants.

## Material and unit choices

| World | Unit | Key | Physical event | Scream |
|---|---|---|---|---|
| Verdant | Gnat | `verdant_fighter_1` | small chitin split, leaf wings, tiny engine bits; dark 150-1700 Hz body plus short room | small strained pilot cry |
| Verdant | Wasp | `verdant_fighter_2` | striped thorax crack, stinger snap, wing scatter; dark 125-1550 Hz body plus short room | nasal tense pilot cry |
| Verdant | Mantis | `verdant_fighter_3` | two scythe-arm breaks, shell and servo rubble; dark 100-1400 Hz body plus short room | rough lower pilot cry |
| Verdant | Hornet Queen | `verdant_fighter_4` | heavy chitin rupture, four wings, iron joints; dark 75-1100 Hz body plus short room | deep insectoid-human rasp |
| Verdant | Dragonsting | `verdant_chaser` | mandible crunch, barbed tail and wing stop; dark 115-1550 Hz body plus short room | none |
| Verdant | Snap Sprout | `verdant_alien` | wet fibrous jaw snap, leaf burst, root juice; dark 85-1200 Hz body plus short room | wet reedy plant chitter |
| Verdant | Bloom Maw | `verdant_big` | petal rupture, pulp and resin, deep maw collapse; dark 40-700 Hz body plus short room | none |
| Verdant | Burr Mine | `verdant_mine` | dry husk crack, pressure pop and seeds; dark 90-1400 Hz body plus short room | none |
| Verdant | Thorn Pod | `verdant_rock_pod` | woody split and hard thorn snaps; dark 75-1350 Hz body plus short room | none |
| Verdant | Spore Rock | `verdant_rock_spore` | muffled spore puff, papery skin and fine dust; dark 60-1000 Hz body plus short room | none |
| Verdant | Bramble Knot | `verdant_rock_knot` | twisted woody splinter and creaking snap; dark 65-1250 Hz body plus short room | none |
| Verdant | Vine Rock | `verdant_rock_vine` | stone crack, whip-like vine breaks and leaves; dark 55-1100 Hz body plus short room | none |
| Verdant | Resin Warden | `verdant_elite_resin_warden` | sticky resin plates, wood armour and gloop; dark 42-780 Hz body plus short room | deep muffled guardian groan |
| Ember | Cinder | `ember_fighter_1` | small char hull crunch and furnace vent spit; dark 145-1650 Hz body plus short room | high startled pilot gasp |
| Ember | Scorch | `ember_fighter_2` | forked iron jaws, four vent ruptures; dark 100-1500 Hz body plus short room | forceful scorched pilot cry |
| Ember | Brand | `ember_fighter_3` | horn and fin snaps, ember flare and iron shear; dark 100-1400 Hz body plus short room | breathy pilot yell |
| Ember | Pyre | `ember_fighter_4` | heavy horned hull breach and furnace core blowout; dark 65-1050 Hz body plus short room | low commander shout |
| Ember | Cinder Fang | `ember_chaser` | hinged hot-metal jaw crunch and sizzling throat; dark 95-1400 Hz body plus short room | none |
| Ember | Ember Imp | `ember_alien` | basalt mask fracture, fire flare and ember scatter; dark 100-1450 Hz body plus short room | crackly fire creature shriek |
| Ember | Magma Skull | `ember_big` | deep basalt boom, stone jaw, slag splash and lava bubbles; dark 35-650 Hz body plus short room | none |
| Ember | Crucible Mine | `ember_mine` | boiling pot overflows, pressure pop and slag; dark 70-1350 Hz body plus short room | none |
| Ember | Magma Rock | `ember_rock_magma` | dense basalt fracture and molten crack; dark 55-1050 Hz body plus short room | none |
| Ember | Cinder Chunk | `ember_rock_cinder` | crumbly cinder, ash puff and dying embers; dark 110-1600 Hz body plus short room | none |
| Ember | Obsidian Shard | `ember_rock_obsidian` | dull glass crack and low passed chips; dark 120-1750 Hz body plus short room | none |
| Ember | Lava Islet | `ember_rock_islet` | large slab split, molten drops and steam; dark 40-850 Hz body plus short room | none |
| Ember | Ash Wraith | `ember_elite_ash_wraith` | soot shell folds, hollow ash burst and shards; dark 55-950 Hz body plus short room | hollow breathy wail |
| Ember | Brass Vulture | `ember_elite_brass_vulture` | brass wing joints and hooked claw shatter; dark 65-1050 Hz body plus short room | raspy bird-like cry |
| Ember | Cauterizer | `ember_elite_cauterizer` | siege cannon rupture and thick hot barrel collapse; dark 38-760 Hz body plus short room | none |
| Ember | Coalrunner | `ember_elite_coalrunner` | compact gunship armour and twin broadside rupture; dark 45-850 Hz body plus short room | muffled gunship pilot |
| Ember | Kilnback | `ember_elite_kilnback` | armoured furnace hauler splits and slag drains; dark 35-700 Hz body plus short room | none |
| Ember | Sunstoke | `ember_elite_sunstoke` | lance shaft buckles, hot fins tear and core bursts; dark 50-850 Hz body plus short room | sharp interceptor pilot |

### Scream decisions

Verdant fighters have tiny pilots within insect-machine shells; the Hornet Queen has a lower strained voice with a small noise rasp. Snap Sprout is a plant creature and Resin Warden reads as an inhabited resin guardian. Ember fighters and the craft-shaped Coalrunner and Sunstoke plausibly have pilots. Ember Imp, Ash Wraith and Brass Vulture read as creatures, so they have brief creature voices. Cauterizer is a siege cannon and Kilnback is an armoured slag hauler in their definitions and sprites; both get mechanical deaths only. Chasers, bigs, mines and rocks have no voices.

### Verification of delivered PCM

| File | s | Peak dBFS | A50 dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | 1-3 kHz ring ms | Clips | Max xcorr | Mix peak dBFS | Scream gain |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `verdant_fighter_1_0.wav` | 0.380 | -9.59 | -15.50 | -19.96 | 1296 | 2.58 | 2.4 | 0.0 | 0.0 | 0 | 0.125 | — | — |
| `verdant_fighter_1_1.wav` | 0.367 | -7.25 | -15.50 | -19.36 | 1280 | 2.59 | 2.4 | 0.0 | 0.0 | 0 | 0.125 | — | — |
| `verdant_fighter_1_2.wav` | 0.397 | -9.02 | -15.50 | -19.85 | 1274 | 2.55 | 2.4 | 0.0 | 0.0 | 0 | 0.125 | — | — |
| `verdant_fighter_1_scream_0.wav` | 0.230 | -9.00 | -19.40 | -21.46 | 1127 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.164 | -8.89 | 0.393 |
| `verdant_fighter_1_scream_1.wav` | 0.221 | -9.00 | -18.44 | -19.88 | 1013 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.164 | -7.25 | 0.353 |
| `verdant_fighter_1_scream_2.wav` | 0.239 | -9.00 | -18.71 | -20.12 | 1005 | 0.00 | 5.3 | 0.0 | 10.0 | 0 | 0.164 | -8.16 | 0.363 |
| `verdant_fighter_2_0.wav` | 0.450 | -8.65 | -15.50 | -19.81 | 1154 | 2.26 | 2.4 | 0.0 | 0.0 | 0 | 0.110 | — | — |
| `verdant_fighter_2_1.wav` | 0.434 | -8.40 | -15.50 | -19.21 | 1215 | 2.39 | 2.4 | 0.0 | 0.0 | 0 | 0.110 | — | — |
| `verdant_fighter_2_2.wav` | 0.470 | -9.45 | -15.50 | -20.35 | 1184 | 2.28 | 2.4 | 0.0 | 0.0 | 0 | 0.110 | — | — |
| `verdant_fighter_2_scream_0.wav` | 0.270 | -9.00 | -19.62 | -22.01 | 950 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.199 | -8.15 | 0.404 |
| `verdant_fighter_2_scream_1.wav` | 0.259 | -9.00 | -19.09 | -21.05 | 1017 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.199 | -8.40 | 0.380 |
| `verdant_fighter_2_scream_2.wav` | 0.281 | -9.00 | -17.77 | -20.12 | 1020 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.199 | -9.05 | 0.326 |
| `verdant_fighter_3_0.wav` | 0.540 | -7.76 | -15.50 | -19.67 | 1091 | 2.07 | 2.4 | 0.0 | 0.0 | 0 | 0.116 | — | — |
| `verdant_fighter_3_1.wav` | 0.521 | -8.27 | -15.50 | -19.50 | 1079 | 1.97 | 2.4 | 0.0 | 0.0 | 0 | 0.116 | — | — |
| `verdant_fighter_3_2.wav` | 0.564 | -8.65 | -15.50 | -19.42 | 1068 | 1.97 | 2.4 | 5.0 | 0.0 | 0 | 0.116 | — | — |
| `verdant_fighter_3_scream_0.wav` | 0.300 | -9.00 | -20.67 | -22.88 | 951 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.239 | -7.76 | 0.456 |
| `verdant_fighter_3_scream_1.wav` | 0.288 | -9.00 | -21.20 | -23.69 | 993 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.239 | -8.27 | 0.484 |
| `verdant_fighter_3_scream_2.wav` | 0.312 | -9.00 | -19.67 | -21.95 | 886 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.239 | -8.31 | 0.406 |
| `verdant_fighter_4_0.wav` | 0.680 | -6.10 | -14.00 | -16.37 | 722 | 1.03 | 2.4 | 0.0 | 10.0 | 0 | 0.129 | — | — |
| `verdant_fighter_4_1.wav` | 0.656 | -6.25 | -14.00 | -16.05 | 727 | 0.93 | 2.4 | 0.0 | 0.0 | 0 | 0.129 | — | — |
| `verdant_fighter_4_2.wav` | 0.711 | -6.83 | -14.00 | -16.70 | 727 | 1.06 | 2.4 | 0.0 | 0.0 | 0 | 0.129 | — | — |
| `verdant_fighter_4_scream_0.wav` | 0.380 | -9.00 | -19.23 | -22.71 | 914 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.145 | -6.00 | 0.459 |
| `verdant_fighter_4_scream_1.wav` | 0.365 | -9.00 | -18.67 | -22.05 | 904 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.145 | -5.09 | 0.430 |
| `verdant_fighter_4_scream_2.wav` | 0.395 | -9.00 | -18.82 | -22.47 | 939 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.145 | -6.83 | 0.438 |
| `verdant_chaser_0.wav` | 0.500 | -8.95 | -15.50 | -20.64 | 1184 | 2.35 | 2.4 | 0.0 | 0.0 | 0 | 0.119 | — | — |
| `verdant_chaser_1.wav` | 0.482 | -8.18 | -15.50 | -20.40 | 1222 | 2.40 | 2.4 | 0.0 | 0.0 | 0 | 0.119 | — | — |
| `verdant_chaser_2.wav` | 0.522 | -8.40 | -15.50 | -20.26 | 1196 | 2.30 | 2.4 | 0.0 | 0.0 | 0 | 0.119 | — | — |
| `verdant_alien_0.wav` | 0.580 | -7.64 | -15.50 | -18.30 | 822 | 1.08 | 2.4 | 5.0 | 0.0 | 0 | 0.113 | — | — |
| `verdant_alien_1.wav` | 0.560 | -7.96 | -15.50 | -18.32 | 852 | 1.35 | 2.4 | 5.0 | 0.0 | 0 | 0.113 | — | — |
| `verdant_alien_2.wav` | 0.606 | -7.81 | -15.50 | -18.69 | 841 | 1.28 | 2.4 | 0.0 | 0.0 | 0 | 0.113 | — | — |
| `verdant_alien_scream_0.wav` | 0.340 | -9.00 | -18.19 | -22.12 | 814 | 0.00 | 5.3 | 5.0 | 0.0 | 0 | 0.188 | -7.59 | 0.342 |
| `verdant_alien_scream_1.wav` | 0.326 | -9.00 | -16.71 | -20.40 | 740 | 0.00 | 5.3 | 20.0 | 0.0 | 0 | 0.188 | -7.40 | 0.289 |
| `verdant_alien_scream_2.wav` | 0.354 | -9.00 | -17.68 | -22.26 | 798 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.188 | -7.81 | 0.323 |
| `verdant_big_0.wav` | 1.100 | -5.09 | -14.00 | -14.97 | 521 | 0.49 | 2.4 | 5.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_big_1.wav` | 1.061 | -5.50 | -14.00 | -15.16 | 535 | 0.50 | 2.4 | 5.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_big_2.wav` | 1.150 | -5.10 | -14.00 | -15.36 | 512 | 0.56 | 2.4 | 10.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_mine_0.wav` | 0.500 | -7.65 | -15.50 | -18.40 | 909 | 1.26 | 2.4 | 0.0 | 0.0 | 0 | 0.115 | — | — |
| `verdant_mine_1.wav` | 0.482 | -7.95 | -15.50 | -18.49 | 864 | 1.07 | 2.4 | 0.0 | 0.0 | 0 | 0.115 | — | — |
| `verdant_mine_2.wav` | 0.522 | -7.73 | -15.50 | -18.59 | 878 | 1.08 | 2.4 | 0.0 | 0.0 | 0 | 0.115 | — | — |
| `verdant_rock_pod_0.wav` | 0.520 | -8.19 | -15.50 | -18.77 | 911 | 1.41 | 2.4 | 0.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_rock_pod_1.wav` | 0.502 | -7.71 | -15.50 | -18.54 | 868 | 1.22 | 2.4 | 0.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_rock_pod_2.wav` | 0.543 | -8.37 | -15.50 | -19.05 | 891 | 1.12 | 2.4 | 0.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_rock_spore_0.wav` | 0.570 | -5.95 | -15.50 | -16.70 | 702 | 0.92 | 2.4 | 0.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_rock_spore_1.wav` | 0.550 | -6.61 | -15.50 | -17.40 | 656 | 0.47 | 2.4 | 0.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_rock_spore_2.wav` | 0.596 | -6.54 | -15.50 | -17.24 | 679 | 0.57 | 2.4 | 0.0 | 0.0 | 0 | 0.108 | — | — |
| `verdant_rock_knot_0.wav` | 0.620 | -7.79 | -15.50 | -19.21 | 915 | 1.48 | 2.4 | 10.0 | 0.0 | 0 | 0.105 | — | — |
| `verdant_rock_knot_1.wav` | 0.598 | -8.19 | -15.50 | -19.23 | 903 | 1.21 | 2.4 | 0.0 | 10.0 | 0 | 0.105 | — | — |
| `verdant_rock_knot_2.wav` | 0.648 | -7.99 | -15.50 | -18.99 | 933 | 1.54 | 2.4 | 0.0 | 0.0 | 0 | 0.105 | — | — |
| `verdant_rock_vine_0.wav` | 0.680 | -7.33 | -15.50 | -18.18 | 870 | 1.36 | 2.4 | 0.0 | 0.0 | 0 | 0.124 | — | — |
| `verdant_rock_vine_1.wav` | 0.656 | -8.02 | -15.50 | -19.11 | 849 | 1.16 | 2.4 | 0.0 | 0.0 | 0 | 0.124 | — | — |
| `verdant_rock_vine_2.wav` | 0.711 | -6.69 | -15.50 | -18.35 | 897 | 1.42 | 2.4 | 0.0 | 0.0 | 0 | 0.124 | — | — |
| `verdant_elite_resin_warden_0.wav` | 0.920 | -4.65 | -14.00 | -14.83 | 564 | 0.65 | 2.4 | 10.0 | 0.0 | 0 | 0.098 | — | — |
| `verdant_elite_resin_warden_1.wav` | 0.888 | -3.60 | -14.00 | -15.00 | 568 | 0.68 | 2.4 | 10.0 | 0.0 | 0 | 0.098 | — | — |
| `verdant_elite_resin_warden_2.wav` | 0.961 | -5.70 | -14.00 | -15.74 | 585 | 0.71 | 2.4 | 5.0 | 0.0 | 0 | 0.098 | — | — |
| `verdant_elite_resin_warden_scream_0.wav` | 0.460 | -9.00 | -21.01 | -24.89 | 927 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.155 | -4.65 | 0.563 |
| `verdant_elite_resin_warden_scream_1.wav` | 0.442 | -9.00 | -20.68 | -24.60 | 910 | 0.00 | 5.3 | 0.0 | 10.0 | 0 | 0.155 | -3.60 | 0.542 |
| `verdant_elite_resin_warden_scream_2.wav` | 0.478 | -9.00 | -19.58 | -23.76 | 936 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.155 | -5.70 | 0.477 |
| `ember_fighter_1_0.wav` | 0.390 | -8.27 | -15.50 | -18.88 | 1179 | 1.79 | 2.4 | 0.0 | 0.0 | 0 | 0.135 | — | — |
| `ember_fighter_1_1.wav` | 0.376 | -8.31 | -15.50 | -18.88 | 1218 | 2.22 | 2.4 | 0.0 | 0.0 | 0 | 0.135 | — | — |
| `ember_fighter_1_2.wav` | 0.408 | -8.30 | -15.50 | -19.03 | 1179 | 1.95 | 2.4 | 0.0 | 0.0 | 0 | 0.135 | — | — |
| `ember_fighter_1_scream_0.wav` | 0.230 | -9.00 | -19.04 | -20.59 | 1044 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.183 | -8.27 | 0.377 |
| `ember_fighter_1_scream_1.wav` | 0.221 | -9.00 | -18.06 | -19.47 | 1043 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.183 | -8.31 | 0.337 |
| `ember_fighter_1_scream_2.wav` | 0.239 | -9.00 | -18.97 | -19.89 | 897 | 0.00 | 5.3 | 5.0 | 0.0 | 0 | 0.183 | -7.98 | 0.375 |
| `ember_fighter_2_0.wav` | 0.490 | -8.93 | -15.50 | -19.66 | 1134 | 2.16 | 2.4 | 0.0 | 0.0 | 0 | 0.120 | — | — |
| `ember_fighter_2_1.wav` | 0.473 | -8.60 | -15.50 | -19.25 | 1091 | 1.65 | 2.4 | 0.0 | 0.0 | 0 | 0.120 | — | — |
| `ember_fighter_2_2.wav` | 0.512 | -8.25 | -15.50 | -18.78 | 1070 | 1.74 | 2.4 | 0.0 | 0.0 | 0 | 0.120 | — | — |
| `ember_fighter_2_scream_0.wav` | 0.270 | -9.00 | -19.04 | -20.79 | 914 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.232 | -7.31 | 0.378 |
| `ember_fighter_2_scream_1.wav` | 0.259 | -9.00 | -20.02 | -22.13 | 920 | 0.00 | 5.3 | 5.0 | 0.0 | 0 | 0.232 | -8.60 | 0.423 |
| `ember_fighter_2_scream_2.wav` | 0.281 | -9.00 | -19.17 | -21.11 | 931 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.232 | -8.25 | 0.383 |
| `ember_fighter_3_0.wav` | 0.530 | -8.32 | -15.50 | -19.10 | 1159 | 2.28 | 2.4 | 0.0 | 0.0 | 0 | 0.133 | — | — |
| `ember_fighter_3_1.wav` | 0.511 | -8.40 | -15.50 | -18.83 | 1061 | 1.76 | 2.4 | 0.0 | 0.0 | 0 | 0.133 | — | — |
| `ember_fighter_3_2.wav` | 0.554 | -8.65 | -15.50 | -19.20 | 1081 | 1.91 | 2.4 | 0.0 | 0.0 | 0 | 0.133 | — | — |
| `ember_fighter_3_scream_0.wav` | 0.290 | -9.00 | -19.75 | -22.48 | 1014 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.218 | -8.32 | 0.410 |
| `ember_fighter_3_scream_1.wav` | 0.278 | -9.00 | -20.27 | -22.70 | 1032 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.218 | -8.40 | 0.435 |
| `ember_fighter_3_scream_2.wav` | 0.302 | -9.00 | -20.16 | -22.83 | 938 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.218 | -8.65 | 0.429 |
| `ember_fighter_4_0.wav` | 0.670 | -5.58 | -14.00 | -15.85 | 646 | 0.79 | 2.4 | 5.0 | 0.0 | 0 | 0.111 | — | — |
| `ember_fighter_4_1.wav` | 0.647 | -5.99 | -14.00 | -15.84 | 641 | 0.56 | 2.4 | 0.0 | 0.0 | 0 | 0.111 | — | — |
| `ember_fighter_4_2.wav` | 0.700 | -5.61 | -14.00 | -15.67 | 700 | 0.98 | 2.4 | 0.0 | 0.0 | 0 | 0.111 | — | — |
| `ember_fighter_4_scream_0.wav` | 0.360 | -9.00 | -21.21 | -23.89 | 941 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.176 | -5.58 | 0.576 |
| `ember_fighter_4_scream_1.wav` | 0.346 | -9.00 | -18.90 | -22.06 | 962 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.176 | -5.99 | 0.441 |
| `ember_fighter_4_scream_2.wav` | 0.374 | -9.00 | -19.78 | -22.23 | 902 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.176 | -4.96 | 0.489 |
| `ember_chaser_0.wav` | 0.540 | -8.40 | -15.50 | -19.22 | 1051 | 1.68 | 2.4 | 0.0 | 0.0 | 0 | 0.116 | — | — |
| `ember_chaser_1.wav` | 0.521 | -8.05 | -15.50 | -18.85 | 1036 | 1.56 | 2.4 | 0.0 | 0.0 | 0 | 0.116 | — | — |
| `ember_chaser_2.wav` | 0.564 | -8.29 | -15.50 | -19.19 | 1047 | 1.63 | 2.4 | 0.0 | 0.0 | 0 | 0.116 | — | — |
| `ember_alien_0.wav` | 0.530 | -8.25 | -15.50 | -19.14 | 1209 | 2.31 | 2.4 | 0.0 | 0.0 | 0 | 0.133 | — | — |
| `ember_alien_1.wav` | 0.511 | -8.66 | -15.50 | -19.37 | 1136 | 1.76 | 2.4 | 0.0 | 0.0 | 0 | 0.133 | — | — |
| `ember_alien_2.wav` | 0.554 | -8.10 | -15.50 | -19.59 | 1240 | 2.52 | 2.4 | 0.0 | 0.0 | 0 | 0.133 | — | — |
| `ember_alien_scream_0.wav` | 0.310 | -9.00 | -17.78 | -20.85 | 856 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.192 | -7.86 | 0.327 |
| `ember_alien_scream_1.wav` | 0.298 | -9.00 | -16.52 | -20.55 | 834 | 0.00 | 5.3 | 5.0 | 0.0 | 0 | 0.192 | -8.26 | 0.282 |
| `ember_alien_scream_2.wav` | 0.322 | -9.00 | -17.67 | -21.55 | 841 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.192 | -8.10 | 0.322 |
| `ember_big_0.wav` | 1.200 | -4.08 | -14.00 | -14.98 | 512 | 0.59 | 2.4 | 29.9 | 0.0 | 0 | 0.112 | — | — |
| `ember_big_1.wav` | 1.158 | -4.98 | -14.00 | -14.78 | 513 | 0.57 | 2.4 | 20.0 | 0.0 | 0 | 0.112 | — | — |
| `ember_big_2.wav` | 1.254 | -4.50 | -14.00 | -15.31 | 539 | 0.61 | 2.4 | 5.0 | 0.0 | 0 | 0.112 | — | — |
| `ember_mine_0.wav` | 0.560 | -7.72 | -15.50 | -18.39 | 785 | 0.85 | 2.4 | 0.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_mine_1.wav` | 0.540 | -7.38 | -15.50 | -18.05 | 772 | 0.86 | 2.4 | 10.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_mine_2.wav` | 0.585 | -7.28 | -15.50 | -17.99 | 812 | 1.05 | 2.4 | 0.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_rock_magma_0.wav` | 0.570 | -6.06 | -15.50 | -17.04 | 779 | 1.14 | 2.4 | 0.0 | 0.0 | 0 | 0.143 | — | — |
| `ember_rock_magma_1.wav` | 0.550 | -6.34 | -15.50 | -16.94 | 713 | 0.62 | 2.4 | 0.0 | 0.0 | 0 | 0.143 | — | — |
| `ember_rock_magma_2.wav` | 0.596 | -6.37 | -15.50 | -17.37 | 724 | 0.84 | 2.4 | 0.0 | 10.0 | 0 | 0.143 | — | — |
| `ember_rock_cinder_0.wav` | 0.480 | -8.36 | -15.50 | -19.43 | 1091 | 1.97 | 2.4 | 0.0 | 0.0 | 0 | 0.121 | — | — |
| `ember_rock_cinder_1.wav` | 0.463 | -7.65 | -15.50 | -19.12 | 1125 | 2.06 | 2.4 | 0.0 | 0.0 | 0 | 0.121 | — | — |
| `ember_rock_cinder_2.wav` | 0.502 | -8.70 | -15.50 | -19.08 | 1052 | 1.78 | 2.4 | 0.0 | 0.0 | 0 | 0.121 | — | — |
| `ember_rock_obsidian_0.wav` | 0.530 | -10.24 | -15.50 | -21.36 | 1315 | 3.00 | 2.4 | 0.0 | 0.0 | 0 | 0.142 | — | — |
| `ember_rock_obsidian_1.wav` | 0.511 | -9.69 | -15.50 | -21.26 | 1321 | 2.93 | 2.4 | 0.0 | 0.0 | 0 | 0.142 | — | — |
| `ember_rock_obsidian_2.wav` | 0.554 | -10.01 | -15.50 | -21.17 | 1279 | 2.85 | 2.4 | 0.0 | 10.0 | 0 | 0.142 | — | — |
| `ember_rock_islet_0.wav` | 0.810 | -5.44 | -14.00 | -15.85 | 689 | 0.90 | 2.4 | 0.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_rock_islet_1.wav` | 0.782 | -5.54 | -14.00 | -15.54 | 671 | 0.86 | 2.4 | 0.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_rock_islet_2.wav` | 0.846 | -5.36 | -14.00 | -15.63 | 671 | 0.86 | 2.4 | 0.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_elite_ash_wraith_0.wav` | 0.820 | -4.84 | -14.00 | -15.84 | 660 | 0.86 | 2.4 | 0.0 | 0.0 | 0 | 0.095 | — | — |
| `ember_elite_ash_wraith_1.wav` | 0.791 | -6.13 | -14.00 | -16.05 | 680 | 0.91 | 2.4 | 0.0 | 0.0 | 0 | 0.095 | — | — |
| `ember_elite_ash_wraith_2.wav` | 0.857 | -5.92 | -14.00 | -16.34 | 675 | 0.90 | 2.4 | 5.0 | 0.0 | 0 | 0.095 | — | — |
| `ember_elite_ash_wraith_scream_0.wav` | 0.430 | -9.00 | -18.47 | -23.19 | 947 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.127 | -4.84 | 0.420 |
| `ember_elite_ash_wraith_scream_1.wav` | 0.413 | -9.00 | -19.14 | -23.00 | 951 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.127 | -5.84 | 0.454 |
| `ember_elite_ash_wraith_scream_2.wav` | 0.447 | -9.00 | -19.15 | -23.12 | 954 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.127 | -5.92 | 0.455 |
| `ember_elite_brass_vulture_0.wav` | 0.870 | -6.51 | -14.00 | -16.96 | 714 | 1.00 | 2.4 | 5.0 | 0.0 | 0 | 0.105 | — | — |
| `ember_elite_brass_vulture_1.wav` | 0.840 | -6.80 | -14.00 | -17.11 | 731 | 1.02 | 2.4 | 0.0 | 0.0 | 0 | 0.105 | — | — |
| `ember_elite_brass_vulture_2.wav` | 0.909 | -7.21 | -14.00 | -17.20 | 781 | 1.15 | 2.4 | 10.0 | 0.0 | 0 | 0.105 | — | — |
| `ember_elite_brass_vulture_scream_0.wav` | 0.390 | -9.00 | -17.70 | -21.02 | 850 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.193 | -5.78 | 0.385 |
| `ember_elite_brass_vulture_scream_1.wav` | 0.374 | -9.00 | -17.46 | -21.12 | 852 | 0.00 | 5.3 | 5.0 | 0.0 | 0 | 0.193 | -6.77 | 0.374 |
| `ember_elite_brass_vulture_scream_2.wav` | 0.406 | -9.00 | -17.16 | -20.79 | 842 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.193 | -6.86 | 0.361 |
| `ember_elite_cauterizer_0.wav` | 0.920 | -4.80 | -14.00 | -14.62 | 511 | 0.56 | 2.4 | 20.0 | 0.0 | 0 | 0.140 | — | — |
| `ember_elite_cauterizer_1.wav` | 0.888 | -3.82 | -14.00 | -13.95 | 477 | 0.48 | 2.4 | 5.0 | 0.0 | 0 | 0.140 | — | — |
| `ember_elite_cauterizer_2.wav` | 0.961 | -4.55 | -14.00 | -14.71 | 508 | 0.56 | 2.4 | 15.0 | 0.0 | 0 | 0.140 | — | — |
| `ember_elite_coalrunner_0.wav` | 0.870 | -5.48 | -14.00 | -15.49 | 592 | 0.73 | 2.4 | 0.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_elite_coalrunner_1.wav` | 0.840 | -6.59 | -14.00 | -16.19 | 596 | 0.75 | 2.4 | 5.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_elite_coalrunner_2.wav` | 0.909 | -5.70 | -14.00 | -15.88 | 603 | 0.76 | 2.4 | 5.0 | 0.0 | 0 | 0.101 | — | — |
| `ember_elite_coalrunner_scream_0.wav` | 0.350 | -9.00 | -19.63 | -23.01 | 951 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.143 | -4.20 | 0.480 |
| `ember_elite_coalrunner_scream_1.wav` | 0.336 | -9.00 | -19.44 | -22.44 | 948 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.143 | -5.95 | 0.470 |
| `ember_elite_coalrunner_scream_2.wav` | 0.364 | -9.00 | -18.57 | -21.35 | 920 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.143 | -5.31 | 0.425 |
| `ember_elite_kilnback_0.wav` | 0.980 | -4.30 | -14.00 | -14.81 | 511 | 0.53 | 2.4 | 0.0 | 0.0 | 0 | 0.123 | — | — |
| `ember_elite_kilnback_1.wav` | 0.946 | -4.62 | -14.00 | -14.33 | 484 | 0.49 | 2.4 | 5.0 | 0.0 | 0 | 0.123 | — | — |
| `ember_elite_kilnback_2.wav` | 1.024 | -4.59 | -14.00 | -14.55 | 475 | 0.47 | 2.4 | 5.0 | 0.0 | 0 | 0.123 | — | — |
| `ember_elite_sunstoke_0.wav` | 0.880 | -6.35 | -14.00 | -16.52 | 678 | 0.89 | 2.4 | 5.0 | 0.0 | 0 | 0.097 | — | — |
| `ember_elite_sunstoke_1.wav` | 0.849 | -6.43 | -14.00 | -16.13 | 691 | 0.94 | 2.4 | 5.0 | 0.0 | 0 | 0.097 | — | — |
| `ember_elite_sunstoke_2.wav` | 0.920 | -5.46 | -14.00 | -15.99 | 678 | 0.91 | 2.4 | 5.0 | 0.0 | 0 | 0.097 | — | — |
| `ember_elite_sunstoke_scream_0.wav` | 0.350 | -9.00 | -18.61 | -21.44 | 970 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.193 | -6.35 | 0.427 |
| `ember_elite_sunstoke_scream_1.wav` | 0.336 | -9.00 | -18.78 | -21.69 | 1023 | 0.00 | 5.3 | 0.0 | 0.0 | 0 | 0.193 | -6.05 | 0.436 |
| `ember_elite_sunstoke_scream_2.wav` | 0.364 | -9.00 | -18.39 | -21.38 | 947 | 0.00 | 5.3 | 5.0 | 0.0 | 0 | 0.193 | -5.46 | 0.417 |

### Audition

`audition_verdant.wav` and `audition_ember.wav` play variant zero in roster order with 1.2 s gaps. For each unit with a voice, the death is followed by the voice alone and then its death/voice mix. The PNG sheets show every variant-zero death and available voice as waveforms and spectrograms.

These are synthesized approximations and need a listening pass on phone speakers. Ear-tune Hornet Queen and Brass Vulture first for a creature rather than buzzer edge; then Bloom Maw and Magma Skull for physical scale and clear material differences. The noise-led source, low spectral centers, brief masked wing bursts, short damped room, strict HF check and persistent 1–3 kHz ring check protect against beepiness and harshness.
