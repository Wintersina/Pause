#!/bin/zsh
# sleep until the Codex image cap resets (epoch given), + 5 min, then run the given jobs in order
T=$1; shift
while [ $(date +%s) -lt $((T + 300)) ]; do sleep 60; done
echo reset-passed
