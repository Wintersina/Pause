#!/bin/bash
# resumable queue: runs each missing job, one image per codex call, under the global lock
S=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad
cd "$(dirname "$0")"
H=$(pwd)
run_job() { n=$1
  [ -f "$H/out/$n.ok" ] && return 0
  for try in 1 2 3 4 5 6; do
    rm -f "$H/out/$n.png"
    $S/codex_lock.sh /Users/sina/.local/bin/codex exec -m gpt-5.6-luna --skip-git-repo-check -s workspace-write -C "$H" -i "$H/in/$n.png" - < "$H/prompts/$n.md" > "$H/out/$n.log" 2>&1
    if [ -s "$H/out/$n.png" ] && $S/venv/bin/python3 -c "from PIL import Image;Image.open('$H/out/$n.png').load()" 2>/dev/null; then touch "$H/out/$n.ok"; return 0; fi
    if grep -q -E "usage_limit_reached|429" "$H/out/$n.log"; then sleep 900; else sleep 20; fi
  done; }
run_job space_rock_cluster_c3
run_job space_rock_crater_c3
run_job space_rock_dark_c3
run_job frost_rock_rime_c3
run_job space_alien_c3
run_job tide_rock_urchin_c3
run_job verdant_rock_knot_c3
run_job verdant_rock_spore_c3
run_job frost_fighter_2_c3
run_job space_rock_cluster_c1
run_job space_rock_cluster_c2
run_job space_rock_crater_c1
run_job space_rock_crater_c2
run_job space_rock_dark_c1
run_job space_rock_dark_c2
run_job frost_rock_rime_c1
run_job frost_rock_rime_c2
run_job space_alien_c1
run_job space_alien_c2
run_job tide_rock_urchin_c1
run_job tide_rock_urchin_c2
run_job verdant_rock_knot_c1
run_job verdant_rock_knot_c2
run_job verdant_rock_spore_c1
run_job verdant_rock_spore_c2
run_job frost_chaser_c2
run_job frost_rock_shard_c1
[ "$(ls $H/out/*.ok 2>/dev/null | wc -l)" -ge 27 ] && touch $H/ALL_DONE
