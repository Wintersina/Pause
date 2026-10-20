#!/bin/bash
# Tests for scripts/unity-batch.sh (queue, medium/heavy slots, stagger, memory
# guard, cleanup). Uses a FAKE Unity binary and private temp lock/queue/log
# dirs: never starts real Unity and never touches /tmp/pause-unity-batch.*.
#
#   scripts/tests/unity-batch-lock-test.sh
#
# Takes about two minutes (real sleeps). Exit 0 = all passed.

set -u
HERE="$(cd "$(dirname "$0")" && pwd -P)"
SCRIPT="$HERE/../unity-batch.sh"

T="$(mktemp -d /tmp/pause-unity-lock-test.XXXXXX)"
FAKE="$T/fake-unity"
PASS=0
FAIL=0
BGPIDS=""

cleanup_all() {
    local p
    for p in $BGPIDS; do kill -TERM "$p" 2>/dev/null; done
    sleep 0.5
    for p in $BGPIDS; do kill -KILL "$p" 2>/dev/null; done
    rm -rf "${T:?}"
}
trap cleanup_all EXIT

cat > "$FAKE" <<'EOF'
#!/bin/bash
# fake Unity: logs START/END with epoch seconds and projectPath, sleeps, exits.
proj=""; lf=""; prev=""
for a in "$@"; do
    [ "$prev" = "-projectPath" ] && proj="$a"
    [ "$prev" = "-logFile" ] && lf="$a"
    prev="$a"
done
echo "START ${FAKE_NAME:-?} $(date +%s) $proj $$" >> "$FAKE_LOG"
echo "ARGS ${FAKE_NAME:-?} $*" >> "$FAKE_LOG"
[ -n "$lf" ] && echo "fake unity log" > "$lf"
sp=""
trap 'kill $sp 2>/dev/null; echo "END ${FAKE_NAME:-?} $(date +%s) killed" >> "$FAKE_LOG"; exit 143' TERM
sleep "${FAKE_SLEEP:-2}" & sp=$!
wait $sp
echo "END ${FAKE_NAME:-?} $(date +%s) ok" >> "$FAKE_LOG"
exit "${FAKE_EXIT:-0}"
EOF
chmod +x "$FAKE"

# fresh state per test
newtest() {
    TEST="$1"
    echo "--- $TEST"
    D="$T/$TEST"
    mkdir -p "$D/pA" "$D/pB" "$D/pC" "$D/pD"
    export UNITY_BATCH_UNITY_BIN="$FAKE"
    export UNITY_BATCH_LOCKDIR="$D/lock"
    export UNITY_BATCH_QUEUEDIR="$D/queue"
    export UNITY_BATCH_LOGDIR="$D/logs"
    export UNITY_BATCH_STAGGER=0
    export UNITY_BATCH_POLL=0.3
    export UNITY_BATCH_NOTE=1
    export UNITY_BATCH_IGNORE_FOREIGN=1
    export UNITY_BATCH_SKIP_HELPER_CLEANUP=1
    export UNITY_BATCH_FAKE_FREE_MB=100000
    export UNITY_BATCH_FAKE_SWAP_MB=0
    unset UNITY_BATCH_TIMEOUT UNITY_BATCH_HEAVY
    export UNITY_BATCH_MAX_JOBS=4
    export FAKE_LOG="$D/fake.log"
    : > "$FAKE_LOG"
}

