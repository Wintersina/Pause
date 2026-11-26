# Musical boss identities

The live fight uses `BossMusicDirector`, not the fallback `BossPatternTables` rotation while music is playing. Each boss now keeps a recognizable geometry throughout its musical fight instead of converging on fans, walls and crossing slants at high intensity.

| Boss | Main attack shape | Movement / rhythm to read |
| --- | --- | --- |
| Void Archon | Narrow-to-wide rotating reactor arcs | Follow the sweeping opening through a syncopated phrase. |
| Hoarfrost Leviathan | Expanding ice rings with alternating densities | Measured paired pulses; re-position between rings. Glare beams remain its special. |
| Bloom Queen | Paired seed clusters separated into petals | Follow the winding opening; the petal seams distinguish it from a solid cone. Vine lash and trunk toss remain. |
| Cinder Drake | A narrow breath opening into a broad flame cone | A concentrated run of volleys followed by recovery. Furnace bombs and flamethrower / eruptions remain. |
| Iron Kraken | Sweeping curtains and double-depth tidal walls | Follow the opening through rolling swells; port/starboard muzzles alternate. Surf and beak specials remain. |
| Tempest Engine | Rain columns staggered on musical subdivisions | Steady thunder beats, with columns arriving in staggered groups. Occasional core arcs preserve rail ricochets. |
| Dune-Eater | Mirrored V-shaped closing / opening jaws | Move through the central corridor as both sides converge. Occasional grit arcs, mines and the lens special remain. |

`WaveRhythm.MaskFor` defines a distinct phrase for each world while preserving the existing number of waves at each intensity. Both forecasting and firing use that phrase. The first wave retains its promised starting beat. Rain offsets are quarter-beat subdivisions, rather than fixed delays in seconds.

The shared `WaveGaps` planner still limits how far the opening can move and accounts for wave depth, rain stagger and the early arrival of pincer edges. Shots still leave measured boss muzzles. Escape corridors, special-attack holds, certified ricochets, shot budgets, pause/resume and the audio clock remain active. Frost's glare narrows from 0.26 to 0.22 units and holds for 0.5 rather than 0.6 seconds to leave room after its new ring sequence.

The native Unity preview `BossIdentityReview.Export` captures real musical boss actors and then exports the dock review. Whole-song solver tests, live actors at 60/120 FPS, stepped audio clocks and the dodge bot exercise these changes. Manual playtesting is still needed to judge how the rhythm feels.
