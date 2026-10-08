# Frost battle-damage image generation

Tool: built-in `image_gen` edit mode, transparent background. Each PNG in
`candidates/` is the untouched 1254 × 1254 generated result. These prompts
were sent with the named input image(s); all requested true alpha, no backdrop,
no baked smoke or electricity in the hull frames, and preserved boss identity.

| Candidate | Inputs | Prompt / requested edit | Used |
| --- | --- | --- | --- |
| `stage1_a.png` | `../ref_idle_cell.png` | Stage 1, 4 hearts. Left ice shoulder heavily cracked and chipped, broken left brass band and rivets, soot and dent on cheek plate, cyan stress fractures on brow, right eye dim. Same pose and footprint, dense crisp neon pixel art. | Yes |
| `stage1_b.png` | `../ref_idle_cell.png` | Alternate stage 1. Broad broken ice shoulder, dented torn riveted cheek plate revealing two copper tubes, cyan brow fracture, snapped brass strap, dark scorch and a dim right eye. | No |
| `stage2_a.png` | `stage1_a.png` | Stage 2, 3 hearts, cumulative. Keep stage 1 damage; add deep branching cyan cracks, sheared left exhaust stack, torn shoulder plating with exposed ribbed machinery and leaking magenta tubes, snapped fang, hanging brass brackets. | Yes |
| `stage2_b.png` | `stage1_a.png` | Alternate stage 2, cumulative. Jagged hollow left chimney stump, open left front armour with magenta pressure tubes, glowing cracks across shoulder and forehead, broken jaw fang, dangling brass brace. | No |
| `stage3_a.png` | `stage2_a.png` as edit target; `stage4_b.png` as breach reference | Stage 3, 2 hearts, cumulative and dramatically wrecked. Huge jagged right hull hole with cyan-white cryo core and dark ribbed machinery; right cannon/ice spikes torn away on cables, missing upper plates, shattered eye, broken right stack, severe frost cracks. | Yes |
| `stage3_b.png` | `../ref_idle_cell.png` | Alternate stage 3. Dramatic right shoulder hull breach with bright cyan core, exposed ribs and copper pipes; damaged eye, broken side chimney, torn right cannon/spike cluster on cables, snapped fang, luminous cracks. | No |
| `stage4_a.png` | `stage3_a.png` | Stage 4, 1 heart, cumulative. Second deep front/left breach and charred rear stack, fractured unstable cyan core, both flank clusters shredded, jaw broken open, missing fangs, central glowing fracture, torn tubes and dangling plates. Preserve recognisable face and footprint. | Yes |
| `stage4_b.png` | `../ref_idle_cell.png` | Alternate stage 4. Barely held together, multiple deep jagged hull breaches, exposed core and ribbed internals, both cannons/spike clusters mangled, snapped chimneys, shattered eye, broken jaw, bright cyan cracks. | No |
| `smoke_a.png` | `../ref_idle_cell.png` for registration | Transparent FX only, no ship. Four thick sooty grey-violet pixel smoke plumes with white-cyan freezing steam and cyan embers, at left/right stacks and shoulder breaches; clear eyes and jaw. | Yes |
| `smoke_b.png` | `smoke_a.png` | Alternate FX only. Four separate darker, chunkier, turbulent plumes with twisting white-cyan steam, hard 4–6 tone clusters and embers. | Yes |
| `electric_a.png` | `../ref_idle_cell.png` for registration | Transparent FX only, no ship. Three bold angular arc groups with 2–3 final-pixel white-hot cores, cyan/magenta fringes, star sparks, across brow/right shoulder/below eyes. | Yes |
| `electric_b.png` | `electric_a.png` | Alternate erratic routing of three separated arcs, extra forks, brighter cores and square-pixel spark bursts. | Yes |

`build_damage.py` fixes the scale and registration once for all four hull
paintings, uses nearest-neighbour resampling, and builds subtle body B frames.
It crops the generated smoke and arc groups to compose six registered effect
frames. The previews show the selected painted stages at full effect strength.
