# Dune-Eater (SKY-DREDGER) flying redesign: source notes

Keys were made with Codex image generation (headless `codex exec -m gpt-5.6-luna`, one image per call). `hull_a.png` is the selected master
candidate (alpha de-fringed); `hull_key.png` is the master after one terracotta-plate edit (a few hull plates repainted brown-orange). `i1..i5` are the
painted idle drawings (sails, turbine fans and glows, cable and mine sway), `t<pose>_<stage>.png` the three drawings of each tell (pose 0 maw / grit fan,
1 crane boom / mines, 2 crown lens / beam), `d1..d4` the damage stages, `death_*.png` / `smoke_key.png` the death strip and smoke plumes. All are edits of
hull_key.png, registered over it by build.py (neck-plate band). The atlas is 576 px cells (384 x 1.5). Rebuild and install: `python3 install.py` (in this
folder); measure_emitters.py pins the Desert parts (Maw, Boom, Lens) to fixed points, with the lowered crane's mine measured by hand.

## Hull prompt (master)

You are making game art for a mobile shoot-em-up (Unity, top-down vertical scroller). Use your image generation tool.

TASK: paint the end-of-world boss "DUNE-EATER" of the Desert world as ONE transparent-background sprite, and save it as a true RGBA PNG at the output path given at the bottom.

REFERENCES (attached): image 1 = the Iron Kraken boss, the QUALITY and richness bar only (dense hand-placed pixel art, thousands of tonal details, many separate mechanical parts). Do not copy its shape or its brown-grey colour. image 2 = the OLD Dune-Eater, a tan tracked mining machine that is being replaced because it drives on the ground and everything in this game FLOATS. Keep only its identity: a giant mining beast with a worm-like toothed maw, a big sun-lens dish and a crane boom that drops mines. Do NOT copy its look, its treads or its tan palette.

DESIGN (front-on, straight top/forward view, bilaterally symmetric EXCEPT the one crane boom described below, wide silhouette that fills about 90 percent of the square canvas width and 90 percent of its height):
- A FLYING brass-and-glass sand leviathan, an airship-beast that hovers in the sky, half manta ray, half armoured sandworm gunship. It is a flying creature only: NO treads, NO wheels, NO legs, NO feet, NO ground, nothing it could ride or drive on.
- GREAT MAW at the lower centre of the body: a huge wide circular-ish mouth seen from the front, rimmed with two hinged lower mandibles (the closing jaws) and rings of curved ivory-bone teeth, a glowing sun-gold and teal-white throat visible inside the closed-but-parted teeth. This maw is where its sand shots come from.
- LENS TURRET on the crown at the top centre: ONE very large round faceted glass lens (pale cyan to white glass with a bright sun-white centre) in a heavy brass gimbal ring with small support struts and a ring of tiny rivets. This is where its beam comes from.
- CRANE BOOM: on the creature's RIGHT flank (image right) a long articulated brass crane arm with hydraulic pistons and a cable, reaching out sideways and a little downward, ending in a claw cradle that holds ONE round spiked sand mine (a dark brass sphere with glowing amber studs). The arm must be clearly readable as a crane that drops mines. On the LEFT flank put a matching winch drum with a coil of cable and a counterweight block (no arm), so the body still feels balanced.
- TWO HUGE SAIL-WINGS spread left and right, like the lateen sails of a desert dhow crossed with manta fins: long curved triangular sails stretched on brass battens and spars, with sail panels in different colours (lapis-blue, saffron-ochre, teal, cream bone) divided by dark seam lines, tattered edges, small riveted reinforcement patches.
- TURBINE THRUSTERS: two large turbine engine pods at the wing roots and two smaller ones near the tail, each a ring cowling with visible spinning blade fans and a bright cyan-teal and sun-white glow, brass and gunmetal casings, pipes and vents.
- BODY: a broad, lacquered, segmented armoured hull (like a plated scarab or armadillo back) made of overlapping lacquer plates: deep verdigris-teal, saffron ochre, lapis blue and dark terracotta-brown, trimmed with polished brass and small bone-white bolts, with a row of exhaust stacks along the shoulders. Two small glowing sun-gold eye-lamps either side above the maw.
- A short tail fin / rudder peeking out below the maw at the bottom centre.
- Many separate readable parts (maw and mandibles, teeth, lens turret, crane boom with mine, winch, sails, battens, turbines, hull plates, stacks, eye lamps, rudder).

