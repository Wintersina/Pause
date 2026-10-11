# Built-in image generation sources

Use case for all five calls: `stylized-concept`. These are painted source
images for portrait, straight-down, orthographic terrain in a phone shooter.
The final tiles are authored from these sources by `build.py`, at 256×512
source pixels and nearest-neighbour ×2. All four environments continue past
their image edges, contain no horizon or actual sky, and omit worms, worm
trails, characters, UI, text and space imagery.

| Final set | Selected source | Direction in the generation prompt |
| --- | --- | --- |
| v1 | `candidate_1.png` | Open crescent barchan sea, many long fine ripples, indigo lee sides and thin bone crests. |
| v2 | `candidate_2_alt.png` | Dry strip-mined winding canyon, matte copper block cliffs, half-buried refinery, giant ribcage gantry and drill heads, visibly chunky pixel edges. |
| v3 | `candidate_3.png` | Cracked polygon salt-and-glass pan, toppled solar array skeleton rows and dark fused-glass scars. |
| v4 | `candidate_4.png` | Night side of the same dune sea, cold indigo sand, narrow pale crests and sparse ruins under moon-glow lighting with no visible moon. |

The first canyon candidate (`candidate_2.png`) had a stronger winding channel,
but the alternate was selected because its gantry, refinery pieces and stepped
pixel edges are clearer at tile scale.

## Final prompt set

### v1 day dunes

> Use case: stylized-concept. Asset type: source painting for a seamless
> pixel-art ground texture in a portrait phone shooter. Paint a VERTICAL,
> STRAIGHT-DOWN ORTHOGRAPHIC AERIAL view of an immense OPEN DESERT DUNE SEA at
> atmosphere level. Crescent barchan dunes dominate, with many long fine wind
> ripples and crisp pale crest lines; lee sides are dusty indigo. No horizon or
> sky. Rich detailed hand-painted pixel art like a dense 1990s game background:
> textured 4-6 tone material ramps, hard pixel edges, many small ripples; broad
> dunes with detailed internal texture, not smooth abstract swirls. Palette
> dark, pale desaturated bone-gold / khaki sand (#BFAE80 darkened), cold
> dusty-indigo shadow (#343958). Only narrow bone-white crests. Even
> environmental illumination, moderate contrast, no big bright areas. Whole
> frame is terrain; composition extends naturally through all edges. No worms,
> tracks, creatures, text, UI, stars, planets, moon, glowing amber, orange,
> red, magenta, violet, saturated turquoise.

### v2 canyon and ruins (selected alternate)

> Use case: stylized-concept. Asset type: game pixel-art ground texture
> candidate for a vertical phone shooter. A portrait STRAIGHT-DOWN TOP-DOWN
> ORTHOGRAPHIC terrain painting: a deep DRY CANYON with a winding ribbon of
> pale desaturated bone-gold sand, huge blocky shadowed RUST-COPPER cliff
> masses at either side, a huge broken RIBCAGE GANTRY crossing the canyon,
> half-sunk refinery cylinders, drill heads, ribs and rubble. Emphasize visibly
> chunky hand-placed pixel art like an elaborate 1990s game map, crisp
> staircase edges, 4-6 hard tones per material, dense tiny directional scuffs
> and wind ripples, 1-pixel dark outlines around metal, no photorealistic smooth
> texture. Dark base, small pale white-gold crest details, matte copper not
> orange. Terrain fills the whole image and continues past every edge. No
> horizon or sky. No worms or trails, characters, text, UI, stars, planets,
> orange or amber glow, red, magenta, violet, green fields or saturated teal.

### v3 salt flat

> Use case: stylized-concept. Asset type: source painting for a seamless
> pixel-art ground texture in a portrait phone shooter. VERTICAL STRAIGHT-DOWN
> ORTHOGRAPHIC AERIAL view of a dead SALT FLAT AND GLASS PAN on a desert planet.
> Broad pale dusty bone-gold and grey salt crust with dense dry polygon cracks,
> several rows of TOPPLED SOLAR ARRAY SKELETONS and dead mirror frames, a few
> winding dark fused-glass scars. Dominant geometry is an open cracked polygon
> salt plain crossed by sparse linear ruined arrays. Detailed 1990s game
> pixel-art painting, many tiny cracks and facets, matte oxidized copper frames
> with 1 px dark outline, 4-6 tone textures, restrained desaturated grey-green
> glass sheen. Dark overall exposure, moderate contrast and small crest glints.
> Top-down ground only, no horizon, no sky, no water, worms, text, UI, stars,
> planets, orange/amber glow, red, magenta, violet, saturated cyan/green.

### v4 night dunes

> Use case: stylized-concept. Asset type: source painting for a seamless
> pixel-art ground texture in a portrait phone shooter. VERTICAL STRAIGHT-DOWN
> ORTHOGRAPHIC AERIAL view of the SAME DEAD DUNE SEA AT NIGHT under cold
> moon-glow lighting. Broad crescent barchan dunes and fine wind ripples are
> dusty dark INDIGO with narrow cold bone-white crests. A few half-buried old
> mining ruins with very small dim pale work lights; the sand dunes dominate.
> Cold, quiet, dark but detailed painterly pixel art with hard edges and 4-6
> tone texture ramps, thin ripples, indigo lee-side shadows. Low contrast in
> body, tiny pale accents. Whole frame is terrain, no horizon or actual sky,
> no stars, no moon disc, no planets, no worms, no worm trails, no text, no UI,
> no orange/amber glow, no red, magenta, violet, saturated cyan/green.
