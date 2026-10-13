#!/bin/bash
# run-tests-parallel.sh -- the whole AllTests suite, sharded over N concurrent Unity editors.
#
#   scripts/run-tests-parallel.sh                 # gate lane, 2 shards
#   scripts/run-tests-parallel.sh -j 3            # 3 shards (needs UNITY_BATCH_MAX_JOBS=3)
#   scripts/run-tests-parallel.sh --full          # nightly lane: exhaustive sampling (PAUSE_TEST_FULL=1)
#   scripts/run-tests-parallel.sh --suites ShopTest,CodexTest
#   scripts/run-tests-parallel.sh --plan          # print the shard split, run nothing
#   scripts/run-tests-parallel.sh --clean         # delete the shard copies afterwards
#
# How it works (docs/parallel-tests.md):
#   1. Suite names come from the Suites table in AllTests.cs (parsed, not a Unity launch).
#   2. They are split into N shards balanced by duration (longest-first onto the
#      least-loaded shard) from scripts/test-durations[-full].txt, filled from the newest
#      full-run log in /tmp/pause-unity-logs for suites the seed lacks, else the median.
#   3. Each shard is its OWN project copy /tmp/pause-shards/shard<i>/Pause (Unity cannot
#      open one project twice): the first time an APFS clone (cp -Rc) of this workspace's
#      Pause/ incl. Library, afterwards an incremental rsync of Assets/ProjectSettings/Packages
#      (Library is reused). Sibling folders the tests read (../docs ...) are synced too.
#      The shard's ProjectSettings gets productName "Pause-shard<i>" so every shard has its
#      own PlayerPrefs plist (~/Library/Preferences/unity.WinterPeace.Pause-shard<i>.plist).
#   4. Every shard runs through scripts/unity-batch.sh (queue, memory/swap guards) with
#      AllTests.RunSuites -suites <its list>, all at once.
#   5. The shard logs are aggregated into one summary; a suite with no result line (the
#      editor crashed or was killed) is rerun once, alone. Exit 1 on any failure.
#   6. The seed durations are refreshed from the run's own timings.
#
# Env: UNITY_BATCH_MAX_JOBS (cap on concurrent editors, default 2: -j is clamped to it),
#      UNITY_BATCH_MAX_SWAP_MB / UNITY_BATCH_MIN_FREE_MB (the wrapper's guards, untouched),
#      PT_SHARD_DIR (/tmp/pause-shards), PT_ALLTESTS, PT_DURATIONS, PT_BATCH (wrapper path).

set -u
HERE="$(cd "$(dirname "$0")" && pwd -P)"
ROOT="$(cd "$HERE/.." && pwd -P)"
ALLTESTS="${PT_ALLTESTS:-$ROOT/Pause/Assets/Editor/Tests/AllTests.cs}"
SHARD_DIR="${PT_SHARD_DIR:-/tmp/pause-shards}"
BATCH="${PT_BATCH:-$HERE/unity-batch.sh}"
LOG_DIR="${UNITY_BATCH_LOGDIR:-${UNITY_BATCH_LOG_DIR:-/tmp/pause-unity-logs}}"

# ---- pure parts (tested by scripts/tests/run-tests-parallel-test.sh) ---------------

# Suite names, in table order, from AllTests.cs.
list_suites() {
    sed -n -E 's/^[[:space:]]*\("([A-Za-z0-9_]+)",[[:space:]]*[A-Za-z0-9_.]+\),.*$/\1/p' "$1"
}

# stdin: "suite seconds" (seconds may be "-" = unknown); $1 = N.
# stdout: "shard suite seconds" with shard in 1..N, longest-first onto the least-loaded shard.
balance() {
    local n="$1"
    awk -v n="$n" '
        { name[NR] = $1; sec[NR] = ($2 ~ /^[0-9.]+$/) ? $2 + 0 : -1; if (sec[NR] >= 0) { k++; known[k] = sec[NR] } }
        END {
            # unknown durations: the median of the known ones (1 s when nothing is known)
            for (i = 1; i <= k; i++) for (j = i + 1; j <= k; j++) if (known[j] < known[i]) { t = known[i]; known[i] = known[j]; known[j] = t }
            med = k ? known[int((k + 1) / 2)] : 1
            for (i = 1; i <= NR; i++) if (sec[i] < 0) sec[i] = med
            # order: seconds descending, name ascending (insertion sort; ~200 items)
            for (i = 1; i <= NR; i++) idx[i] = i
            for (i = 2; i <= NR; i++) {
                v = idx[i]; j = i - 1
                while (j >= 1 && (sec[idx[j]] < sec[v] || (sec[idx[j]] == sec[v] && name[idx[j]] > name[v]))) { idx[j + 1] = idx[j]; j-- }
                idx[j + 1] = v
            }
            for (s = 1; s <= n; s++) load[s] = 0
            for (i = 1; i <= NR; i++) {
                b = 1
                for (s = 2; s <= n; s++) if (load[s] < load[b]) b = s
                load[b] += sec[idx[i]]
                printf "%d %s %.1f\n", b, name[idx[i]], sec[idx[i]]
            }
        }'
}

