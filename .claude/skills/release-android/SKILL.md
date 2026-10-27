---
name: release-android
description: Cut a Google Play release of Pause end to end -- clean old workspaces, fetch latest master, bump AndroidBundleVersionCode via a PR, build the signed release AAB from a clean master workspace, verify it, stage it in Finder, and open the Play Console release page in Chrome (stopping before "Send for review"). Use when the user says "cut a release", "ship to Play", "new AAB", "bump and upload", "release-android". Coordinator playbook: main chat coordinates, heavy steps go to Sonnet subagents.
---

# Release Android (Play)

Ordered playbook for the coordinator (main chat). Run it only when the user invoked it.
Do not ask questions unless a step says so. Delegate the heavy steps (clean-up audit,
build, verify) to subagents spawned with `model: sonnet`; the main chat decides and reports.

## Hard rules

- Never build from, or touch, the main checkout `/Users/sina/Developer/Pause` working copy
  (Codex's uncommitted edits live there; they must never leak into a release or be
  stashed/committed). Read-only access to its `Pause/PauseKey.keystore` is fine.
- Load `jj-sensei:wisdom` before any VCS command. jj only, no raw git for commits/pushes.
  jj needs homebrew git: prefix with `PATH=/opt/homebrew/bin:$PATH` (memory: jj-needs-homebrew-git).
- No Claude attribution lines in commits or PR bodies (memory: no-claude-attribution).
- Merging is pre-approved ONLY for the version-bump PR (step 3). Never merge anything else.
- Tests run on CI only, never locally. One Unity process at a time.
- Never print, log, echo or commit keystore passwords. Never build or upload a debug-signed AAB
  (`Pause-DEBUGSIGNED.*`).
- Never click "Send for review" / "Start rollout" in the Play Console.

## 0. Preconditions

```bash
pgrep -fl Unity            # nothing, except the Android emulator qemu, is fine
df -h /Users/sina/Developer   # need ~40 GB free (a fresh Library import is ~9 GB)
ls /Applications/Unity/Hub/Editor/   # must contain 6000.3.23f1
ls /Users/sina/Developer/Pause/Pause/PauseKey.keystore /Users/sina/Developer/keystore.txt
```
Record the `df -h` as "before". If Unity is running, wait or stop; if disk is short, step 1 may fix it.

## 1. Clean previous working trees (before anything else)

Subagent (sonnet) task; it reports a table, main chat approves nothing extra (rules below are final).

```bash
export PATH=/opt/homebrew/bin:$PATH
cd /Users/sina/Developer/Pause
jj --no-pager git fetch
jj --no-pager workspace list
git worktree list                       # read-only listing is fine
ls -d .claude/ws-* .claude/worktrees/agent-* .claude/release-* .claude/rel* /Users/sina/Developer/Pause-* 2>/dev/null
```
For each candidate, find its branch/bookmark and decide:
- `gh pr list --repo Wintersina/Pause --head <branch> --state all --json number,state,mergedAt`
  must show MERGED (or the branch tip is an ancestor of master:
  `jj --no-pager log -r '<tip> & ::master'` non-empty), AND
- its working copy is clean: `jj --no-pager diff --stat` run from inside that workspace shows
  nothing except `.meta` files, and nothing unpushed (no commits outside `::master` that are
  not on a pushed bookmark).

Remove only those:
- jj workspace: `make worktree-done NAME=<name>` (refuses unmerged/dirty ones; `make worktree-list`
  shows them all), or by hand from the main repo dir `jj --no-pager workspace forget <name>`, then `rm -rf <dir>`.
- git worktree (e.g. `.claude/worktrees/agent-*`): `rm -rf <dir>` then `git worktree prune`.

