#!/bin/bash
# usage: runcx.sh <Key> <pose> <frame> <stage>   -> one codex image, under the shared lock
K=$1; P=$2; F=$3; S=$4
W=/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/w
WT=/Users/sina/Developer/Pause/.claude/worktrees/agent-acc36733dee767472
SRC=$WT/Pause/Assets/Art/Bosses~/low-health-fx/src
k=$(echo $K | tr 'A-Z' 'a-z')
OUT=$SRC/${k}_${P}_stage${S}_raw.png
[ -f "$OUT" ] && { echo "exists $OUT"; exit 0; }
python3 $W/prep_cx.py $K $P $F $S >/dev/null
python3 $W/cx_prompt.py $K $P $S $OUT > $W/cx/${k}_${P}_s${S}.md
/private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/codex_lock.sh \
  /Users/sina/.local/bin/codex exec -C $WT -s workspace-write -c model_reasoning_effort='"high"' \
  -i $SRC/${k}_${P}_clean.png -i $SRC/${k}_wreck_stage${S}.png \
  -o $W/cx/${k}_${P}_s${S}_last.txt - < $W/cx/${k}_${P}_s${S}.md > $W/cx/${k}_${P}_s${S}.log 2>&1
echo "done $K $P $S rc=$?"
[ -f "$OUT" ] && python3 $W/check_raw.py $OUT $K $P $S
ls -la $OUT 2>&1
