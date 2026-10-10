# unity-batch.sh: queue, slots and the lock

`scripts/unity-batch.sh` runs every headless Unity job on this Mac. It keeps
the machine inside its 18 GB: a full `AllTests.RunAll` peaks near 2 GB, a small
focused run near 0.8 GB, Android builds and texture-import jobs much more.

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
   - light jobs only: free+inactive(+speculative) memory >= 4 GB and swap used
     < 2 GB (`vm_stat`, `sysctl vm.swapusage`).
4. **Start.** The job takes the old lock (below), writes its slot file
   `<queue>/running/<pid>`, removes its ticket and launches Unity.

While waiting it prints `waiting: position N, holder pid ...: <reason>` when
the position changes and at least every 60 s. It re-checks every 5 s.

## Classes

Chosen from the `-executeMethod` argument; anything unknown is HEAVY.

| class | methods |
|-------|---------|
| LIGHT | `AllTests.RunSuites*`, anything containing `Preview` |
| HEAVY | `AllTests.RunAll*`, `BuildScript.*`, `AndroidTextureDiet.*`, `EliteArtSync.*`, `TextureDiet*`, any name containing `Build`, `ApplyAll` or `Sync`, no `-executeMethod`, anything unlisted, or env `UNITY_BATCH_HEAVY=1` |

Heavy patterns win over light ones. A HEAVY job starts only when no job runs
and nothing starts while it runs. A LIGHT job runs beside at most ONE other job,
which must be LIGHT on a DIFFERENT projectPath (Unity cannot open a project
twice; two light jobs on the same project serialise). Two concurrent jobs
therefore need two worktrees.

## Compatibility with old copies

Other worktrees may still hold the old script, which only knows the mkdir lock
`/tmp/pause-unity-batch.lock` (`pid` and `owner` files). The new script keeps
using that dir:

- HEAVY job: creates it alone (`class` = `heavy`, `pid` = job pid).
- First LIGHT job: creates it with `class` = `light`. The second LIGHT job
  joins without touching it. When the owner (`pid`) exits and another light job
  still runs, the `pid` file is atomically replaced by the survivor's pid; the
  last light job removes the dir. A light lock with no live slot is stale.
- An old copy sees the lock held by a live pid whenever any new job runs, so
  it waits like for a heavy job and can never run beside a new job. A new job
  that finds the lock without `class` = `light` (old-copy holder) waits too.
- Remaining window: if a light lock owner is SIGKILLed, the `pid` file is dead
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
| `UNITY_BATCH_STAGGER` | 25 | min seconds between Unity starts |
| `UNITY_BATCH_POLL` / `UNITY_BATCH_NOTE` | 5 / 60 | poll interval / status line interval |
| `UNITY_BATCH_MIN_FREE_MB` / `UNITY_BATCH_MAX_SWAP_MB` | 4096 / 2048 | memory guard |
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
Unity binary in private temp dirs (about 100 s; it never starts Unity and never
touches the live lock or queue).