# job <name> <method> <project letter> <sleep secs> [VAR=val ...]; sets PID_<name>
job() {
    local name="$1" method="$2" pr="$3" sl="$4"; shift 4
    env FAKE_NAME="$name" FAKE_SLEEP="$sl" "$@" "$SCRIPT" -executeMethod "$method" -projectPath "$D/p$pr" \
        > "$D/$name.out" 2>&1 &
    eval "PID_$name=$!"
    BGPIDS="$BGPIDS $!"
    sleep 0.6   # fixes the arrival order
}
pidof_job() { eval "echo \$PID_$1"; }
finish() {   # wait for jobs, store rc in RC_<name>
    local n p rc
    for n in "$@"; do p="$(pidof_job "$n")"; wait "$p"; rc=$?; eval "RC_$n=$rc"; done
}
st() { awk -v n="$1" '$1 == "START" && $2 == n { print $3; exit }' "$FAKE_LOG"; }
en() { awk -v n="$1" '$1 == "END" && $2 == n { print $3; exit }' "$FAKE_LOG"; }
ran() { [ -n "$(st "$1")" ]; }
ok() { PASS=$((PASS + 1)); echo "  ok   $1"; }
bad() { FAIL=$((FAIL + 1)); echo "  FAIL $1"; }
check() { # check <desc> <command...>
    local d="$1"; shift
    if "$@"; then ok "$d"; else bad "$d"; fi
}
ge() { [ "$1" -ge "$2" ] 2>/dev/null; }
lt() { [ "$1" -lt "$2" ] 2>/dev/null; }
eq() { [ "$1" = "$2" ]; }
dump() { echo "    fake.log:"; sed 's/^/      /' "$FAKE_LOG"; }

# ---------------------------------------------------------------------------
newtest fifo_heavy
job H1 BuildScript.BuildAndroidDev A 2
job H2 BuildScript.BuildAndroidDev B 2
job H3 BuildScript.BuildAndroidDev C 2
finish H1 H2 H3
check "H2 starts after H1 ends" ge "$(st H2)" "$(en H1)"
check "H3 starts after H2 ends" ge "$(st H3)" "$(en H2)"
check "all exit 0" eq "$RC_H1$RC_H2$RC_H3" "000"
check "queue + lock empty afterwards" eq "$(ls "$D/queue" "$D/queue/running" 2>/dev/null | grep -c '^[0-9]')$([ -d "$D/lock" ] && echo L)" "0"

# ---------------------------------------------------------------------------
newtest fifo_light
export UNITY_BATCH_MAX_JOBS=2
job L1 AllTests.RunSuites A 3
job L2 AllTests.RunSuites B 3
job L3 AllTests.RunSuites C 3
job L4 AllTests.RunSuites D 3
finish L1 L2 L3 L4
check "L3 starts only after L1 ends (FIFO)" ge "$(st L3)" "$(en L1)"
check "L4 starts only after L2 ends (FIFO)" ge "$(st L4)" "$(en L2)"
check "L3 starts no later than L4" ge "$(st L4)" "$(st L3)"

# ---------------------------------------------------------------------------
newtest two_light_overlap
export UNITY_BATCH_MAX_JOBS=2
job L1 AllTests.RunSuites A 5
job L2 AllTests.RunSuites B 5
job L3 BootPreview.Run C 2
finish L1 L2 L3
check "L2 starts before L1 ends (overlap)" lt "$(st L2)" "$(en L1)"
check "L3 waits for a slot" ge "$(st L3)" "$(en L1)"
check "L3 did not start with both lights running" ge "$(st L3)" "$(( $(st L2) + 4 ))"

# ---------------------------------------------------------------------------
newtest heavy_drains_lights
job L1 AllTests.RunSuites A 4
job L2 AllTests.RunSuites B 4
job H1 BuildScript.BuildAndroidDev C 2
job L3 AllTests.RunSuites D 2
finish L1 L2 H1 L3
check "heavy starts after both lights ended" ge "$(st H1)" "$(en L2)"
check "later light waits for the heavy" ge "$(st L3)" "$(en H1)"

# ---------------------------------------------------------------------------
newtest heavy_not_starved
job L1 AllTests.RunSuites A 5
job H1 BuildScript.BuildAndroidDev B 2
job L2 AllTests.RunSuites C 2
job L3 AllTests.RunSuites D 2
finish L1 H1 L2 L3
check "earlier heavy starts before later light L2" ge "$(st L2)" "$(en H1)"
check "earlier heavy starts before later light L3" ge "$(st L3)" "$(en H1)"
check "L2 did not slip beside L1 (free slot)" ge "$(st L2)" "$(en L1)"

