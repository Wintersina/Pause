#!/bin/bash
# Resumable Codex queue for the damaged elite cells. Sleeps until the image cap resets, then one image per call under the shared lock.
D=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/dmg
LOCK=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh
PY=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/venv/bin/python3
GO=$((1792190536 + 360))
NOW=$(date +%s)
if [ "$NOW" -lt "$GO" ]; then sleep $((GO - NOW)); fi
mkdir -p $D/out $D/logs
KEYS="tide_elite_riptide tide_elite_thunderfin tide_elite_bathyscaphe tide_elite_pearl_warden tide_elite_trawler storm_elite_anvil_cloud storm_elite_refinery_tender storm_elite_rod_dancer storm_elite_skyhook_harrier storm_elite_static_bastion desert_elite_caravan_hauler desert_elite_dune_stalker desert_elite_glass_cannon desert_elite_lens_matriarch desert_elite_mirror_bastion desert_elite_scorpion_walker frost_elite_floe_harrower frost_elite_glacier_tender frost_elite_whiteout_sentinel space_elite_eventide_bastion space_elite_orbit_reaver space_elite_rift_lancer space_elite_singularity_hauler"
valid() { $PY -c "
from PIL import Image
import sys
im=Image.open(sys.argv[1]); im.verify(); im=Image.open(sys.argv[1]); assert im.size[0]>=512 and im.size[1]>=512
" "$1" 2>/dev/null; }
for pass in 1 2 3 4 5 6 7 8; do
  pending=0
  for k in $KEYS; do
    f=$D/out/$k.png
    if [ -f "$f" ] && valid "$f"; then continue; fi
    rm -f "$f"
    pending=1
    $LOCK codex exec -m gpt-5.6-luna --skip-git-repo-check -s workspace-write -C $D -i $D/in/$k.png -o $D/logs/$k.last.txt < $D/in/$k.md > $D/logs/$k.log 2>&1
    if [ ! -f "$f" ]; then
      if grep -qi "usage_limit\|usage limit\|429\|try again" $D/logs/$k.log; then echo "$k rate limited, sleeping 15 min"; sleep 900; fi
    else echo "$k done"; fi
  done
  [ $pending -eq 0 ] && break
done
for k in $KEYS; do f=$D/out/$k.png; if ! { [ -f "$f" ] && valid "$f"; }; then echo "MISSING $k"; fi; done
echo ALL_DONE
