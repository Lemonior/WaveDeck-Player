#!/usr/bin/env bash
set -eu
cd /mnt/d/MusicPlayer
chmod +x WaveDeck-Linux/WaveDeck WaveDeck-Linux/tools/yt-dlp WaveDeck-Linux/tools/deno
export SDL_AUDIODRIVER=dummy
set +e
WaveDeck-Linux/WaveDeck --self-test --data-dir /tmp/wavedeck-native-validation-1.0.0
test_result=$?
set -e
mkdir -p artifacts/1.0.0-linux-self
cp /tmp/wavedeck-native-validation-1.0.0/results.json /tmp/wavedeck-native-validation-1.0.0/environment.json artifacts/1.0.0-linux-self/
if [ "$test_result" -ne 0 ]; then exit "$test_result"; fi
WaveDeck-Linux/WaveDeck --smoke-test --data-dir /mnt/d/MusicPlayer/artifacts/1.0.0-linux-ui
