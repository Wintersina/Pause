# Space World enemy death sounds

44.1 kHz, mono, 16-bit PCM. Three deterministic round-robin variants per key. The measured figures below are read back from the final integer PCM WAVs. Peak and RMS are dBFS; centroid is Hz; HF is energy above 4 kHz. Attack is the 10%-90% rise of the controlled primary impact envelope; pre-impact swells on shield, ion and gravity cues are excluded. Tonal is the longest run of 20 ms Hann frames (5 ms hop) whose strongest FFT bin exceeds 45% of active-frame energy. Cross-correlation is the maximum normalized full-lag value for any variant pair.

All cues have a masked short low thump, filtered mid-band material body, specific debris/detail and a low-level 105-180 ms FFT-convolved dark room tail. All final samples receive a 20 ms fade and DC removal.

## Needle — `space_fighter_1`

Evokes single dry snap, air fizz. Main body noise band: 220-1800 Hz. Nominal duration 0.30 s. Standard RMS target -20 dBFS.

Layers: Small low pressure pop; short dry mid-band snap; softened airy noise fizz; two or three minute fragments.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.300 | -3.00 | -20.01 | 1104 | 0.71 | 2.27 | 0.0 | 0 |
| 1 | 0.290 | -3.00 | -20.00 | 1126 | 0.58 | 2.27 | 0.0 | 0 |
| 2 | 0.313 | -3.00 | -20.00 | 1110 | 0.29 | 2.27 | 0.0 | 0 |

Maximum variant cross-correlation: 0.190.

## Steel Claw — `space_fighter_2`

Evokes metal crunch, short clang, shrapnel. Main body noise band: 140-1750 Hz. Nominal duration 0.45 s. Standard RMS target -20 dBFS.

Layers: Low punch; dense gunmetal noise crunch; three brief inharmonic plate modes; sparse shrapnel rattle.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.450 | -3.00 | -20.00 | 993 | 0.66 | 2.27 | 0.0 | 0 |
| 1 | 0.434 | -3.00 | -20.00 | 978 | 0.16 | 2.27 | 0.0 | 0 |
| 2 | 0.470 | -3.00 | -20.00 | 1019 | 0.52 | 2.27 | 5.0 | 0 |

Maximum variant cross-correlation: 0.162.

## Twin Claw — `space_fighter_3`

Evokes two 60 ms spaced crunches, steel tear. Main body noise band: 95-1500 Hz. Nominal duration 0.50 s. Standard RMS target -20 dBFS.

Layers: Two separately textured low crunches about 60 ms apart; deeper noise body; short torn plate resonance; metal fragments.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.500 | -3.00 | -20.00 | 687 | 0.01 | 2.27 | 0.0 | 0 |
| 1 | 0.482 | -3.00 | -20.00 | 696 | 0.01 | 2.27 | 0.0 | 0 |
| 2 | 0.522 | -3.00 | -20.00 | 771 | 0.05 | 2.27 | 0.0 | 0 |

Maximum variant cross-correlation: 0.165.

## Warden — `space_fighter_4`

Evokes low shield whump, muffled hull, energy sigh. Main body noise band: 75-1100 Hz. Nominal duration 0.60 s. Standard RMS target -20 dBFS.

Layers: Low electrical noise whump; delayed low thump and muffled hull burst; progressively darker noise sigh; minor metal scatter.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.600 | -3.00 | -20.00 | 439 | 0.01 | 2.27 | 5.0 | 0 |
| 1 | 0.579 | -3.00 | -20.00 | 466 | 0.02 | 2.27 | 5.0 | 0 |
| 2 | 0.627 | -3.00 | -20.00 | 413 | 0.00 | 2.27 | 5.0 | 0 |

Maximum variant cross-correlation: 0.218.

## Steel Hound — `space_chaser`

Evokes servo wind down, clunk, sparks. Main body noise band: 95-1500 Hz. Nominal duration 0.55 s. Standard RMS target -20 dBFS.

