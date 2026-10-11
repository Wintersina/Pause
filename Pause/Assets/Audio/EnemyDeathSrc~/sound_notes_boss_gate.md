# Boss deaths and HapticGate sound design

Run `python3 build_boss_gate_sounds.py` to rebuild deterministically. Mono 44.1 kHz, 16-bit PCM; NumPy and Python standard library only. The existing Verdant/Ember toolkit supplies filtered noise, low thumps, modal plates, compact convolution, WAV and PNG utilities. All output audio is verified after PCM export.

The brief names 11 cue families and explicitly asks for variants 0 and 1, yielding 22 final WAVs. Its references to 15 variant-0 sounds, 45 files and three variants conflict with that list; the sheet contains all 11 specified variant-0 sounds and the full audition contains all 22 delivered files.

## Recipes and parameters

| Cue | Intended image | Layers | Duration target | RMS target | Peak target |
|---|---|---|---:|---:|---:|
| `boss_space` | Void Archon reactor overload | sub swell, dull boom, plasma crackle, torn plates, falling pressure; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 2.20 s | -15 dBFS | -3 dBFS |
| `boss_frost` | Leviathan hull implosion | hull groan, burst, ice fissures, pressure hiss, frozen rubble; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 2.40 s | -15 dBFS | -3 dBFS |
| `boss_verdant` | Bloom Queen core rupture | wet tear, brass plate, seed burst, leaf and petal shower; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 2.20 s | -15 dBFS | -3 dBFS |
| `boss_ember` | Cinder Drake molten detonation | lava rumble, obsidian fracture, steam, falling rock; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 2.40 s | -15 dBFS | -3 dBFS |
| `boss_tide` | Iron Kraken underwater breach | deep pressure boom, trapped air, iron groan, water rush; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 2.40 s | -15 dBFS | -3 dBFS |
| `boss_frost_scream` | Leviathan breathy cry | noise formants, low hull groan, ice rasp, falling breath; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 1.20 s | -20 dBFS | -9 dBFS |
| `boss_verdant_scream` | Bloom Queen guttural roar | low noise formants, wet rasp, seed rattle, petal flutter; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 1.20 s | -20 dBFS | -9 dBFS |
| `gate_rattle` | Strained iron latch | low plate shiver, hydraulic strain, loose bolts, dark room; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 1.00 s | -16 dBFS | -3 dBFS |
| `gate_step` | Forced open lurch | deep clunk, steam burst, brass bend, recoil rattle; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 0.50 s | -16 dBFS | -3 dBFS |
| `gate_crack` | Broken gate pipe and seam | low metal fracture, pipe hiss, short falling shards; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 0.60 s | -16 dBFS | -3 dBFS |
| `gate_smash` | Gate torn apart | concussive blow, tearing plates, vented pipes, settling debris; 110 ms room (gate) or 180 ms room (boss), 20 ms fade | 1.40 s | -16 dBFS | -3 dBFS |

## Delivered PCM verification

| File | Duration s | Peak dBFS | RMS dBFS | Centroid Hz | >4 kHz % | Attack ms | Tonal ms | Clips | Pair xcorr |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| `boss_space_0.wav` | 2.200 | -3.00 | -15.00 | 663 | 0.523 | 3.0 | 5 | 0 | 0.071 |
| `boss_space_1.wav` | 2.123 | -3.00 | -15.00 | 615 | 0.139 | 4.0 | 5 | 0 | 0.071 |
| `boss_frost_0.wav` | 2.400 | -3.00 | -15.00 | 673 | 0.423 | 2.0 | 40 | 0 | 0.055 |
| `boss_frost_1.wav` | 2.316 | -3.00 | -15.00 | 688 | 0.445 | 3.5 | 5 | 0 | 0.055 |
| `boss_verdant_0.wav` | 2.200 | -3.00 | -15.00 | 699 | 0.503 | 5.0 | 20 | 0 | 0.054 |
| `boss_verdant_1.wav` | 2.123 | -3.00 | -15.00 | 674 | 0.416 | 3.0 | 0 | 0 | 0.054 |
| `boss_ember_0.wav` | 2.400 | -3.00 | -15.00 | 579 | 0.294 | 2.0 | 15 | 0 | 0.074 |
| `boss_ember_1.wav` | 2.316 | -3.00 | -15.00 | 589 | 0.306 | 3.5 | 5 | 0 | 0.074 |
| `boss_tide_0.wav` | 2.400 | -3.00 | -15.00 | 544 | 0.310 | 2.0 | 0 | 0 | 0.065 |
| `boss_tide_1.wav` | 2.316 | -3.00 | -15.00 | 523 | 0.196 | 3.0 | 15 | 0 | 0.065 |
| `boss_frost_scream_0.wav` | 1.200 | -9.00 | -20.00 | 519 | 0.029 | 2.5 | 0 | 0 | 0.076 |
| `boss_frost_scream_1.wav` | 1.248 | -9.00 | -20.00 | 512 | 0.014 | 5.0 | 0 | 0 | 0.076 |
| `boss_verdant_scream_0.wav` | 1.200 | -9.00 | -20.00 | 460 | 0.010 | 6.0 | 0 | 0 | 0.075 |
| `boss_verdant_scream_1.wav` | 1.248 | -9.00 | -20.00 | 472 | 0.023 | 3.0 | 5 | 0 | 0.075 |
| `gate_rattle_0.wav` | 1.000 | -3.00 | -16.00 | 556 | 0.015 | 2.5 | 0 | 0 | 0.098 |
| `gate_rattle_1.wav` | 0.965 | -3.00 | -16.00 | 567 | 0.005 | 4.0 | 0 | 0 | 0.098 |
| `gate_step_0.wav` | 0.500 | -3.00 | -16.00 | 730 | 0.403 | 4.5 | 5 | 0 | 0.122 |
| `gate_step_1.wav` | 0.482 | -3.00 | -16.00 | 672 | 0.121 | 3.5 | 0 | 0 | 0.122 |
| `gate_crack_0.wav` | 0.600 | -3.00 | -16.00 | 941 | 0.548 | 4.5 | 0 | 0 | 0.128 |
| `gate_crack_1.wav` | 0.579 | -3.00 | -16.00 | 941 | 0.617 | 2.5 | 10 | 0 | 0.128 |
| `gate_smash_0.wav` | 1.400 | -3.00 | -16.00 | 767 | 0.371 | 4.0 | 25 | 0 | 0.083 |
| `gate_smash_1.wav` | 1.351 | -3.00 | -16.00 | 744 | 0.271 | 4.0 | 0 | 0 | 0.083 |

**Result:** PASS: 22 delivered WAVs; 11 pair correlations under 0.6.

No listening monitor was available. Dark filtering, short masked low thumps, low-level modal fragments, bounded high-frequency share, spectral centroid and tonality checks reduce the risk of beeps and piercing treble. Check on a phone speaker before shipping.
