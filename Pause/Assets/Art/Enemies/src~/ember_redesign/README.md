# Ember enemy redesign (WIP, PR #28)

Ground-up redesign, one Codex image per sprite (the way Tide was done): new detail, re-thought parts, a palette invented per enemy.
`briefs.py` holds the 14 text-only prompts (no reference image is attached, so Codex cannot copy colours). Approved and frozen, never
regenerated: `ember_fighter_1` (Cinder), `ember_chaser` (Cinder Fang), `ember_elite_brass_vulture`. Their strips in the repo are final.

Resume after the Codex quota reset (10:41 PM on 2026-10-15), from this folder, one sprite at a time:

    python3 run_codex.py                 # skips finished sprites, stops cleanly on a quota message
    # open src/*.png and vet each at 1x and 3x; re-run with --force NAME to redo one
    python3 build_redesign.py            # src -> strips/ (7 cells; elites reuse the elite builder's motion, no rim, no hue clean)
    bash install.sh                      # strips + death strips into Resources (+ Enemies/Elite/Ember)

Then: re-measure the elite nozzle / muzzle points in `Resources/Elites/Defs/ember_elite_*.json` (add-elite-ship skill) and the
`ember_elite_*` entries in `ember_elites_refresh/build.py` SHIPS, update codex descriptions in `EnemyRoster.cs`, and run `make test` plus
`READABILITY_WORLDS=Ember ... ReadabilitySweep.Run`. Outline rule: nothing in Ember has a border (see `ship_scale_border~`, Ember removed).