# ---------------------------------------------------------------------------
newtest classes
for m in BuildScript.BuildAndroidDev AndroidTextureDiet.Run EliteArtSync.Go TextureDiet.Apply \
         Foo.DoBuildThing Foo.ApplyAll Foo.SyncX Unknown.Method; do
    n="$(echo "$m" | tr -c 'A-Za-z0-9\n' '_')"
    UNITY_BATCH_TIMEOUT=0 env FAKE_NAME=x FAKE_SLEEP=0 "$SCRIPT" -executeMethod "$m" -projectPath "$D/pA" > "$D/c_$n.out" 2>&1
    check "$m is heavy" grep -q "class: heavy" "$D/c_$n.out"
done
for m in AllTests.RunAll AllTests.RunSuites AllTests.RunFast BootPreview.Run ShipPreview.RunAll2; do
    n="$(echo "$m" | tr -c 'A-Za-z0-9\n' '_')"
    env FAKE_NAME=x FAKE_SLEEP=0 "$SCRIPT" -executeMethod "$m" -projectPath "$D/pA" > "$D/c_$n.out" 2>&1
    check "$m is medium" grep -q "class: medium" "$D/c_$n.out"
done
env UNITY_BATCH_HEAVY=1 FAKE_NAME=x FAKE_SLEEP=0 "$SCRIPT" -executeMethod AllTests.RunSuites -projectPath "$D/pA" > "$D/c_env.out" 2>&1
check "UNITY_BATCH_HEAVY=1 forces heavy" grep -q "class: heavy" "$D/c_env.out"

# ---------------------------------------------------------------------------
newtest same_project
job L1 AllTests.RunSuites A 3
job L2 AllTests.RunSuites A 2
job L3 AllTests.RunSuites B 2
finish L1 L2 L3
check "same project serialised" ge "$(st L2)" "$(en L1)"
check "later job does not overtake the blocked one (FIFO)" ge "$(st L3)" "$(st L2)"

# ---------------------------------------------------------------------------
newtest stagger
job L1 AllTests.RunSuites A 7 UNITY_BATCH_STAGGER=4
job L2 AllTests.RunSuites B 4 UNITY_BATCH_STAGGER=4
finish L1 L2
check "L2 overlaps L1 but starts >= 3 s after it" ge "$(( $(st L2) - $(st L1) ))" 3
check "L2 started before L1 ended" lt "$(st L2)" "$(en L1)"
check "stagger reason was printed" grep -q "start stagger" "$D/L2.out"

# ---------------------------------------------------------------------------
newtest dead_ticket
job H1 BuildScript.BuildAndroidDev A 6
job H2 BuildScript.BuildAndroidDev B 2
job H3 BuildScript.BuildAndroidDev C 2
kill -9 "$PID_H2" 2>/dev/null
wait "$PID_H2" 2>/dev/null
check "dead ticket stays until purged" eq "$(ls "$D/queue" | grep -c "^[0-9]\{8\}-$PID_H2\$")" 1
finish H1 H3
check "H3 started right after H1 (dead H2 purged)" lt "$(( $(st H3) - $(en H1) ))" 3
check "H2 never ran" eq "$(st H2)" ""
check "H2 ticket purged" eq "$(ls "$D/queue" | grep -c "^[0-9]\{8\}-$PID_H2\$")" 0

