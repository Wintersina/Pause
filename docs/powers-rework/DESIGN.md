# Ship powers rework: design (Oct 11)

Terms: "ultimate" = ShipAttack, "secondary" = SecretPower (see AUDIT.md). Pink only in Space. Neon pixel art, HD, pale readable outline, Point filter.

## Mechanics changes (code agents implement; each needs a test)
| id | Ship | Secondary (new behaviour) | Ultimate change |
|---|---|---|---|
| 1 | Neon Comet | Shield Pulse: 1.2 s invulnerable, expanding ring that also deletes enemy shots in radius 2.2 | keep |
| 2 | Volt Viper | EMP: trigger at 3+ enemies; stun 3 s AND cancels/freezes enemy shots; arcs on each frozen enemy | keep |
| 3 | Solar Fang | Solar Nova: radius 3.0, leaves 2 s burning ring that kills what enters | keep |
| 4 | Crimson Halo | Capacitor Dump: fires the rail shot immediately (full-screen piercing), no <65% condition | keep |
| 5 | Ion Lancer | Ion Battery: +2 pauses, plus 2 s slow-time, visible battery pack flying to pause counter; trigger pauses <= 2 | piercing lances (pierce 3 targets) |
| 6 | Jade Phantom | Phase Cloak: 4 s, ship ghosted with afterimage trail, passes through hazards | bouncing wisps (4 bounces) |
| 7 | Gold Warden | Gold Magnet: 5 s pull + dust worth x2 while active, golden field lines | area missiles (blast radius 1.6) |
| 8 | Lightning | Thunderclap: lightning strikes kill the hazards within 2.6 (bosses take weighted hit) | keep |
| 9 | Ligher | Flare Decoy: lure also redirects shooters' aim for 2.5 s; bright pulsing flare, pops with kill radius | keep |
| 10 | Paranoid | Time Bubble: visible dome, 0.45x inside world slow 2.5 s, distortion rings | keep |
| 11 | Ninja | Blink Dash: dodge to safest side with afterimage line + smoke both ends; also fires proactively when threat within 2 | keep |
| 12 | Saboteur | Black Hole: trigger 3+ enemies, accretion disc with debris | keep |
| 13 | UFO | Tractor Pull: beam pulls enemies/debris toward ship then star dust rains from kills (replaces pure Star Shower) | Tractor Beam really pulls targets toward the beam centre |
| 14 | Dove | Mending: heal one heart, visible feather burst + heart restore | keep |
| 15 | Turtle | Hard Shell: hex shell visible on hull whole duration, shatter animation on hit absorbed | keep |

## Art (Codex)
Per ship, two 256 px-cell strips in the neon pixel-art style, Point filter, transparent bg, own palette (no pink outside Space ship):
- `<Key>_power.png`: 16 cells = 8 loop + 8 burst.
- `<Key>_attack.png`: 16 cells = 8 shot/charge loop + 8 impact/burst.
Ultimates also get a muzzle flash and trail in the strip if room (cells 12-15 of impact optional).

## Previews (dock)
Baked looping frame-sequence atlases (no VideoPlayer): for each ship, ~24 frames secondary + ~24 frames ultimate, captured in play mode by a batchmode editor tool, shown above the LAUNCH/BUY card (tap to toggle ultimate / secondary).
