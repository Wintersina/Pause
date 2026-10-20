# unity-batch.sh: queue, slots and the lock

`scripts/unity-batch.sh` runs every headless Unity job on this Mac. It keeps
the machine inside its 18 GB: a full `AllTests.RunAll` peaks near 2 GB, a small
focused run near 0.8 GB, Android builds and texture-import jobs much more.
Test-style (MEDIUM) jobs run up to `UNITY_BATCH_MAX_JOBS` at once. The default
is **1**: one Unity at a time (each editor costs 1.5-2 GB plus ~400 MB for the
compiler on an 18 GB Mac). Raise it only on a roomier machine; builds run solo.
CI runs the suites sharded (see `ci-unity-tests.md`); local runs are for MP4 captures and one-off checks.

## How a job gets to run

1. **Ticket.** Every invocation creates `<queue>/<8-digit seq>-<pid>`
   (default queue dir `/tmp/pause-unity-batch.queue`). The sequence number is
   highest-existing + 1, taken under a short mutex (`<queue>/.mutex`, a mkdir
   lock with stale takeover), so the order is strict FIFO. The file holds
   class, project, time and log path.
2. **Turn.** A job may start only when its ticket is the first live ticket.
   Nobody overtakes anybody, so a waiting heavy job is never starved by later
   light jobs. Tickets of dead pids are purged by whoever looks next.
3. **Fit.** The head job starts only when all of these hold:
   - class rules (below);
   - no foreign Unity `-batchmode` process (one not started by a wrapper)
     is running, unless `UNITY_BATCH_IGNORE_FOREIGN=1`;
   - the last Unity start was at least 25 s ago (`<queue>/.laststart`), because
     the licensing helper hangs when two editors start together;
   - medium jobs only, the memory guard: free+inactive(+speculative) memory
     >= `MIN_FREE_MB` (4 GB) is the real gate. Swap used (`sysctl vm.swapusage`)
     is a soft signal: it must be < `MAX_SWAP_MB` (4 GB) unless free+inactive is
     already >= 2x `MIN_FREE_MB` (8 GB), in which case swap is ignored. macOS
     keeps GBs in swap long after pressure ends; the old hard 2 GB swap limit
     deadlocked the queue several times with 6+ GB of RAM free.
4. **Start.** The job takes the old lock (below), writes its slot file
   `<queue>/running/<pid>`, removes its ticket and launches Unity.

While waiting it prints `waiting: position N, holder pid ...: <reason>` when
the position changes and at least every 60 s. It re-checks every 5 s.

## Classes

Chosen from the `-executeMethod` argument; anything unknown is HEAVY.

| class | methods |
|-------|---------|
| MEDIUM | `AllTests.Run*` (`RunAll`, `RunSuites`, `RunFast`), anything containing `Preview` |
| HEAVY (solo) | `BuildScript.*`, `AndroidTextureDiet.*`, `EliteArtSync.*`, `TextureDiet*`, any name containing `Build`, `ApplyAll` or `Sync`, no `-executeMethod`, anything unlisted, or env `UNITY_BATCH_HEAVY=1` |

Heavy patterns win over medium ones. A HEAVY job starts only when no job runs
and nothing starts while it runs. A MEDIUM job starts when fewer than
`UNITY_BATCH_MAX_JOBS` jobs run, all of them MEDIUM on projectPaths different
from its own (Unity cannot open a project twice; same-project jobs serialise).
N concurrent jobs therefore need N worktrees. Strict FIFO still holds: a
blocked head job (a build waiting to drain, a same-project job) holds back
everyone behind it, so a build is never starved. The 25 s start stagger and the
memory guard apply to every start.

## Compatibility with old copies

Other worktrees may still hold the old script, which only knows the mkdir lock
`/tmp/pause-unity-batch.lock` (`pid` and `owner` files). The new script keeps
using that dir:

- HEAVY job: creates it alone (`class` = `heavy`, `pid` = job pid).
- First MEDIUM job: creates it with `class` = `medium`. Further MEDIUM jobs
  (up to N) join without touching it. When the owner (`pid`) exits and other
  medium jobs still run, the `pid` file is atomically replaced by a survivor's
  pid; the last medium job removes the dir. A medium lock with no live slot is
  stale.
- An old copy sees the lock held by a live pid whenever any new job runs, so
  it waits like for a heavy job and can never run beside a new job. A new job
  that finds the lock without `class` = `medium` (old-copy holder) waits too.
- Remaining window: if a medium lock owner is SIGKILLed, the `pid` file is dead
  until the next waiting job repoints it to the survivor (within one poll); an
  old copy polling in that instant could take it over.

## Environment

| variable | default | meaning |
|----------|---------|---------|
| `UNITY` / `UNITY_BATCH_UNITY_BIN` | Unity 6000.3.23f1 | editor binary |
| `UNITY_BATCH_LOCKDIR` (alias `UNITY_BATCH_LOCK`) | `/tmp/pause-unity-batch.lock` | old-style lock dir |
| `UNITY_BATCH_QUEUEDIR` | `/tmp/pause-unity-batch.queue` | tickets, `running/`, `.mutex`, `.laststart` |
| `UNITY_BATCH_LOGDIR` (alias `UNITY_BATCH_LOG_DIR`) | `/tmp/pause-unity-logs` | per-job log files |
| `UNITY_BATCH_TIMEOUT` | 0 (forever) | seconds to wait; then exit 75 |
| `UNITY_BATCH_HEAVY` | unset | `1` forces HEAVY |
| `UNITY_BATCH_IGNORE_FOREIGN` | 0 | `1` skips waiting for foreign batch editors |
| `UNITY_BATCH_MAX_JOBS` | 1 | concurrent MEDIUM jobs (each on its own project); builds are always solo |
| `UNITY_BATCH_STAGGER` | 25 | min seconds between Unity starts |
| `UNITY_BATCH_POLL` / `UNITY_BATCH_NOTE` | 5 / 60 | poll interval / status line interval |
| `UNITY_BATCH_MIN_FREE_MB` / `UNITY_BATCH_MAX_SWAP_MB` | 4096 / 4096 | memory guard (swap limit ignored while free+inactive >= 2x min) |
| `UNITY_BATCH_FAKE_FREE_MB` / `UNITY_BATCH_FAKE_SWAP_MB` | unset | fake the readings (tests) |
| `UNITY_BATCH_SKIP_HELPER_CLEANUP` | 0 | `1` never kills licensing/compiler helpers (tests) |
| `UNITY_BATCH_RSS_LOG` | unset | file for the 2 s memory samples |

Licensing.Client and orphan VBCSCompiler cleanup run only when no Unity editor
and no other wrapper job is running.

## Debugging a stuck queue

```sh
ls -l /tmp/pause-unity-batch.queue            # tickets, oldest first
for f in /tmp/pause-unity-batch.queue/[0-9]*; do echo "$f"; cat "$f"; done
ls /tmp/pause-unity-batch.queue/running       # running jobs (file name = wrapper pid)
cat /tmp/pause-unity-batch.queue/running/*    # class, project, log, start
cat /tmp/pause-unity-batch.lock/{class,pid,owner}
cat /tmp/pause-unity-batch.queue/.laststart; date +%s   # stagger
ps -p <pid> -o pid,etime,args                 # is a holder alive?
ls -dt /tmp/pause-unity-logs/* | head         # newest logs
```

The head job's waiting line names the reason (slots, stagger, memory guard,
foreign editor, old-lock holder). Dead pids are cleaned automatically; to
force it by hand remove the dead ticket / `running/<pid>` file / lock dir.
If the whole thing is wedged, `rm -rf /tmp/pause-unity-batch.queue
/tmp/pause-unity-batch.lock` is safe when no job is running.

## Tests

`scripts/tests/unity-batch-lock-test.sh` runs the real script against a fake
Unity binary in private temp dirs (a few minutes; it never starts Unity and never
touches the live lock or queue).
