#!/bin/bash
# worktree.sh -- small jj workspaces for Pause: clone a warm Unity Library into
# new workspaces (APFS copy-on-write), and slim / delete them when a job ends.
# See docs/worktrees.md. Never rewrites history; never touches the default
# workspace's files.
#
#   scripts/worktree.sh add <name> [base]      new workspace + cloned Library   (make worktree)
#   scripts/worktree.sh library <name>         clone the warm Library into an existing workspace (make worktree-library)
#   scripts/worktree.sh warm [from]            refresh the warm Library copy    (make warm-library)
#   scripts/worktree.sh slim <name>            delete only Pause/Library + Pause/Temp (make worktree-slim)
#   scripts/worktree.sh done <name>            forget + delete the workspace    (make worktree-done)
#   scripts/worktree.sh list                   path, disk, merge state, Unity   (make worktree-list)
#
# Env:
#   WARM_LIBRARY   warm Library copy (default ~/Developer/Pause-warm-library/Library)
#   WORKTREES_DIR  where new workspaces go (default <default workspace>/../Pause-worktrees)
#   FORCE=1        done: delete even with unmerged work / uncommitted edits
#   FETCH=0        add: skip `jj git fetch`
#   DU=0           list: skip the (slow) disk-use column
set -euo pipefail

export PATH="/opt/homebrew/bin:$PATH"   # jj git fetch needs Homebrew git

WARM_LIBRARY="${WARM_LIBRARY:-$HOME/Developer/Pause-warm-library/Library}"
FORCE="${FORCE:-0}"
FETCH="${FETCH:-1}"
DU="${DU:-1}"

die() { echo "worktree: $*" >&2; exit 1; }
say() { echo "worktree: $*"; }
JJ() { jj --no-pager "$@"; }

# --- workspace lookup -------------------------------------------------------

# "name<TAB>root" for every jj workspace (read-only: no snapshot of anyone).
workspaces() {
  JJ --ignore-working-copy workspace list -T 'name ++ "\t" ++ root ++ "\n"'
}
ws_root() { workspaces | awk -F'\t' -v n="$1" '$1 == n { print $2 }'; }
default_root() { ws_root default; }

valid_name() {
  [[ "$1" =~ ^[A-Za-z0-9._-]+$ ]] || die "bad workspace name '$1' (letters, digits, . _ - only)"
  [ "$1" != default ] || die "refusing to operate on the default workspace"
}

# --- disk helpers -----------------------------------------------------------

free_kb() { df -k "$1" | awk 'NR == 2 { print $4 }'; }
dev_of() { stat -f %d "$1"; }
human_kb() { awk -v k="$1" 'BEGIN { s = (k < 0) ? "-" : ""; k = (k < 0) ? -k : k;
  if (k >= 1048576) printf "%s%.2f GB", s, k / 1048576; else printf "%s%.0f MB", s, k / 1024 }'; }

# --- Unity detection --------------------------------------------------------

