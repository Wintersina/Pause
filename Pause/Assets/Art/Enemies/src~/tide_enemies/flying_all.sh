#!/bin/bash
D=/Users/sina/Developer/Pause/.claude/worktrees/agent-a10fa502a0c8b52e0/Pause/Assets/Art/Enemies/src~/tide_enemies
R="$D/flying_run.sh"
run() { [ -f "$D/flying_src/$1_flying.png" ] || "$R" "$1" "$2" > "$D/flying_src/$1.log" 2>&1; }
run tide_chaser "Wire Eel: an airborne serpent drone; its fins become small swept wings and its tail ends in a jet nozzle with glowing exhaust, segments stay mechanical."
run tide_big "Nautilus Bulwark: an airborne armoured gunship; the tentacle pipes become jet thruster nozzles with glowing exhaust and side stub-wings, shell stays as armour plating."
run tide_fighter_1 "Remora: an airborne fighter; the paired fins become angular swept wings, the tail fin becomes a twin-nozzle jet engine with glowing exhaust."
run tide_fighter_2 "Needlefish: an airborne dart fighter; the paired fins become sharp swept wings, rear jet engine nozzle with glowing exhaust, dorsal blade stays as a tail fin."
run tide_fighter_3 "Lantern Angler: an airborne gunship; the pectoral fins become big wings with wingtip thrusters, a rear jet pair with glowing exhaust, the lure stays as a glowing lamp on its stalk."
run tide_fighter_4 "Hammerhead: an airborne bomber; the broad hammer becomes a wide wing with twin tip cannons and thruster pods, gills become engine vents, glowing tail jet."
run tide_rock_brain "Brain Coral: a floating coral-armoured boulder held up by anti-gravity hover jets: add four small jet nozzles / hover rings with glowing exhaust around its underside edge, remove drips."
run tide_rock_staghorn "Staghorn Spire: a floating reef spire hovering on a thruster ring at its base with glowing jet exhaust, branch tips glowing, no seabed roots."
run tide_rock_urchin "Spine Urchin: a spiked aerial mine-drone; a few spines tipped with small jet thrusters and glowing exhaust, spiked shell stays."
run tide_rock_islet "Kelp Islet: a floating slab held up by hover thrusters: replace the roots and drips with jet nozzles and glowing exhaust, replace the tide pool with a glowing energy pool or antenna deck, no kelp, no water."
echo done > "$D/flying_src/ALL_DONE"
