#!/usr/bin/env bash
set -euo pipefail

package_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
app_executable="$package_dir/LaunchPad.Ignite.App"
data_home="${XDG_DATA_HOME:-$HOME/.local/share}"
applications_dir="$data_home/applications"
desktop_entry="$applications_dir/io.github.redheaddev.launchpadignite.desktop"
previous_desktop_entry="$applications_dir/io.github.redheaddev.LaunchPad.Ignite.desktop"

if [[ ! -x "$app_executable" ]]; then
  echo "LaunchPad.Ignite executable not found or not executable: $app_executable" >&2
  exit 1
fi

escape_desktop_exec_argument() {
  local value="$1"
  value="${value//\\/\\\\}"
  value="${value//\"/\\\"}"
  value="${value//\`/\\\`}"
  value="${value//\$/\\\$}"
  value="${value//%/%%}"
  printf '%s' "$value"
}

mkdir -p "$applications_dir"
temporary_entry="$(mktemp "$applications_dir/.launchpad-ignite.XXXXXX")"
trap 'rm -f "$temporary_entry"' EXIT
escaped_executable="$(escape_desktop_exec_argument "$app_executable")"
icon_name="io.github.redheaddev.launchpadignite"
icon_sources=(
  "$package_dir/$icon_name.png"
  "$package_dir/share/icons/hicolor/256x256/apps/$icon_name.png"
)
icon_theme_dir="$HOME/.local/share/icons/hicolor/256x256/apps"
mkdir -p "$icon_theme_dir"
for icon_source in "${icon_sources[@]}"; do
  if [[ -f "$icon_source" ]]; then
    cp "$icon_source" "$icon_theme_dir/$icon_name.png"
    break
  fi
done

cat > "$temporary_entry" <<EOF
[Desktop Entry]
Type=Application
Name=LaunchPad Ignite
Exec="$escaped_executable"
Icon=$icon_name
Terminal=false
Categories=Utility;
StartupWMClass=LaunchPad.Ignite.App
EOF

chmod 644 "$temporary_entry"
mv "$temporary_entry" "$desktop_entry"
rm -f "$previous_desktop_entry"
trap - EXIT

printf 'Installed LaunchPad Ignite in the applications menu.\n'
printf 'Desktop application ID: io.github.redheaddev.launchpadignite\n'
printf 'Keep the application directory at %s.\n' "$package_dir"
printf 'Launch LaunchPad Ignite from the applications menu for Wayland hotkey support.\n'
