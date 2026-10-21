#!/usr/bin/env bash
# Builds GidEditor for Linux and packages it as a .deb and a portable .tar.gz.
#
#   build/linux/package.sh <rid> <version> [output-dir]
#   build/linux/package.sh linux-x64 0.2.0
#
# Supported RIDs: linux-x64, linux-arm64.
set -euo pipefail

RID="${1:?usage: package.sh <linux-x64|linux-arm64> <version> [output-dir]}"
VERSION="${2:?usage: package.sh <rid> <version> [output-dir]}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="$(mkdir -p "${3:-$ROOT/publish}" && cd "${3:-$ROOT/publish}" && pwd)"

# In Debian versions "~" sorts before the release, so 0.2.0~beta.1 < 0.2.0 (a "-" would mean a package revision).
DEB_VERSION="${VERSION//-/"~"}"

case "$RID" in
  linux-x64) DEB_ARCH=amd64 ;;
  linux-arm64) DEB_ARCH=arm64 ;;
  *) echo "Unsupported RID: $RID" >&2; exit 1 ;;
esac

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
APP="$WORK/app"

# Invariant globalization removes the dependency on the system ICU library, whose package name differs
# between distributions (libicu70, libicu74, …). The UI is not localized, so nothing is lost.
dotnet publish "$ROOT/src/GidEditor/GidEditor.csproj" \
  -c Release -r "$RID" --self-contained \
  -p:Version="$VERSION" \
  -p:InvariantGlobalization=true \
  -o "$APP"

# Normalize permissions so they don't depend on the builder's umask: dirs 755, files 644, programs 755.
find "$APP" -type d -exec chmod 755 {} +
find "$APP" -type f -exec chmod 644 {} +
chmod 755 "$APP/GidEditor" "$APP/createdump"

# ---- Portable archive -------------------------------------------------------
TAR_NAME="GidEditor-$VERSION-$RID"
mkdir -p "$WORK/$TAR_NAME"
cp -a "$APP/." "$WORK/$TAR_NAME/"
tar -C "$WORK" -czf "$OUT/$TAR_NAME.tar.gz" "$TAR_NAME"

# ---- Debian package ---------------------------------------------------------
PKG="$WORK/deb"
install -d "$PKG/DEBIAN" "$PKG/opt/gideditor" "$PKG/usr/bin" \
  "$PKG/usr/share/applications" "$PKG/usr/share/icons/hicolor/512x512/apps" \
  "$PKG/usr/share/doc/gideditor"

cp -a "$APP/." "$PKG/opt/gideditor/"
ln -s /opt/gideditor/GidEditor "$PKG/usr/bin/gideditor"

install -m 644 "$ROOT/build/linux/gideditor.desktop" "$PKG/usr/share/applications/gideditor.desktop"
install -m 644 "$ROOT/src/GidEditor/Assets/gideditor.png" "$PKG/usr/share/icons/hicolor/512x512/apps/gideditor.png"
install -m 644 "$ROOT/LICENSE" "$PKG/usr/share/doc/gideditor/copyright"
install -m 644 "$ROOT/THIRD-PARTY-NOTICES.txt" "$PKG/usr/share/doc/gideditor/THIRD-PARTY-NOTICES.txt"

INSTALLED_KB="$(du -sk "$PKG/opt" | cut -f1)"
cat > "$PKG/DEBIAN/control" <<EOF
Package: gideditor
Version: $DEB_VERSION
Architecture: $DEB_ARCH
Maintainer: Giovanni Cortes <giovanni@giovannicortes.com>
Installed-Size: $INSTALLED_KB
Depends: libc6, libgcc-s1, libstdc++6, libx11-6, libice6, libsm6, libfontconfig1
Section: editors
Priority: optional
Homepage: https://github.com/Gidrek/GidEditor
Description: Markdown notes editor that keeps your notes in a folder you own
 GidEditor opens a folder as a workspace and edits the Markdown files in it,
 with editor, split and preview views. Keep the folder on your computer,
 in Dropbox, Google Drive or anywhere you sync.
EOF

# Refresh the desktop menu and icon cache so the app shows up right after installing.
cat > "$PKG/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q /usr/share/applications || true
command -v gtk-update-icon-cache >/dev/null 2>&1 && gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true
exit 0
EOF
cp "$PKG/DEBIAN/postinst" "$PKG/DEBIAN/postrm"
chmod 755 "$PKG/DEBIAN/postinst" "$PKG/DEBIAN/postrm"

find "$PKG" -type d -exec chmod 755 {} +
dpkg-deb --root-owner-group --build "$PKG" "$OUT/gideditor_${DEB_VERSION}_${DEB_ARCH}.deb"

echo "Created:"
ls -lh "$OUT/$TAR_NAME.tar.gz" "$OUT/gideditor_${DEB_VERSION}_${DEB_ARCH}.deb"
