#!/bin/bash
# Keeps running flying_all.sh until all 11 repaints exist (Codex usage limits reset); gives up after about 8 hours.
D="$(cd "$(dirname "$0")" && pwd)"
for i in $(seq 1 48); do
  "$D/flying_all.sh"
  [ "$(ls "$D"/flying_src/*_flying.png 2>/dev/null | wc -l)" -ge 11 ] && exit 0
  sleep 600
done
exit 1
