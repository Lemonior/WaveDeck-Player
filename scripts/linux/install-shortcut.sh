#!/usr/bin/env bash
set -eu
app_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
data_dir="${XDG_DATA_HOME:-$HOME/.local/share}"
mkdir -p "$data_dir/applications"
chmod +x "$app_dir/WaveDeck" "$app_dir/tools/yt-dlp" "$app_dir/tools/deno"
escaped_dir="$(printf '%s' "$app_dir" | sed 's/\\/\\\\/g; s/"/\\"/g; s/`/\\`/g; s/\$/\\$/g')"
cat > "$data_dir/applications/wavedeck.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=WaveDeck
Comment=Classic music, new frequencies
Exec="$escaped_dir/WaveDeck" %F
Icon=$app_dir/WaveDeck.png
Terminal=false
Categories=AudioVideo;Audio;Player;
StartupWMClass=WaveDeck
EOF
printf '%s\n' 'WaveDeck is now in the applications menu. Keep this application folder in place.'
