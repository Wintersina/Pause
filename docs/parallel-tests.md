# Parallel test runner

`scripts/run-tests-parallel.sh` runs the whole `AllTests` suite as N shards in N Unity
editors at once and prints one summary. Same pass/fail results as `AllTests.RunAll`.

```
scripts/run-tests-parallel.sh                  # gate lane, 2 shards (the machine's UNITY_BATCH_MAX_JOBS default)
UNITY_BATCH_MAX_JOBS=3 scripts/run-tests-parallel.sh -j 3
scripts/run-tests-parallel.sh --full           # exhaustive lane (sets PAUSE_TEST_FULL=1 for the editors)
scripts/run-tests-parallel.sh --suites ShopTest,CodexTest
scripts/run-tests-parallel.sh --plan           # show the shard split, run nothing
scripts/run-tests-parallel.sh --clean          # delete the shard copies afterwards (default: keep, they hold the reusable Library)
```

Exit code 0 only when every suite ran and passed. `-j` is clamped to `UNITY_BATCH_MAX_JOBS`
(default 2); the queue's memory / swap guards (`UNITY_BATCH_MIN_FREE_MB`,
`UNITY_BATCH_MAX_SWAP_MB`) apply to every shard unchanged, so a busy machine makes shards wait
instead of crash. Each editor needs 2-3 GB.

## How it works

1. **Suite list**: parsed from the `Suites` table in `AllTests.cs` (`("Name", Class.Method),`
   lines). A suite added or split there is picked up automatically.
2. **Balancing**: durations come from `scripts/test-durations.txt` (gate lane) or
   `scripts/test-durations-full.txt` (`--full`), `Suite seconds` per line. Suites missing from the
   seed take the newest full-run log's time in `/tmp/pause-unity-logs`, else the median. Suites are
   handed out longest first to the least-loaded shard. `--plan` prints the result.
3. **Shard copies**: `/tmp/pause-shards/shard<i>/Pause`, one project per editor (Unity cannot open a
   project twice). The first run clones this workspace's `Pause/Library` with APFS `cp -Rc` (instant,
   no extra space until written); every run then rsyncs `Assets`, `ProjectSettings` and `Packages` over
   it (the Library is reused, so only changed assets reimport). The repo's other top-level folders
   (`docs`, `scripts`, ...) are synced next to it: tests read `../docs/...` and `../../...`.
4. **Run**: each shard goes through `scripts/unity-batch.sh -projectPath <shard> -executeMethod
   AllTests.RunSuites -suites <list>` at the same time. Logs: `/tmp/pause-shards/runs/<stamp>/shard<i>.log`.
5. **Aggregate**: the `[ALL] Suite: PASS|FAIL` lines are merged. The summary lists failed suites with
   their `[TAG] FAIL ...` check lines, suites with no result line, per-shard wall time and total wall
   time. A suite with no result (editor crashed or was killed) is rerun once, alone, in the same shard.
6. **Seed refresh**: after an all-PASS run the seed file is rewritten from the measured times (commit
   it now and then; `--no-refresh` skips it).

## Shared state

Editors run side by side, so anything outside the editor's own project folder is shared:

* **PlayerPrefs** live in `~/Library/Preferences/unity.<company>.<product>.plist`. Every shard gets a
  private `productName` (`Pause-shard<i>`) in its copy of `ProjectSettings.asset`, hence its own plist;
  the real `unity.WinterPeace.Pause.plist` is never touched by a sharded run.
  `TestHarness.Sandbox` still restores the prefs it changed inside a shard.
* **Files**: never write to a fixed path outside the project (`/tmp/foo`, the home folder, the main
  checkout). Use `Application.dataPath`-relative paths (`Temp/`, `../Temp`) or a path built from the
  process id. Preview dumps behind an env var (`PAUSE_DOCK_PREVIEW_DIR`, ...) are fine: they are off in
  test runs.
* **Repo files read by tests** (`../docs/...`) must be among the folders synced into the shard root
  (everything at the repo top level except `Pause` and `HapticGateFramesV2`). A test needing another
  folder must be added to `prep_shard` in the script.
* No fixed ports, no `EditorPrefs`, no `SessionState` for suite state; no wall-clock thresholds tighter
  than ~2x the unloaded time (editors share the CPU): use frame counts / simulated time.
* Allocation meters (`TestHarness.AllocatedBytes`) take the smallest of several readings so stray
  background allocations under load do not fail "allocates nothing" checks.
* Suites must not depend on running after another suite: a shard holds an arbitrary subset in table order.

## Tests of the runner

`scripts/tests/run-tests-parallel-test.sh` checks suite parsing, balancing, log parsing and `--plan`
without starting Unity.

## Lanes and timing notes

* `AttackBotAbTest` (dodge-bot reference-vs-fast equivalence, ~43 s) runs only in the full lane (`PAUSE_TEST_FULL=1`); in the gate lane it is registered but returns at once.
* `ScreenFitScreens.All` is split into `ScreenFitTest1..4` at screen ids (`ScreenFitTest.SliceStarts`), by measured time, not count. The `[FITTIME]` log lines give each screen's seconds.
* Balancing: shard s starts with (s-1) x the start stagger as load (editors start one stagger apart; the runner sets `UNITY_BATCH_STAGGER` to 12 s unless you set it).
* The summary marks a shard INCOMPLETE only when it has suites without a result line.
* Steady-state fixed cost per shard: ~4-7 s editor startup (2 domain reloads, ~3 s asset refresh when nothing changed) plus the start stagger; a script change adds a 6-14 s compile per shard.
