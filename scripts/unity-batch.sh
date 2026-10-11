#!/bin/bash
# unity-batch.sh -- run headless Unity batch jobs machine-wide: FIFO queue,
# at most two LIGHT jobs at once, HEAVY jobs alone.
#
# Every agent / worktree runs Unity through this, so the Mac never has
# several 2-5 GB batch editors resident at once (18 GB of RAM).
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
# PROTOCOL (see docs/unity-batch-lock.md)
#   * Ticket: every invocation takes a ticket <8-digit seq>-<pid> in the queue
#     dir (/tmp/pause-unity-batch.queue), numbered under a short mutex so the
#     order is strictly FIFO. A job may start only when it is the FIRST live
#     ticket (strict FIFO: nobody overtakes, so a heavy job is never starved);
#     tickets of dead pids are purged. Waiting prints
#     "waiting: position N, holder pid ..." every ~60 s.
#   * Class, from the -executeMethod argument:
#       LIGHT: AllTests.RunSuites*, *Preview*  (short focused runs)
#       HEAVY: everything else (unknown methods too), explicitly AllTests.RunAll,
#              BuildScript.*, AndroidTextureDiet.*, EliteArtSync.*, TextureDiet*,
#              names containing Build / ApplyAll / Sync, no -executeMethod at all,
#              or env UNITY_BATCH_HEAVY=1. Heavy wins over light on any match.
#   * Slots: running jobs are files in <queue>/running/<pid>. A HEAVY job starts
#     only with no job running and nothing starts while it runs. A LIGHT job
#     starts when at most ONE other job runs and that one is LIGHT on a
#     DIFFERENT projectPath (Unity cannot open one project twice).
#   * Old-lock compatibility: older copies of this script only know the mkdir
#     lock /tmp/pause-unity-batch.lock and treat it as "a job runs". We keep it:
#       - a HEAVY job holds it alone (class file "heavy");
#       - the LIGHT group holds it jointly: the first light job creates it with
#         class file "light", the second joins without touching it, and when
#         the pid-file owner exits it hands the pid file to the surviving light
#         job (atomic rename); the last light job removes the lock.
#       - so an OLD copy sees the lock taken whenever any new job runs (it is
#         treated like a HEAVY job: it waits for light AND heavy ones), and a new
#         job seeing the lock without class "light" (old-copy owner) waits like
#         for a heavy holder. An old copy can therefore never run beside a new
#         job. Only a tiny window while a crashed light owner is repaired.
#   * Memory guard (light jobs): free+inactive memory >= 4 GB and swap used
#     < 2 GB, else wait (re-checked every poll) and say why.
#   * Stagger: no two Unity processes start within 25 s of each other
#     (Unity's licensing helper hangs when editors start together).
#   * While waiting it also waits for any OTHER Unity -batchmode process (one
#     started without this wrapper) to finish, unless UNITY_BATCH_IGNORE_FOREIGN=1.
#   * With no Unity editor and no other wrapper job running, kills a leftover
#     Unity.Licensing.Client helper (a stale one makes batch runs hang at
#     "Compiling Scripts" or fail with fake "Scripts have compiler errors";
#     Unity relaunches it) and any orphaned Roslyn VBCSCompiler servers.
#   * adds -batchmode -quit, and an absolute -projectPath when missing, and
#     runs Unity from the project folder (tests read Assets/... paths).
#   * samples Unity's resident memory every 2 s and prints the peak.
#   * always cleans up (normal exit, Ctrl-C, kill): stops Unity and its whole
#     helper tree, kills orphaned VBCSCompiler servers (only when no other job
#     runs), and removes only THIS job's ticket, slot and (if owned) lock.
#   * reports how Unity ended: exit code, a signal / crash, crash lines from the
#     log, a macOS crash report, and the log's tail on any failure.
#   * exits with Unity's exit code (75 if it gave up waiting).
#
# Not added: -nographics (a dozen suites read rendered pixels back),
# -buildTarget (switching the Library's target reimports every texture).
#
# Env: UNITY / UNITY_BATCH_UNITY_BIN (editor binary), UNITY_BATCH_LOCKDIR (old
#      lock dir, alias UNITY_BATCH_LOCK), UNITY_BATCH_QUEUEDIR (queue + slots +
#      mutex + stamp), UNITY_BATCH_LOGDIR (alias UNITY_BATCH_LOG_DIR),
#      UNITY_BATCH_TIMEOUT (seconds to wait; 0 = forever, default),
#      UNITY_BATCH_HEAVY=1, UNITY_BATCH_IGNORE_FOREIGN=1, UNITY_BATCH_RSS_LOG,
#      UNITY_BATCH_STAGGER (25), UNITY_BATCH_POLL (5), UNITY_BATCH_NOTE (60),
#      UNITY_BATCH_MIN_FREE_MB (4096), UNITY_BATCH_MAX_SWAP_MB (2048),
#      UNITY_BATCH_FAKE_FREE_MB / UNITY_BATCH_FAKE_SWAP_MB (tests),
#      UNITY_BATCH_SKIP_HELPER_CLEANUP=1 (tests: never touch real helpers).