# ---------------------------------------------------------------------------
newtest sigterm
job H1 BuildScript.BuildAndroidDev A 30
job H2 BuildScript.BuildAndroidDev B 2
kill -TERM "$PID_H2"; wait "$PID_H2" 2>/dev/null; RC_H2=$?
check "waiting job exits 143 on SIGTERM" eq "$RC_H2" 143
check "its ticket is gone" eq "$(ls "$D/queue" | grep -c "^[0-9]\{8\}-$PID_H2\$")" 0
check "H1's ticket/slot/lock untouched" eq "$([ -d "$D/lock" ] && [ -f "$D/queue/running/$PID_H1" ] && echo y)" y
kill -TERM "$PID_H1"; wait "$PID_H1" 2>/dev/null; RC_H1=$?
check "running job exits 143 on SIGTERM" eq "$RC_H1" 143
check "fake Unity got stopped" eq "$(awk '$1=="END" && $2=="H1" {print $4}' "$FAKE_LOG")" killed
check "slot removed" eq "$(ls "$D/queue/running" | wc -l | tr -d ' ')" 0
check "lock removed" eq "$([ -d "$D/lock" ] && echo present || echo gone)" gone
check "queue empty" eq "$(ls "$D/queue" | grep -c '^[0-9]')" 0

# ---------------------------------------------------------------------------
newtest timeout75
job H1 BuildScript.BuildAndroidDev A 5
job H2 BuildScript.BuildAndroidDev B 2 UNITY_BATCH_TIMEOUT=2
finish H2
check "gave up with exit 75" eq "$RC_H2" 75
check "H2 never ran" eq "$(st H2)" ""
check "its ticket is gone" eq "$(ls "$D/queue" | grep -c "^[0-9]\{8\}-$PID_H2\$")" 0
finish H1
check "H1 unaffected" eq "$RC_H1" 0
check "message mentions position and holder" grep -q "waiting: position 1, holder pid" "$D/H2.out"

# ---------------------------------------------------------------------------
newtest exit_code
job H1 BuildScript.BuildAndroidDev A 1 FAKE_EXIT=3
finish H1
check "Unity's exit code is passed through" eq "$RC_H1" 3
check "lock freed after failure" eq "$([ -d "$D/lock" ] && echo present || echo gone)" gone

# ---------------------------------------------------------------------------
newtest memory_guard
job L1 AllTests.RunSuites A 2 UNITY_BATCH_FAKE_FREE_MB=1000 UNITY_BATCH_TIMEOUT=3
finish L1
check "low free memory blocks a light job (75)" eq "$RC_L1" 75
check "it says why" grep -q "memory guard: only 1000 MB" "$D/L1.out"
job L2 AllTests.RunSuites A 1 UNITY_BATCH_FAKE_FREE_MB=5000 UNITY_BATCH_FAKE_SWAP_MB=5000 UNITY_BATCH_TIMEOUT=3
finish L2
check "high swap blocks a light job (75)" eq "$RC_L2" 75
check "it says why (swap)" grep -q "swap used 5000 MB" "$D/L2.out"
job L2b AllTests.RunSuites A 1 UNITY_BATCH_FAKE_FREE_MB=9000 UNITY_BATCH_FAKE_SWAP_MB=5000 UNITY_BATCH_TIMEOUT=3
finish L2b
check "swap is ignored while free+inactive >= 2x min (runs)" eq "$RC_L2b" 0
job L2c AllTests.RunSuites A 1 UNITY_BATCH_FAKE_FREE_MB=5000 UNITY_BATCH_FAKE_SWAP_MB=3000 UNITY_BATCH_TIMEOUT=3
finish L2c
check "swap under the new 4096 default does not block" eq "$RC_L2c" 0
job L3 AllTests.RunSuites A 1 UNITY_BATCH_FAKE_FREE_MB=5000
finish L3
check "enough memory lets it run" eq "$RC_L3" 0
job L4 AllTests.RunSuites A 3
job L5 AllTests.RunSuites B 1 UNITY_BATCH_FAKE_FREE_MB=1000 UNITY_BATCH_TIMEOUT=2
finish L5 L4
check "memory guard also blocks the second light job" eq "$RC_L5" 75

