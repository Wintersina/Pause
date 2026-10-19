# Unity tests on GitHub Actions

`.github/workflows/unity-tests.yml` runs the project's own test runner
(`AllTests.RunAll`, see `Pause/Assets/Editor/Tests/AllTests.cs`) on every PR to
`master` and every push to `master`, so it does not have to run on a dev Mac.

## How it works

- Image: `unityci/editor:ubuntu-6000.3.23f1-android-3.2.2` (GameCI, exact editor version; the android variant because `BuildScript.cs` references the Android build-support assemblies, so the base image fails to compile
  from `ProjectVersion.txt`). Bump the `UNITY_IMAGE_TAG` env when the project's Unity version changes.
- The editor is launched directly in the container:
  `xvfb-run Unity -batchmode -quit -force-glcore -projectPath Pause -executeMethod AllTests.RunAll`.
  Pass/fail is the editor exit code (0 pass, 1 fail) plus the `[ALL] RESULT: PASS|FAIL` log line;
  the job requires both.
- Graphics suites: a dozen suites read rendered pixels back, so `-nographics` is not used.
  Instead Xvfb plus Mesa software GL (llvmpipe) provides a GL context. If software GL turns out
  not to work for some suite, dispatch the workflow manually with `graphics = skip`: the job then
  derives the graphics suites (sources containing `Camera.Render`, `Graphics.Blit`, `ReadPixels`
  or `RenderTexture`), logs the excluded list, and runs the rest. Excluded suites still run locally
  (`make test`).
- `Pause/Library` is cached (key: ProjectVersion + `Packages/manifest.json` + hash of all
  `Assets/**/*.meta`, with fallbacks to older keys), so only changed assets re-import. Every shard
  restores it (`actions/cache/restore`); shard 0 alone saves it (`actions/cache/save`, `if: always()`),
  so it is stored even when tests fail, and only when the exact key was not already restored.
- Fork PRs have no secrets and are skipped with a notice. Draft PRs are not tested; mark the PR ready
  for review to run the tests.

## Sharding, selection and concurrency

Jobs: `plan` (a few seconds) -> `test` matrix (shards in parallel) -> `AllTests` (aggregate).

- `AllTests` is the stable check name. It fails if planning failed or any shard did not succeed.
- `plan` runs `scripts/tools/ci_plan.py`, which picks the suites, then packs them longest-first into
  duration-balanced shards using `scripts/ci-suite-times.txt` (seconds per suite from a full run; suites
  missing from it count 8 s). Re-measure it occasionally from the `[ALL] <suite>: PASS (12.3s ...)` log lines.
  The shard count is `min(max shards, suites, ceil(estimated seconds / 480))`, because each shard pays
  roughly 10 minutes of editor start and import. Each shard runs `AllTests.RunSuites -suites ...`.
- What runs:
  - pull request: the changed files (GitHub API, `pulls/N/files`, renames included) are mapped to suites
    by `scripts/tools/affected_tests.py` (a test file maps to its suites; other `.cs` to every test file
    that mentions a type it declares; art/audio/data to the tests that mention the asset or its folder),
    plus a smoke set (`AchievementCatalogTest,WorldLogicTest,RowHeartsTest,ColumnSpeedTest`).
  - the FULL suite instead when the change touches this workflow, the planner or its tables, `AllTests.cs`,
    `TestHarness*.cs`, `ProjectSettings/`, `Packages/` or an `.asmdef`, when more than 45 % of the suites
    are selected, or when the file list cannot be read. Files nothing maps to (new art no test mentions)
    run only the smoke set on the PR; the push to master afterwards runs everything.
  - push to master and `workflow_dispatch`: the full suite, sharded.
  - dispatch inputs: `suites` (exact list, still sharded), `graphics` (`skip` drops pixel-reading suites),
    `shards` (max shard count).
  - the chosen suites per shard are written to the job summary of `plan`.
- Concurrency: one group per PR with `cancel-in-progress` (a newer push cancels that PR's older run only);
  master pushes and manual runs are never cancelled. There is no repo-wide seat queue any more.
- Serial fallback: set the repository variable `UNITY_CI_SHARDS=1` (Settings > Secrets and variables >
  Actions > Variables) or dispatch with `shards = 1`. That runs one shard and puts every run back on the
  single global `unity-personal-seat` group (no cancelling). Use it if Unity refuses parallel activations.
- Licensing with parallel shards: GameCI documents that concurrent jobs need extra seats but that this is
  not an issue for free (Personal) licenses. Each shard is its own runner VM with its own container HOME,
  so licensing state is never shared; shards start their login 20 s apart; each logs in exactly once and
  returns its seat on exit. Unknown: whether `--deactivate-all` in one shard also invalidates an
  entitlement another shard is using (it is a client-side return of that machine's seat as far as the
  client documents). If a shard dies with a licensing error while others finish, switch to the serial
  fallback and note it here.
- Skipped on purpose: `BossLadderBotTest` no longer re-runs the old-table baseline (info only) unless
  `PAUSE_LADDER_BASELINE=1`; `BossWaveTest`'s pause/slow-motion stress runs 60 s instead of 90 s.

## One-time setup (repo admin)

Unity removed manual (.ulf) activation for Personal accounts, so CI activates the Personal seat
with the account login through the Unity Licensing Client
(`Unity.Licensing.Client --activate-all --include-personal --username ... --password ...`,
the approach of game-ci/cli PR #246; game-ci/unity-test-runner@v4 does not support it yet).

Add two repository secrets (Settings > Secrets and variables > Actions, or `gh secret set NAME`):

1. `UNITY_EMAIL` - the Unity account email.
2. `UNITY_PASSWORD` - its password.

Notes:
- Use an account without 2FA / device verification; headless activation cannot answer a challenge.
  A dedicated CI-only Unity account is recommended.
- Personal activation is entitlement based (no .ulf), so the seat is returned with `--deactivate-all` (one extra login per run), done by an EXIT trap in the container, with an `if: always()` backstop step that only acts if activation succeeded but deactivation did not complete. Both are non-fatal. Activation is attempted exactly once per run, never retried.
- The flags are undocumented by Unity and may change; they live in one place in the workflow.
- Secrets are masked and passed to the container as environment variables, never echoed.

Without the secrets the job fails fast with a message listing the missing ones.

## First run

Expect a long first run per shard: the container is pulled (~4 GB), the ~3 GB of assets
are imported from scratch, and the Library cache is saved. Later runs restore the cache and only
re-import what changed. Caches are scoped per branch with fallback to master's, so a push to master
after merging warms the cache for every PR.

## Reading results

- Job summary: the `[ALL]` totals and any failed suites.
- Artifacts `unity-log-shard-N`: each shard's full editor log (search `[ALL]` and `FAIL`) and its suite list.
- Manual run: Actions > Unity tests > Run workflow; optionally `suites` (e.g. `ShopTest,CodexTest`) and `graphics`.
