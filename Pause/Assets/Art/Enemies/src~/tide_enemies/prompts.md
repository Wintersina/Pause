# Tide enemy image-generation sources

Generated with the built-in imagegen tool. Each candidate sheet requested three
separated designs on transparent canvas. Selected columns are zero based.

## Shared candidate-sheet prompt

Use case: stylized-concept. Asset: SOURCE CANDIDATE SHEET for Tide world vertical
2D shooter. THREE distinct candidates in three separated columns on a genuinely
transparent background, each a complete isolated top-down orthographic sprite,
same scale, no overlap, no labels, no background, no shadow, no water. Pixel art
with crisp hard pixel edges, selective 1px dark outline, dense rich 4-6 tone
material ramps, 1-2px rivets scratches barnacles and shell flecks. Pale
mint-white 2px rim upper left, luminous MINT #7CF2C0 eye/core, tiny blue-violet
accents. Materials dark gunmetal, barnacle grey-green plating, weathered brown
brass hue 15-28, pearl/bone shell. Menacing rugged cyberpunk/steampunk drowned
industrial ocean, visibly readable at 192px cell (big 256px). Generous
transparent margin. Absolutely NO red, lime, cyan fields, orange glow, dominant
pink, blur, typography.

The Glow Jelly sheet used the same palette and composition instructions with
the subject wording expanded to emphasize a translucent bell and tendrils.

| File | Selected candidate | Subject instruction |
| --- | ---: | --- |
| `tide_alien_candidates.png` | 0 | Glow Jelly: translucent mint bell, brass collar, tendrils, living jelly drone. |
| `tide_chaser_candidates.png` | 1 | Wire Eel: long segmented iron and brass mechanical eel, sharp downward head, mint eye, thruster fins, S curve. |
| `tide_big_candidates.png` | 1 | Nautilus Bulwark: huge layered spiral shell plates, barnacles, tentacle pipes, pearl lights, mint-lit maw. |
| `tide_fighter_1_candidates.png` | 0 | Remora: small suckerfish drone with oval back disc, paired fins, tail fin, mint eye. |
| `tide_fighter_2_candidates.png` | 1 | Needlefish: narrow skiff body, needle beak cannon, dorsal blade, paired fins. |
| `tide_fighter_3_candidates.png` | superseded | Lantern Angler: armoured head, hinged jaw, pearl teeth, curved stalk with mint lure. |
| `tide_fighter_4_candidates.png` | 2 | Hammerhead: broad transverse hammer with twin tip cannons, plated gills and tail. |
| `tide_rock_brain_candidates.png` | 0 | Brain Coral: rounded maze-fold coral boulder, barnacle pits, faint mint cracks. |
| `tide_rock_staghorn_candidates.png` | 0 | Staghorn Spire: upright branched reef blade, rusted brass tips, glowing polyps. |
| `tide_rock_urchin_candidates.png` | 0 | Spine Urchin: uneven bone-white iron spines, round shell, mint core. |
| `tide_rock_islet_candidates.png` | 1 | Kelp Islet: upright floating slab, top tide pool, kelp cap, hanging roots and drips. |

## Lantern Angler final source

The three-candidate sheet did not give the lure a distinct enough silhouette.
The selected replacement is `tide_fighter_3_selected_source.png`, generated
with this prompt:

> A SINGLE COMPLETE isolated game enemy pixel-art sprite centered on a genuinely
> transparent square canvas with at least 15% empty transparent margin on ALL
> FOUR SIDES. Top-down orthographic view facing down for a vertical phone
> shooter. Tide world's "Lantern Angler": unmistakable anglerfish silhouette
> with huge round armoured head, open hinged dark lower jaw and two rows of bone
> white teeth, side pectoral fins, a long thin curved stalk arcing forward from
> forehead and carrying a LARGE BRIGHT MINT #7CF2C0 lure bulb visibly offset
> to the upper right of the head. The lure must be a separate lamp on a stalk,
> not the eye or central core. Rugged drowned industrial machine, weathered
> brown brass rings (hue 15-28), dark gunmetal, barnacle-encrusted grey green
> plates, pearl teeth, tiny blue-violet status lights. Dense detailed rich pixel
> art: crisp pixel edges, 1 pixel dark outline, 2 pixel pale mint-white rim
> upper-left, 4-6 tone ramps with rivets and scratches. Readable silhouette at
> 192px. No red, lime, cyan fields, orange glow, pink, shadow, backdrop, text,
> blur. One sprite only.

`build.py` crops the selected painted sources, uses nearest-neighbour reduction,
warps local appendages for four idle poses, and draws small tell and hit effects.
