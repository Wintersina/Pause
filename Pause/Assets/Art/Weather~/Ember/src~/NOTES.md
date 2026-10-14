# Ember weather art

Painted source images were generated with the built-in image generation tool on
transparent backgrounds. Two candidates were made and inspected for each hazard.
`ash_candidate_1.png` was selected for its two rising soot spirals; candidate 2
had a broad soft halo. `geyser_candidate_2.png` was selected for its clearer
hollow centre and natural plume; candidate 1 read too much like a blast.

Selected prompt set:

- Ash: “One single tall vertical column of weather, foot at bottom centre,
  swirling charcoal and ash-grey soot, jagged ash flakes and sparse molten-gold
  cinders climbing in helical paths; airy centre, denser intricate rims;
  detailed crisp pixel art, upper-left light, transparent background.”
- Geyser: “A natural rising plume of hot dusty steam and soot, narrowly rooted
  at bottom centre, surging upward and rolling outward near the top; broken
  wisps and curved ash ribbons carry small isolated slag droplets outward;
  hollow translucent centre, hand-placed-looking pixel art, transparent.”
- Ambient: “A transparent 4-by-4 study with four amber embers, four curled grey
  ash flakes, four tiny amber-white cinder sparks and four charcoal soot puffs;
  each isolated, with crisp pixel edges and a pale upper-left rim.”

The source paintings were cropped, nearest-neighbour scaled and restricted to
the Ember amber/ash/umber ramps. The live ash animation moves cinders upward in
six steps. The geyser changes height and width through one surge and fall.
Warnings are thinned copies of those same painted forms. `build_ember_v2.py`
rebuilds the selected draft under `src~/draft2`.

Review caveat: the geyser's two top billows still read more as steam and ash
than molten spray. Its slag droplets are deliberately restrained so the hazard
does not look like an explosion or obscure ships.