# stdin: shard log; stdout: "R <suite> PASS|FAIL <fails> <seconds>" per suite result line and
# "C <suite> <check line>" for each failing check logged in the suite before its result line.
parse_log() {
    awk '
        /^\[ALL\] [A-Za-z0-9_]+: (PASS|FAIL)/ && $2 != "RESULT:" {
            name = $2; sub(/:$/, "", name)
            st = $3; fails = 0; secs = 0
            if (match($0, /\(([0-9]+)\)/)) { fails = substr($0, RSTART + 1, RLENGTH - 2) + 0 }
            if (match($0, /\([0-9.]+s,/)) { secs = substr($0, RSTART + 1, RLENGTH - 3) + 0 }
            printf "R %s %s %d %.1f\n", name, st, fails, secs
            for (i = 1; i <= nbuf; i++) printf "C %s %s\n", name, buf[i]
            nbuf = 0; next
        }
        /^\[[A-Za-z0-9_-]+\] FAIL/ && !/^\[ALL\]/ { if (nbuf < 40) buf[++nbuf] = $0; next }
        /^\[ALL\] [A-Za-z0-9_]+ threw:/ { if (nbuf < 40) buf[++nbuf] = $0; next }
    '
}

# (the test sources the file for the functions above: PT_SOURCE_ONLY=1 stops here)
[ "${PT_SOURCE_ONLY:-0}" = 1 ] && return 0

# ---- options ---------------------------------------------------------------------
JOBS=2; SUITES=""; FULL=0; PLAN=0; CLEAN=0; RERUN=1; REFRESH=1
while [ $# -gt 0 ]; do
    case "$1" in
        -j) JOBS="${2:-2}"; shift ;;
        -j[0-9]*) JOBS="${1#-j}" ;;
        --suites) SUITES="${2:-}"; shift ;;
        --full) FULL=1 ;;
        --gate) FULL=0 ;;
        --plan) PLAN=1 ;;
        --keep) CLEAN=0 ;;
        --clean) CLEAN=1 ;;
        --no-rerun) RERUN=0 ;;
        --no-refresh) REFRESH=0 ;;
        -h|--help) sed -n 2,30p "$0"; exit 0 ;;
        *) echo "unknown option: $1" >&2; exit 2 ;;
    esac
    shift
done
case "$JOBS" in ''|*[!0-9]*|0) echo "-j needs a positive number" >&2; exit 2 ;; esac
MAXJ="${UNITY_BATCH_MAX_JOBS:-2}"
if [ "$JOBS" -gt "$MAXJ" ]; then
    echo "[parallel] -j $JOBS exceeds UNITY_BATCH_MAX_JOBS=$MAXJ: using $MAXJ (set UNITY_BATCH_MAX_JOBS=$JOBS to allow more)" >&2
    JOBS="$MAXJ"
fi
LANE=gate; SUFFIX=""
if [ "$FULL" = 1 ]; then LANE=full; SUFFIX="-full"; export PAUSE_TEST_FULL=1; else unset PAUSE_TEST_FULL; fi
DURS="${PT_DURATIONS:-$HERE/test-durations$SUFFIX.txt}"

# ---- the suite list and the durations -------------------------------------------------
all="$(list_suites "$ALLTESTS")"
[ -n "$all" ] || { echo "[parallel] no suites found in $ALLTESTS" >&2; exit 2; }
if [ -n "$SUITES" ]; then
    sel=""
    for want in $(echo "$SUITES" | tr ',' ' '); do
        hit="$(echo "$all" | grep -ix -e "$want" -e "${want}Test" | head -1)"
        if [ -z "$hit" ]; then echo "[parallel] unknown suite: $want" >&2; exit 2; fi
        sel="$sel$hit"$'\n'
    done
    all="$(printf '%s' "$sel" | sed '/^$/d' | sort -u)"
fi

