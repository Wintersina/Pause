# Cinder Drake v2 source notes

Keys were made with Codex image generation (headless `codex exec`), then sampled with nearest-neighbour only by `build.py` / `build_damage.py`. `hull_a.png` is the selected hull (copied to `hull_key.png`); `t<pose>_<stage>.png` are the three drawings of each tell (pose 0 fire breath, 1 furnace slugs, 2 brow laser), `d1..d4` the damage stages, `death_*.png` / `smoke_key.png` the death strip and smoke plumes. The atlas is rendered at 576 px cells (384 x 1.5; the keys are 1254-1280 px, so the higher resolution is a re-fit of the same approved keys, no regeneration). Rebuild and install: `python3 install.py` (in this folder).

## Hull prompt

You are making game art for a mobile shoot-em-up (Unity, top-down vertical scroller). Use your image generation tool.

TASK: paint the end-of-world boss "CINDER DRAKE v2" as ONE transparent-background sprite, and save it as a true RGBA PNG at the output path given at the bottom.

REFERENCES (attached): image 1 = the Iron Kraken boss, the quality and richness bar (dense hand-placed pixel art, thousands of tonal details, many distinct materials, many separate mechanical parts). image 2 = the OLD Cinder Drake, which is too simple (one orange-red lump, a flat face with bat-wing triangles) and is being replaced. Keep only its identity: a front-on, symmetric, armoured dragon head with a burning maw, a chest furnace and a brow gem. Do NOT copy its look.

DESIGN (front-on, straight top/forward view, bilaterally symmetric, wide silhouette that fills about 90 percent of the square canvas width and 90 percent of its height):
- A FLYING mechanical-basalt wyrm, a dragon-shaped gunship that hovers and flaps. It is a flying creature only: no legs, no feet, no ground, no land animal anatomy.
- HEAD: a long horned armoured skull seen from the front with two glowing sulfur-yellow eyes, swept-back brass-plated horns, a jaw of two hinged lower mandibles with fangs that can open on a molten orange maw (keep the maw closed but visibly glowing at the seams). Exactly ONE large faceted sulfur-yellow brow gem in the centre of the forehead, between and slightly above the eyes (this is where its laser comes from).
- NECK AND CHEST: ribbed segmented neck plates and a heavy breastplate with ONE round riveted chest furnace in the middle of the lower chest (a circular grate with a bright molten orange to yellow core glowing through bars). Smaller exhaust stacks with soot-grey smoke wisps on the shoulders.
- TWO HUGE WINGS spread symmetrically left and right, bat-like with long jointed steel spars (fingers). Between the spars: translucent-looking molten membrane panels glowing orange to deep amber, with dark cooled-lava veins. EACH wing carries a chunky brass-and-steel cannon pod mounted on the wing root with a visible dark barrel, plus riveted armour plates, hydraulic pistons and coolant pipes.
- A short armoured tail tip with a spade-shaped blade peeking out below the chest at the bottom centre.
- Many separate readable parts (head, horns, mandibles, brow gem, furnace, neck plates, shoulder stacks, wing spars, wing membranes, cannon pods, tail blade).

COLOUR (several distinctly different colours, strong value contrast, a believable rich ramp per material):
- gunmetal blue-steel and blue-grey armour plates (cool, clearly NOT brown), with deep obsidian-violet shadows
- aged brass / gold trim, rivets and horns
- molten lava orange and amber in the membranes, maw seams and furnace, with yellow-white hot cores
- sulfur / lemon yellow for the brow gem and eyes
- small teal-green coolant pipe accents and a few teal verdigris patches on the brass
- soot grey smoke
Hue rules: the player's ship owns pure red, so use NO pure red, NO crimson, NO magenta, NO pink anywhere; keep warm tones in the orange / amber / yellow band (hue about 20 to 50 degrees). No white outline and NO dark or light outline stroke drawn around the silhouette: edges are defined by value contrast and a soft cool rim light from the upper left on the steel only.

