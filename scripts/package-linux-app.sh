#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/LaunchPad.Ignite.App/LaunchPad.Ignite.App.csproj"
artifact_dir="$repo_root/LaunchPad.Ignite.App/artifacts/linux"

command -v dotnet >/dev/null
command -v tar >/dev/null
version="${1:-$(dotnet msbuild "$project" -getProperty:Version -nologo)}"
runtime_identifier="${2:-linux-x64}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must use numeric major.minor.patch format: $version" >&2
  exit 1
fi
if [[ "$runtime_identifier" != "linux-x64" && "$runtime_identifier" != "linux-arm64" ]]; then
  echo "Runtime identifier must be linux-x64 or linux-arm64: $runtime_identifier" >&2
  exit 1
fi
architecture="${runtime_identifier#linux-}"
package_name="LaunchPad.Ignite-${version}-${runtime_identifier}"
publish_dir="$artifact_dir/publish-${runtime_identifier}"
package_dir="$artifact_dir/$package_name"
archive="$artifact_dir/$package_name.tar.gz"

rm -rf "$publish_dir" "$package_dir" "$archive"
mkdir -p "$artifact_dir"
dotnet publish "$project" \
  --configuration Release \
  --runtime "$runtime_identifier" \
  --self-contained true \
  --output "$publish_dir" \
  "-p:Version=$version"

app_executable="$publish_dir/LaunchPad.Ignite.App"
if [[ ! -f "$app_executable" ]]; then
  echo "Published app executable not found: $app_executable" >&2
  exit 1
fi

mkdir -p "$package_dir"
cp -R "$publish_dir/." "$package_dir/"
icon_source="$repo_root/LaunchPad.Ignite.App/Assets/rocket.png"
icon_name="io.github.redheaddev.launchpadignite.png"
icon_target="$package_dir/$icon_name"
icon_theme_dir="$package_dir/share/icons/hicolor/256x256/apps"
mkdir -p "$icon_theme_dir"
if [[ -f "$icon_source" ]]; then
  cp "$icon_source" "$icon_target"
  cp "$icon_source" "$icon_theme_dir/$icon_name"
fi
find "$package_dir" -type d -exec chmod 755 {} +
find "$package_dir" -type f -exec chmod 644 {} +
chmod 755 "$package_dir/LaunchPad.Ignite.App"
cp "$repo_root/scripts/install-linux-app.sh" "$package_dir/install-linux-app.sh"
sed -i 's/\r$//' "$package_dir/install-linux-app.sh"
chmod 755 "$package_dir/install-linux-app.sh"
cat > "$package_dir/README-Linux.txt" <<EOF
LaunchPad.Ignite Linux $architecture

Keep this extracted directory in a permanent location. Run
chmod +x ./LaunchPad.Ignite.App ./install-linux-app.sh, then run
./install-linux-app.sh to add LaunchPad Ignite to the applications menu and
launch it from there. This desktop entry lets Wayland portals identify the app.
A .NET runtime is included. The Linux desktop still needs the native libraries
required by Avalonia and an X11 or Wayland session. Global activation hotkeys
use a keyboard hook in X11 sessions and the desktop's GlobalShortcuts portal in
Wayland sessions. Wayland availability depends on portal support and may require
approving the shortcut.
Wayland shortcut diagnostics are written to
\${XDG_STATE_HOME:-\$HOME/.local/state}/LaunchPad.Ignite/activation-hotkey.log.

Secret storage is not supported yet. Secret commands report that limitation
and do not store values.
EOF

tar -czf "$archive" -C "$artifact_dir" "$package_name"
printf 'Created %s\n' "$archive"