# ---------------------------------------------------------------------------
newtest old_lock_compat
# an OLD copy (or foreign holder) owns the lock: pid file only, no class
sleep 30 & OLDPID=$!; BGPIDS="$BGPIDS $OLDPID"
mkdir "$D/lock"; echo "$OLDPID" > "$D/lock/pid"; echo "old" > "$D/lock/owner"
job L1 AllTests.RunSuites A 1 UNITY_BATCH_TIMEOUT=2
finish L1
check "light job waits for an old-copy holder" eq "$RC_L1" 75
job H1 BuildScript.BuildAndroidDev A 1 UNITY_BATCH_TIMEOUT=2
finish H1
check "heavy job waits for an old-copy holder" eq "$RC_H1" 75
kill "$OLDPID"; wait "$OLDPID" 2>/dev/null
job L2 AllTests.RunSuites A 1
finish L2
check "stale old lock is taken over" eq "$RC_L2" 0
# while a new light job runs, an old copy would see a lock owned by a live pid
job L3 AllTests.RunSuites A 3
check "light job holds the old lock (class medium, owner pid)" eq "$(cat "$D/lock/class" 2>/dev/null)/$(cat "$D/lock/pid" 2>/dev/null)" "medium/$PID_L3"
job L4 AllTests.RunSuites B 6
finish L3
check "owner handover to the surviving light job" eq "$(cat "$D/lock/pid" 2>/dev/null)" "$PID_L4"
check "lock still held after first light exits" eq "$([ -d "$D/lock" ] && echo present || echo gone)" present
finish L4
check "last light job removes the lock" eq "$([ -d "$D/lock" ] && echo present || echo gone)" gone
job H2 BuildScript.BuildAndroidDev A 3
check "heavy holds the old lock (class heavy)" eq "$(cat "$D/lock/class" 2>/dev/null)/$(cat "$D/lock/pid" 2>/dev/null)" "heavy/$PID_H2"
finish H2

# ---------------------------------------------------------------------------
newtest four_concurrent
mkdir -p "$D/pE"
job M1 AllTests.RunAll A 6
job M2 AllTests.RunSuites B 6
job M3 BootPreview.Run C 6
job M4 AllTests.RunAll D 6
job M5 AllTests.RunSuites A 2
job M6 AllTests.RunSuites E 2
finish M1 M2 M3 M4 M5 M6
check "4 jobs on 4 projects overlap (M4 starts before M1 ends)" lt "$(st M4)" "$(en M1)"
check "M3 overlaps M1" lt "$(st M3)" "$(en M1)"
check "5th job (same project A as M1) waits for M1" ge "$(st M5)" "$(en M1)"
check "6th job waits behind the 5th (FIFO)" ge "$(st M6)" "$(st M5)"
check "all exit 0" eq "$RC_M1$RC_M2$RC_M3$RC_M4$RC_M5$RC_M6" "000000"
check "5th said why it waits" grep -q "waiting: position" "$D/M5.out"
check "queue, slots and lock empty afterwards" eq "$(ls "$D/queue" "$D/queue/running" 2>/dev/null | grep -c '^[0-9]')$([ -d "$D/lock" ] && echo L)" "0"

# ---------------------------------------------------------------------------
newtest fifth_waits_on_slots
mkdir -p "$D/pE"
job M1 AllTests.RunSuites A 6
job M2 AllTests.RunSuites B 6
job M3 AllTests.RunSuites C 6
job M4 AllTests.RunSuites D 6
job M5 AllTests.RunSuites E 2
finish M1 M2 M3 M4 M5
check "5th (different project) waits for a slot" ge "$(st M5)" "$(en M1)"
check "slot-busy reason printed" grep -q "all 4 slots busy" "$D/M5.out"

