#!/bin/zsh
# one Codex job per strip, one after another
cd "$(dirname $0)"
for n in "$@"; do
  mkdir -p work_$n
  cp -R refs work_$n/ 2>/dev/null
  zsh /Users/sina/Developer/Pause-wt-codex-attacks/scripts/codex-art.sh $PWD/work_$n $PWD/prompt_$n.txt $PWD/out_$n.txt $PWD/refs/style_paragon.png $PWD/refs/style_dove.png > log_$n.txt 2>&1
  echo "$n done $(date)" >> progress.txt
done
