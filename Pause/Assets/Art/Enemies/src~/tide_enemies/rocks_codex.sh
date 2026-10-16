#!/bin/bash
# Codex redesign of the two Tide hazard rocks that were not kept as-is (Staghorn, Islet). They are ROCKS blown around Tide space by the wind: no engines.
# Brain Coral and Spine Urchin are the old art (rocks_keep_old.py). Run under the shared lock:  codex_lock.sh rocks_codex.sh
# Resumable: skips finished sprites.  usage: rocks_codex.sh [name ...]
D=/Users/sina/Developer/Pause/.claude/worktrees/agent-a10fa502a0c8b52e0/Pause/Assets/Art/Enemies/src~/tide_enemies
F="$D/flying_src"
REF="$F/tide_big_flying.png"
LOCK=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh
run() {
  N="$1"; BRIEF="$2"
  [ -f "$F/${N}_flying.png" ] && return
  PROMPT="You are creating ONE game hazard sprite: a ROCK that drifts, tumbles and spins through the sky of an ocean-planet world, blown around by the wind and coming at the player. First attached image: ${N}_in.png is the old teal version, use it only as a loose size reference (a different design is wanted). Second attached image, tide_big_flying.png, is the approved art-direction reference for the FINISH only: strong materials, saturated colour, dense light-and-shadow ramps, gold/brass trim and many small brass rivets, a small glowing blue light. Produce a NEW top-down pixel-art sprite (crisp hard pixel edges, 4-6 tone ramps, rich texture) on a genuinely transparent square background with generous margin, the sprite centred and filling about the same area as the old one. ${BRIEF} It is a natural hazardous rock: NO engines, NO jet nozzles, NO thrusters, NO flames, NO propellers, NO wings, nothing mechanical that flies. Because it tumbles in every direction it must read from any rotation: a compact chunky silhouette, no long vertical stalk. Colour discipline: one dominant material family with natural light and shadow that keeps texture detail, brass/gold only as small fittings and bands, at most one small glowing blue accent. FORBIDDEN: pink, magenta, red, lime green, muddy brown-grey, outlines of any kind (no pale rim, no white border, no halo). Output exactly one sprite. Save the final RGBA PNG to ${F}/${N}_flying.png (copy from the generated images folder if needed) and reply with just the path."
  cd "$F"
  printf "%s" "$PROMPT" | "$LOCK" ~/.local/bin/codex exec -s workspace-write -C "$F" -i "$F/${N}_in.png" -i "$REF" - > "$F/${N}.log" 2>&1
}
run tide_rock_staghorn "Staghorn Boulder: a rounded coral-encrusted reef boulder of deep blue-slate stone, thickly overgrown with branching orange staghorn coral antlers (short and chunky, not a tall spire), brass bands and rivets strapped round it, barnacle clusters, one small glowing blue-cyan lamp set in a brass fitting."
run tide_rock_islet "Kelp Islet: a sunken kelp-wrapped chunk of rock island: a jagged slab of weathered steel-blue reef rock with a small flat glowing tide pool on one face, wrapped in strands of olive-gold kelp and old brass chains, barnacles, a few brass rivets and one small brass lamp. Compact, no dangling roots or drips."
ls "$F"/tide_rock_staghorn_flying.png "$F"/tide_rock_islet_flying.png 2>/dev/null | wc -l | grep -q 2 && echo done > "$F/ALL_ROCKS_DONE"