newtest max_jobs_default_1
unset UNITY_BATCH_MAX_JOBS
job M1 AllTests.RunSuites A 5
job M2 AllTests.RunSuites B 5
job M3 AllTests.RunSuites C 2
finish M1 M2 M3
check "default is one Unity at a time (M2 waits for M1)" ge "$(st M2)" "$(en M1)"
check "default: 3rd waits for 2nd" ge "$(st M3)" "$(en M2)"
check "default: reason says all 1 slots busy" grep -q "all 1 slots busy" "$D/M3.out"

newtest max_jobs_env
job M1 AllTests.RunSuites A 5 UNITY_BATCH_MAX_JOBS=1
job M2 AllTests.RunSuites B 2 UNITY_BATCH_MAX_JOBS=1
finish M1 M2
check "UNITY_BATCH_MAX_JOBS=1 serialises" ge "$(st M2)" "$(en M1)"

# ---------------------------------------------------------------------------
newtest build_solo
job M1 AllTests.RunSuites A 4
job M2 AllTests.RunSuites B 4
job B1 BuildScript.BuildAndroidDev C 3
job M3 AllTests.RunAll D 2
job M4 AllTests.RunSuites A 2
finish M1 M2 B1 M3 M4
check "build starts only after all running jobs ended" ge "$(st B1)" "$(en M2)"
check "later medium jobs wait for the build" ge "$(st M3)" "$(en B1)"
check "later same-project job waits for the build too" ge "$(st M4)" "$(en B1)"

newtest build_blocks_new
job B1 BuildScript.BuildAndroidDev A 5
job M1 AllTests.RunSuites B 2
finish B1 M1
check "medium job waits while a build runs" ge "$(st M1)" "$(en B1)"
check "reason names the heavy job" grep -q "is a heavy job" "$D/M1.out"

# ---------------------------------------------------------------------------
newtest fifo_not_starved
job M1 AllTests.RunSuites A 6
job M2 AllTests.RunSuites B 6
job B1 BuildScript.BuildAndroidDev C 2
job M3 AllTests.RunSuites D 2
job M4 AllTests.RunSuites C 2
finish M1 M2 B1 M3 M4
check "build (head) is not overtaken by M3" ge "$(st M3)" "$(en B1)"
check "build (head) is not overtaken by M4" ge "$(st M4)" "$(en B1)"
check "build ran after M1 and M2" ge "$(st B1)" "$(en M1)"

# ---------------------------------------------------------------------------
newtest memory_guard_concurrent
job M1 AllTests.RunSuites A 5
job M2 AllTests.RunSuites B 1 UNITY_BATCH_FAKE_FREE_MB=3000 UNITY_BATCH_TIMEOUT=2
job M3 AllTests.RunSuites C 1 UNITY_BATCH_FAKE_FREE_MB=5000 UNITY_BATCH_FAKE_SWAP_MB=5000 UNITY_BATCH_TIMEOUT=2
finish M2 M3
check "low memory blocks a 2nd concurrent start (75)" eq "$RC_M2" 75
check "high swap blocks a concurrent start (75)" eq "$RC_M3" 75
check "reason: memory guard" grep -q "memory guard: only 3000 MB" "$D/M2.out"
finish M1

# ---------------------------------------------------------------------------
newtest old_lock_many_medium
job M1 AllTests.RunSuites A 3
job M2 AllTests.RunSuites B 6
job M3 AllTests.RunSuites C 9
check "owner is the first medium job" eq "$(cat "$D/lock/class")/$(cat "$D/lock/pid")" "medium/$PID_M1"
finish M1
check "handover after owner exit goes to a live job" eq "$([ "$(cat "$D/lock/pid")" = "$PID_M2" ] || [ "$(cat "$D/lock/pid")" = "$PID_M3" ] && echo y)" y
finish M2
check "lock held while last job runs" eq "$([ -d "$D/lock" ] && echo present || echo gone)" present
finish M3
check "last one out removes the lock" eq "$([ -d "$D/lock" ] && echo present || echo gone)" gone

# ---------------------------------------------------------------------------
echo
echo "passed: $PASS, failed: $FAIL"
[ "$FAIL" = 0 ]
