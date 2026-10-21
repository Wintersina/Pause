#!/bin/zsh
# usage: runcodex.sh <workdir> <prompt-file> <last-msg-file> [image refs...]
# one codex at a time: mkdir lock shared by every run of this script
LOCK=/tmp/pause-codex.lock.d
while ! mkdir $LOCK 2>/dev/null; do sleep 20; done
trap "rmdir $LOCK" EXIT
WD=$1; P=$2; OUT=$3; shift 3
ARGS=()
for i in "$@"; do ARGS+=(-i "$i"); done
~/.local/bin/codex exec -m gpt-5.6-luna --skip-git-repo-check -s workspace-write -C "$WD" -c model_reasoning_effort='"high"' "${ARGS[@]}" -o "$OUT" - < "$P"
