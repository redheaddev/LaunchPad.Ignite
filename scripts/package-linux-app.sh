#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/LaunchPad.Ignite.App/LaunchPad.Ignite.App.csproj"
artifact_dir="$repo_root/LaunchPad.Ignite.App/artifacts/linux"

command -v dotnet >/dev/null
command -v tar >/dev/null
version="${1:-$(dotnet msbuild "$project" -getProperty:Version -nologo)}"
if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must use numeric major.minor.patch format: $version" >&2
  exit 1
fi
package_name="LaunchPad.Ignite-${version}-linux-x64"
publish_dir="$artifact_dir/publish-linux-x64"
package_dir="$artifact_dir/$package_name"
archive="$artifact_dir/$package_name.tar.gz"

rm -rf "$publish_dir" "$package_dir" "$archive"
mkdir -p "$artifact_dir"
dotnet publish "$project" \
  --configuration Release \
  --runtime linux-x64 \
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
cat > "$package_dir/README-Linux.txt" <<'EOF'
LaunchPad.Ignite Linux x64

Run ./LaunchPad.Ignite.App from a graphical desktop session.
If the executable bit was not preserved by the archive tool, run
chmod +x ./LaunchPad.Ignite.App first.
A .NET runtime is included. The Linux desktop still needs the native libraries
required by Avalonia and an X11 or Wayland session.

Secret storage and global activation hotkeys are not supported yet. Secret
commands report that limitation and do not store values.
EOF

tar -czf "$archive" -C "$artifact_dir" "$package_name"
printf 'Created %s\n' "$archive"
