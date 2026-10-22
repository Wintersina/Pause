#!/bin/zsh
# Saboteur Codex chain. Usage:
#   chain_a.sh        stage A: wait for the image cap reset, probe, run J1 (key poses), stop for review
#   chain_a.sh b      stage B: J1b (pose sheets) then J2 (flames)   (no wait)
#   chain_a.sh skins  stage C: the four skin jobs, one after another (no wait)
# One Codex job at a time (private lock). Progress log: scratchpad/saboteur/status.txt
WT=/Users/sina/Developer/Pause/.claude/worktrees/agent-a71e29e38407814db
SC=/private/tmp/claude-501/-Users-sina-Developer-Pause/f81f62e1-5015-4fec-8294-5f213a5c6d1f/scratchpad/saboteur
CX=$WT/Pause/Assets/Art/Resources/ShipArt/Saboteur/src~/codex
export CODEX_LOCK=/tmp/pause-codex-saboteur.lock.d
RESET=1792685598
note() { echo "$(date +%H:%M:%S) $*" >> $SC/status.txt; }

run() {   # name, then refs
  name=$1; shift
  note "start $name"
  $WT/scripts/codex-art.sh $WT $CX/briefs/$name.md $CX/last_$name.txt "$@" > $CX/log_$name.txt 2>&1
  note "end $name rc=$?"
  if grep -q usage_limit_reached $CX/log_$name.txt; then note "CAP HIT in $name"; exit 2; fi
}

stage_a() {
  target=$(( RESET + 600 + RANDOM % 300 ))
  now=$(date +%s)
  if (( target > now )); then note "sleeping $(( target - now ))s until reset+"; sleep $(( target - now )); fi
  tries=0
  while true; do
    echo 'Generate with IMAGE GENERATION a 256x256 red square on a flat green background and save it as probe.png in ./Pause/Assets/Art/Resources/ShipArt/Saboteur/src~/codex/tools/ . Say only OK.' > $SC/probe.md
    $WT/scripts/codex-art.sh $WT $SC/probe.md $SC/probe_out.txt > $SC/probe_log.txt 2>&1
    grep -q usage_limit_reached $SC/probe_log.txt || break
    tries=$(( tries + 1 ))
    r=$(grep -o '"resets_at":[0-9]*' $SC/probe_log.txt | tail -1 | grep -o '[0-9]*')
    note "probe hit the cap (resets_at=$r), try $tries"
    (( tries > 6 )) && { note "GIVE UP at probe"; exit 1; }
    now=$(date +%s)
    if [[ -n "$r" && $r -gt $now ]]; then sleep $(( r - now + 300 + RANDOM % 200 )); else sleep 900; fi
  done
  note "probe ok"
  run j1 $CX/paragon_ship_alone.png $CX/paragon_damaged.png $CX/ref_old_state0.png $CX/ref_old_state1.png $CX/ref_old_state2.png
  [[ -f $CX/key_state2.png ]] || { note "j1 produced no keys"; exit 3; }
  note "STAGE A DONE (review the keys, then: chain_a.sh b)"
}

stage_b() {
  run j1b $CX/key_state0.png $CX/key_state1.png $CX/key_state2.png $CX/ref_neoncomet_banks.png
  run j2 $CX/paragon_ship_alone.png $CX/key_state0.png $CX/ref_old_flame.png
  note "STAGE B DONE"
}

stage_c() {
  for s in Night Sodium Bone Jester; do
    run skin_$s $CX/skinref_state0.png $CX/skinref_state1.png $CX/skinref_state2.png
  done
  note "STAGE C DONE"
}

case "$1" in
  b) stage_b ;;
  skins) stage_c ;;
  *) stage_a ;;
esac
