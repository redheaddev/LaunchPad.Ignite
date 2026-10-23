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
find "$package_dir" -type d -exec chmod 755 {} +
find "$package_dir" -type f -exec chmod 644 {} +
chmod 755 "$package_dir/LaunchPad.Ignite.App"
cat > "$package_dir/README-Linux.txt" <<EOF
LaunchPad.Ignite Linux $architecture

Run ./LaunchPad.Ignite.App from a graphical desktop session.
If the executable bit was not preserved by the archive tool, run
chmod +x ./LaunchPad.Ignite.App first.
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
