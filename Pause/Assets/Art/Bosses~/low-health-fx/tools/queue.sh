#!/bin/bash
# sequential codex queue; stops cleanly on a usage limit
W=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w
STAGE=${1:-4}
shift
for K in "$@"; do
  for pp in tell0_0:5 tell0_1:6 tell1_0:7 tell1_1:8 tell2_0:10 tell2_1:11 fire:9; do
    P=${pp%%:*}; F=${pp##*:}
    k=$(echo $K | tr 'A-Z' 'a-z')
    $W/runcx.sh $K $P $F $STAGE
    OUT=/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472/Pause/Assets/Art/Bosses~/low-health-fx/src/${k}_${P}_stage${STAGE}_raw.png
    if [ ! -f "$OUT" ]; then
      echo "MISSING $OUT"
      if grep -qi "usage limit\|try again at\|hit your" $W/cx/${k}_${P}_s${STAGE}.log; then
        echo "USAGE LIMIT"; grep -i "try again\|usage limit" $W/cx/${k}_${P}_s${STAGE}.log | head -3
        exit 3
      fi
    fi
  done
done
echo QUEUE-DONE
