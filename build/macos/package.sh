#!/usr/bin/env bash
# Builds GidEditor for macOS as a GidEditor.app bundle inside a drag-to-install .dmg. Must run on macOS.
#
#   build/macos/package.sh <rid> <version> [output-dir]
#   build/macos/package.sh osx-arm64 0.2.0
#
# Supported RIDs: osx-arm64 (Apple Silicon), osx-x64 (Intel).
# The app is ad-hoc signed only. Users must allow it once (see README) until it is signed with an
# Apple Developer ID and notarized.
set -euo pipefail

RID="${1:?usage: package.sh <osx-arm64|osx-x64> <version> [output-dir]}"
VERSION="${2:?usage: package.sh <rid> <version> [output-dir]}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$(mkdir -p "${3:-$ROOT/publish}" && cd "${3:-$ROOT/publish}" && pwd)"

case "$RID" in
  osx-arm64 | osx-x64) ;;
  *) echo "Unsupported RID: $RID" >&2; exit 1 ;;
esac

# Bundle versions must be purely numeric (1.2.3), so drop any pre-release suffix like "-beta.1".
BUNDLE_VERSION="${VERSION%%-*}"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
APP="$WORK/dmg/GidEditor.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

dotnet publish "$ROOT/src/GidEditor/GidEditor.csproj" \
  -c Release -r "$RID" --self-contained \
  -p:Version="$VERSION" \
  -o "$APP/Contents/MacOS"

# ---- Icon: build an .icns from the 512 px PNG -------------------------------
ICONSET="$WORK/gideditor.iconset"
mkdir -p "$ICONSET"
SRC_ICON="$ROOT/src/GidEditor/Assets/gideditor.png"
for size in 16 32 128 256; do
  sips -z "$size" "$size" "$SRC_ICON" --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) "$SRC_ICON" --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
cp "$SRC_ICON" "$ICONSET/icon_512x512.png"
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/gideditor.icns"

# ---- Info.plist -------------------------------------------------------------
cat > "$APP/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleName</key>
    <string>GidEditor</string>
    <key>CFBundleDisplayName</key>
    <string>GidEditor</string>
    <key>CFBundleIdentifier</key>
    <string>com.giovannicortes.gideditor</string>
    <key>CFBundleExecutable</key>
    <string>GidEditor</string>
    <key>CFBundleIconFile</key>
    <string>gideditor</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>CFBundleShortVersionString</key>
    <string>$BUNDLE_VERSION</string>
    <key>CFBundleVersion</key>
    <string>$BUNDLE_VERSION</string>
    <key>LSApplicationCategoryType</key>
    <string>public.app-category.productivity</string>
    <key>NSHighResolutionCapable</key>
    <true/>
    <key>NSHumanReadableCopyright</key>
    <string>Copyright © 2026 Giovanni Cortes. MIT License.</string>
</dict>
</plist>
EOF
plutil -lint "$APP/Contents/Info.plist"

# ---- Signing ----------------------------------------------------------------
# Apple Silicon refuses to run unsigned code, so at minimum sign ad-hoc.
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"

# ---- Disk image -------------------------------------------------------------
ln -s /Applications "$WORK/dmg/Applications"
DMG="$OUT/GidEditor-$VERSION-$RID.dmg"
rm -f "$DMG"
hdiutil create -volname "GidEditor" -srcfolder "$WORK/dmg" -ov -format UDZO "$DMG"

echo "Created:"
ls -lh "$DMG"
