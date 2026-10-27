#!/bin/zsh
cd /Users/sina/Developer/Pause/.claude/ws-storm-ground
source /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/briefs.sh
REFS=(/Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/refs/s1_supercell.png /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/refs/ember_v3_mid.png /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/refs/verdant_v1_mid.png /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/refs/OLD_storm_v3_mid_do_not_copy.png)
job() { n=$1; b=$2
  [ -f /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/$n.png ] && return
  { cat /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/common_ground.md; echo; echo "$b"; echo; echo "Output: write the final PNG to /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/$n.png"; } > /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/prompt_$n.md
  zsh scripts/codex-art.sh /Users/sina/Developer/Pause/.claude/ws-storm-ground /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/prompt_$n.md /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/$n.lastmsg.txt ${REFS[@]} > /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/$n.log 2>&1
  echo "$n exit $? $(ls /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/$n.png 2>&1|tail -1)" >> /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/progress.txt
}
job v1_a "$V1 Arrangement A: bands run from upper left to lower right, widest sheets near the left edge."
job v1_b "$V1 Arrangement B, clearly different from A: bands run the opposite way, tighter, with a large curl scar near the right edge."
job v2_a "$V2 Arrangement A: the biggest dome low on the left edge, smaller towers up the right."
job v2_b "$V2 Arrangement B, clearly different from A: biggest dome high on the right edge, clusters of mid towers down the left."
job v3_a "$V3 Arrangement A: a large lit bloom near the upper left and long canyons running diagonally."
job v3_b "$V3 Arrangement B, clearly different from A: lightning concentrated in a few branching veins low in the frame and smaller billows."
job v4_a "$V4 Arrangement A: arcs sweeping from lower left to upper right, widest crescents on the outer edges."
job v4_b "$V4 Arrangement B, clearly different from A: arcs curving the opposite way, tighter and denser."
echo ALLDONE >> /Users/sina/Developer/Pause/.claude/ws-storm-ground/Pause/Assets/Art/Worlds/Storm/backdrop_v4~/restyle/src~/progress.txt
