#!/bin/bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/LaunchPad.Ignite.App/LaunchPad.Ignite.App.csproj"
icon_source="$repo_root/LaunchPad.Ignite.App/Assets/rocket.png"
output_dir="$repo_root/LaunchPad.Ignite.App/artifacts/macos"
version="${1:-$(dotnet msbuild "$project" -getProperty:Version -nologo)}"
runtime_id="${2:-}"

if [[ ! "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must use numeric major.minor.patch format: $version" >&2
  exit 1
fi

if [[ -z "$runtime_id" ]]; then
  case "$(uname -m)" in
	arm64) runtime_id=osx-arm64 ;;
	x86_64) runtime_id=osx-x64 ;;
	*) echo "Unsupported macOS architecture: $(uname -m)" >&2; exit 1 ;;
  esac
fi

case "$runtime_id" in
  osx-x64|osx-arm64) ;;
  *) echo "Runtime identifier must be osx-x64 or osx-arm64: $runtime_id" >&2; exit 1 ;;
esac

package_name="LaunchPad.Ignite-${version}-${runtime_id}"
bundle="$output_dir/${package_name}.app"
archive="$output_dir/${package_name}.zip"
contents="$bundle/Contents"

command -v dotnet >/dev/null
publish_dir="$output_dir/publish-$runtime_id"
rm -rf "$publish_dir" "$bundle" "$archive"
dotnet publish "$project" --configuration Release --runtime "$runtime_id" --self-contained true --output "$publish_dir" "-p:Version=$version" "-p:InformationalVersion=$version" "-p:IncludeSourceRevisionInInformationalVersion=false"

rm -rf "$bundle"
mkdir -p "$contents/MacOS" "$contents/Resources"
cp -R "$publish_dir/." "$contents/MacOS/"

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

cat > "$contents/Info.plist" <<PLIST
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
	<string>${version}</string>
  <key>CFBundleVersion</key>
	<string>${version}</string>
  <key>LSMinimumSystemVersion</key>
  <string>12.0</string>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
PLIST

/usr/libexec/PlistBuddy -c 'Print' "$contents/Info.plist" >/dev/null
chmod +x "$contents/MacOS/LaunchPad.Ignite.App"
ditto -c -k --sequesterRsrc --keepParent "$bundle" "$archive"
printf 'Created %s\n' "$archive"
if [[ "${OPEN_PACKAGE:-false}" == "true" ]]; then
  open "$bundle"
fi
