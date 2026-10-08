# Image generation sources

Built-in `image_gen` was used. All six selected source PNGs are kept beside this file. `generate.py` converts them with nearest-neighbour sampling and indexed palettes. The Space planet was a quality and composition reference; the Frost far backdrop was also supplied to the first planet prompt as a palette/setting reference.

## Selected prompts

### `planet_source.png`

Use case: stylized-concept. Asset type: pixel-art ice planet game sprite, independent visual candidate. Image 1 is quality reference only: match its intricate hand-crafted pixel art and strong silhouette, but create a distinct Frost ice world. Complete centered globe on transparent black-looking alpha field, spare margin. One massive dark steel industrial ring crosses the equator in tilted perspective; strongly structured plate panels, relay towers, crimson magenta/cyan/copper lamp clusters. Readable ice-white glacial continents and fractured blue ice sheets on upper left, blue-violet night on lower right with city-light constellations. Layered cyan atmospheric rim, wispy curved storm bands, polar aurora. A curved, soft pixel-dithered day/night transition. Expert crisp limited-palette pixel art, organic intricate detail, tiny dark outlines, no blur, no straight terminator, no text.

### `limb_surface_source.png`

Use case: stylized-concept. Asset type: seamless-looking close-up frozen planet surface material for a 2D pixel art game's planetary approach, to fill the ENTIRE rectangular image. Top-down oblique orbital view. The whole frame is dense detailed glacier geology: enormous cracked ice plates, jagged glacial ridges and blue-black deep crevasse valleys, pale cyan-blue ice plains, winding frozen storm cloud streaks, and scattered coherent dark steel industrial relay city districts and antenna spires with tiny magenta, cyan and warm copper light clusters. Bright icy white-blue near the TOP grading through steel blue to deep blue-violet toward the BOTTOM, with curved cloud ribbons. Edge-to-edge terrain at every side, NO horizon, NO globe, NO black or transparent empty field. High quality handcrafted 16-bit game pixel art with crisp selective 1-pixel outlines and thoughtful color clusters; no blur, no procedural noise look, no text.

### `cloud_source.png`

Use case: stylized-concept. Asset type: repeating game cloud deck pixel art source tile. A straight top-down orbital view of a very dense field of true billowing FROST cloud tops, with overlapping scalloped cumulus lobes, sharp illuminated cyan-white crests, deep blue cobalt shadow troughs, wispy tendrils and distinct gaps revealing dark ice/ground. Limited 5-tone blue-white pixel palette, scattered ice crystal sparkle points, premium hand-placed-looking crisp pixel clusters and one-pixel dark edge details. Image should be an even coverage TEXTURE without single central focal subject; shapes enter and leave ALL four sides to support toroidal seamless tiling after edge conversion. Flat top-down map view, no horizon, no planet sphere, no industrial city, no text, no soft blur.

### `cloud_dark_source.png`

Use case: stylized-concept. Asset type: deeper parallax repeating pixel-art cloud deck source tile. Top-down view of expansive cold storm clouds made of many overlapping sculpted billows, wispy filaments and wide irregular openings showing almost-black indigo ice below. Shadowy dark blue-violet, slate and deep cobalt with selective dim cyan moonlit cloud crests, tiny ice-crystal glints, 3-5 stepped tone ramp. Premium detailed crisp hand-authored pixel art, square cluster edges, tiny dark outlines in solid forms. An evenly populated TEXTURE: cloud shapes cross all four image borders for subsequent seamless tiling, no central composition, no planet sphere, no horizon, no industrial city, no text, no blur.

### `entry_source.png`

Use case: stylized-concept. Asset type: transparent-background 2D game atmospheric-entry plasma sprite source. Vertical portrait composition. An empty clear circular ship socket near upper-center (around 30% height) with a fierce coherent flame shroud wrapping around it and trailing to bottom: incandescent irregular white-hot core, magenta pink plasma mids, electric cyan-white outside edge. Broken asymmetric flame tongues, jagged layered pixel flames and glowing ember knots, shed sparks, long tapering vapor ribbons and scattered icy particles. Strong silhouette with fire concentrated near the centered ship socket then a powerful turbulent luminous trail to the bottom. Premium hand-authored neon pixel-art, crisp stepped square edges, opaque hard-edged clusters with alpha transparency outside; no blur, no smooth airbrush, no ship, no background, no text.

### `breakthrough_source.png`

Use case: stylized-concept. Asset type: transparent-background 2D game cloud breakthrough burst sprite source. Centered circular shock explosion, view from directly above. An intensely bright white-cyan center flash and several expanding irregular shells of billowing pale cyan cloud puffs, torn vapor crescents, glowing ice crystal shards and long fine radial rays. Dense fully formed impact burst, strong silhouette, dynamic broken edges and small debris sparks around it; dark cyan and cobalt details within cloud undersides, selective magenta twinkles. Premium hand-authored pixel art, sharp stepped clusters, one-pixel dark edges for solid puffs, limited luminous palette, absolutely transparent outside the bursting circle, no blur, no empty thin rings, no text.

## Explored candidates

Two planet candidates were generated. The first emphasized the ring and brighter ice; the selected second candidate had a more legible night side and better city lights. Two generated limb candidates were inspected: both left a large empty area below their horizon, so the selected generated terrain source was projected under a hand-built curved atmosphere. The four hero assets and both FX sources all derive from image generation; the speed streaks, sparse beacons, and animation motion are hand-built in `generate.py`.
