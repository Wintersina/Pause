# Tide backdrop run C: painted key sheets

The interrupted run created these three transparent source sheets with the
built-in image generation tool. The exact tool request text was not retained;
these are the production specifications represented by the saved images. The
key sheets, not procedural primitives, supply the contours, material bands,
texture, and lighting used by `build_c.py` and `finish_c.py`.

## Shared art direction

Top-down game art for a portrait 2D shooter at atmospheric altitude over an
industrial ocean. Detailed neon pixel art with pixel-stepped contours, rugged
rusted machinery, 1 px dark selective outlines, 4–6 tone ramps, mint rim light,
and tiny cool highlights. Drowned black-green water and smoky storm atmosphere;
mint-green bioluminescence and restrained blue-violet accents. Individual
objects on transparent backgrounds with room for animation. No red, lava,
orange glow, stars, planets, spacecraft, or broad cyan, lime, or pink fields.

## `painted_smoke_fire_keys.png`

An eight-key sheet: three distinct chimney plumes (heavy stack smoke lit from
below, pale steam smoke, wind-leaning oily black smoke), two small mint-green
flare-stack flames, a small burning oil-slick patch, a rising bubble column,
and drifting bioluminescent motes. Each object isolated. Smoke remains porous
and dark enough that bullets and enemies read through it. The rig fixtures are
weathered steel rather than generic sci-fi devices.

## `painted_surf_keys.png`

An eight-key sheet: rolling crest, long breaking wave, foam collar around a
corroded pylon, concentric ripple, vessel wake, rotating whirlpool, crash spray,
and light caustics over drowned city ruins. Waves have layered abyssal-water
planes and thin cool-white foam, while the caustics carry soft mint-green
emission. The generated pylon and boat were removed in the overlay flipbooks
so those effects can attach to any run-B prop.

## `painted_leaks_lights_keys.png`

Rusted pipe drip, falling rain curtain, mint beacon, tiny pink warning beacon,
rig windows, white strobe, and lighthouse/searchlight beam keys. Detailed
industrial housings, barnacles, and restrained light spill. In the final
flipbooks the painted lit glass is isolated for true OFF/igniting/ON/fading
exposure frames; the permanent fixtures are provided by run-B art.

## In-between direction

All resizing and rotations use nearest-neighbour sampling. Water contours
shift cyclically; smoke and flames deform more toward their tips while an exact
bottom-centre source pixel remains fixed. The whirlpool key is made fourfold
rotationally symmetric before rotating one quarter-turn across the loop. Lamp
cores and pixel-stepped halo alpha change independently from fixtures. The
final `verify_c.py` records seam, visible-pixel motion, brightness, anchors,
margins, and hue-band shares.
