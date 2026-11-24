#!/usr/bin/env bash
# 打 macOS 包：release/macos/H2sim-<version>-macos-x64.dmg
# 走 GTK3 + Mono 6.12（x86_64）。Eto 的 XamMac2 启动器已确认不可用。
# 运行目标机需要 x86_64 的 GTK3（Intel Homebrew 的 /usr/local）。
# 不打包 ThermoCS（i386）和 DWSIM.ProFeatures.dll。
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="$(tr -d '[:space:]' < VERSION)"
SRC="DWSIM.UI.Desktop/bin/Release"
ICON="packages/Eto.Platform.XamMac2.2.8.2/build/Icon.icns"
BUNDLE_GTK3="${H2SIM_BUNDLE_GTK3:-0}"

if [[ ! -f "$SRC/DWSIM.UI.Desktop.exe" ]]; then
  echo "缺少 $SRC/DWSIM.UI.Desktop.exe。先在本机构建 DWSIM.UI.Desktop（Release）。" >&2
  exit 1
fi
if [[ ! -f "DWSIM/References/Xamarin.Mac.dll" ]]; then
  echo "缺少 DWSIM/References/Xamarin.Mac.dll" >&2
  exit 1
fi

STAGE="$(mktemp -d "${TMPDIR:-/tmp}/h2sim-macos.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/H2sim.app"
BUNDLE="$APP/Contents/MonoBundle"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$BUNDLE"

# GTK3 外壳：确认 Rosetta、Mono 和 GTK3，然后用 mono 直接启动。
cat > "$APP/Contents/MacOS/H2sim" << 'EOF'
#!/bin/bash
set -euo pipefail
if ! arch -x86_64 /usr/bin/true >/dev/null 2>&1; then
  osascript -e 'display dialog "H2sim 需要 Rosetta。在终端执行：softwareupdate --install-rosetta --agree-to-license" buttons {"好"} default button 1 with title "H2sim"' || true
  exit 1
fi
MONO=/Library/Frameworks/Mono.framework/Versions/Current/bin/mono
if [[ ! -x "$MONO" ]]; then
  osascript -e 'display dialog "H2sim 需要 Mono 6.12。请安装 Mono Framework MDK 后再打开。" buttons {"好"} default button 1 with title "H2sim"' || true
  exit 1
fi
DIR="$(cd "$(dirname "$0")" && pwd)"
BUNDLE_DIR="$(cd "$DIR/../MonoBundle" && pwd)"
export H2SIM_MAC_RENDERER=Gtk3
export PANGOCAIRO_BACKEND=fontconfig
if [[ -f "$BUNDLE_DIR/lib/libgtk-3.0.dylib" ]]; then
  export DYLD_LIBRARY_PATH="$BUNDLE_DIR/lib:${DYLD_LIBRARY_PATH:-}"
  export GTK_DATA_PREFIX="$BUNDLE_DIR"
  export XDG_DATA_DIRS="$BUNDLE_DIR/share:/usr/share:${XDG_DATA_DIRS:-}"
  CACHE_SRC="$BUNDLE_DIR/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"
  if [[ -f "$CACHE_SRC" ]]; then
    CACHE_TMP="${TMPDIR:-/tmp}/h2sim-pixbuf-$$.cache"
    sed "s|@H2SIM_BUNDLE@|$BUNDLE_DIR|g" "$CACHE_SRC" > "$CACHE_TMP"
    export GDK_PIXBUF_MODULE_FILE="$CACHE_TMP"
  fi
  export GDK_PIXBUF_MODULEDIR="$BUNDLE_DIR/lib/gdk-pixbuf-2.0/2.10.0/loaders"
  export GSETTINGS_SCHEMA_DIR="$BUNDLE_DIR/share/glib-2.0/schemas"
