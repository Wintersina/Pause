# Desert elite flight art (ground-up redesign, Oct 2026)

All six Desert elites were redesigned from scratch as FLYING craft, one Codex image each (prompts, masters and build scripts in `Enemies/src~/desert_elites_redesign/`): hard-surface, grounded, no outline, a palette invented per ship (not sand/copper). The old stand-in strips (donor recolours, walking Scorpion, land-bound Hauler) are gone from the game.

| Key | Display name | Design | Palette |
| --- | --- | --- | --- |
| `desert_elite_dune_stalker` | Dune Stalker | lean lance fighter: brass drill-lance, shark-scute spine, swept blade fins | cobalt armour, bone ceramic, brass |
| `desert_elite_caravan_hauler` | Caravan Hauler | flying heavy-lift freighter, three slung canisters, lift fans, three stern engines | charcoal frame, saffron canisters, turquoise lamps |
| `desert_elite_glass_cannon` | Glass Cannon | airborne siege deck, one long barrel over a faceted sun lens | graphite + titanium, amber-gold lens |
| `desert_elite_lens_matriarch` | Lens Matriarch | slim heliograph: six hinged mirror panels round a green spine | malachite green, mirror blue, brass |
| `desert_elite_mirror_bastion` | Mirror Bastion | heavy warship ringed in shingled mirror plates, lens cannon + cap turrets | aubergine armour, silvered glass, gold |
| `desert_elite_scorpion_walker` | Scorpion Skiff (was Scorpion Walker; key kept) | legless scorpion gunship: pincer arms, raised stinger tail, stub-wing fans | obsidian gunmetal, venom-orange seams, gold claws |

## Cell contract (unchanged)

`desert_elite_<name>.png` is **2688 x 384** (7 cells of 384 px, HD size; the Glass Cannon moved up from 192 to 384 too). `_death.png` is **1152 x 384** (3 cells).

| Flight cell | State |
| --- | --- |
| 0 | Parked (engines off) |
| 1 | Parked idle (pilot lights) |
| 2 | Lift-off ignition (long jets) |
| 3 | Hover (flight cell, short jets) |
| 4 / 5 | Bank left / right (hull leans 5 degrees and narrows a little, pivot kept) |
| 6 | Damaged (cracked hull, low ragged jets) |

Death cells: 0 flash on the hull, 1 rupture, 2 dispersal (rebuilt from each new hull in the ship's own colours; no pink).
Muzzles and nozzles are measured on the new art (`src~/desert_elites_redesign/points.py`) and live in the defs (`cellPixels` 384). Every cell is at least 6 px clear of its border (`DesertFramingTest`).
