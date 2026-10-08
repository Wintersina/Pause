# Frost backdrop v3 / run B painted sources

Generated with the built-in image generation tool, using true transparent output.
Each painted candidate is a sheet of separate pixel-art sprites. `build_b.py`
selects one sheet per family, cuts the grid into cells, resamples with nearest
neighbour, removes low-alpha spill, applies discrete colour/alpha ramps, and
packs the exported atlases. All final painted silhouettes originate in these
generated sheets.

## Shared production brief

Use case: `stylized-concept`. Unity 2D Frost backdrop art for a portrait shooter.
Directly overhead/slightly oblique view of a planetary frozen ocean beneath
clouds, matching the committed `mid.png`: cracked floes, open-water leads,
coastal ice cliffs, offshore industry, rugged gunmetal/icebreaker steel,
limited deep navy and blue-violet palette, sparse cyan/magenta status lights
and occasional muted copper. High-quality hand-placed crisp pixel clusters.
Strict isolated sprite grid with transparent gutters and no text. No blur,
photorealism, smooth anti-aliasing, outer space, stars, planets, asteroids,
space stations, spacecraft, or sky horizon.

## Candidates and selection

| Family | Candidate A | Candidate B | Selected | Reason |
| --- | --- | --- | --- | --- |
| Landmarks | `landmarks_a.png` | `landmarks_b.png` | A | Clearer, more varied rigs, icebreakers, bridge, and cliff silhouettes; transparent gutters. |
| Weather | `weather_a.png` | `weather_b.png` | A | Better transition from broad curling banks to wisps, mist and diagonal gusts. |
| Sites | `sites_a.png` | `sites_b.png` | A | Most legible matching closed/open portal pairs and centered pad/hatch states. |

## Family prompts

**Landmarks A.** A strict 4 × 4 grid. Row 1: two distinct offshore oil/relay
rigs with ice legs, flare stacks, cranes and helipads; two ice-locked tank and
walkway platforms. Row 2: two coastal refinery compounds without smoke; two
antenna/relay arrays and cable runs. Row 3: a rotatable rail causeway across
broken ice; two distinct overhead icebreakers with fractured-ice channel wakes;
one coastal ice cliff with shoreline rubble. Row 4: a second distinct cliff;
drilling derrick; tracked crawler convoy; a small service platform. Center each
isolated painted sprite in its cell.

**Landmarks B.** Alternate silhouettes for the same 4 × 4 lineup: narrow-flare
rig, tank platform, compact refinery, radar and relay array, longer rail bridge,
orange and blue icebreakers, alternate headlands, derrick, convoy and service
platform. More restrained neon, still transparent and isolated.

**Weather A.** A strict 4 × 4 grid of translucent pixel-clustered cloud and snow
art. Row 1: four broad cool blue-white cloud banks. Row 2: four narrow curled
cloud wisps. Row 3: three low horizontal mist bands and one diagonal blizzard
gust. Row 4: two further diagonal wind-driven gust sheets and two fields of
many distinct snowflake sprites. Stepped translucent edges, no opaque white
patches, no space imagery.

**Weather B.** Alternate cloud masses of decreasing density, four cirrus wisps,
three horizontal fog streaks, three sparse diagonal gusts and two tiny
snowflake fields. Cool blue-white and frost-blue pixel clusters, transparent
gaps and gutters.

**Sites A.** A strict 4 × 3 grid of matching launch-site state pairs with the
emergence point centered. Row 1: closed/open steel hangar in coastal ice;
closed/open lift bay in an offshore rig deck. Row 2: idle/active circular relay
pad ring; closed/open crawler garage in an industrial hull. Row 3: closed/open
round silo hatch in cracked ice; unlit/lit pad marker lamp row. Tiny cyan and
magenta lights, hard steel, frost, rails and restrained caution marks.

**Sites B.** Alternate version of the same twelve pairs: segmented hangar
blast doors, extraction-deck lift, relay pad with aerial, crawler bay, round
shaft and low marker lights. The selected A version reads more clearly at the
small intended screen sizes.

The named user list contains 15 landmarks and 15 weather pieces while asking
for 16 in each atlas. `platform_02` and `cloud_bank_03` occupy the spare cells;
the fourth row of the 12-piece sites atlas remains transparent.
