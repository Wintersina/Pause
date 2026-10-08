#!/bin/bash
# unity-batch.sh -- run ONE headless Unity batch job at a time, machine-wide.
#
# Every agent / worktree runs Unity through this, so the Mac never has
# several 2-5 GB batch editors resident at once (18 GB of RAM: three or four
# of them plus their compilers swap the machine to a crash).
#
#   scripts/unity-batch.sh -executeMethod AllTests.RunAll
#   scripts/unity-batch.sh -executeMethod AllTests.RunSuites -suites ShopTest,CodexTest
#   scripts/unity-batch.sh -projectPath /abs/worktree/Pause -executeMethod BuildScript.BuildAndroidDev
#
# Run it from the repo root or its Pause/ folder (or pass -projectPath).
# Leave out -logFile: the wrapper picks a unique one per run,
#   /tmp/pause-unity-logs/<worktree>-<YYYYmmdd-HHMMSS>-<pid>.log
# and prints it first and last ("[unity-batch] log: ..."). An explicit
# -logFile is used as given.
#
# What it does:
#   * queues on a mkdir lock (/tmp/pause-unity-batch.lock). A lock whose
#     owner PID is gone is stale and taken over. While holding it, it also
#     waits for any OTHER Unity -batchmode process (one started without this
#     wrapper) to finish, unless UNITY_BATCH_IGNORE_FOREIGN=1.
#   * with no Unity editor running, kills a leftover Unity.Licensing.Client
#     helper (a stale one makes batch runs hang at "Compiling Scripts" or fail
#     with fake "Scripts have compiler errors"; Unity relaunches it) and any
#     orphaned Roslyn VBCSCompiler servers.
#   * adds -batchmode -quit, and an absolute -projectPath when missing, and
#     runs Unity from the project folder (tests read Assets/... paths).
#   * samples Unity's resident memory every 2 s and prints the peak.
#   * always cleans up (normal exit, Ctrl-C, kill): stops Unity and its whole
#     helper tree (UnityPackageManager, ILPP.Runner, UnityShaderCompiler,
#     AssetImportWorkers, ...), kills orphaned VBCSCompiler servers left by
#     the compile, and frees the lock.
#   * reports how Unity ended: exit code, a signal / crash (SIGKILL usually
#     means macOS killed it for memory), crash lines from the log, a macOS
#     crash report, and the log's tail on any failure.
#   * exits with Unity's exit code (75 if it gave up waiting for the lock).
#
# Not added: -nographics (it would save the GPU side, but a dozen suites read
# rendered pixels back), -buildTarget (switching the Library's target
# reimports every texture).
#
# Env: UNITY (editor binary), UNITY_BATCH_LOCK (lock dir),
#      UNITY_BATCH_TIMEOUT (seconds to wait for the lock; 0 = forever, default),
#      UNITY_BATCH_IGNORE_FOREIGN=1, UNITY_BATCH_LOG_DIR (default
#      /tmp/pause-unity-logs), UNITY_BATCH_RSS_LOG (file for the 2 s samples).

set -u

UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity}"
LOCK="${UNITY_BATCH_LOCK:-/tmp/pause-unity-batch.lock}"
TIMEOUT="${UNITY_BATCH_TIMEOUT:-0}"
IGNORE_FOREIGN="${UNITY_BATCH_IGNORE_FOREIGN:-0}"
LOG_DIR="${UNITY_BATCH_LOG_DIR:-/tmp/pause-unity-logs}"
RSS_LOG="${UNITY_BATCH_RSS_LOG:-}"

say() { echo "[unity-batch] $*" >&2; }

# ---- arguments ---------------------------------------------------------
args=("$@")
has() { local a; for a in "${args[@]+"${args[@]}"}"; do [ "$a" = "$1" ] && return 0; done; return 1; }

