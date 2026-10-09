# Ember death painted candidates

The 11 PNGs in `candidates/` are selected results from image generation with each
enemy's idle cell 3 at 3× nearest neighbour as its anatomy reference. Each result
contains two square panels: rupture on the left and final debris on the right.
The reference PNGs are saved beside this file as `*_idle3_3x.png`.

Shared request for every key: transparent two-panel game sprite; the left panel
has recognizable but cracked and scorched anatomy with a white-amber core,
radial molten-orange seams, flying metal or slag; the right panel has no intact
hull, just sparse chips, embers, and short charcoal smoke wisps. Center each
panel at the original anchor with clear margins. Match the reference's dense,
crisp pixel art, selective dark outlines, material ramps, and stepped glow.
Use only scorched iron, obsidian, copper or brass, molten orange, amber, and
yellow-white; remove pink/purple reference lights. No cyan, magenta, green,
red, background, checkerboard, blur, or text.

| Key | Anatomy and break direction in individual prompt |
| --- | --- |
| ember_alien | Sideways armored drone, circular snout/core to the right, toothed fins and exposed pipes; burst snout and separate fins. |
| ember_chaser | Predatory head, hooked pipes, side pods, fang grille and mouth; split brow and fangs, eject curved pipe and jaw chips. |
| ember_fighter_1 | Front fighter, cheek engines, visor, copper vents, pointed lower keel; split visor and keel, eject side plates. |
| ember_fighter_2 | Broad crescent claw wings, triangular reactor, twin exhaust stacks; separate claws and crack reactor. |
| ember_fighter_3 | Compact horned fighter, tall antler fins, pointed nose and cheek pods; fracture antlers and core. |
| ember_fighter_4 | Round bomber, chimney, spiked shoulder, furnace grille, lower keel; burst grille and tear off chimney. |
| ember_rock_cinder | Upright stack of basalt and industrial slabs, vertical furnace seam; break stacked blocks into glowing edged chunks. |
| ember_rock_islet | Wide flat floating basalt shelf with lava drips and braces; split horizontal shelf into slabs. |
| ember_rock_magma | Diagonal oval bolted meteor with branching molten seams; shatter along diagonal momentum. |
| ember_rock_obsidian | Tall slender black-glass shard with vertical fissure and splinters; split lengthwise into razor chips. |
| ember_big | Broad horned iron face, curled horns, segmented brow, amber eyes and furnace teeth; blast forehead and mouth, break both horns. |

`build.py` copies the original idle cell 3 to death cell 0, recolors its foreign
lights to amber, and overlays a pixel starburst. It fits the painted panels to
the idle silhouette with nearest-neighbour resampling, quantizes them to the
Ember palette, and exports the strips and previews. `audit.py` checks the outputs.
