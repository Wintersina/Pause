# Cryo Siren v2

Seven 192×192 RGBA cells in `frost_elite_cryo_siren.png`: landed, grounded
idle, ignition, hover, bank left, bank right, second-heart damaged. This is a
staged art strip; the existing Resources sprite has not been changed.

## Painted source

Three hero concepts were made with the built-in image generation tool and
inspected before conversion. `candidate_c.png` is the selected source and
`cryo_siren_hero_concept.png` is its clearly named copy. Candidate A carried a
dark backdrop into its alpha; B had a less prominent dish and very long plumes;
C gave the clearest radial ice dish, thin pointed nose, exposed coolant hoses,
and two small vents. Generated concept geometry was resampled with nearest
neighbour, then the anchors, one-pixel outline, ignition, bank poses, and damage
details were finished at game-pixel resolution.

Selected image generation prompt:

> Use case: stylized-concept. Asset type: isolated painted hero concept for one
> 2D vertical shooter enemy spacecraft, later converted to 192 px pixel art.
> Transparent background, orthographic top-down, centered tall needle-like
> hull, nose pointing exactly up, twin close-set rear thrusters down. Frost
> Cryo Siren, a fast fragile kiter, preserve a slender 70:150 silhouette. Long
> pointed dark blue-steel prow, forward white-cyan cryo orb outlet above a
> dominant round segmented coolant dish centered 41 percent from the top.
> Dish: pale chipped ice armor ring, dark navy separations, little brass
> bearing teeth, luminous white-cyan central well. Long narrow stern with two
> exposed symmetric coolant hoses and small rear vents. The dish must be a
> true circle and the widest part, with fine but legible details and high
> material contrast, 1-pixel-like dark selective outline and delicate cold
> rim light. Visually related to richly painted pixel-art industrial top-down
> spaceships. Frost palette: dark navy steel, pale blue-white ice, white-cyan
> light at hue 200 degrees, sparse dull brass. No red, pink, magenta, green,
> orange light, text, scenery, camera tilt, wings extending wider than the
> dish, or separate objects. Candidate C: threatening needle and open radial
> coolant vanes, compact and readable at game scale.

## Rebuild and checks

```sh
python3 src~/convert.py
python3 src~/verify.py
```

The converter writes the strip, `contact_sheet.png`, and `preview.gif`.
`verify.py` checks dimensions, alpha mode, per-cell margins, bank registration,
colour density, hostile hue exclusions, and solid pixels at the muzzle and
nozzle anchors. It prints the full measurement table.

Hover anchors, measured from the left and top of cell 3: dish (96,79), forward
cryo-orb muzzle (96,62), nozzles (90,155) and (102,155). The hover hull without
its short jets spans x=60..132 and y=16..164, a 73×149 px footprint.

The generated concept has soft painted gradients and transparent edge shading;
the converter thresholds the hull to solid pixel alpha and deliberately keeps
the rich colour variation inside the forms. The tiny grounded vent glow and
damage sparks will be subtle at true gameplay size.
