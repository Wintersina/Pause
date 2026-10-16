#!/bin/bash
# Sleep until the image-generation cap resets (epoch 1792190536 + 6 min), then run the resumable queue.
cd "$(dirname "$0")"
T=1792190896
while [ "$(date +%s)" -lt "$T" ]; do sleep 60; done
exec /private/tmp/claude-501/-Users-sina-Developer-Pause/4bec0c4f-4fa4-46dd-8c13-2a797152b684/scratchpad/venv/bin/python3 run_queue.py