NEVER remove: the main checkout, anything named `codex-*` (Codex's worktrees), workspaces of
open PRs, dirty or unmerged ones, and the workspace you are currently running in (if any).
Do not delete remote branches (report merged ones only). Keep one warm `Library` donor if the
only clean one would otherwise vanish? No: step 2 clones Library from the main checkout's
last build or any idle workspace, so pick the donor BEFORE deleting (`ls -d */Pause/Library`,
`du -sh`), and remove the donor last or keep it.

Report to the user: removed list, skipped list with the reason for each, `df -h` before/after.

## 2. Cut from latest master, in a clean workspace

```bash
export PATH=/opt/homebrew/bin:$PATH
cd /Users/sina/Developer/Pause
jj --no-pager git fetch
jj --no-pager log -r 'master' -n 1 -T builtin_log_oneline --no-graph    # note the commit
gh run list --repo Wintersina/Pause --branch master --limit 3             # latest must be success
```
If CI on master is red or running, tell the user and stop (or wait if it is running).
Then a fresh workspace, in its own step cd into it:
```bash
jj --no-pager workspace add /Users/sina/Developer/Pause/.claude/ws-release-<code> -r master
```
Speed up the Unity import (~9 GB cold): `make worktree-library NAME=ws-release-<code>` clones the
warm Library (docs/worktrees.md; APFS clone, instant, no extra disk), or `cp -Rc <donor>/Pause/Library
<new>/Pause/Library` from an idle clean workspace. Never copy anything else from the main checkout except the keystore path
reference. Lighter alternative used before: a tarball export of master (`.claude/rel10008/src.tgz`)
plus the Library clone. Do not make additional full repo copies.

## 3. Bump the version code

Read `AndroidBundleVersionCode` in `Pause/ProjectSettings/ProjectSettings.asset` (was 10008 after
PR #213; `bundleVersion` 1.0.0; iOS buildNumber is NOT bumped per release). Ask the user ONE
question only if unknown: "What is the highest version code already uploaded to Play?" (or read it
in Play Console > Test and release > App bundle explorer). If current code <= uploaded code, set
code = uploaded + 1; if the current number is not yet uploaded, no bump is needed, skip to step 4.

Do the bump in the release workspace:
```bash
jj --no-pager bookmark create chore/version-code-<N> -r @     # after editing the one line
jj --no-pager describe -m "Bump Android version code to <N> (<N-1> shipped to Play)"
jj --no-pager git push --bookmark chore/version-code-<N>
gh pr create --repo Wintersina/Pause --base master --head chore/version-code-<N> \
  --title "Bump Android version code to <N>" --body "Version code <N> for the next Play upload."
```
`jj --no-pager diff --stat` must show only ProjectSettings.asset. If jj push is blocked by the
sandbox, fall back to the GitHub API for that ONE file (`gh api -X PUT repos/Wintersina/Pause/contents/...`
on a new branch, as done for PR #213), never the whole tree. Wait for CI green
(`gh pr checks <n> --watch`), then merge (pre-approved for this PR only):
`gh pr merge <n> --merge`. Then `jj git fetch` and `jj new master` in the release workspace so the
build includes the bump. Confirm the merged value with `grep AndroidBundleVersionCode`.

## 4. Build the signed AAB

Method: `BuildScript.BuildAndroidRelease` (Pause/Assets/Editor/Build/BuildScript.cs); also `make android-release`.
Output: `Pause/Builds/Android/Release/Pause.aab` plus `*.symbols.zip`. Signed only when
`PAUSE_KEYSTORE_PASS`, `PAUSE_KEYALIAS_PASS` are set and the keystore exists; alias is `pausealias`.
Keystore: `PAUSE_KEYSTORE_PATH=/Users/sina/Developer/Pause/Pause/PauseKey.keystore` (the workspace has none; it is gitignored).

Subagent (sonnet) runs, from the release workspace, in a way that never echoes secrets:
```bash
cd /Users/sina/Developer/Pause/.claude/ws-release-<code>
# read the passwords from /Users/sina/Developer/keystore.txt into env vars WITHOUT printing them
# (open the file only inside the shell, e.g. via a small awk/read; do not cat it into the transcript)
export PAUSE_KEYSTORE_PATH=/Users/sina/Developer/Pause/Pause/PauseKey.keystore
PAUSE_KEYSTORE_PASS=... PAUSE_KEYALIAS_PASS=... \
  scripts/unity-batch.sh -projectPath "$PWD/Pause" -buildTarget Android -executeMethod BuildScript.BuildAndroidRelease
```
Incremental ~340 s; cold import adds a long time. The wrapper prints its log path; check for
`[BUILD] release signing with` (not "DEBUG-signed") and the absence of `error CS`.

Pitfalls:
- Hang or fake compiler errors with no `error CS` line: kill the stale `Unity.Licensing.Client`
  helper (`pkill -f Licensing.Client`), rerun (memory: unity-licensing-hang). If the queue is stuck, see unity-queue-swap-deadlock; report to the user.
- One Unity at a time; never run tests locally.
- Keep androidx.fragment forced to 1.6.2 (Pause/Assets/Editor/AndroidxFragmentDependencies.xml) and
  the kotlin-bom line in `mainTemplate.gradle`; do not remove either.
- Never build with the debug key; if the output is `Pause-DEBUGSIGNED.aab`, the passwords/keystore were missing: fix and rebuild.

## 5. Verify the AAB (subagent, sonnet)

Tools: `unzip` always; `aapt2`/`bundletool` live in the Android SDK build-tools or the Unity
AndroidPlayer folder (`find /Applications/Unity -name 'aapt2*'`); `keytool` ships with the JDK in Unity.
```bash
AAB=Pause/Builds/Android/Release/Pause.aab
ls -l "$AAB"; unzip -l "$AAB" | grep -E "BundleConfig|base/manifest/AndroidManifest.xml|dependencies.pb"
unzip -p "$AAB" base/manifest/AndroidManifest.xml | strings | grep -E "me\.hapticgate|permission"   # rough
# proper: aapt2 dump xmltree --file base/manifest/AndroidManifest.xml ... (after unzip -o to a scratch dir),
# or: java -jar bundletool.jar dump manifest --bundle "$AAB"
unzip -p "$AAB" BUNDLE-METADATA/com.android.tools.build.libraries/dependencies.pb | strings | grep -i "androidx.fragment"
keytool -printcert -jarfile "$AAB" | grep -E "SHA1|SHA256"
```
Checks (all must pass; report pass/fail):
- package `me.hapticgate.pause`; versionCode == the bumped number; versionName `1.0.0`.
- permissions: INTERNET, ACCESS_NETWORK_STATE, `com.android.vending.BILLING` (intentional, pre-registration reward). Flag `AD_ID` or anything else.
- no `androidx.fragment` 1.1.0 in dependencies.pb (1.6.2 expected).
- signing SHA-1 printed. Remind the user: the Play Games OAuth client must use the Play App Signing SHA-1 (App signing page), not only this upload key.
- size: Play's base-module download limit warning is 200 MB; last AAB ~385 MB. Report the size.

Stage the artifact:
```bash
mkdir -p /Users/sina/Developer/Pause-releases/<code>
cp Pause/Builds/Android/Release/Pause.aab Pause/Builds/Android/Release/*.symbols.zip /Users/sina/Developer/Pause-releases/<code>/
open -R /Users/sina/Developer/Pause-releases/<code>/Pause.aab
```

## 6. Open Chrome for the upload

Load `claude-in-chrome` first (one ToolSearch for the whole tool set). Use the user's signed-in Chrome,
a new tab. HapticGate Play account, app dashboard:
`https://play.google.com/console/u/1/developers/8173525466058968827/app/4974749408591263517/app-dashboard`
Find the track of the most recent release: Test and release > Testing > Closed testing (the 12 testers
for 14 days plan) or Internal testing; or Production if the user says so. Ask the user which track only
if it is ambiguous. Navigate to that track's "Create new release" page
(`.../tracks/<id>/releases/<n>/prepare` after clicking "Create new release"; do not guess ids, click through).

The Chrome file_upload tool only takes files under 10 MB, so the agent cannot upload the AAB. Leave the
page ready and tell the user to drag the .aab (and optionally symbols.zip under "native debug symbols")
from the open Finder window into the "App bundles" box. Put the release name and notes on the clipboard:
```bash
printf '%s (1.0.0)' <code> | pbcopy      # release name; then offer the notes (step 7) the same way
```
Or fill them into the form after the user confirms the upload finished; then check the review summary.
Open extra tabs when relevant: App bundle explorer, and Setup > App signing (for the SHA-1 reminder).
STOP before "Next/Save" turns into "Send for review" / "Start rollout": never click the final button; tell the user.
Chrome rules: no JS alert/confirm/prompt dialogs; after 2-3 failed attempts at one action, stop and ask.

## 7. Release notes

Find the previous release boundary: the last merged version bump,
`jj --no-pager log -r 'description(glob:"*version code*") & ::master' -n 3 -T builtin_log_oneline --no-graph`
(convention: `chore/version-code-10007 | Bump Android version code to 10007 (10006 shipped to Play)`).
Collect merged PR titles after the previous shipped bump:
`gh pr list --repo Wintersina/Pause --state merged --search "merged:>=<date>" --limit 100 --json number,title`.
Rewrite as short, player-facing bullets (no internal jargon: drop CI, refactor, tests, version bumps),
at most 500 characters per language, in Play's tag format:
```
<en-US>
- New: ...
- Fixed: ...
</en-US>
```
Show the draft to the user, then clipboard it (`pbcopy`) or paste it into the form.

## 8. Final report

- Version: `<code> (1.0.0)`, master commit built, bump PR link.
- AAB path `/Users/sina/Developer/Pause-releases/<code>/Pause.aab`, symbols path, size.
- Verify table (package, versionCode, permissions, fragment, SHA-1, size).
- Clean-up result (removed/skipped, disk before/after).
- Left for the user: drag the AAB into the open page, check release notes, press Send for review, and the Play App Signing SHA-1 reminder for Play Games.
