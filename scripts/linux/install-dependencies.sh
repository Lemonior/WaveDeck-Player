#!/usr/bin/env bash
set -eu
if [ "$(uname -m)" != x86_64 ]; then printf '%s\n' 'This WaveDeck build requires Linux x86_64.'; exit 1; fi
if command -v apt-get >/dev/null; then
    sudo apt-get update
    icu_package="$(apt-cache search --names-only '^libicu[0-9]+$' | awk '{print $1}' | sort -V | tail -n 1)"
    ssl_package=libssl3
    if apt-cache show libssl3t64 >/dev/null 2>&1; then ssl_package=libssl3t64; fi
    sudo apt-get install -y ffmpeg libsdl2-2.0-0 libfontconfig1 libice6 libsm6 libx11-6 libxkbcommon0 xdg-utils "$icu_package" "$ssl_package"
elif command -v dnf >/dev/null; then
    sudo dnf install -y ffmpeg-free SDL2 fontconfig libICE libSM libX11 libxkbcommon libicu xdg-utils
elif command -v pacman >/dev/null; then
    sudo pacman -S --needed ffmpeg sdl2 fontconfig libice libsm libx11 libxkbcommon icu xdg-utils
else
    printf '%s\n' 'Install FFmpeg (with ffprobe), SDL2, fontconfig, ICU, OpenSSL, X11, libICE/libSM, libxkbcommon and xdg-utils using your distribution package manager.'
    exit 1
fi
printf '%s\n' 'Dependencies installed. Launch with: bash launch.sh'
