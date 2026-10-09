# Verdant backdrop v3, run B painted sources

Built-in image generation produced all five painted sprite families as RGBA
cutout sheets. `build_b.py` isolates painted islands, resizes with nearest
neighbour, uses median-cut colour ramps without dithering, steps alpha, and
packs the resulting sprites. The geometry, texture, and material detail in the
atlases come from these paintings.

## Shared production prompt

Use case: `stylized-concept`. Production pixel-art sprite sheets for *Pause*, a
portrait 2D shooter. The camera is directly overhead to slightly oblique at
atmosphere level over a jungle-industrial planet, below the lime spore-cloud
ceiling. Match the existing Verdant `v1` through `v4` terrain: emerald/teal
canopy, rusted copper plant-metal, vine-grown trellises and pipe racks,
lime-gold bioluminescent rivers, and sparse magenta-pink status lights. Crisp
hand-placed pixel clusters, separate transparent sprites, generous gutters,
no labels, no blur, no ice, no outer space, no stars, planets, space stations,
comets, or asteroids, and no player's red hue.

## Candidates and selection

| Family | Candidates | Selected | Reason |
| --- | --- | --- | --- |
| Landmarks | `landmarks_a.png` | A | Distinct silhouettes, detailed refinery stacks and pipes, river craft, waterfalls and canopy terrain cutouts. |
| Pipes | `pipes_a.png`, `pipes_b.png` | B | Complete flanged ends and clearer valve/bridge/leak architecture; connected-component extraction removes bleed between uneven source rows. |
| Fires | `fires_a.png` | A | Four readable canopy fronts, varied burn islands, coal beds and scorched industrial ruins. |
| Weather | `weather_a.png`, `weather_b.png` | B | More isolated cloud, smoke, and particle shapes; the first sheet had a broad continuous background. |
| Sites | `sites_a.png` | A | The five closed/open or idle/active pairs keep matching terrain shapes and centered apertures. |

## Family prompt set

**Landmarks A:** Strict 4 by 4 sheet on transparent alpha. Row 1: two distinct
vine-grown pipe-trellis towers, two refinery tank-and-pipe compounds. Row 2:
third tall-stack refinery, two antenna relays, vine-wrapped river rail
viaduct. Row 3: two river barges with wake and two cliff-step waterfalls with
pipe intakes. Row 4: bio-dome greenhouse cluster, seed silo, sap derrick, and
canopy pipe intake station. Each sits on its own irregular piece of jungle or
river terrain. Dark and hazy like the ground tiles; no baked smoke.

**Pipes A/B:** Strict 4 by 4 sheet. Two straight runs, two right-angle bends,
T and cross junctions, two valve manifolds, two pipe bridges, pump house, two
burst-seam leaks, two root/pipe tangles, and one valve loop. Copper flanges,
wheel valves, gauges, trestles, vines, moss, and small pink lamps. B further
requested fully visible endpoint flange rings and clear transparent margins.
The sprite ends point toward sensible cell sides for chaining.

**Fires A:** Strict 4 by 4 sheet of static wildfire grounds. Row 1: four
diagonal fronts where blackened forest meets irregular orange-amber coal edges.
Row 2: four isolated burning canopy patches. Row 3: two glowing coal beds and
two scorched copper industrial ruins. Row 4: firebreak with a tiny crew
crawler, two char-forest patches, and an extra burning patch. Ember flecks and
heat seams are painted; animated flame and smoke remain emitter cues for a
later run.

**Weather A/B:** Strict 4 by 4 sheet. Four broad lime-white spore cloud banks
with teal undersides, four curled cloud wisps, three low green mist bands, two
diagonal pollen gusts, two grey-brown wildfire smoke palls, and one field of
distinct spores and leaves. B emphasized true transparent alpha and open
pixel-clustered silhouettes. Export caps alpha by type so these overlays do
not hide combat.

**Sites A:** Strict 4 by 3 sheet of dark ground launch sites. Row 1:
root-hangar closed/open and riverbank dock bay closed/open. Row 2: seed-pod
pad idle/active and industrial tower-foot bay closed/open. Row 3: forest silo
hatch closed/open and marker-light row off/on. Matching pair silhouette,
centered emergence aperture, small lime and pink lamps.

The named landmark, pipe, and fire lists each supplied 15 names for a 16-cell
atlas. `canopy_intake_00`, `pipe_loop_00`, and `burnpatch_04` fill those cells.