# Prints a reason and returns 0 when a Unity process has <project dir> open
# (a Unity whose command line names it via -projectPath, a Unity whose cwd is
# it, or anyone holding its Temp/UnityLockfile).
unity_using() {
  local proj="$1" pid cwd cmd lproj
  proj="$(cd "$proj" 2>/dev/null && pwd -P)" || return 1
  lproj="$(printf '%s' "$proj" | tr '[:upper:]' '[:lower:]')"   # APFS is case-insensitive
  while read -r pid; do
    [ -n "$pid" ] || continue
    cmd="$(ps -o command= -p "$pid" 2>/dev/null | tr '[:upper:]' '[:lower:]') "
    # the path as a whole argument (followed by a space or a slash), not a prefix of a longer name
    if [[ "$cmd" == *"$lproj "* || "$cmd" == *"$lproj/ "* ]]; then
      echo "Unity pid $pid has -projectPath $proj"; return 0
    fi
    cwd="$(lsof -a -p "$pid" -d cwd -Fn 2>/dev/null | sed -n 's/^n//p' | head -1)"
    if [ -n "$cwd" ] && { [ "$cwd" = "$proj" ] || [[ "$cwd" == "$proj"/* ]]; }; then
      echo "Unity pid $pid runs in $cwd"; return 0
    fi
  done < <(pgrep -x Unity 2>/dev/null; pgrep -f 'Unity.app/Contents/MacOS/Unity' 2>/dev/null)
  if [ -e "$proj/Temp/UnityLockfile" ]; then
    pid="$(lsof -t "$proj/Temp/UnityLockfile" 2>/dev/null | head -1 || true)"
    if [ -n "$pid" ]; then echo "pid $pid holds $proj/Temp/UnityLockfile"; return 0; fi
  fi
  return 1
}

# --- cloning ----------------------------------------------------------------

# clone_library <src Library> <dest Library>: APFS clonefile copy, verified by
# the volume's free space (du would count every clone in full).
clone_library() {
  local src="$1" dest="$2" before after used
  [ -d "$src" ] || die "no Library at $src"
  [ ! -e "$dest" ] || die "$dest already exists; not overwriting it"
  mkdir -p "$(dirname "$dest")"
  [ "$(dev_of "$src")" = "$(dev_of "$(dirname "$dest")")" ] ||
    die "$src and $(dirname "$dest") are on different volumes; a clone is impossible (a real copy would cost ~16 GB)"
  before="$(free_kb "$(dirname "$dest")")"
  local t0=$SECONDS
  # cp -c uses clonefile(2) and silently falls back to a real copy where it
  # cannot, hence the free-space check below. cp only reads the source.
  cp -cR "$src" "$dest"
  after="$(free_kb "$(dirname "$dest")")"
  used=$((before - after))
  say "cloned $src -> $dest in $((SECONDS - t0)) s; volume free space $(human_kb "$before") -> $(human_kb "$after") (real cost $(human_kb "$used"))"
  if [ "$used" -gt $((2 * 1048576)) ]; then
    echo "worktree: WARNING: the clone used $(human_kb "$used") of real space; clonefile was probably not used (or other disk activity overlapped)" >&2
  fi
}

# --- commands ---------------------------------------------------------------

cmd_add() {
  local name="${1:-}" base="${2:-master}" droot dir
  [ -n "$name" ] || die "usage: make worktree NAME=<name> [BASE=master]"
  valid_name "$name"
  droot="$(default_root)"
  dir="${WORKTREES_DIR:-$(dirname "$droot")/Pause-worktrees}/$name"
  [ -z "$(ws_root "$name")" ] || die "a workspace named '$name' already exists at $(ws_root "$name")"
  [ ! -e "$dir" ] || die "$dir already exists"
  if [ "$FETCH" = 1 ]; then JJ git fetch; fi
  mkdir -p "$(dirname "$dir")"
  JJ workspace add --name "$name" -r "$base" "$dir"
  library_into "$dir"
  say "ready: $dir  (cd there; when the job ends: make worktree-slim NAME=$name or make worktree-done NAME=$name)"
}

# Clone the warm Library into <workspace dir>/Pause/Library.
library_into() {
  local dir="$1" dest="$1/Pause/Library" reason
  if [ -e "$dest" ]; then die "$dest already exists; delete it first (make worktree-slim) to re-clone"; fi
  if [ ! -d "$WARM_LIBRARY" ]; then
    say "no warm Library at $WARM_LIBRARY: leaving Pause/Library empty, so the first Unity run imports cold (~4 min+)."
    say "create one with: make warm-library [FROM=<workspace with a fresh Library>]"
    return 0
  fi
  if [ -e "$WARM_LIBRARY.refreshing" ]; then
    say "the warm Library is being refreshed right now ($WARM_LIBRARY.refreshing): leaving Pause/Library empty; rerun make worktree-library NAME=<name> later"
    return 0
  fi
  if reason="$(unity_using "$(dirname "$WARM_LIBRARY")")"; then
    die "warm Library is open in Unity ($reason); not cloning a half-written Library"
  fi
  clone_library "$WARM_LIBRARY" "$dest"
  # The source's IL post-processor pid means nothing here (docs/build-speed.md).
  rm -f "$dest/ilpp.pid"
}

cmd_library() {
  local name="${1:-}" dir
  [ -n "$name" ] || die "usage: make worktree-library NAME=<name>"
  valid_name "$name"
  dir="$(ws_root "$name")"; [ -n "$dir" ] || die "no workspace named '$name'"
  [ -d "$WARM_LIBRARY" ] || die "no warm Library at $WARM_LIBRARY (make warm-library FROM=...)"
  library_into "$dir"
}

# Newest Library among workspaces (by ArtifactDB mtime) that no Unity has open.
freshest_library_ws() {
  local best="" best_t=0 name root t
  while IFS=$'\t' read -r name root; do
    [ -f "$root/Pause/Library/ArtifactDB" ] || continue
    unity_using "$root/Pause" >/dev/null && continue
    t="$(stat -f %m "$root/Pause/Library/ArtifactDB")"
    if [ "$t" -gt "$best_t" ]; then best_t="$t"; best="$name"; fi
  done < <(workspaces)
  echo "$best"
}

cmd_warm() {
  local from="${1:-}" root src reason warm_parent tmp
  if [ -z "$from" ]; then
    from="$(freshest_library_ws)"
    [ -n "$from" ] || die "no idle workspace has a Pause/Library; pass FROM=<workspace or path>"
    say "FROM not given: using '$from', the workspace whose Library was written most recently"
  fi
  if [ -d "$from/Pause/Library" ]; then root="$(cd "$from" && pwd -P)"
  else root="$(ws_root "$from")"; [ -n "$root" ] || die "'$from' is neither a workspace name nor a checkout path"; fi
  src="$root/Pause/Library"
  [ -f "$src/ArtifactDB" ] || die "$src is not an imported Unity Library"
  if reason="$(unity_using "$root/Pause")"; then
    die "$reason; close it (or wait for the job) so the Library is not cloned half-written"
  fi
  warm_parent="$(dirname "$WARM_LIBRARY")"
  mkdir -p "$warm_parent"
  [ ! -e "$WARM_LIBRARY.refreshing" ] || die "another refresh is running ($WARM_LIBRARY.refreshing); delete that marker if it is stale"
  echo "$$ from $src $(date '+%F %T')" > "$WARM_LIBRARY.refreshing"
  trap 'rm -f "$WARM_LIBRARY.refreshing"' EXIT
  tmp="$WARM_LIBRARY.new.$$"
  clone_library "$src" "$tmp"
  rm -f "$tmp/ilpp.pid"
  if [ -d "$WARM_LIBRARY" ]; then
    mv "$WARM_LIBRARY" "$WARM_LIBRARY.old.$$"
    mv "$tmp" "$WARM_LIBRARY"
    rm -rf "$WARM_LIBRARY.old.$$"
  else
    mv "$tmp" "$WARM_LIBRARY"
  fi
  echo "from $src ($from) at $(date '+%F %T')" > "$warm_parent/SOURCE.txt"
  say "warm Library refreshed: $WARM_LIBRARY (from $src)"
}

cmd_slim() {
  local name="${1:-}" dir reason before
  [ -n "$name" ] || die "usage: make worktree-slim NAME=<name>"
  valid_name "$name"
  dir="$(ws_root "$name")"; [ -n "$dir" ] || die "no workspace named '$name'"
  if reason="$(unity_using "$dir/Pause")"; then die "$reason; close it first"; fi
  before="$(free_kb "$dir")"
  rm -rf "$dir/Pause/Library" "$dir/Pause/Temp"
  say "slimmed $dir (Library + Temp gone, checkout kept); freed $(human_kb $(( $(free_kb "$dir") - before )))"
}

# Unmerged non-empty commits of a workspace's line of work, relative to trunk.
unmerged_revset() { echo "(::\"$1\"@ ~ ::trunk()) ~ empty() ~ root()"; }

cmd_done() {
  local name="${1:-}" dir here reason n before
  [ -n "$name" ] || die "usage: make worktree-done NAME=<name> [FORCE=1]"
  valid_name "$name"
  dir="$(ws_root "$name")"; [ -n "$dir" ] || die "no workspace named '$name'"
  case "$(default_root)/" in "$dir"/*) die "refusing: $dir is (or contains) the default workspace" ;; esac
  [ "$dir" != / ] && [ "$dir" != "$HOME" ] || die "refusing to delete $dir"
  here="$(pwd -P)"
  if [ "$here" = "$dir" ] || [[ "$here" == "$dir"/* ]]; then die "run this from another workspace, not from inside $dir"; fi
  if reason="$(unity_using "$dir/Pause")"; then die "$reason; close it first (FORCE does not override this)"; fi
  if [ -d "$dir/.jj" ]; then
    # Snapshot the workspace's on-disk edits into its working-copy commit so
    # the check below sees uncommitted work too.
    if ! (cd "$dir" && JJ status >/dev/null 2>&1); then
      [ "$FORCE" = 1 ] || die "could not snapshot $dir (stale or broken working copy?); inspect it, or FORCE=1"
    fi
  fi
  n="$(JJ --ignore-working-copy log --no-graph -r "$(unmerged_revset "$name")" -T 'change_id.short() ++ "\n"' | grep -c . || true)"
  if [ "$n" -gt 0 ]; then
    echo "worktree: '$name' has $n non-empty change(s) not in trunk():" >&2
    JJ --ignore-working-copy log -r "$(unmerged_revset "$name")" -T builtin_log_oneline >&2
    [ "$FORCE" = 1 ] || die "refusing to delete unmerged work; merge it or rerun with FORCE=1 (the commits stay in the repo either way, but the files on disk go)"
    say "FORCE=1: deleting anyway (the commits above stay reachable in jj)"
  fi
  before="$(free_kb "$(dirname "$dir")")"
  JJ workspace forget "$name"
  rm -rf "$dir"
  say "forgot workspace '$name' and deleted $dir; freed $(human_kb $(( $(free_kb "$(dirname "$dir")") - before )))"
}

cmd_list() {
  local name root size state wc unity n
  printf '%-22s %-9s %-40s %-7s %s\n' WORKSPACE DISK STATE UNITY PATH
  while IFS=$'\t' read -r name root; do
    size="-"
    if [ "$DU" = 1 ] && [ -d "$root" ]; then
      # default: skip .claude/, which holds other workspaces (counted on their own rows)
      if [ "$name" = default ]; then size="$(du -sk -I .claude "$root" 2>/dev/null | awk '{ print $1 }')"
      else size="$(du -sk "$root" 2>/dev/null | awk '{ print $1 }')"; fi
      size="$(human_kb "${size:-0}")"
    fi
    [ -d "$root" ] || size="missing"
    n="$(JJ --ignore-working-copy log --no-graph -r "$(unmerged_revset "$name")" -T 'change_id.short() ++ "\n"' 2>/dev/null | grep -c . || true)"
    wc="$(JJ --ignore-working-copy log --no-graph -r "\"$name\"@" -T 'if(empty, "", "wc-edits")' 2>/dev/null || true)"
    if [ "$n" -eq 0 ]; then
      if JJ --ignore-working-copy log --no-graph -r "\"$name\"@- & ::trunk()" -T '"x"' 2>/dev/null | grep -q x; then state="merged/empty (on trunk)"; else state="empty (base off trunk)"; fi
    else
      state="UNMERGED: $n non-empty${wc:+, $wc}"
    fi
    unity="-"; unity_using "$root/Pause" >/dev/null 2>&1 && unity="IN USE"
    [ "$name" = default ] && state="$state (default, kept)"
    printf '%-22s %-9s %-40s %-7s %s\n' "$name" "$size" "$state" "$unity" "$root"
  done < <(workspaces)
  if [ -d "$WARM_LIBRARY" ]; then
    printf '%-22s %-9s %-40s %-7s %s\n' "(warm library)" "$( [ "$DU" = 1 ] && human_kb "$(du -sk "$WARM_LIBRARY" | awk '{ print $1 }')" || echo -)" "$(cat "$(dirname "$WARM_LIBRARY")/SOURCE.txt" 2>/dev/null | cut -c1-40)" "-" "$WARM_LIBRARY"
  fi
  echo
  echo "DISK is logical size (du). APFS clones share blocks, so a cloned Library costs far less real"
  echo "space than shown and the column sums to more than the disk holds. State reads each workspace's"
  echo "last snapshot (no snapshot is taken here), so edits since its last jj command are not seen."
  echo "Volume free: $(human_kb "$(free_kb "$(default_root)")")"
}

case "${1:-}" in
  add) shift; cmd_add "$@" ;;
  library) shift; cmd_library "$@" ;;
  warm) shift; cmd_warm "$@" ;;
  slim) shift; cmd_slim "$@" ;;
  done) shift; cmd_done "$@" ;;
  list) shift; cmd_list "$@" ;;
  *) sed -n '2,20p' "$0" >&2; exit 2 ;;
esac
