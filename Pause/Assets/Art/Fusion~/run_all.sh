#!/bin/zsh
# usage: run_all.sh key...   (one Codex job at a time via scripts/codex-art.sh; sources live in this ~ folder, not shipped)
cd "$(dirname "$0")"; ROOT=$(cd ../../../..; pwd)
for k in "$@"; do
  [ -s raw/$k.png ] && continue
  $ROOT/scripts/codex-art.sh "$PWD" prompts/$k.md raw/$k.last.txt ../Resources/Weapons/Powers2/Paragon_attack_shockwave.png > raw/$k.log 2>&1
  echo "$k: $(ls -l raw/$k.png 2>&1 | tail -1)"
done
