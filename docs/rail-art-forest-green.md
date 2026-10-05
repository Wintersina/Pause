# Forest green rail and common world thickness

Forest's live asset is `Pause/Assets/Art/Resources/Worlds/Verdant/rail_forest_wide_v1.png`.
Edited with built-in imagegen. Green lamps, mint tubes and weathered forest-green
pipes replace magenta/cyan lighting and grey hosework; copper braces remain.

All four worlds now use Frost's approved visible width as the standard.
`WorldPainter` compensates for each texture's transparent padding and aligns
its visible center with Frost, keeping both lane-facing edges consistent.
Width measurements use alpha > 128 and RGB maximum > 4 to exclude black matte.
Measured horizontal bounds (inclusive):

| World | Canvas width | Left rail bounds |
| --- | --- | --- |
| Space | 725 | 159–498 |
| Frost | 725 | 140–582 |
| Forest | 725 | 139–579 |
| Ember | 725 | 157–496 |

`WorldRailReview.Run` independently reads image bounds and checks visible
world-space thickness and inner-edge alignment against Frost, along with
texture bindings, mirroring and switching back to Space. It renders all four
worlds under `Pause/Builds/RailPreview/`.

Rendering correction: Space now uses the full-resolution supplied rail,
`Pause/Assets/Art/Resources/Worlds/Space/rail_space_wide_v1.png`, mirrored on
the right. All rails use a transparent unlit shader at queue 3000 with black
matte removal and a small vertical overlap. `RailFit` repeats texture UVs
according to the texture's aspect ratio instead of stretching one tile over
the full screen height. The review captures at 900 × 1600 with the live
`CameraFit.ComputeSize` policy. Real GPU probes verify that green/cyan/pink/
orange lamps retain source colors and that black matte reveals a known backdrop.
The framing follows `docs/art-references/rail-world-target.jpeg`: gameplay's
minimum half-width is 3.72 so the full rails stay visible. Tests cover 9:16,
19.5:9 and tall 21:9 phone screens.

## Final edit prompt

Use case: precise-object-edit. Edit target: supplied FOREST industrial game rail. Change its PINK/MAGENTA illuminated lamps, coils, tiny status lights and their reflected halos to luminous leaf GREEN / emerald green, with pale yellow-green hot cores. Change pipework and ribbed hoses to weathered dark forest-green metal with moss-green patina, restrained jade highlights and occasional exposed steel ridges. Existing cyan lamp tubes may shift to complementary pale mint-green so all lighting reads forest themed. Preserve warm copper/bronze clamps and bolts, charcoal steel armor, existing leaves, moss, vines and roots, exact mechanical layout and every major contour. Keep exact 725 x 2169 canvas size, rail position, thickness, transparency and exterior margins, vertical continuous framing, all plate shapes, vents, brackets, hose bends and dense crisp rugged pixel-art detail. This is a targeted color/material edit, no redesign or extra vegetation. Genuine transparent exterior, no black matte or opaque background, no text or watermark. Keep dark structural masses, green lights only in existing light locations; no pink or magenta remains.