else
  GTK=/usr/local/lib/libgtk-3.0.dylib
  if [[ ! -f "$GTK" ]] || ! file "$GTK" | grep -q 'x86_64'; then
    osascript -e 'display dialog "H2sim 的自包含 GTK3 包缺少内置库。请反馈构建问题，或安装 x86_64 GTK3（Intel：brew install gtk+3；Apple Silicon：Rosetta 前缀 /usr/local）。" buttons {"好"} default button 1 with title "H2sim"' || true
    exit 1
  fi
  export GTK_DATA_PREFIX=/usr/local
  export XDG_DATA_DIRS="/usr/local/share:${XDG_DATA_DIRS:-}"
fi
cd "$BUNDLE_DIR"
exec arch -x86_64 "$MONO" DWSIM.UI.Desktop.exe
EOF
chmod +x "$APP/Contents/MacOS/H2sim"

# 托管程序集。pdb / xml 不进包。Pro 和 ThermoC 不进 macOS 包。
find "$SRC" -maxdepth 1 \( -name '*.dll' -o -name '*.exe' -o -name '*.config' -o -name '*.dylib' \) \
  ! -name 'DWSIM.ProFeatures.*' \
  ! -name 'DWSIM.Thermodynamics.ThermoC.*' \
  -exec cp {} "$BUNDLE/" \;
cp "DWSIM/References/Xamarin.Mac.dll" "$BUNDLE/Xamarin.Mac.dll"

# 数据库、样例、位图。macOS 原生库放在程序集旁边，ThermoCS 目录排除。
if [[ -d PlatformFiles/Common ]]; then
  cp -R PlatformFiles/Common/. "$BUNDLE/"
fi
if [[ -d PlatformFiles/macOS ]]; then
  find PlatformFiles/macOS -maxdepth 1 -mindepth 1 ! -name ThermoCS -exec cp -R {} "$BUNDLE/" \;
fi
rm -rf "$BUNDLE/ThermoCS"
rm -f "$BUNDLE/DWSIM.ProFeatures.dll" "$BUNDLE/DWSIM.Thermodynamics.ThermoC.dll"

