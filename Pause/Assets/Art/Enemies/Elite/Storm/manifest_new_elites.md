# Storm elite flight art

Five elites, redesigned from the ground up with Codex (art/storm-elites): each hull has its own invented palette, no outline, no pink, and reads as an airborne machine. Sources, briefs, build and measuring scripts: `Art/Enemies/src~/storm_elites/` (`all.sh` briefs, `masters/`, `build_strips.py`, `wire_defs.py`, `contrast.py`, `make_sheets.py`, before/after `sheets/`). The `_concept.png` files are the keyed Codex masters (hero images; the sync ignores them).

| Ship | Palette | Launch site | Attack idea |
| --- | --- | --- | --- |
| Skyhook Harrier | graphite steel, safety orange, black steel hook | skyhook dock | hooked dash lance |
| Refinery Tender | emerald enamel, galvanised zinc, brass, cream | refinery pad | sinking canisters |
| Rod Dancer | carbon black, royal blue, chrome, turquoise arcs | balloon hangar | blink and arc fan |
| Anvil Cloud | bone ceramic, black iron, brass, gold glow | gantry hatch | lane-telegraphed lightning shell |
| Static Bastion | petrol teal, silver, ceramic white, ice-cyan | cloud bay | curtain of arcs / ward |

## Cell contract

Every `storm_elite_<name>.png` is **2688 x 384 RGBA**: seven 384 px cells. Every `storm_elite_<name>_death.png` is **1152 x 384**: three cells.

| Flight cell | State |
| --- | --- |
| 0 | Landed: engines off, hull dimmed |
| 1 | Grounded idle: pilot lights |
| 2 | Lift-off ignition: long jets |
| 3 | Straight hover: short jets, energy spots lit |
| 4 | Bank left: outer parts flex, anchored |
| 5 | Bank right |
| 6 | Second-heart damage: scorched, cracked, ragged jets |

| Death cell | State |
| --- | --- |
| 0 | Storm-white flash on the hull |
| 1 | Hull rupture with a white core |
| 2 | Armour fragments and sparks |

Muzzle and nozzle points are measured on hover cell 3 and live in the defs (`Resources/Elites/Defs/storm_elite_*.json`). Every cell keeps 6 px clear of the cell edge (StormFramingTest).
