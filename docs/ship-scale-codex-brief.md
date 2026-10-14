# Codex brief: ship readability redraws (staged, not wired)

Context: `docs/ship-scale-and-lockon-analysis.md`. The size tiers are done in code / data (the outline experiment is staged on `art/ship-outline-staged`, not part of this); what is left for Codex is
only where detail is lost even at the new size. Do not repaint the whole ship, keep silhouettes, cell grids, anchors and registration identical
(`EliteArtSync` measures points from the strips). No pink outside Space; enemies never use the player's red.

Rules per ship (design language, section 3.3): one hot focal accent (cockpit / eye / weapon core) in the world's cue colour, >= 6 art px across for
elites and >= 5 art px for 192 px enemy cells; highlights on >= 4% of the pixels; light from the top-left; no 1 px white specks inside the body; the
a pale outline is NOT wanted for now (the user rejected full outlines): do not draw one.

Batch 1 (elites, darkest / accent-starved): Storm Anvil Cloud, Refinery Tender, Static Bastion, Rod Dancer; Tide Thunderfin, Trawler, Riptide;
Verdant Thornlash; Space Singularity Hauler, Eventide Bastion; Ember Kilnback, Cauterizer.
Batch 2 (enemies whose key features are < 3 px at 720 even after x1.2): Ember Cinder / Brand (fighter_1 / 3), Storm Stormray / Thunderhead /
Tempest Anvil (fighters 2-4) and Cyclone Drone, Tide Wire Eel / Remora / Hammerhead, Desert Dust Devil Tank (fighter 4), Frost Frost Lancer,
Verdant Gnat features, all Tide rocks.

Launch: `~/.local/bin/codex exec -C <worktree> -s workspace-write -c model_reasoning_effort='"high"' -i <ref> -o <last.txt> - < prompt.md`
(one job at a time). Open the result for the user to vet before any wiring.