set -u

UNITY="${UNITY_BATCH_UNITY_BIN:-${UNITY:-/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity}}"
LOCK="${UNITY_BATCH_LOCKDIR:-${UNITY_BATCH_LOCK:-/tmp/pause-unity-batch.lock}}"
QUEUE="${UNITY_BATCH_QUEUEDIR:-/tmp/pause-unity-batch.queue}"
TIMEOUT="${UNITY_BATCH_TIMEOUT:-0}"
IGNORE_FOREIGN="${UNITY_BATCH_IGNORE_FOREIGN:-0}"
LOG_DIR="${UNITY_BATCH_LOGDIR:-${UNITY_BATCH_LOG_DIR:-/tmp/pause-unity-logs}}"
RSS_LOG="${UNITY_BATCH_RSS_LOG:-}"
STAGGER="${UNITY_BATCH_STAGGER:-25}"
POLL="${UNITY_BATCH_POLL:-5}"
NOTE_EVERY="${UNITY_BATCH_NOTE:-60}"
MIN_FREE_MB="${UNITY_BATCH_MIN_FREE_MB:-4096}"
MAX_SWAP_MB="${UNITY_BATCH_MAX_SWAP_MB:-2048}"
SKIP_HELPERS="${UNITY_BATCH_SKIP_HELPER_CLEANUP:-0}"
RUNNING="$QUEUE/running"
MUTEX="$QUEUE/.mutex"
STAMP="$QUEUE/.laststart"

say() { echo "[unity-batch] $*" >&2; }

# ---- arguments ---------------------------------------------------------
args=("$@")
has() { local a; for a in "${args[@]+"${args[@]}"}"; do [ "$a" = "$1" ] && return 0; done; return 1; }

