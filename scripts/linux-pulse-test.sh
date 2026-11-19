#!/usr/bin/env bash
set -eu
cd /mnt/d/MusicPlayer
export SDL_AUDIODRIVER=pulseaudio
set +e
WaveDeck-Linux/WaveDeck --self-test --data-dir /tmp/wavedeck-pulse-validation-1.0.0
test_result=$?
set -e
cp /tmp/wavedeck-pulse-validation-1.0.0/results.json artifacts/1.0.0-linux-pulse-results.json
cp /tmp/wavedeck-pulse-validation-1.0.0/environment.json artifacts/1.0.0-linux-pulse-environment.json
exit "$test_result"
