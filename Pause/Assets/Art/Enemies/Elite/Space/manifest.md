# Space Elite Ships (hard-surface redesign, engine-burn animation)

The four Space elites, redesigned in the grounded military / industrial language of the Steel Hound and the Static / Mirror Bastion
(hard-surface plating, real weight, restrained shapes, accent colours inset as strips and lamps) in place of the earlier intricate
and then cartoony versions. The ships stay level: there are no bank / swing cells, the animation is the engines burning.

Each `space_elite_<name>.png` is a transparent 4608 x 384 strip of twelve 384 x 384 cells (def `cellPixels` 384):

| Cell | State |
| --- | --- |
| 0 | Parked, engines off (bare hull) |
| 1 | Pilot lights |
| 2 | Ignition (stub flames) |
| 3-10 | Burn cycle: 8 drawings, each engine on its own phase (the flight loop and the codex loop) |
| 11 | Damaged (cracked hull, low ragged flames) |

`<name>_death.png` is 3 x 384 (flash, rupture, dispersal), cut from the bare hull cell 0.

| Asset stem | Design | Flame |
| --- | --- | --- |
| `space_elite_rift_lancer` | Armoured ion lance: blade spine, two prong pylons with cyan emitters, magenta spine strip | blue / cyan |
| `space_elite_eventide_bastion` | Heavy shield gunship: layered violet shield plates, cyan emitter housing, twin-barrel shoulder turrets | violet / cyan |
| `space_elite_orbit_reaver` | Claw raider: two segmented armour claw horns with magenta tips, magenta crystal lens | magenta / pink |
| `space_elite_singularity_hauler` | Industrial salvage tug: bolted containment collar holding a magenta core, two hydraulic pincer arms (full redesign) | gold / amber |

Source and tooling live in `Art/Enemies/src~/space_elites_engine_burn/`: `masters/v2/` (Codex's hard-surface masters, flat #00FF40
key, nose up, empty engine bells; `hauler_b.png` is the unused alternative), `prompts/` + `refs/` (the Codex briefs and the Steel Hound /
Bastion style references), `build_strips.py` (key, scale, draw the burn cycle), `death.py`, `defs.py` + `points.py` (cell map, muzzles and
nozzles measured on the built strip), `install.py`. The old `_concept.png` / `_strip_candidate.png` files in this folder are the superseded
earlier designs and are not installed (candidate / concept suffixes keep `EliteArtSync` from copying them).
