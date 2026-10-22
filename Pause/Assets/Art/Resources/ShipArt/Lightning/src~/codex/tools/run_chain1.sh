#!/bin/zsh
# Lightning chain 1: wait for the Codex image cap reset, then J1c -> J1b -> interim build -> J2, one Codex job at a time.
# Skins are NOT in this chain: they must be painted from the FINAL flipbook (after I review J1c/J1b and set damage_zones).
# usage: zsh run_chain1.sh   (log: $LOG)
W=/Users/sina/Developer/Pause/.claude/worktrees/agent-af29d7e39594f3719
A=$W/Pause/Assets/Art/Resources/ShipArt
L=$A/Lightning/src~/codex
LOG=/private/tmp/claude-501/-Users-sina-Developer-Pause/f81f62e1-5015-4fec-8294-5f213a5c6d1f/scratchpad/chain1.log
export CODEX_LOCK=/tmp/pause-codex-lightning.lock.d
RESET=1792685598
T=$(( RESET + 780 ))
echo "waiting until $T (now $(date +%s))" > $LOG
while [ $(date +%s) -lt $T ]; do sleep 60; done
cx() { zsh $W/scripts/codex-art.sh $W "$@"; }

echo "== J1c $(date)" >> $LOG
cx $L/briefs/j1c.md $L/j1c_last.txt $L/key_state0.png $L/paragon_damaged.png $L/v1/key_state1.png $L/v1/key_state2.png >> $LOG 2>&1
echo "== J1b $(date)" >> $LOG
cx $L/briefs/j1b.md $L/j1b_last.txt $L/key_state0.png $L/key_state1.png $L/key_state2.png $L/ref_neoncomet_banks.png >> $LOG 2>&1
echo "== interim build $(date)" >> $LOG
python3 $A/src~/build_restyle.py Lightning --no-skins --no-flame >> $LOG 2>&1
echo "== J2 $(date)" >> $LOG
cx $L/briefs/j2.md $L/j2_last.txt $L/paragon_ship_alone.png $L/key_state0.png $L/ref_old_flame.png >> $LOG 2>&1
echo "== chain1 done $(date)" >> $LOG