Layers: Eight overlapping descending noise bands with slow modulation; late mechanical clunk; a few low-level sparks.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.550 | -3.00 | -20.00 | 643 | 0.11 | 2.27 | 0.0 | 0 |
| 1 | 0.531 | -3.00 | -20.00 | 590 | 0.04 | 2.27 | 0.0 | 0 |
| 2 | 0.575 | -3.00 | -20.01 | 639 | 0.16 | 2.27 | 5.0 | 0 |

Maximum variant cross-correlation: 0.175.

## Bile Mite — `space_alien`

Evokes wet splat, low bubbles, slimy tail. Main body noise band: 85-1300 Hz. Nominal duration 0.55 s. Standard RMS target -20 dBFS.

Layers: Low organic thump; two wobbly filtered wet-noise splats; small soft bubble bursts; darker slimy burble tail.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.550 | -3.00 | -20.00 | 654 | 0.01 | 2.27 | 0.0 | 0 |
| 1 | 0.531 | -3.00 | -20.00 | 620 | 0.00 | 2.27 | 0.0 | 0 |
| 2 | 0.575 | -3.00 | -20.00 | 606 | 0.04 | 2.27 | 0.0 | 0 |

Maximum variant cross-correlation: 0.186.

## Bastion — `space_big`

Evokes sub thump, dull boom, tearing plates, reactor thud. Main body noise band: 45-740 Hz. Nominal duration 1.10 s. Heavy/elite RMS target -17 dBFS.

Layers: Deep low thump; long dark boom; three tearing metal-plate bursts with brief modes; settling fragments; reactor thud-hum noise.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 1.100 | -3.00 | -17.00 | 456 | 0.08 | 2.27 | 10.0 | 0 |
| 1 | 1.061 | -3.00 | -16.98 | 469 | 0.15 | 2.27 | 29.9 | 0 |
| 2 | 1.150 | -3.00 | -17.00 | 479 | 0.10 | 2.27 | 15.0 | 0 |

Maximum variant cross-correlation: 0.144.

## Rail Mine — `space_mine`

Evokes bass thoomp, arc, tiny shrapnel. Main body noise band: 80-1450 Hz. Nominal duration 0.50 s. Standard RMS target -20 dBFS.

Layers: Compact bass thoomp; band-limited arc burst; scattered short crackles; tiny metal fragments.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.500 | -3.00 | -20.00 | 753 | 0.59 | 2.27 | 5.0 | 0 |
| 1 | 0.482 | -3.00 | -20.01 | 675 | 0.20 | 2.27 | 0.0 | 0 |
| 2 | 0.522 | -3.00 | -20.00 | 648 | 0.05 | 2.27 | 5.0 | 0 |

Maximum variant cross-correlation: 0.240.

## Beacon Rock — `space_rock_crater`

Evokes one crack, a few chunks, dust. Main body noise band: 75-1400 Hz. Nominal duration 0.50 s. Standard RMS target -20 dBFS.

Layers: One low split crack; five or six larger mineral fragments; lightly filtered dust tail.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.500 | -3.00 | -19.99 | 734 | 0.18 | 2.27 | 5.0 | 0 |
| 1 | 0.482 | -3.00 | -20.00 | 778 | 0.17 | 2.27 | 0.0 | 0 |
| 2 | 0.522 | -3.00 | -20.00 | 751 | 0.19 | 2.27 | 5.0 | 0 |

Maximum variant cross-correlation: 0.167.

## Cluster Rock — `space_rock_cluster`

Evokes staggered cracks, many stones, dust. Main body noise band: 100-1600 Hz. Nominal duration 0.65 s. Standard RMS target -20 dBFS.

Layers: Three staggered rock fractures; many small stone grains; a longer dusty tail.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.650 | -3.00 | -20.00 | 873 | 0.32 | 2.27 | 0.0 | 0 |
| 1 | 0.627 | -3.00 | -20.00 | 868 | 0.13 | 2.27 | 5.0 | 0 |
| 2 | 0.679 | -3.00 | -20.00 | 940 | 0.35 | 2.27 | 0.0 | 0 |

Maximum variant cross-correlation: 0.127.

## Coal Rock — `space_rock_dark`

Evokes dense thud, coal crumble. Main body noise band: 48-950 Hz. Nominal duration 0.50 s. Standard RMS target -20 dBFS.