if [[ "$BUNDLE_GTK3" == "1" ]]; then
  command -v dylibbundler >/dev/null 2>&1 || {
    echo "H2SIM_BUNDLE_GTK3=1 需要 dylibbundler（brew install dylibbundler）。" >&2
    exit 1
  }
  GTK_SOURCE="$(realpath /usr/local/lib/libgtk-3.0.dylib)"
  if [[ ! -f "$GTK_SOURCE" ]]; then
    echo "找不到 x86_64 GTK3：/usr/local/lib/libgtk-3.0.dylib" >&2
    exit 1
  fi
  mkdir -p "$BUNDLE/lib"
  cp "$GTK_SOURCE" "$BUNDLE/lib/libgtk-3.0.dylib"
  dylibbundler -cd -b -of -x "$BUNDLE/lib/libgtk-3.0.dylib" \
    -d "$BUNDLE/lib" -p "@loader_path" -s /usr/local/lib
  # Eto/GtkSharp P/Invokes the unversioned names.
  for alias in libgdk_pixbuf-2.0 libgobject-2.0 libglib-2.0 libgio-2.0 \
               libgmodule-2.0 libpango-1.0 libpangocairo-1.0 libatk-1.0; do
    src="$(ls "$BUNDLE/lib/${alias}"*.dylib 2>/dev/null | head -1 || true)"
    if [[ -n "$src" ]]; then
      ln -sf "$(basename "$src")" "$BUNDLE/lib/${alias}.dylib"
    fi
  done
  cp -RL /usr/local/lib/gtk-3.0 "$BUNDLE/lib/" 2>/dev/null || true
  cp -RL /usr/local/lib/gdk-pixbuf-2.0 "$BUNDLE/lib/" 2>/dev/null || true
  mkdir -p "$BUNDLE/share" "$BUNDLE/etc"
  cp -RL /usr/local/share/gtk-3.0 "$BUNDLE/share/" 2>/dev/null || true
  cp -RL /usr/local/share/themes "$BUNDLE/share/" 2>/dev/null || true
  cp -RL /usr/local/share/icons "$BUNDLE/share/" 2>/dev/null || true
  cp -RL /usr/local/share/glib-2.0 "$BUNDLE/share/" 2>/dev/null || true
  cp -RL /usr/local/etc/gtk-3.0 "$BUNDLE/etc/" 2>/dev/null || true
  cp -RL /usr/local/etc/fonts "$BUNDLE/etc/" 2>/dev/null || true
  for loader in "$BUNDLE"/lib/gdk-pixbuf-2.0/2.10.0/loaders/*.so; do
    [[ -f "$loader" ]] || continue
    dylibbundler -b -of -x "$loader" \
      -d "$BUNDLE/lib/gdk-pixbuf-2.0/2.10.0/loaders" \
      -p "@loader_path" -s /usr/local/lib >/dev/null 2>&1 || true
  done
  if command -v gdk-pixbuf-query-loaders >/dev/null 2>&1; then
    GDK_PIXBUF_MODULEDIR="$BUNDLE/lib/gdk-pixbuf-2.0/2.10.0/loaders" \
      gdk-pixbuf-query-loaders "$BUNDLE"/lib/gdk-pixbuf-2.0/2.10.0/loaders/*.so \
      > "$BUNDLE/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache" 2>/dev/null || true
    # The cache stores absolute paths from the staging directory. Replace the
    # bundle prefix with a placeholder; the launcher expands it at runtime.
    CACHE="$BUNDLE/lib/gdk-pixbuf-2.0/2.10.0/loaders.cache"
    if [[ -f "$CACHE" ]]; then
      sed -i '' "s|$BUNDLE|@H2SIM_BUNDLE@|g" "$CACHE"
    fi
  fi
  echo "Bundled GTK3 libraries: $(ls "$BUNDLE/lib" | wc -l | tr -d ' ')"
fi

if [[ -f "$ICON" ]]; then
  cp "$ICON" "$APP/Contents/Resources/Icon.icns"
fi

cat > "$APP/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>H2sim</string>
	<key>CFBundleDisplayName</key>
	<string>H2sim</string>
	<key>CFBundleIdentifier</key>
	<string>com.h2sim.app</string>
	<key>CFBundleVersion</key>
	<string>${VERSION}</string>
	<key>CFBundleShortVersionString</key>
	<string>${VERSION}</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleSignature</key>
	<string>????</string>
	<key>CFBundleExecutable</key>
	<string>H2sim</string>
	<key>CFBundleIconFile</key>
	<string>Icon.icns</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>LSMinimumSystemVersion</key>
	<string>10.14</string>
	<key>LSArchitecturePriority</key>
	<array>
		<string>x86_64</string>
	</array>
	<key>NSPrincipalClass</key>
	<string>NSApplication</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSRequiresAquaSystemAppearance</key>
	<false/>
	<key>MonoMinimumVersion</key>
	<string>6.12</string>
	<key>MonoBundleExecutable</key>
	<string>DWSIM.UI.Desktop.exe</string>
</dict>
</plist>
EOF

# 确认包里没有不该出现的东西。
if [[ -e "$BUNDLE/ThermoCS" || -e "$BUNDLE/DWSIM.ProFeatures.dll" || -e "$BUNDLE/DWSIM.Thermodynamics.ThermoC.dll" ]]; then
  echo "打包内容检查失败：ThermoCS 或 ProFeatures 仍在包内。" >&2
  exit 1
fi
if [[ ! -f "$BUNDLE/Xamarin.Mac.dll" || ! -f "$BUNDLE/DWSIM.UI.Desktop.exe" ]]; then
  echo "打包内容检查失败：缺少启动所需的程序集。" >&2
  exit 1
fi

mkdir -p release/macos
DMG="release/macos/H2sim-${VERSION}-macos-x64.dmg"
rm -f "$DMG"
hdiutil create -volname "H2sim ${VERSION}" -srcfolder "$APP" -ov -format UDZO "$DMG" >/dev/null

echo "PACKAGE OK: $DMG"
ls -lh "$DMG"
