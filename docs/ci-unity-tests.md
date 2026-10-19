# Unity tests on GitHub Actions

`.github/workflows/unity-tests.yml` runs the project's own test runner
(`AllTests.RunAll`, see `Pause/Assets/Editor/Tests/AllTests.cs`) on every PR to
`master` and every push to `master`, so it does not have to run on a dev Mac.

## How it works

- Image: `unityci/editor:ubuntu-6000.3.23f1-base-3.2.2` (GameCI, exact editor version
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

Add three repository secrets (Settings > Secrets and variables > Actions > New repository secret),
or with `gh secret set NAME`:

1. `UNITY_EMAIL` - the Unity account email.
2. `UNITY_PASSWORD` - its password.
3. `UNITY_LICENSE` - the full contents of a Unity 6 Personal license file (`.ulf`).

Where to find a `.ulf`:

- macOS: `/Library/Application Support/Unity/Unity_lic.ulf` (Unity 6 / Hub licensing; may not exist
  if you use the newer entitlement-based licensing).
- Linux: `~/.local/share/unity3d/Unity/Unity_lic.ulf`; Windows: `C:\ProgramData\Unity\Unity_lic.ulf`.

If you do not have one (Unity 6 Personal), follow GameCI's activation guide
(https://game.ci/docs/github/activation): run the "Acquire activation file" flow with the same image
to get a `.alf`, upload it at https://license.unity3d.com/manual, choose Unity Personal, and download the
`.ulf`. Paste its whole contents into the `UNITY_LICENSE` secret.
Note: Personal licenses are tied to the activating machine type; if Unity rejects it in CI, the log
(artifact `unity-log`) says so near the top.

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
