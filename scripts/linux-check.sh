#!/usr/bin/env bash
set -eu
ldconfig -p | grep -E 'SDL2|fontconfig|X11|ICE|SM' || true
command -v xdg-open || true
command -v apt-get || true
ls /mnt/wslg