Layers: Dense low coal thud; dark crumbly crackle; small close-spaced mineral bits; subdued dust.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.500 | -3.00 | -20.00 | 479 | 0.04 | 2.27 | 5.0 | 0 |
| 1 | 0.482 | -3.00 | -20.00 | 473 | 0.01 | 2.27 | 5.0 | 0 |
| 2 | 0.522 | -3.00 | -20.00 | 513 | 0.03 | 2.27 | 0.0 | 0 |

Maximum variant cross-correlation: 0.169.

## Eventide Bastion — `space_elite_eventide_bastion`

Evokes shield failure, large hull explosion. Main body noise band: 45-860 Hz. Nominal duration 0.90 s. Heavy/elite RMS target -17 dBFS.

Layers: Shield-pressure failure in dark noise; delayed heavy low hull detonation; brief torn plate mode; settling metal.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.900 | -3.00 | -16.99 | 493 | 0.10 | 2.27 | 0.0 | 0 |
| 1 | 0.869 | -3.00 | -17.01 | 509 | 0.21 | 2.27 | 0.0 | 0 |
| 2 | 0.940 | -3.00 | -17.01 | 511 | 0.15 | 2.27 | 5.0 | 0 |

Maximum variant cross-correlation: 0.134.

## Orbit Reaver — `space_elite_orbit_reaver`

Evokes tearing metal, quick whoosh burst. Main body noise band: 85-1150 Hz. Nominal duration 0.70 s. Heavy/elite RMS target -17 dBFS.

Layers: Fast low punch; sharp filtered metal tear; four overlapping descending whoosh-noise pieces; a short plate ring and fragments.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.700 | -3.00 | -16.99 | 754 | 0.36 | 2.27 | 0.0 | 0 |
| 1 | 0.676 | -3.00 | -17.01 | 803 | 0.64 | 2.27 | 0.0 | 0 |
| 2 | 0.731 | -3.00 | -17.01 | 801 | 0.76 | 2.27 | 0.0 | 0 |

Maximum variant cross-correlation: 0.142.

## Rift Lancer — `space_elite_rift_lancer`

Evokes rising noise charge, crack, ion crackle. Main body noise band: 80-1250 Hz. Nominal duration 0.80 s. Heavy/elite RMS target -17 dBFS.

Layers: Five ascending filtered-noise charge bands; low discharge and broad crack; short ion crackles; dim low fade.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0.800 | -3.00 | -17.00 | 805 | 0.71 | 2.27 | 0.0 | 0 |
| 1 | 0.772 | -3.00 | -17.01 | 852 | 0.79 | 2.27 | 0.0 | 0 |
| 2 | 0.836 | -3.00 | -17.00 | 827 | 0.75 | 2.27 | 5.0 | 0 |

Maximum variant cross-correlation: 0.099.

## Singularity Hauler — `space_elite_singularity_hauler`

Evokes inward noise swell, boom, warbling low drone. Main body noise band: 40-670 Hz. Nominal duration 1.00 s. Heavy/elite RMS target -17 dBFS.

Layers: Five inward swelling dark-noise layers; delayed heavy boom; modulated low pressure drone; sparse debris and dying hum.

| Variant | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | HF % | Attack ms | Tonal ms | Clipped |
|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 1.000 | -3.00 | -17.01 | 373 | 0.04 | 2.27 | 10.0 | 0 |
| 1 | 0.965 | -3.00 | -16.99 | 350 | 0.05 | 2.27 | 15.0 | 0 |
| 2 | 1.045 | -3.00 | -16.99 | 368 | 0.13 | 2.27 | 24.9 | 0 |

Maximum variant cross-correlation: 0.166.

## Audition and integration

`audition_space.wav` plays variant 0 in roster order. `audition_space_all.wav` plays all three variants per key in roster order. Adjacent cues are separated by exactly 1.2 s of silence. These files and the sheet remain in `EnemyDeathSrc~`, which Unity ignores.

The current `EnemyDeathAudio.cs` still synthesizes cues at runtime. Per the asset-only scope, it was not edited; the game will need a later integration change to load these Resources WAVs.
