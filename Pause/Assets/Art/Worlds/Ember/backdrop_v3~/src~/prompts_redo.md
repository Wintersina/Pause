# Ember backdrop redo source prompts

Built-in imagegen generated ten painted candidates. The existing `v1`–`v4`
mid images were supplied as **style and world-material references**, except
`v3` mid was a subject reference for changing its ash plain. The two existing
`v3_flow` sources were supplied as transparency and particle-style references.
The generated candidates are saved in this folder as `v*_redo_terrain_{a,b}.png`
and `v3_redo_flow_{a,b}.png`.

Shared terrain prompt direction: top-down orthographic aerial map for a tall
1:2 seamless scrolling pixel-art background in a vertical 2D shooter. Dense,
crisp 16-bit clusters; deep charcoal-black basalt and obsidian, violet-brown
shadow haze, hot amber, orange and gold lava, tiny white-gold lights, sparse
brass machinery and a few magenta warning lamps. Fill the whole frame with
terrain. No tan/grey desert wash, crimson/red, green, teal, blue, outer space,
stars, horizon, large smoke, text, blur or photorealism.

| Source pair | Candidate A subject | Candidate B subject |
| --- | --- | --- |
| v1 | Black crater mesas, concentric caldera rims, molten lakes, winding channels, stepped lava falls and old pipe racks. | Alternate uneven mesa chain with remote luminous lakes, fine amber channels, pipe bridges and summit vents. |
| v2 | Massive overhead forge city with basalt roofs, brass pipes, furnace mouths, glowing canals and tiny city lights. | Alternate offset foundry districts, furnace wells, long canals, dark streets and warm lights. |
| v3 | Dark wind-carved ash dune bands, narrow ember rivers, internally lit fumaroles, buried brass machines and underlit ruins; avoid broad lava pools. | Alternate diagonal ash ridges, branching ember troughs, lit vents, excavators and half-buried ruins; avoid circular calderas. |
| v4 | Night-dark glassy obsidian plates, a partial crater, thin hot fissures, glowing cone summits and isolated derricks. | Alternate dark fractured glass field, a partial volcano cone, fine amber fissures, brass derricks and tiny forge lights. |

The flow pair prompt requested genuinely transparent tall canvases with only
5–10% sparse, separate diagonal black-brown ash gust dashes and amber/gold
ember specks. It prohibited terrain, broad clouds, baked smoke, crimson/red,
text and blur. The second candidate used shorter gust marks and more isolated
cinders. `redo.py` uses both painted candidates and keeps their alpha sparse.