COLOUR (several distinctly different colours, strong value contrast, a believable rich ramp per material; NOT a tan or beige monochrome):
- lacquered deep verdigris-teal hull plates
- saffron / ochre armour plates and sail panels
- lapis / cobalt blue sail panels and trim
- dark terracotta-brown and charcoal shadow plates, near-black deep shadows
- polished aged brass and gold trim, rivets, battens, gimbals
- bone-ivory teeth and cream sail panels
- glowing cyan-teal and sun-white in the turbines and lens glass, glowing sun-gold in the maw throat and eye lamps
Hue rules: the player's ship owns pure red, so use NO red, NO crimson, NO magenta, NO pink anywhere (terracotta must stay brown-orange); no purple. No white outline and NO dark or light outline stroke drawn around the silhouette: edges are defined by value contrast and a soft rim light from the upper left on the metal only.

STYLE: premium rugged cyberpunk / steampunk hand-placed pixel art look, high resolution painting with crisp stepped pixel edges, thousands of tonal details, selective hard shadows, glowing parts bloom slightly into neighbouring armour. Lit from the upper left. Same rendering finish as image 1. Sharp, not blurry; no soft airbrush, no photo look.

TRANSPARENCY: the PNG must have a real alpha channel with fully transparent background everywhere outside the creature (no checkerboard, no baked-in colour backdrop, no ground shadow, no glow field, no text, no frame). Verify alpha at the four corners is 0 and that no opaque background colour fringe remains on the silhouette.

DELIVER: square image, at least 1024 x 1024. Save to the output path and print its size and mode. Do not write any other files except what is asked.

OUTPUT PATH: 


## Terracotta edit, pose / damage / death job preamble (shared; one image per call, the per-image line follows it)

You are making game art for a mobile shoot-em-up (Unity, vertical scroller). Use your image generation tool ONCE to make ONE image.

REFERENCE (attached): image 1 is the approved DUNE-EATER boss hull: a front-on, symmetric (except the crane) FLYING brass-and-glass sand leviathan, half manta ray and half armoured sandworm gunship: a great toothed MAW with two hinged lower mandibles at the lower centre, a big faceted glass LENS turret in a brass gimbal on the crown, a long articulated CRANE boom on the right flank holding a spiked mine (winch drum and counterweight on the left flank), two huge tattered lateen sail-wings in lapis, saffron, teal and cream panels, four turbine pods with glowing teal-white fans, lacquered hull plates in verdigris teal, saffron, lapis and terracotta with brass trim. It is the single source of truth for the design.

Make an EDIT of that exact boss. Keep the identical design, the identical palette, the identical front-on camera, the identical canvas size and ratio, the same silhouette placement and the same overall scale, centred, so the result can be registered over the reference: the hull core (the neck and shoulder plates between the lens and the eye lamps, the eye lamps, the body, the turbine pod positions) must sit at the same pixel positions as in the reference; only the stated parts change. Same hand-placed hard-edged pixel-art rendering, same lighting from the upper left, same level of detail and finish. Hue rules: no pure red, no crimson, no magenta, no pink, no purple; terracotta stays brown-orange; light and fire stay sun-gold / amber / white / cyan-teal. NO outline strokes drawn around the silhouette. Transparent background with a real alpha channel (corners alpha 0, no baked backdrop, no ground shadow, no text). Save as a true RGBA PNG at exactly the output path given, the same square size as the reference. Print the size and mode. Do not write other files.



## Per-image lines