STYLE: premium rugged cyberpunk / steampunk hand-placed pixel art look, high resolution painting with crisp stepped pixel edges, thousands of tonal details, selective hard shadows, glowing parts bloom slightly into neighbouring armour. Lit from the upper left. Same rendering finish as image 1. Sharp, not blurry; no soft airbrush, no photo look.

TRANSPARENCY: the PNG must have a real alpha channel with fully transparent background everywhere outside the creature (no checkerboard, no baked-in colour backdrop, no ground shadow, no glow field, no text, no frame). Smoke wisps may be semi-transparent. Verify alpha at the four corners is 0 and that no opaque background colour fringe remains on the silhouette.

DELIVER: square image, at least 1024 x 1024. Save to the output path and print its size and mode. Do not write any other files except what is asked.


## Pose / damage / death jobs (preamble shared by all)

You are making game art for a mobile shoot-em-up (Unity, vertical scroller). Use your image generation tool once per image below.

REFERENCE (attached): image 1 is the approved CINDER DRAKE boss hull: a front-on, symmetric, FLYING mechanical-basalt dragon with gunmetal blue-steel armour, brass trim, molten orange/amber wing membranes, teal coolant pipes, a sulfur-yellow brow gem, a chest furnace grate, two wing cannon pods and a spade tail blade. It is the single source of truth for the design.

For EVERY image below: make an EDIT of that exact boss. Keep the identical design, the identical palette, the identical front-on camera, the identical canvas size and ratio, the same silhouette placement and the same overall scale, centred, so the result can be registered over the reference cell by cell (the body core, head, chest furnace and cannon pods must sit at the same pixel positions as in the reference; only the stated parts change). Same hand-placed hard-edged pixel-art rendering, same lighting from the upper left, same level of detail. Hue rules: no pure red, no crimson, no magenta, no pink; warm light stays orange / amber / yellow-white. NO outline strokes drawn around the silhouette. Transparent background with a real alpha channel (corners alpha 0, no baked backdrop, no ground shadow, no text). Save each as a true RGBA PNG at exactly the given path. Print each file's size and mode at the end. Do not write other files.

IMAGES:
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t0_0.png : FIRE BREATH, stage 1 of 3 (anticipation). The lower jaw begins to part and the maw glows brighter orange inside, a faint heat shimmer; the wing spars lift a little and the wing cannons stay put. Subtle: the pose is a small step away from the idle.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t0_1.png : FIRE BREATH, stage 2 of 3 (charging). The jaw is open clearly wider with fangs showing, a bright orange-yellow fire ball forming inside the throat, neck armour plates flared slightly, wings raised a little higher and spread wider, small embers drifting up.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t0_2.png : FIRE BREATH, stage 3 of 3 (firing). The jaw is open fully wide and a blazing white-yellow-orange fireball and flame jet roars out of the open mouth downward below the chin (it may extend below the head over the chest, but keep the whole creature within the canvas), neck plates flared, wings thrown back and wide, eyes blazing white-yellow.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t1_0.png : FURNACE SLUGS, stage 1 of 3 (anticipation). The chest furnace grate glows brighter and hotter yellow-orange, steam hisses from the shoulder stacks, both wing cannon pods swivel inward a few degrees toward the centre line, wings slightly lowered.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t1_1.png : FURNACE SLUGS, stage 2 of 3 (charging). The chest furnace grate shutters slide open to reveal a bright molten core, heat bloom spilling onto the breastplate, both cannon pods glowing hot at their barrels, wings lowered and drawn in a little, steam jets.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t1_2.png : FURNACE SLUGS, stage 3 of 3 (firing). The furnace is fully open and blazing white-yellow with three molten slugs and a hot flash bursting out of it downward, both cannon barrels flashing with muzzle fire, recoil: the whole body appears pushed slightly back, shock steam from the stacks.


