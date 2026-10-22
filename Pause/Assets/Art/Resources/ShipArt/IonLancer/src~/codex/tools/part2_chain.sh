#!/bin/zsh
# Part 2 chain for IonLancer: wait for the Codex image cap reset, then run the jobs in order.
# usage: part2_chain.sh <reset_epoch>   (seconds; the cap reset is ~2026-10-22 11:05 CDT = 1792685100)
# Log: <scratch>/part2.log ; each step writes a marker  <scratch>/part2_<step>.done
# The damage layers are the IDLE damaged cells (col 0); damage_overlay.py maps them onto every other cell.
# Verification, the 1x/2x dark-background view, commit and push are done by hand afterwards.
RESET=${1:-1792685100}
S=/private/tmp/claude-501/-Users-sina-Developer-Pause/f81f62e1-5015-4fec-8294-5f213a5c6d1f/scratchpad/ionlancer
W=/Users/sina/Developer/Pause/.claude/worktrees/agent-a15dd13c95b43c273
H=$W/Pause/Assets/Art/Resources/ShipArt/src~
C=$W/Pause/Assets/Art/Resources/ShipArt/IonLancer/src~/codex
CX=/private/tmp/claude-501/-Users-sina-Developer-Pause/f81f62e1-5015-4fec-8294-5f213a5c6d1f/scratchpad/ion-codex.sh
mkdir -p $S
exec >> $S/part2.log 2>&1
now=$(date +%s)
if [ $now -lt $((RESET + 300)) ]; then echo "waiting $((RESET + 300 - now)) s"; sleep $((RESET + 300 - now)); fi
echo "start $(date)"
step() { echo "== $1 $(date)"; }

step wreck2
$CX $W $C/briefs/j1_wreck2.md $S/wreck2_out.txt $C/key_state0.png $C/ref_bar_wreck.png
[ -f $C/key_state1_v3.png ] && [ -f $C/key_state2_v3.png ] || { echo "FAILED wreck2 (cap? see wreck2_out.txt)"; exit 1; }
cp $C/key_state1_v3.png $C/key_state1.png
cp $C/key_state2_v3.png $C/key_state2.png
touch $S/part2_wreck2.done

step build-noskins
python3 $H/build_restyle.py IonLancer --no-skins --no-flame

step skins
for n in Night Sodium Bone Lance; do
  $CX $W $C/briefs/skin_${n}_damage.md $S/skin_${n}_out.txt $C/skinref_state1.png $C/skinref_state2.png $C/skin_${n}_state0.png
  for s in 1 2; do [ -f $C/skin_${n}_state${s}_v2.png ] && cp $C/skin_${n}_state${s}_v2.png $C/skin_${n}_state${s}.png || echo "MISSING skin $n state $s"; done
done
touch $S/part2_skins.done

step final-build
python3 $H/build_restyle.py IonLancer
python3 $H/patch_restyle.py IonLancer
step overlay
python3 $H/damage_overlay.py IonLancer --skins --preview $S/ov
echo "ALL DONE $(date)"
touch $S/part2_all.done
