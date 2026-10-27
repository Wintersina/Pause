# Worktrees (jj workspaces)

A jj workspace checkout is ~3.8 GB of tracked files. What made workspaces huge
was the Unity `Pause/Library` (16-20 GB) that every workspace imported on its
own. `.git` / `.jj` are shared and cost nothing per workspace.

Now a new workspace gets an APFS copy-on-write clone of one warm Library, which
costs ~15 MB of real disk and opens in Unity without a full import.

    make worktree NAME=<n> [BASE=master]   # ../Pause-worktrees/<n> + cloned Library
    make worktree-list                     # every workspace: disk, merge state, Unity in use
    make worktree-slim NAME=<n>            # delete only Pause/Library + Pause/Temp
    make worktree-done NAME=<n> [FORCE=1]  # jj workspace forget + delete the folder
    make warm-library [FROM=<workspace>]   # refresh the warm Library
    make worktree-library NAME=<n>         # clone the warm Library into an existing workspace

Rule: **slim or delete your worktree when the job ends.** `worktree-done` when
the work is merged (or pushed and you will not build it again), `worktree-slim`
when the checkout must stay but Unity will not run there for a while.

All of it is `scripts/worktree.sh`. It never rewrites history and never
touches the default workspace.

## make worktree

1. `jj git fetch` (skip with `FETCH=0`), then
   `jj workspace add --name <n> -r <BASE> <default root>/../Pause-worktrees/<n>`
   (`WORKTREES_DIR=` overrides the parent folder).
2. `cp -cR <warm>/Library <n>/Pause/Library` (clonefile), then deletes the
   clone's `ilpp.pid` (the source's IL post-processor pid).

It checks the source and destination are on the same volume, and measures the
volume's free space before and after (`du` counts every clone in full, so it
cannot show the saving). It warns if the clone cost more than 2 GB, which means
clonefile was not used. It refuses if the name or folder already exists, or if
a Unity process has the warm copy open. With no warm Library it leaves
`Pause/Library` empty and says so; the first Unity run then imports cold.

## The warm Library

Default: `~/Developer/Pause-warm-library/Library` (`WARM_LIBRARY=` overrides).
Unity never opens it; it only serves as the clone source, so it is never
half-written. `SOURCE.txt` next to it says where and when it came from.

`make warm-library FROM=<workspace name or checkout path>` re-clones it from a
workspace whose Library is fresh. Without `FROM` it picks the workspace whose
`Library/ArtifactDB` was written most recently and that no Unity has open. It
refuses while Unity has the source project open (a `-projectPath` match, a
Unity running inside it, or a holder of `Temp/UnityLockfile`), only reads the
source, and builds the new copy beside the old one before swapping it in.

Refresh it after big asset changes land on master (new world art, a Unity
upgrade), otherwise new clones import the difference on first open (seconds
to a minute).

The warm copy shares blocks with the workspace it came from; once that
workspace is deleted the warm copy owns those blocks (one Library, ~16 GB),
which is the only full Library that has to exist.

## make worktree-done / worktree-slim / worktree-list

`worktree-done` snapshots the workspace (`jj status` inside it), then refuses if
`(::<n>@ ~ ::trunk()) ~ empty()` is non-empty, i.e. it has unmerged non-empty
commits or uncommitted edits, unless `FORCE=1`. With `FORCE=1` the commits stay
in jj (only the files on disk go). It always refuses the default workspace,
the workspace you run it from, and one Unity has open (FORCE does not override
that). Then `jj workspace forget <n>` and `rm -rf` the folder.

`worktree-slim` deletes only `Pause/Library` and `Pause/Temp` (refuses while
Unity has it open). `make worktree-library NAME=<n>` re-clones later.

`worktree-list` prints per workspace: logical size (`du`, slow; `DU=0` skips
it), merge state against `trunk()`, `IN USE` if Unity has it open, and path.
The size is logical: clones share blocks, so the column overstates real use and
sums to more than the disk holds. It reads each workspace's last snapshot
without taking one, so edits made since that workspace's last jj command are
not seen (`worktree-done` does snapshot).

## Measured (2026-10-27, this Mac)

| Step | Real disk | Time |
|---|---|---|
| `make warm-library` (16 GB Library) | 16 MB | 5 s |
| `make worktree NAME=wt-test` (checkout + Library clone) | 3.8 GB checkout + 15 MB Library | 20 s incl. fetch |
| First Unity batch open of the clone | | 16 s Unity (1 asset imported, scripts recompiled 7.9 s) vs ~173 s for a cold asset import ([build-speed.md](build-speed.md)) |
| `make worktree-done NAME=wt-test` | frees 3.9 GB | 2 s |
