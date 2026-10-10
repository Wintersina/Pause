# Tide elite flight art

Painted source masters: `src~/*_master.png` (built-in image generation). `src~/build_tide_elites.py` creates all strips by nearest-neighbour sampling and stepped pixel effects.

| Cell | Pose |
| --- | --- |
| 0 | Landed, engines off |
| 1 | Grounded idle, restrained mint glow |
| 2 | Lift-off ignition |
| 3 | Straight hover |
| 4 | Bank left |
| 5 | Bank right |
| 6 | Second-heart damage, hull crack and sparks |

Each optional `<key>_death.png` has three 192 px cells: flash, rupture, debris.

## Registration and hardpoints

Coordinates below are approximate hover-cell pixels from the left/top. Muzzles name the intended origin; hostile shot and heart colours are supplied in game.

| Ship | Ground site | Signature | Nozzles (x,y) | Muzzles (x,y) | Moving parts |
| --- | --- | --- | --- | --- | --- |
| Riptide | trench hatch | surf dash / lance charge | (87,160), (105,160) | lance tip (96,22) | swept fins flex |
| Bathyscaphe | rig bay | depth charge lob | (64,142), (128,142) | port claw (35,119), starboard claw (157,119), charge rack (96,136) | cable claws reach |
| Trawler | reef dock | lane snare net | (80,145), (112,145) | port net boom (34,94), starboard net boom (158,94), winch (96,109) | net booms bow |
| Thunderfin | vent stack | telegraphed lane strike | (89,151), (103,151) | lightning rod (96,30), port coil (66,77), starboard coil (126,77) | ray fins ripple and coils pulse |
| Pearl Warden | wreck bay | pearl curtain / shell shield | (71,144), (121,144) | pearl aperture (96,77), port shell (49,84), starboard shell (143,84) | shell halves breathe |

## Cell measurements

Alpha bbox at threshold 24 and width × height. Body registration is measured on cells 3, 4 and 5.

| Ship | Hover bbox (x0,y0,x1,y1) | Hover size | Bank left size | Bank right size | Hover→left centroid Δ | Hover→right centroid Δ | Min margin, cells 0–6 | Opaque colours, hover | Red-band share, hover |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| riptide | (44, 22, 147, 170) | 103×148 | 104×148 | 104×148 | 0.48 px | 0.35 px | 19 px | 3281 | 0.156% |
| bathyscaphe | (34, 42, 158, 151) | 124×109 | 123×109 | 123×109 | 0.56 px | 0.55 px | 34 px | 5511 | 0.248% |
| trawler | (33, 41, 159, 153) | 126×112 | 124×112 | 124×112 | 0.70 px | 0.64 px | 33 px | 4687 | 0.154% |
| thunderfin | (39, 33, 153, 159) | 114×126 | 112×126 | 112×126 | 0.62 px | 0.59 px | 28 px | 3712 | 0.024% |
| pearl_warden | (35, 40, 157, 153) | 122×113 | 120×113 | 120×113 | 1.05 px | 1.11 px | 35 px | 7104 | 0.202% |

The generated concepts were scaled to flight cells with nearest-neighbour sampling. Tiny procedural cracks, exhaust, fin bends and debris make the flight and death keys; the painted masters carry the material detail. Weapon telegraphs, nets, pearls, and hostile-pink shots are gameplay FX rather than baked into the hull.
