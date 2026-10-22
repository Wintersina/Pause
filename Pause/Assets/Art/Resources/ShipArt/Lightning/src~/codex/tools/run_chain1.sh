#!/bin/zsh
# Lightning stage runner. usage: zsh run_chain1.sh <stage>
#   j1c  : wait for the Codex image cap reset, then J1c (damage key poses). STOPS; I check the keys.
#   j1b  : J1b (pose sheets, banks) then the interim build. STOPS; I check banks (width 0.80-1.02, lean 6-8 deg, L/R distinct).
#   j2   : J2 flames (run after damage_zones are authored).
#   skins: the 4 skin jobs, one at a time (after the final flipbook build).
# One Codex job at a time via CODEX_LOCK. Log: $LOG.
W=/Users/sina/Developer/Pause/.claude/worktrees/agent-af29d7e39594f3719
A=$W/Pause/Assets/Art/Resources/ShipArt
L=$A/Lightning/src~/codex
LOG=/private/tmp/claude-501/-Users-sina-Developer-Pause/f81f62e1-5015-4fec-8294-5f213a5c6d1f/scratchpad/chain_$1.log
export CODEX_LOCK=/tmp/pause-codex-lightning.lock.d
cx() { zsh $W/scripts/codex-art.sh $W "$@"; }
case $1 in
j1c)
  RESET=1792685598
  T=$(( RESET + 780 ))
  echo "waiting until $T (now $(date +%s))" > $LOG
  while [ $(date +%s) -lt $T ]; do sleep 60; done
  echo "== J1c $(date)" >> $LOG
  cx $L/briefs/j1c.md $L/j1c_last.txt $L/key_state0.png $L/paragon_damaged.png $L/v1/key_state1.png $L/v1/key_state2.png >> $LOG 2>&1
  ;;
j1b)
  echo "== J1b $(date)" > $LOG
  cx $L/briefs/j1b.md $L/j1b_last.txt $L/key_state0.png $L/key_state1.png $L/key_state2.png $L/ref_neoncomet_banks.png >> $LOG 2>&1
  echo "== interim build $(date)" >> $LOG
  python3 $A/src~/build_restyle.py Lightning --no-skins --no-flame >> $LOG 2>&1
  ;;
j2)
  echo "== J2 $(date)" > $LOG
  cx $L/briefs/j2.md $L/j2_last.txt $L/paragon_ship_alone.png $L/key_state0.png $L/ref_old_flame.png >> $LOG 2>&1
  ;;
skins)
  echo "== skins $(date)" > $LOG
  for s in Night Sodium Bone Hazard; do
    echo "== skin $s $(date)" >> $LOG
    cx $L/briefs/skin_$s.md $L/skin_${s}_last.txt $L/skinref_state0.png $L/skinref_state1.png $L/skinref_state2.png >> $LOG 2>&1
  done
  ;;
esac
echo "== stage $1 done $(date)" >> $LOG