IMAGES:
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t2_0.png : BROW LASER, stage 1 of 3 (anticipation). The faceted sulfur-yellow brow gem brightens and its facets light up, the eyes flare brighter, the head dips slightly forward, wings relaxed slightly forward.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t2_1.png : BROW LASER, stage 2 of 3 (charging). The brow gem blazes bright lemon-yellow with a glowing halo, thin bright lines of light run along the horns converging on the gem, sparks crackle at the horn tips, wings held wide and still.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/t2_2.png : BROW LASER, stage 3 of 3 (firing). The brow gem is white-hot, a bright white-yellow starburst flares at the gem and a short bright laser flash leaves it, the eyes are pure white-yellow, the horns arc with yellow lightning, wings flared wide.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/d1.png : DAMAGE STAGE 1 of 4 (lightly damaged, idle pose, wings in the same position as the reference). Cracked and scorched armour on the left shoulder and brow, one horn tip chipped, a dent in the breastplate, a few torn holes in the right wing membrane, blast scoring, a faint amber glow in the cracks. Everything else intact. Do not add smoke or electricity.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/d2.png : DAMAGE STAGE 2 of 4 (moderately damaged, idle pose, wings as in the reference). Large torn gaps in both wing membranes, a sheared wing spar tip, a broken horn, one cannon pod dented with a bent barrel, the left shoulder plate torn open exposing black machinery and a severed teal coolant pipe, glowing lava cracks across the chest. Do not add smoke or electricity.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/d3.png : DAMAGE STAGE 3 of 4 (heavily damaged, idle pose, same footprint). Huge hull breach on the chest exposing a molten reactor, one wing mostly shredded with bare broken spars, the other wing torn, one cannon pod ripped half off and hanging by cables, a cracked dim eye, broken horns, the jaw cracked, large lava cracks everywhere. Recognisable face. Do not add smoke or electricity.


IMAGES:
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/d4.png : DAMAGE STAGE 4 of 4 (near destroyed, idle pose, same footprint). Both wings reduced to skeletal broken spars with a few molten membrane scraps, both cannon pods mangled and hanging, the breastplate blown open showing an unstable white-hot reactor, a shattered eye, cracked and half-detached jaw, hanging cables and slag, deep breaches. The face and silhouette are still recognisable as the same drake. Do not add smoke or electricity.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/death_split.png : DEATH KEYFRAME 1: the drake splitting apart. Armour plates and wing sections separate and fly outward, a white-hot orange fireball erupts from the chest furnace and torn face, cracked glowing seams, the silhouette still roughly the drake's.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/death_overload.png : DEATH KEYFRAME 2: reactor overload. The body is half hidden by a growing white-yellow-orange fireball, spars and plates blown outward around it, bright flame lobes, dark chunks silhouetted against the blast.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/death_peak.png : DEATH KEYFRAME 3: the peak of the explosion. A huge rough white-hot yellow-orange fireball fills most of the canvas with irregular flame lobes and dark angular hull chunks, wing spars and a cannon pod flying at the rim. No intact face.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/death_shockwave.png : DEATH KEYFRAME 4: the aftermath. A broken expanding orange shockwave ring, sooty grey smoke and glowing molten slag, burning scraps of wing membrane and brass chunks drifting, a mostly hollow transparent centre, no intact ship.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/death_fade.png : DEATH KEYFRAME 5: the last embers. Fading grey smoke wisps, sparse dim orange embers and a few settled dark hull chunks, mostly empty transparent canvas.
- /Users/sina/Developer/Pause/.claude/worktrees/agent-acd89fe077192ec06/Pause/Assets/Art/Bosses/Ember~/src~/smoke_key.png : DAMAGE SMOKE OVERLAY only (do NOT draw the boss): three thick billowing plumes of sooty grey-black smoke with pale hot steam, orange cinders and glowing bases, laid out on one transparent canvas: one plume at the upper left, one in the upper centre, one at the upper right (where the shoulder stacks and the head crown sit), chunky hard-edged pixel clusters, true alpha.