```python
JOBS = {
    "i1": IDLE.format(n=1) + "Both sail-wings ripple: the wing tips are swept a small step upward and inward, the tattered sail edges flutter a few pixels, the turbine fan blades are rotated about 20 degrees from the reference and the turbine glows are a little brighter, the winch counterweight swings a few pixels to the left and the mine hangs a hair to the right.",
    "i2": IDLE.format(n=2) + "The sail-wings are at the top of the beat: both wing tips raised clearly higher and flared slightly outward, sail edges fluttering, fan blades rotated about 40 degrees from the reference, turbine glows and the eye lamps at their brightest, the counterweight centred, the mine swinging a few pixels left.",
    "i3": IDLE.format(n=3) + "The sail-wings are coming back down: the wing tips about half way between the reference and the raised pose, sail edges fluttering the other way, fan blades about 60 degrees from the reference, turbine glows medium-bright, the mine back at centre.",
    "i4": IDLE.format(n=4) + "The sail-wings are lowered a little: the wing tips slightly below the reference and drawn slightly in, sail edges drooping, fan blades about 80 degrees from the reference, turbine glows slightly dimmer than the reference, the counterweight a few pixels to the right.",
    "i5": IDLE.format(n=5) + "The sail-wings are at the bottom of the beat: the wing tips lowest and drooped, sail edges trailing, fan blades about 100 degrees from the reference, turbine glows at their dimmest, the mine swinging a few pixels right, the counterweight swinging right.",
    "t0_0": "THE MAW, GRIT FAN, stage 1 of 3 (anticipation). The two hinged lower mandibles part a little and the sun-gold and teal-white throat glow brightens, a faint sandy dust shimmer at the teeth, the turbine glows a little brighter, the sails hold their reference position. Subtle: a small step away from the idle. The lens, crane and hull are unchanged.",
    "t0_1": "THE MAW, GRIT FAN, stage 2 of 3 (charging). The lower mandibles are hinged clearly wider open with the ivory teeth spread, a blazing sun-gold and white ball of glowing grit forming in the throat, small sand grains swirling up into the mouth, the turbine glows brighter, the sail-wings flared a little wider. The lens, crane and hull are unchanged.",
    "t0_2": "THE MAW, GRIT FAN, stage 3 of 3 (firing). The lower mandibles are fully open and a burst of white-gold light and a spray of sand grit shoots out of the open maw downward below the chin (it may extend below the creature over the tail fin; keep the whole creature inside the canvas), eye lamps blazing white-gold, the sail-wings thrown back and wide, the turbines flaring teal-white, the body pushed slightly back. The lens, crane and hull are otherwise unchanged.",
    "t1_0": "THE CRANE BOOM, MINES, stage 1 of 3 (anticipation). The crane arm on the right begins to lower a few degrees, the hydraulic pistons extend slightly, the amber studs on the spiked mine brighten, the winch drum on the left turns with the cable tightening and the counterweight swinging up. The maw, lens and hull are unchanged.",
    "t1_1": "THE CRANE BOOM, MINES, stage 2 of 3 (charging). The crane arm is lowered clearly further and extended outward, the pistons fully extended, the spiked mine hangs lower on its cable in the claw cradle with all its amber studs blazing, glowing sparks on the cable, the winch drum on the left spinning with the counterweight hoisted high, the right sail-wing pushed back a little. The maw, lens and hull are unchanged.",
    "t1_2": "THE CRANE BOOM, MINES, stage 3 of 3 (release). The crane arm is fully lowered and the claw cradle is open: the spiked mine has just been released and falls just below the claw (the sphere detached with a bright amber-white flash and sparks at the claw), the cable slack, the winch drum on the left unwinding with the counterweight dropping, the body recoils slightly. The maw, lens and hull are otherwise unchanged.",
    "t2_0": "THE CROWN LENS, BEAM, stage 1 of 3 (anticipation). The facets of the big glass lens light up one by one and its sun-white centre brightens, the gimbal ring tilts a few degrees, thin glow along the support struts, the eye lamps flare brighter, the sails relaxed slightly forward. The maw, crane and hull are unchanged.",
    "t2_1": "THE CROWN LENS, BEAM, stage 2 of 3 (charging). The lens blazes bright white-cyan with a glowing halo, thin bright lines of light run along the brass struts and ring converging on the sun-white centre, teal-white sparks crackle around the gimbal, the sail-wings held wide and still. The maw, crane and hull are unchanged.",
    "t2_2": "THE CROWN LENS, BEAM, stage 3 of 3 (firing). The lens is white-hot: a bright white starburst flare at its centre and a short bright white-cyan beam flash leaves it, the glass facets shining white, the eye lamps pure white, the turbines flaring white-teal, the sail-wings flared wide. The maw, crane and hull are otherwise unchanged.",
    "d1": "DAMAGE STAGE 1 of 4 (lightly damaged, idle pose, sails and everything in the same position as the reference). Scorched and scratched hull plates on the left shoulder, a chipped brass gimbal strut on the lens, a dent in one mandible, a few extra torn holes in the right sail, a few tooth chips, blast scoring, a faint teal glow in the cracks. Everything else intact. Do not add smoke or electricity.",
    "d2": "DAMAGE STAGE 2 of 4 (moderately damaged, idle pose, sails as in the reference). Large torn gaps in both sails, a sheared batten tip, a few cracked lens facets, one turbine pod dented with a bent cowling, the left shoulder plate torn open exposing black machinery and a severed brass pipe, two broken teeth, the crane arm dented with a bent piston, glowing teal cracks across the hull. Do not add smoke or electricity.",
    "d3": "DAMAGE STAGE 3 of 4 (heavily damaged, idle pose, same footprint). A huge hull breach on the shoulder exposing a glowing reactor, one sail mostly shredded to bare broken battens, the other torn, one turbine pod ripped half off and hanging by cables, the lens cracked with dark missing facets, a dim eye lamp, many broken teeth, one mandible cracked, the crane arm bent, large glowing teal cracks everywhere. Recognisable creature. Do not add smoke or electricity.",
    "d4": "DAMAGE STAGE 4 of 4 (near destroyed, idle pose, same footprint). Both sail-wings reduced to skeletal broken battens with a few cloth scraps, both rear turbine pods mangled and hanging, the hull blown open showing an unstable white-hot reactor, the lens shattered to a few shards, a shattered eye lamp, a cracked half-detached lower mandible and most teeth broken, the crane arm hanging by its cable, hanging cables and slag, deep breaches. The silhouette is still recognisable as the same boss. Do not add smoke or electricity.",
    "death_split": "DEATH KEYFRAME 1: the boss splitting apart. Hull plates and sail sections separate and fly outward, a white-hot sun-gold fireball erupts from the maw and the torn hull, cracked glowing seams, the silhouette still roughly the boss's. (This image is NOT registered over the reference: it is a full new keyframe; keep the square canvas, the creature roughly centred and the whole thing inside the canvas.)",
    "death_overload": "DEATH KEYFRAME 2: reactor overload. The body is half hidden by a growing white-gold fireball, battens and plates blown outward around it, bright flame lobes, dark chunks silhouetted against the blast, teal-white arcs. (Not registered: a full new keyframe in the same square canvas, centred, all inside the canvas.)",
    "death_peak": "DEATH KEYFRAME 3: the peak of the explosion. A huge rough white-hot sun-gold fireball fills most of the canvas with irregular flame lobes and dark angular hull chunks, sail scraps, a turbine pod and the crane arm flying at the rim. No intact face. (Not registered: a full new keyframe, centred, all inside the canvas.)",
    "death_shockwave": "DEATH KEYFRAME 4: the aftermath. A broken expanding gold shockwave ring, sooty grey smoke and glowing slag, burning scraps of sail cloth and brass chunks drifting, a mostly hollow transparent centre, no intact boss. (Not registered: a full new keyframe, centred, all inside the canvas.)",
    "death_fade": "DEATH KEYFRAME 5: the last embers. Fading grey smoke wisps, sparse dim amber embers and a few settled dark hull chunks and sail scraps, mostly empty transparent canvas. (Not registered: a full new keyframe, centred, all inside the canvas.)",
}
```