project=""
log=""
method=""
for ((i = 0; i < ${#args[@]}; i++)); do
    next=$((i + 1))
    if [ "${args[$i]}" = "-projectPath" ] && [ $next -lt ${#args[@]} ]; then
        project="$(cd "${args[$next]}" 2>/dev/null && pwd -P)" || { say "no such project: ${args[$next]}"; exit 2; }
        args[$next]="$project"
    elif [ "${args[$i]}" = "-logFile" ] && [ $next -lt ${#args[@]} ]; then
        log="${args[$next]}"
        case "$log" in /*|-) ;; *) log="$(pwd -P)/$log"; args[$next]="$log" ;; esac
    elif [ "${args[$i]}" = "-executeMethod" ] && [ $next -lt ${#args[@]} ]; then
        method="${args[$next]}"
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

# ---- class: light / heavy, by -executeMethod (unknown = heavy) ------------
classify() {
    [ "${UNITY_BATCH_HEAVY:-0}" = 1 ] && { echo heavy; return; }
    case "$method" in
        "") echo heavy ;;
        AllTests.RunAll*|BuildScript.*|AndroidTextureDiet.*|EliteArtSync.*|TextureDiet*) echo heavy ;;
        *Build*|*ApplyAll*|*Sync*) echo heavy ;;
        AllTests.RunSuites*|*Preview*) echo light ;;
        *) echo heavy ;;
    esac
}
class="$(classify)"
say "log: $log"
say "class: $class (${method:-no -executeMethod})"

# ---- processes ---------------------------------------------------------
# Unity editor processes, matched on the executable (a shell that merely
# mentions Unity in its arguments must not count).
unity_pids() { ps -axo pid=,comm= | awk '$2 ~ /\/Unity\.app\/Contents\/MacOS\/Unity$/ { print $1 }'; }
# -batchmode editors NOT started by a live wrapper job (their parent has no slot)
foreign_pids() {
    local p pp
    for p in $(unity_pids); do
        ps -o args= -p "$p" 2>/dev/null | grep -q -- "-batchmode" || continue
        pp="$(ps -o ppid= -p "$p" 2>/dev/null | tr -d ' ')"
        [ -n "$pp" ] && [ -f "$RUNNING/$pp" ] && continue
        echo "$p"
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

# ---- queue state -------------------------------------------------------
mkdir -p "$QUEUE" "$RUNNING" || { say "cannot create $QUEUE"; exit 2; }
run_dir="$(mktemp -d /tmp/pause-unity-run.XXXXXX)"
child=""
sampler=""
nap_pid=""
ticket=""
slot_held=0
why=""
my_pos=0

# A short critical section around every queue/slot/lock decision. Stale
# (dead owner, or ownerless for > 5 s) mutexes are taken over.
mutex_lock() {
    local owner age
    while :; do
        if mkdir "$MUTEX" 2>/dev/null; then echo "$$" > "$MUTEX/pid"; return; fi
        owner="$(cat "$MUTEX/pid" 2>/dev/null)"
        if [ "$owner" = "$$" ]; then return; fi   # re-entrant: a signal arrived inside a critical section
        if [ -z "$owner" ]; then
            age=$(( $(date +%s) - $(stat -f %m "$MUTEX" 2>/dev/null || date +%s) ))
            if [ "$age" -gt 5 ]; then rm -rf "${MUTEX:?}"; continue; fi
        elif ! kill -0 "$owner" 2>/dev/null; then
            rm -rf "${MUTEX:?}"; continue
        fi
        sleep 0.1
    done
}
mutex_unlock() {
    if [ "$(cat "$MUTEX/pid" 2>/dev/null)" = "$$" ]; then rm -rf "${MUTEX:?}"; fi
    return 0
}

nap() { sleep "$1" & nap_pid=$!; wait "$nap_pid" 2>/dev/null; nap_pid=""; }

tickets() { ls "$QUEUE" 2>/dev/null | grep -E '^[0-9]{8}-[0-9]+$' | sort; }
live_slots() {
    local f p
    for f in "$RUNNING"/*; do
        [ -f "$f" ] || continue
        p="${f##*/}"
        if kill -0 "$p" 2>/dev/null; then echo "$p"; else rm -f "${f:?}"; fi
    done
}
other_slots() { live_slots | grep -vx "$$"; }
slot_field() { sed -n "$2p" "$RUNNING/$1" 2>/dev/null; }   # 1 class, 2 project

make_ticket() {   # mutex held: next number = highest existing + 1
    local last n
    last="$(tickets | tail -1)"; last="${last%%-*}"
    n=$((10#${last:-0} + 1))
    ticket="$(printf '%08d-%d' "$n" "$$")"
    printf '%s\n%s\n%s\n%s\n' "$class" "$project" "$(date '+%F %T')" "$log" > "$QUEUE/$ticket"
}

free_mb() {
    if [ -n "${UNITY_BATCH_FAKE_FREE_MB:-}" ]; then echo "$UNITY_BATCH_FAKE_FREE_MB"; return; fi
    vm_stat 2>/dev/null | awk '
        /page size of/ { ps = $8 }
        /^Pages free/ { f = $3 + 0 } /^Pages inactive/ { i = $3 + 0 } /^Pages speculative/ { s = $3 + 0 }
        END { if (ps > 0) printf "%d", (f + i + s) * ps / 1048576; else printf "999999" }'
}
swap_mb() {
    if [ -n "${UNITY_BATCH_FAKE_SWAP_MB:-}" ]; then echo "$UNITY_BATCH_FAKE_SWAP_MB"; return; fi
    sysctl -n vm.swapusage 2>/dev/null | awk '{ for (i = 1; i <= NF; i++) if ($i == "used") { v = $(i + 2); u = substr(v, length(v)); n = v + 0; if (u == "G") n *= 1024; printf "%d", n; exit } }'
}

# Decide (mutex held) whether THIS job may start now; if so register it
# (lock, slot, stamp) and return 0. Otherwise set $why and return 1.
try_start() {
    local t p pos=0 slots nslots=0 holders="" reason="" lp lc other ocls oproj age fr sw last since
    my_pos=0
    for t in $(tickets); do
        p="${t#*-}"
        if ! kill -0 "$p" 2>/dev/null; then rm -f "${QUEUE:?}/${t:?}"; continue; fi   # dead ticket
        pos=$((pos + 1))
        [ "$t" = "$ticket" ] && my_pos=$pos
    done
    if [ "$my_pos" = 0 ]; then make_ticket; why="ticket was purged, re-queued"; return 1; fi

    slots="$(live_slots | tr '\n' ' ')"
    for p in $slots; do nslots=$((nslots + 1)); holders="$holders $p"; done

    # the old-style lock: stale handling
    lp="$(cat "$LOCK/pid" 2>/dev/null)"; lc="$(cat "$LOCK/class" 2>/dev/null)"
    if [ -d "$LOCK" ]; then
        if [ "$lc" = light ] && [ "$nslots" = 0 ]; then
            say "removing stale light lock (no job running)"; rm -rf "${LOCK:?}"
        elif [ -z "$lp" ]; then
            # mid-creation by another script copy, or a crash between mkdir and
            # the pid write: stale once it is older than 10 s
            age=$(( $(date +%s) - $(stat -f %m "$LOCK" 2>/dev/null || date +%s) ))
            if [ "$age" -gt 10 ]; then say "removing ownerless lock"; rm -rf "${LOCK:?}"; fi
        elif ! kill -0 "$lp" 2>/dev/null; then
            if [ "$lc" = light ]; then
                for p in $slots; do
                    echo "$p" > "$LOCK/pid.$$" && mv -f "$LOCK/pid.$$" "$LOCK/pid"
                    say "light lock owner $lp died; handed to pid $p"; break
                done
            else
                say "removing stale lock of dead pid $lp"; rm -rf "${LOCK:?}"
            fi
        fi
        lp="$(cat "$LOCK/pid" 2>/dev/null)"; lc="$(cat "$LOCK/class" 2>/dev/null)"
    fi
    if [ -d "$LOCK" ] && [ -n "$lp" ]; then
        case " $holders " in *" $lp "*) ;; *) holders="$holders $lp" ;; esac
    fi

    if [ "$my_pos" -gt 1 ]; then
        t="$(tickets | head -1)"
        why="position $my_pos, holder pid${holders:- none} (queue head: pid ${t#*-})"; return 1
    fi

    # class rules
    if [ "$class" = heavy ]; then
        if [ "$nslots" -gt 0 ]; then reason="heavy job waits for $nslots running job(s) to drain"
        elif [ -d "$LOCK" ]; then reason="lock held by pid ${lp:-?} ($(head -1 "$LOCK/owner" 2>/dev/null))"; fi
    else
        if [ "$nslots" -ge 2 ]; then reason="both slots busy"
        elif [ "$nslots" = 1 ]; then
            other="${slots%% *}"; ocls="$(slot_field "$other" 1)"; oproj="$(slot_field "$other" 2)"
            if [ "$ocls" != light ]; then reason="pid $other is a $ocls job"
            elif [ "$oproj" = "$project" ]; then reason="pid $other runs the same project"
            elif [ "$lc" != light ]; then reason="lock held by non-light owner (pid ${lp:-?})"; fi
        elif [ -d "$LOCK" ]; then reason="lock held by pid ${lp:-?} ($(head -1 "$LOCK/owner" 2>/dev/null))"; fi
    fi
    if [ -z "$reason" ] && [ "$IGNORE_FOREIGN" != 1 ]; then
        fr="$(foreign_pids | tr '\n' ' ')"
        [ -n "${fr// /}" ] && reason="another Unity batch process is running (pid ${fr% }, not started by this wrapper)"
    fi
    if [ -z "$reason" ] && [ "$STAGGER" -gt 0 ]; then
        last="$(cat "$STAMP" 2>/dev/null)"; last="${last:-0}"
        since=$(( $(date +%s) - last ))
        [ "$since" -lt "$STAGGER" ] && reason="start stagger: previous Unity started ${since}s ago (need ${STAGGER}s)"
    fi
    if [ -z "$reason" ] && [ "$class" = light ]; then
        fr="$(free_mb)"; sw="$(swap_mb)"; fr="${fr:-999999}"; sw="${sw:-0}"
        if [ "$fr" -lt "$MIN_FREE_MB" ]; then reason="memory guard: only ${fr} MB free+inactive (need ${MIN_FREE_MB} MB)"
        elif [ "$sw" -ge "$MAX_SWAP_MB" ]; then reason="memory guard: swap used ${sw} MB (limit ${MAX_SWAP_MB} MB)"; fi
    fi
    if [ -n "$reason" ]; then
        why="position $my_pos, holder pid${holders:- none}: $reason"; return 1
    fi

    # take the old-style lock (heavy: alone; light: first creates, second joins)
    if ! [ -d "$LOCK" ]; then
        if mkdir "$LOCK" 2>/dev/null; then
            echo "$class" > "$LOCK/class"
            echo "$$" > "$LOCK/pid"
            printf '%s\n%s\n%s\n' "$project" "$(date '+%F %T')" "$log" > "$LOCK/owner"
        else
            why="position $my_pos, holder pid${holders:- none}: lock just taken by an older script copy"; return 1
        fi
    fi
    printf '%s\n%s\n%s\n%s\n' "$class" "$project" "$log" "$(date '+%F %T')" > "$RUNNING/$$"
    slot_held=1
    rm -f "${QUEUE:?}/${ticket:?}"
    date +%s > "$STAMP"
    return 0
}

# Remove only THIS job's ticket and slot; hand over or drop the old lock.
release() {
    mutex_lock
    [ -n "$ticket" ] && rm -f "${QUEUE:?}/${ticket:?}"
    rm -f "${RUNNING:?}/$$"
    if [ "$slot_held" = 1 ]; then
        local lp lc nxt=""
        lp="$(cat "$LOCK/pid" 2>/dev/null)"; lc="$(cat "$LOCK/class" 2>/dev/null)"
        if [ "$lp" = "$$" ]; then
            if [ "$class" = light ]; then
                nxt="$(other_slots | head -1)"
                if [ -n "$nxt" ]; then
                    echo "$nxt" > "$LOCK/pid.$$" && mv -f "$LOCK/pid.$$" "$LOCK/pid"
                else rm -rf "${LOCK:?}"; fi
            else
                rm -rf "${LOCK:?}"
            fi
        elif [ "$lc" = light ] && [ -z "$(live_slots)" ]; then
            rm -rf "${LOCK:?}"
        fi
    fi
    slot_held=0
    mutex_unlock
}

cleanup() {
    [ -n "$nap_pid" ] && kill "$nap_pid" 2>/dev/null
    nap_pid=""
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
    # orphaned compilers: only when nothing else (editor or wrapper job) runs
    if [ "$slot_held" = 1 ] && [ "$SKIP_HELPERS" != 1 ] && [ -z "$(unity_pids)" ] && [ -z "$(other_slots)" ]; then
        local orphans; orphans="$(orphan_compilers)"
        [ -n "$orphans" ] && kill_all $orphans
    fi
    release
    rm -rf "${run_dir:?}"
}
trap cleanup EXIT
trap 'say "interrupted"; exit 130' INT
trap 'say "terminated"; exit 143' TERM HUP

# ---- queue: ticket, then wait for our turn -------------------------------
mutex_lock; make_ticket; mutex_unlock
started=$(date +%s)
last_note=0
last_pos=0
while :; do
    mutex_lock; try_start; rc=$?; mutex_unlock
    [ "$rc" = 0 ] && break
    now=$(date +%s)
    if [ "$TIMEOUT" != 0 ] && [ $((now - started)) -ge "$TIMEOUT" ]; then
        say "gave up after ${TIMEOUT}s: $why"; exit 75
    fi
    if [ $((now - last_note)) -ge "$NOTE_EVERY" ] || [ "$my_pos" != "$last_pos" ]; then
        say "waiting: $why"; last_note=$now; last_pos=$my_pos
    fi
    nap "$POLL"
done
waited=$(( $(date +%s) - started ))
[ "$waited" -gt 5 ] && say "got a $class slot after ${waited}s"

# ---- stale helpers -----------------------------------------------------
# Only with no Unity editor at all and no other wrapper job (two may run now).
if [ "$SKIP_HELPERS" != 1 ] && [ -z "$(unity_pids)" ] && [ -z "$(other_slots)" ]; then
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
echo "$ran" > "$STAMP"
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
