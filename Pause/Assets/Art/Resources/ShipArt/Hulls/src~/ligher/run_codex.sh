#!/bin/zsh
# usage: run_codex.sh <brief name without .md> <ref image names (in codex/) ...>
# per-ship lock (parallel restyle agents), jobs never overlap within Ligher
W=/Users/sina/Developer/Pause/.claude/worktrees/agent-aa7f3d662ca2ad32f
C=$W/Pause/Assets/Art/Resources/ShipArt/Hulls/src~/ligher/codex
LOCK=/tmp/pause-codex-ligher.lock.d
while ! mkdir $LOCK 2>/dev/null; do sleep 20; done
trap "rmdir $LOCK" EXIT
B=$1; shift
ARGS=()
for i in "$@"; do ARGS+=(-i "$C/$i"); done
~/.local/bin/codex exec -m gpt-5.6-luna --skip-git-repo-check -s workspace-write -C "$W" -c model_reasoning_effort='"high"' "${ARGS[@]}" -o "$C/last_$B.txt" - < "$C/briefs/$B.md" > "$C/log_$B.txt" 2>&1
