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
  `Assets/**/*.meta`, with fallbacks to older keys), so only changed assets re-import.
- Superseded runs on the same PR are cancelled. Timeout 120 min. Fork PRs have no secrets and are skipped
  with a notice.

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

Expect a long first run (roughly 40-90 min): the container is pulled (~4 GB), the ~3 GB of assets
are imported from scratch, and the Library cache is saved. Later runs restore the cache and only
re-import what changed. Caches are scoped per branch with fallback to master's, so a push to master
after merging warms the cache for every PR.

## Reading results

- Job summary: the `[ALL]` totals and any failed suites.
- Artifact `unity-log`: the full editor log (search `[ALL]` and `FAIL`), plus the list of graphics suites.
- Manual run: Actions > Unity tests > Run workflow; optionally `suites` (e.g. `ShopTest,CodexTest`) and `graphics`.