# newest full-run log (RunAll with a RESULT line) as the fallback duration source
newest_full_log() {
    local f
    for f in $(ls -t "$LOG_DIR"/*.log 2>/dev/null | head -400); do
        grep -q "^\[ALL\] RunAll: " "$f" 2>/dev/null && grep -q "^\[ALL\] RESULT: " "$f" 2>/dev/null && { echo "$f"; return; }
    done
}
declare_durs() {   # stdout: "suite seconds" for every suite in $all
    local tmp seedlog; tmp="$(mktemp)"
    [ -f "$DURS" ] && grep -E '^[A-Za-z0-9_]+ [0-9.]+$' "$DURS" > "$tmp"
    if [ "$LANE" = gate ] || [ ! -s "$tmp" ]; then
        seedlog="$(newest_full_log)"
        [ -n "$seedlog" ] && parse_log < "$seedlog" | awk '$1 == "R" { print $2, $5 }' >> "$tmp.log"
    fi
    echo "$all" | awk -v seed="$tmp" -v lg="$tmp.log" '
        BEGIN { while ((getline l < seed) > 0) { split(l, a, " "); d[a[1]] = a[2] }
                while ((getline l < lg) > 0) { split(l, a, " "); if (!(a[1] in d)) d[a[1]] = a[2] } }
        { print $1, ($1 in d) ? d[$1] : "-" }'
    rm -f "$tmp" "$tmp.log"
}
plan="$(declare_durs | balance "$JOBS")"

if [ "$PLAN" = 1 ]; then
    echo "$plan" | awk '{ n[$1]++; t[$1] += $3; l[$1] = l[$1] (l[$1] ? "," : "") $2 }
        END { for (s in n) printf "shard %s: %d suites, ~%.0f s: %s\n", s, n[s], t[s], l[s] }' | sort
    exit 0
fi

# ---- shard project copies -------------------------------------------------------------
prep_shard() {   # $1 = shard number
    local i="$1" dir="$SHARD_DIR/shard$1" src="$ROOT/Pause" item
    mkdir -p "$dir/Pause"
    if [ ! -d "$dir/Pause/Library" ] && [ -d "$src/Library" ]; then
        echo "[parallel] shard$i: cloning Library (APFS clonefile)..." >&2
        cp -Rc "$src/Library" "$dir/Pause/Library" || { echo "[parallel] clone failed for shard$i" >&2; return 1; }
    fi
    for item in Assets ProjectSettings Packages; do
        [ -d "$src/$item" ] && rsync -a --delete "$src/$item/" "$dir/Pause/$item/"
    done
    # product name per shard: a private PlayerPrefs plist (the editor keys it by company + product)
    sed -i '' -E "s/^([[:space:]]*productName:).*/\1 Pause-shard$i/" "$dir/Pause/ProjectSettings/ProjectSettings.asset"
    # the tests read ../docs/... and similar: sync the sibling folders (not the heavy or repo-private ones)
    for item in "$ROOT"/*; do
        case "$(basename "$item")" in Pause|HapticGateFramesV2) continue ;; esac
        if [ -d "$item" ]; then rsync -a --delete "$item/" "$dir/$(basename "$item")/"; else cp -p "$item" "$dir/"; fi
    done
}

RUN="$SHARD_DIR/runs/$(date +%Y%m%d-%H%M%S)-$$"
mkdir -p "$RUN"
T0=$SECONDS
echo "[parallel] lane $LANE, $JOBS shard(s), $(echo "$all" | wc -l | tr -d ' ') suites, run dir $RUN"
for ((i = 1; i <= JOBS; i++)); do prep_shard "$i" || exit 2; done
echo "[parallel] shards prepared in $((SECONDS - T0)) s"
echo "$plan" | awk '{ n[$1]++; t[$1] += $3 } END { for (s in n) printf "[parallel] shard %s: %d suites, ~%.0f s\n", s, n[s], t[s] }' | sort

run_shard() {   # $1 = shard, $2 = comma list, $3 = log file; echoes the exit code
    local i="$1" list="$2" lg="$3"
    "$BATCH" -projectPath "$SHARD_DIR/shard$i/Pause" -logFile "$lg" -executeMethod AllTests.RunSuites -suites "$list" > "$lg.wrapper" 2>&1
    echo $?
}

shard_main() {   # runs in the background: first pass, then one rerun of suites without a result
    local i="$1" list="$2" t=$SECONDS rc rc2 missing
    rc="$(run_shard "$i" "$list" "$RUN/shard$i.log")"
    parse_log < "$RUN/shard$i.log" | awk '$1 == "R" { print $2 }' | sort > "$RUN/shard$i.done"
    missing="$(echo "$list" | tr ',' '\n' | sort | comm -23 - "$RUN/shard$i.done" | paste -sd, -)"
    echo "$rc" > "$RUN/shard$i.rc"
    echo "$missing" > "$RUN/shard$i.missing"
    if [ -n "$missing" ] && [ "$RERUN" = 1 ]; then
        echo "[parallel] shard$i: no result for $missing (exit $rc): rerunning once" >&2
        rc2="$(run_shard "$i" "$missing" "$RUN/shard$i-rerun.log")"
        echo "$rc2" > "$RUN/shard$i-rerun.rc"
        parse_log < "$RUN/shard$i-rerun.log" | awk '$1 == "R" { print $2 }' | sort > "$RUN/shard$i-rerun.done"
        echo "$missing" | tr ',' '\n' | sort | comm -23 - "$RUN/shard$i-rerun.done" | paste -sd, - > "$RUN/shard$i.missing"
    fi
    echo $((SECONDS - t)) > "$RUN/shard$i.secs"
}

pids=""
for ((i = 1; i <= JOBS; i++)); do
    list="$(echo "$plan" | awk -v s="$i" '$1 == s { printf "%s%s", (n++ ? "," : ""), $2 }')"
    [ -n "$list" ] || continue
    echo "$list" > "$RUN/shard$i.list"
    shard_main "$i" "$list" &
    pids="$pids $!"
done
trap 'kill $pids 2>/dev/null; exit 130' INT TERM
for p in $pids; do wait "$p"; done

# ---- aggregate ------------------------------------------------------------------------
: > "$RUN/results.txt"
for ((i = 1; i <= JOBS; i++)); do
    for f in "$RUN/shard$i.log" "$RUN/shard$i-rerun.log"; do
        [ -f "$f" ] && parse_log < "$f" | sed "s/^\([RC]\) /\1 $i /" >> "$RUN/results.txt"
    done
done
total=$(echo "$all" | wc -l | tr -d ' ')
ran=$(awk '$1 == "R" { print $3 }' "$RUN/results.txt" | sort -u | wc -l | tr -d ' ')
failed_n=$(awk '$1 == "R" && $4 == "FAIL" { print $3 }' "$RUN/results.txt" | sort -u | wc -l | tr -d ' ')
echo
echo "=================== parallel test summary ($LANE lane) ==================="
awk '$1 == "R" && $4 == "FAIL" { print $3 }' "$RUN/results.txt" | sort -u | while read -r s; do
    awk -v s="$s" '$1 == "R" && $3 == s { printf "FAIL %s (%s failures, shard %s)\n", s, $5, $2 }
                   $1 == "C" && $3 == s { sub(/^C [0-9]+ [A-Za-z0-9_]+ /, ""); print "     " $0 }' "$RUN/results.txt"
done
bad=0
for ((i = 1; i <= JOBS; i++)); do
    [ -f "$RUN/shard$i.missing" ] || continue
    m="$(cat "$RUN/shard$i.missing")"
    if [ -n "$m" ]; then echo "NO RESULT (shard $i, editor exit $(cat "$RUN/shard$i.rc" 2>/dev/null), log $RUN/shard$i.log): $m"; bad=1; fi
done
for ((i = 1; i <= JOBS; i++)); do
    [ -f "$RUN/shard$i.secs" ] && echo "shard $i: $(wc -l < "$RUN/shard$i.done" | tr -d ' ') suites, $(cat "$RUN/shard$i.secs") s wall$( [ -s "$RUN/shard$i.missing" ] && echo ", INCOMPLETE")"
done
wall=$((SECONDS - T0))
verdict=PASS
{ [ "$failed_n" -gt 0 ] || [ "$bad" = 1 ] || [ "$ran" -lt "$total" ]; } && verdict=FAIL
echo "suites: $ran/$total run, $failed_n failed; wall time ${wall} s ($((wall / 60)) min $((wall % 60)) s); logs $RUN"
echo "[parallel] RESULT: $verdict suites=$ran/$total failed=$failed_n"

# ---- refresh the seed ---------------------------------------------------------------------
if [ "$REFRESH" = 1 ] && [ "$verdict" = PASS ]; then
    tmp="$(mktemp)"
    { [ -f "$DURS" ] && grep -E '^[A-Za-z0-9_]+ [0-9.]+$' "$DURS"; awk '$1 == "R" { print $3, $6 }' "$RUN/results.txt"; } |
        awk '{ d[$1] = $2 } END { for (k in d) print k, d[k] }' | sort -k2,2nr -k1,1 > "$tmp" && mv "$tmp" "$DURS"
fi
[ "$CLEAN" = 1 ] && rm -rf "$SHARD_DIR/shard"*
[ "$verdict" = PASS ]
