#!/usr/bin/env bash
set -eu
app_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
chmod +x "$app_dir/WaveDeck" "$app_dir/tools/yt-dlp" "$app_dir/tools/deno"
if ! command -v ffmpeg >/dev/null || ! command -v ffprobe >/dev/null; then
    printf '%s\n' 'FFmpeg is required. Run: bash install-dependencies.sh'
    exit 1
fi
exec "$app_dir/WaveDeck" "$@"
