# Storm backdrop v3 painted sources

All eight PNG candidates in this folder were made with the built-in `image_gen` tool. The two
candidate paintings for each variant were inspected side by side in `candidates_contact.png`.
The existing Planetfall Storm art supplied the charcoal-slate / pale-lightning direction;
the existing Tide backdrop supplied the forbidden ocean comparison. Neither was copied into
the tiles.

Shared generation instruction: portrait 1:2, directly overhead view from just under the
ceiling of a gas-giant thunderstorm, full-frame dense HD pixel-art cloud texture, painterly
four-to-six-tone billows, small dark outlines, pale rim highlights, graphite and low-saturation
slate-indigo, sparse buried cold white-blue lightning. No water, ocean, waves, foam, space,
stars, planetary horizon, structures, ships, UI, green/teal fields, strong purple, red,
magenta, brown, or amber.

| Variant | A direction | B direction | Selection |
| --- | --- | --- | --- |
| v1 | Diagonal billowing belts and cyclone curl scars | Continuous long shear bands, narrow hail-grey belt, smaller vortices | B for mid/sky, A for far; B makes the band geometry clearest. |
| v2 | Dense cauliflower thunderhead field with deep canyons | Larger piled anvil crowns and lightning fissures | A for mid, B for far/sky; A separates the towers best. |
| v3 | One immense dark cyclone eye with anvil spiral wall | Offset asymmetric eye and sweeping spiral arms | A for mid, B for far/sky; A gives the strongest single eye. |
| v4 | Near-black billows, buried sheet lightning and tiny distant glints | Ink-indigo canyons and subtler lightning breaks | A for mid, B for far/sky; A keeps the cloud tops readable at night brightness. |

`build.py` resamples those paintings only with nearest-neighbour, applies per-pixel tonal and
slate-chroma correction without blur or a fixed global colour palette, and blends opposing
edge strips into exact wraps. The transparent flow strips reuse bright painted crest fragments
and add sparse static slanted rain lines. `../verify.py` audits the final files.
