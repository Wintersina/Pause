# Tempest Engine battle damage source notes

All painted source PNGs in this folder were made with the built-in image generator on transparent backgrounds. `pristine_cell.png` is an unmodified crop of idle cell 0 from `../../Storm~/Storm.png`, kept here only as a registration reference. The generator `build_damage.py` composes the final 384 px cells with nearest-neighbor resampling.

## Body candidates

- `stage1_candidate_a.png`: exact Tempest Engine edit; right shoulder crack, bent rod, dented grille, dim coil.
- `stage2_candidate_a.png`: edit of stage 1; opened shoulder, torn right fan, snapped rings, exposed pipes.
- `stage3_candidate_a.png`: edit of stage 2; large opened drum and bright core, hanging coil, skeletal fan and missing rods.
- `stage4_candidate_a.png`: edit of stage 3; both towers mangled, extensive missing plating, rods reduced to stumps. **Selected** for final stage.
- `stage4_candidate_b.png`: alternate edit of stage 3. Rejected because the front gained too much pale intact plating, making it read less wrecked at phone scale.

The shared body prompt required the original frontal turbine, twin coils, rod crown, side fans, fixed center and dark slate industrial pixel style. Every stage requested cumulative physical tearing with true dark gaps, no baked smoke or bolts, storm-white light with a thin pale-yellow edge, and no red, orange, green, violet or magenta.

## FX candidates

- `smoke_candidate_a.png`: three separated oily smoke and white vapor plumes, underlit by storm-white. **Selected** because its larger stepped forms remain legible at game scale.
- `smoke_candidate_b.png`: alternate narrower plumes. Rejected because the puffs become thinner and more photographic after reducing to sprite size.
- `electric_candidate_a.png`: separated painted lightning branches. The build script extracts their bright veins and combines them with registered 2–3 px storm-white paths.

## Death candidates

- `death_split_candidate_a.png`: edited stage-4 hull splitting along the core and throwing blades and rods.
- `explosion_candidate_a.png`: storm-white overload with dark turbine fragments. Selected for death frame 2.
- `explosion_peak_candidate_b.png`: alternate later overload with a larger, brighter white center and fewer visible pieces. Selected for death frame 3.
- `shockwave_candidate_a.png`: broken vapor and soot ring with drifting hot chunks. Used at two sizes and light levels for frames 4–5.

Selected source art is cleaned only for stray hue and low-alpha dust. The rich source color range is preserved; there is no global palette quantization or blur.
