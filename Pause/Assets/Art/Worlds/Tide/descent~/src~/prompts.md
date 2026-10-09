# Tide descent image-generation sources

All `*_source.png` and `planet_candidate_*.png` files here were produced with the built-in image-generation tool. `build.py` keeps the painted content and registers it with nearest-neighbor sampling. No source from Frost, Verdant, or Ember is copied into the output.

| Generated candidate | Painted request | Use |
| --- | --- | --- |
| `planet_candidate_a.png` | Spherical ocean world with a storm spiral, mint rim, oil-rig tether ring, and lit underwater cities; transparent orbit sprite in dense neon pixel art | Selected for the ring/platform silhouette and night-side city detail |
| `planet_candidate_b.png` | Alternate drowned ocean sphere with a more prominent paired storm spiral and sparse tether platforms | Retained as an alternate; ring silhouette was weaker |
| `limb_source.png` | Close-approach curved sea horizon with foam, rig cities, submerged lights and transparent upper sky | Selected |
| `cloud_source.png` | High slate grey-teal storm-cloud tops with mint-lit crests, viewed straight down | Selected upper cloud deck |
| `cloud_dark_source.png` | Deeper slate-violet-black thunderhead tops with restrained mint edge light | Selected deep cloud deck |
| `fx_source_a.png` | Wide mint-white and blue-green entry plasma shroud around a transparent ship opening | Selected primary wake |
| `fx_source_b.png` | Alternate narrower shroud with braided plasma arms | Added faintly to alternating frames for flicker |
| `breakthrough_source.png` | Transparent ring of sea spray, white foam and water shards | Scaled into five burst poses |
| `streaks_source.png` | Transparent vertical layered water/rain speed ribbons | Selected beneath added 1 px rain lines |

The common negative prompt excluded red, orange, lime, large bright cyan fields, pink, stars, ships, lettering, and external scenery. The orbit planet alone permits the space context; the other sources depict atmospheric entry or straight-down clouds.
