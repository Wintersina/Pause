#!/bin/bash
# Codex repaint of the four Tide hazard rocks in the approved Tide style (Nautilus Bulwark level). Resumable: skips finished sprites.
# usage: rocks_codex.sh   -> flying_src/<rock>_flying.png ; ALL_ROCKS_DONE when every rock exists; RATE_LIMITED if Codex refuses.
D=/Users/sina/Developer/Pause/.claude/worktrees/agent-a10fa502a0c8b52e0/Pause/Assets/Art/Enemies/src~/tide_enemies
F="$D/flying_src"
REF="$F/tide_big_flying.png"
run() {
  N="$1"; BRIEF="$2"
  [ -f "$F/${N}_flying.png" ] && return
  PROMPT="You are repainting ONE game hazard sprite (first attached image: ${N}_in.png, the old teal version, top-down view, transparent background). The second attached image, tide_big_flying.png, is the approved art-direction reference: a Tide enemy painted with strong materials: a saturated orange shell with dense value ramps, gold/brass trim on every structural edge and rim, many small brass rivets, blue and violet engine pods, bright blue jet flames with white cores, a small glowing blue core. Use your image generation tool to produce a NEW image of the SAME hazard (same pose, camera, silhouette, texture detail and pixel-art style) on a genuinely transparent square background with generous margin, the sprite centred, matching the reference's level of material work and palette. ${BRIEF} It must read as a flying machine-organism: add hover jet nozzles with blue flame like the reference, remove anything water, drip, kelp, seabed or tide-pool. Colour discipline: ONE dominant hull family with natural light and shadow that keeps all original texture, panel lines and rivets; gold/brass as the second material on trim and fittings; blue and violet only on the jets, nozzles and small lights; no hard colour bands across the body, no rainbow. FORBIDDEN: pink, magenta, red, lime green, muddy brown-grey. NO outline of any kind: no pale rim, no white border, no halo. Output exactly one sprite. Save the final RGBA PNG to ${F}/${N}_flying.png (copy from the generated images folder if needed) and reply with just the path."
  cd "$F"
  printf "%s" "$PROMPT" | ~/.local/bin/codex exec -s workspace-write -C "$F" -i "$F/${N}_in.png" -i "$REF" - > "$F/${N}.log" 2>&1
}
run tide_rock_brain "Brain Coral: a floating coral boulder with an orange-coral maze-fold surface, gold brass bands and fittings, blue-violet hover jet pods on its underside edge, a glowing blue core in one fitting."
run tide_rock_staghorn "Staghorn Spire: a floating reef spire with orange-coral branches, gold brass tips and collars, a twin thruster pod base with blue jet flames, small glowing blue polyps."
run tide_rock_urchin "Spine Urchin: a spiked aerial mine-drone with a violet-indigo armoured shell, gold brass ring and fittings around a bright glowing blue core, bone-white and gold spines, small jet nozzles on the lower spines with blue flames."
run tide_rock_islet "Kelp Islet: a floating reef slab with an orange-coral and gold crust, gold trim, a glowing blue energy vent on top instead of a pool, a row of blue-violet hover jet nozzles with blue flames under it."
if grep -q "usage limit" "$F"/tide_rock_*.log 2>/dev/null; then echo limited > "$F/RATE_LIMITED"; fi
ls "$F"/tide_rock_*_flying.png | wc -l | grep -q 4 && echo done > "$F/ALL_ROCKS_DONE"
