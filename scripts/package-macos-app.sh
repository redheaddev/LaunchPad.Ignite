#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/LaunchPad.Ignite.App/LaunchPad.Ignite.App.csproj"
icon_source="$repo_root/LaunchPad.Ignite.App/Assets/rocket.png"
output_dir="$repo_root/LaunchPad.Ignite.App/artifacts/macos"
bundle="$output_dir/LaunchPad.Ignite.app"
contents="$bundle/Contents"

case "$(uname -m)" in
  arm64) runtime_id=osx-arm64 ;;
  x86_64) runtime_id=osx-x64 ;;
  *) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 1 ;;
esac

command -v dotnet >/dev/null
dotnet publish "$project" --configuration Release --runtime "$runtime_id" --self-contained true --output "$output_dir/publish"

rm -rf "$bundle"
mkdir -p "$contents/MacOS" "$contents/Resources"
cp -R "$output_dir/publish/." "$contents/MacOS/"

iconset="$(mktemp -d)/LaunchPadIcon.iconset"
mkdir -p "$iconset"
trap 'rm -rf "$(dirname "$iconset")"' EXIT

make_icon() {
  local pixels="$1"
  local suffix="$2"
  sips -z "$pixels" "$pixels" "$icon_source" --out "$iconset/icon_${3}${suffix}.png" >/dev/null
}

make_icon 16 '' 16x16
make_icon 32 '@2x' 16x16
make_icon 32 '' 32x32
make_icon 64 '@2x' 32x32
make_icon 128 '' 128x128
make_icon 256 '@2x' 128x128
make_icon 256 '' 256x256
make_icon 512 '@2x' 256x256
make_icon 512 '' 512x512
make_icon 1024 '@2x' 512x512
iconutil -c icns "$iconset" -o "$contents/Resources/LaunchPadIcon.icns"

cat > "$contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleExecutable</key>
  <string>LaunchPad.Ignite.App</string>
  <key>CFBundleIconFile</key>
  <string>LaunchPadIcon</string>
  <key>CFBundleIdentifier</key>
  <string>com.launchpad.ignite</string>
  <key>CFBundleName</key>
  <string>LaunchPad.Ignite</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>1.0.22</string>
  <key>CFBundleVersion</key>
  <string>1.0.22</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

/usr/libexec/PlistBuddy -c 'Print' "$contents/Info.plist" >/dev/null
chmod +x "$contents/MacOS/LaunchPad.Ignite.App"
printf 'Created %s\n' "$bundle"
open "$bundle"
