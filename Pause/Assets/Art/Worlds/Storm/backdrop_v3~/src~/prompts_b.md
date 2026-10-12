# Storm piece imagegen prompts and selections

All eight source sheets were generated with the built-in image generation tool
on transparent backgrounds. Each prompt requested HD pixel art, separated
sprites with no text or frames, charcoal/slate/graphite material, pale
hail-grey highlights, cold white-blue electricity, and no water, ocean,
boats, oil rigs, green, teal, saturated violet, red, orange, or brown.

## Landmarks

- `landmarks_candidate_a.png`: 4×4 contact sheet in atlas order: four
  lightning-lit anvil towers; two lightning-rod platforms; two balloon and
  cable skyhooks; three suspended gas refineries; gas siphon; airship dock,
  gantry crane, turbine raft, relay dish. High-detail painted pixel clusters.
- `landmarks_candidate_b.png`: second composition of the same sixteen pieces,
  emphasizing flatter anvil caps, more distinct tower silhouettes, and darker
  floating gunmetal structures. **Selected.**

## Sites

- `sites_candidate_a.png`: 4-column closed/open pairs for skyhook dock,
  refinery pad, balloon hangar, gantry hatch, and cloud bay; final two pieces
  are a horizontal row of four dark and lit lamps. Top-down view, centred
  emergence aperture, graphite plating and storm-white work lights.
  **Selected** for its darker plating and clear pair transitions.
- `sites_candidate_b.png`: second variant of the same site roster, with
  stronger white-blue aperture glows and pale ballast spheres.

## Weather

- `weather_candidate_a.png`: translucent sheet of four thunderhead banks,
  three torn wisps, two mists, two diagonal gust sheets, one pall, and four
  slanted rain sheets. More isolated cloud silhouettes.
- `weather_candidate_b.png`: second sheet in the same order, with dense
  graphite cloud detail, lightning inside the ceiling banks, and layered
  pale slanted rain over fine foot mist. **Selected** for cleaner crop
  separation and better match to the Storm planetfall deck.

## Lightning

- `lightning_candidate_a.png`: six forked cloud-to-cloud bolts, two impact
  strikes, two flashes, horizontal sheet arc, then hull panel, drum, bent
  antenna, cable spool, solar fin. **Selected for five debris sprites.**
- `lightning_candidate_b.png`: second set of the same roster, with thinner,
  more natural irregular branches and wider blue-white painted blooms.
  **Selected for bolts, strikes, flashes, and sheet arc.**

The assembly script retains the painted forms, rescales only with nearest
neighbour, removes imagegen matte pixels, caps weather alpha, corrects
forbidden hue noise, and places fixed rod/strike points.
