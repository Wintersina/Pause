#!/bin/bash
# run_codex.sh -- launch ONE headless Codex art job in its own worktree + branch.
#
#   .claude/skills/add-world/scripts/run_codex.sh <job-name> <prompt.md> [ref-image ...]
#
# Creates /Users/sina/Developer/Pause/.claude/worktrees/codex-<job-name> on branch art/<job-name> from the
# current HEAD of the CALLER'S branch (merge/rebase onto master first), then runs
#
#   codex exec -C <worktree> -s workspace-write -c model_reasoning_effort='"high"' \
#              [-i ref ...] -o <scratch>/codex_<job>_last.txt - < prompt.md > <scratch>/codex_<job>.log 2>&1
#
# in the BACKGROUND (the prompt is on stdin; every Codex job needs `high` effort and an explicit
# "use image generation" line in the prompt). Prints the worktree, log and last-message paths.
# Codex cannot be messaged mid-run: if a job is off, let it finish or kill it and relaunch a narrower
# prompt - but LOOK AT THE OUTPUT FOLDER before calling a run stuck (a "stuck" run had already written its files).
# At most 2 Codex jobs at once. Codex shares the usage limit of the user's ChatGPT login: a limit message in the
# log means "wait for the reset", not "retry".
set -eu
job="${1:?job name}"; prompt="${2:?prompt file}"; shift 2
repo="${PAUSE_REPO:-/Users/sina/Developer/Pause}"
scratch="${SCRATCH:-/private/tmp/claude-501/-Users-sina-Developer-Pause/scratch}"
mkdir -p "$scratch"
wt="$repo/.claude/worktrees/codex-$job"
if [ ! -d "$wt" ]; then git -C "$repo" worktree add "$wt" -b "art/$job" "${BASE:-master}"; fi
refs=()
for r in "$@"; do refs+=(-i "$r"); done
nohup codex exec -C "$wt" -s workspace-write -c model_reasoning_effort='"high"' "${refs[@]+"${refs[@]}"}" \
      -o "$scratch/codex_${job}_last.txt" - < "$prompt" > "$scratch/codex_${job}.log" 2>&1 &
echo "worktree: $wt"; echo "branch:   art/$job"; echo "log:      $scratch/codex_${job}.log"; echo "last msg: $scratch/codex_${job}_last.txt"; echo "pid:      $!"