project=""
log=""
for ((i = 0; i < ${#args[@]}; i++)); do
    next=$((i + 1))
    if [ "${args[$i]}" = "-projectPath" ] && [ $next -lt ${#args[@]} ]; then
        project="$(cd "${args[$next]}" 2>/dev/null && pwd -P)" || { say "no such project: ${args[$next]}"; exit 2; }
        args[$next]="$project"
    elif [ "${args[$i]}" = "-logFile" ] && [ $next -lt ${#args[@]} ]; then
        log="${args[$next]}"
        case "$log" in /*|-) ;; *) log="$(pwd -P)/$log"; args[$next]="$log" ;; esac
    fi
done
if [ -z "$project" ]; then
    if [ -d Assets ] && [ -d ProjectSettings ]; then project="$(pwd -P)"
    elif [ -d Pause/Assets ]; then project="$(cd Pause && pwd -P)"
    else say "run from the repo or its Pause/ folder, or pass -projectPath"; exit 2; fi
    args+=(-projectPath "$project")
fi
if [ -z "$log" ]; then
    tree="$(basename "$(dirname "$project")")"
    mkdir -p "$LOG_DIR"
    log="$LOG_DIR/$tree-$(date +%Y%m%d-%H%M%S)-$$.log"
    args+=(-logFile "$log")
fi
has -quit || args=(-quit "${args[@]}")
has -batchmode || args=(-batchmode "${args[@]}")
say "log: $log"

# ---- processes ---------------------------------------------------------
# Unity editor processes, matched on the executable (a shell that merely
# mentions Unity in its arguments must not count).
unity_pids() { ps -axo pid=,comm= | awk '$2 ~ /\/Unity\.app\/Contents\/MacOS\/Unity$/ { print $1 }'; }
batch_pids() {
    local p
    for p in $(unity_pids); do
        [ "$p" = "${child:-}" ] && continue
        ps -o args= -p "$p" 2>/dev/null | grep -q -- "-batchmode" && echo "$p"
    done
}
# All descendants of $1.
descendants() {
    ps -axo pid=,ppid= | awk -v root="$1" '
        { kids[$2] = kids[$2] " " $1 }
        END { n = split(kids[root], q, " "); i = 1
              while (i <= n) { p = q[i++]; print p; m = split(kids[p], k, " "); for (j = 1; j <= m; j++) q[++n] = k[j] } }'
}
# A pid we may kill: still a Unity helper or a Roslyn server (pids get reused).
is_unity_helper() {
    ps -o args= -p "$1" 2>/dev/null | grep -qE '/Unity\.app/|VBCSCompiler|AssetImportWorker'
}
orphan_compilers() {
    ps -axo pid=,ppid=,args= | awk '$2 == 1 && /VBCSCompiler\.dll/ && /Unity\.app/ { print $1 }'
}
kill_all() {   # kill_all <pids...>: TERM, a grace period, then KILL
    local p alive=""
    for p in "$@"; do is_unity_helper "$p" && kill -TERM "$p" 2>/dev/null && alive="$alive $p"; done
    [ -z "$alive" ] && return
    for _ in 1 2 3 4 5; do
        local left=""
        for p in $alive; do kill -0 "$p" 2>/dev/null && left="$left $p"; done
        alive="$left"; [ -z "$alive" ] && return
        sleep 1
    done
    for p in $alive; do kill -KILL "$p" 2>/dev/null; done
}

run_dir="$(mktemp -d /tmp/pause-unity-run.XXXXXX)"
locked=0
child=""
sampler=""

cleanup() {
    [ -n "$sampler" ] && kill "$sampler" 2>/dev/null
    sampler=""
    local helpers=""
    if [ -n "$child" ]; then
        helpers="$(descendants "$child") $(sort -u "$run_dir/tree" 2>/dev/null)"
        if kill -0 "$child" 2>/dev/null; then
            say "stopping Unity (pid $child)"
            kill -TERM "$child" 2>/dev/null
            for _ in $(seq 1 15); do kill -0 "$child" 2>/dev/null || break; sleep 1; done
            kill -KILL "$child" 2>/dev/null
        fi
        # helpers that name the editor's pid in their arguments (UPM "-s <pid>",
        # the shader compiler's IPC name) even after being re-parented
        helpers="$helpers $(ps -axo pid=,args= | awk -v p="$child" '/Unity\.app/ && ($0 ~ ("-s " p "( |$)") || $0 ~ ("-" p "-")) { print $1 }')"
        kill_all $helpers
        child=""
    fi
    if [ "$locked" = 1 ] && [ -z "$(unity_pids)" ]; then
        local orphans; orphans="$(orphan_compilers)"
        [ -n "$orphans" ] && kill_all $orphans
    fi
    if [ "$locked" = 1 ] && [ "$(cat "$LOCK/pid" 2>/dev/null)" = "$$" ]; then rm -rf "$LOCK"; fi
    locked=0
    rm -rf "$run_dir"
}
trap cleanup EXIT
trap 'say "interrupted"; exit 130' INT
trap 'say "terminated"; exit 143' TERM HUP

# ---- lock --------------------------------------------------------------
started=$(date +%s)
last_note=0
while :; do
    if mkdir "$LOCK" 2>/dev/null; then
        echo "$$" > "$LOCK/pid"
        printf '%s\n%s\n%s\n' "$project" "$(date '+%F %T')" "$log" > "$LOCK/owner"
        locked=1
    else
        owner="$(cat "$LOCK/pid" 2>/dev/null)"
        if [ -z "$owner" ]; then
            # mid-creation by another waiter, or a crash between mkdir and the
            # pid write: stale once it is older than 10 s
            age=$(( $(date +%s) - $(stat -f %m "$LOCK" 2>/dev/null || date +%s) ))
            if [ "$age" -gt 10 ]; then say "removing ownerless lock"; rm -rf "$LOCK"; continue; fi
        elif ! kill -0 "$owner" 2>/dev/null; then
            say "removing stale lock of dead pid $owner"
            rm -rf "$LOCK"
            continue
        fi
    fi

    if [ "$locked" = 1 ]; then
        foreign=""
        [ "$IGNORE_FOREIGN" = 1 ] || foreign="$(batch_pids | tr '\n' ' ')"
        [ -z "${foreign// /}" ] && break
        why="another Unity batch process is running (pid ${foreign% }, not started by this wrapper)"
    else
        why="lock held by pid $owner ($(head -1 "$LOCK/owner" 2>/dev/null))"
    fi

    now=$(date +%s)
    if [ "$TIMEOUT" != 0 ] && [ $((now - started)) -ge "$TIMEOUT" ]; then
        say "gave up after ${TIMEOUT}s: $why"; exit 75
    fi
    if [ $((now - last_note)) -ge 60 ]; then say "waiting: $why"; last_note=$now; fi
    sleep 5
done
waited=$(( $(date +%s) - started ))
[ "$waited" -gt 5 ] && say "got the lock after ${waited}s"

# ---- stale helpers -----------------------------------------------------
if [ -z "$(unity_pids)" ]; then
    for lp in $(pgrep -f "Unity.Licensing.Client" 2>/dev/null); do
        say "killing leftover Unity.Licensing.Client (pid $lp)"
        kill "$lp" 2>/dev/null
    done
    orphans="$(orphan_compilers)"
    if [ -n "$orphans" ]; then say "killing orphaned VBCSCompiler ($(echo $orphans))"; kill_all $orphans; fi
    sleep 1
fi

# ---- run ---------------------------------------------------------------
say "Unity ${args[*]}"
cd "$project" || exit 2
ran=$(date +%s)
touch "$run_dir/start"
"$UNITY" "${args[@]}" &
child=$!

(
    peak=0
    while kill -0 "$child" 2>/dev/null; do
        r=$(ps -o rss= -p "$child" 2>/dev/null | tr -d ' ')
        if [ -n "$r" ]; then
            [ "$r" -gt "$peak" ] && peak=$r && echo "$peak" > "$run_dir/peak"
            [ -n "$RSS_LOG" ] && echo "$(date +%H:%M:%S) $r" >> "$RSS_LOG"
        fi
        descendants "$child" >> "$run_dir/tree"
        sleep 2
    done
) &
sampler=$!

wait "$child"
code=$?
kill "$sampler" 2>/dev/null; wait "$sampler" 2>/dev/null; sampler=""
peak=$(cat "$run_dir/peak" 2>/dev/null || echo 0)
secs=$(( $(date +%s) - ran ))

# ---- report ------------------------------------------------------------
if [ "$code" -gt 128 ]; then
    sig=$((code - 128))
    name="$(kill -l "$sig" 2>/dev/null)"
    hint=""; [ "$sig" = 9 ] && hint=" -- SIGKILL from outside: usually macOS killing it for memory"
    say "CRASH: Unity died of signal $sig (SIG$name) after ${secs}s$hint"
fi
if [ -f "$log" ]; then
    marks="$(grep -aE 'Crash!!!|Native Crash|Received signal|Obtained [0-9]+ stack frames|Segmentation fault|Bus error|Aborting batchmode|Scripts have compiler errors|error CS[0-9]+|Timed-out .*waiting for channel|Out of memory|Could not allocate memory' "$log" | head -12)"
    [ -n "$marks" ] && { say "notable log lines:"; echo "$marks" | cut -c1-240 >&2; }
    result="$(grep -a '^\[ALL\] RESULT' "$log" | tail -1)"
    [ -n "$result" ] && say "$result"
    if [ "$code" != 0 ]; then
        say "log tail:"
        tail -n 25 "$log" | cut -c1-240 >&2
    fi
else
    say "no log at $log"
fi
report="$(find "$HOME/Library/Logs/DiagnosticReports" -maxdepth 1 -name 'Unity*' -newer "$run_dir/start" 2>/dev/null | head -1)"
[ -n "$report" ] && say "macOS crash report: $report"
say "Unity exited $code after ${secs}s (waited ${waited}s); peak RSS $((peak / 1024)) MB"
say "log: $log"
exit "$code"
