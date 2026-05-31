#!/bin/bash
# One-command release: build -> Developer-ID sign (hardened runtime) -> notarize -> staple -> .dmg -> notarize dmg -> staple.
# Usage: ./package_release.sh           (full release: sign + notarize + dmg)
#        ./package_release.sh dev        (build + sign only, no notarize — fast dev loop)
set -euo pipefail
cd "$(dirname "$0")"

MODE="${1:-release}"
ID="Developer ID Application: Your Name (TEAMID)"
APP="native/dist/Vibe XASR.app"
ENT="native/app/Resources/VibeIME.entitlements"
PROFILE="vibeime"
VER="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "native/app/Resources/Info.plist" 2>/dev/null || echo 0.2.0)"
DMG="native/dist/VibeXASR-${VER}.dmg"

echo "== 1/5 build =="
( cd native/app && ./build_app.sh ) 2>&1 | grep -iE "Build complete|error:" | grep -v "error: &err" | tail -3
[ -d "$APP" ] || { echo "❌ 没有产出 $APP"; exit 1; }

echo "== 2/5 sign (frameworks first, then app, hardened runtime + entitlements) =="
if [ -d "$APP/Contents/Frameworks" ]; then
  find "$APP/Contents/Frameworks" -name "*.dylib" -print0 | while IFS= read -r -d '' dy; do
    codesign --force --options runtime --timestamp -s "$ID" "$dy" >/dev/null
  done
fi
codesign --force --options runtime --timestamp --entitlements "$ENT" -s "$ID" "$APP"
codesign --verify --deep --strict --verbose=2 "$APP" 2>&1 | tail -2 || true

if [ "$MODE" = "dev" ]; then
  echo "== dev 模式:跳过公证,直接装机 =="
  pkill -f "MacOS/VibeIME" 2>/dev/null || true; sleep 1
  rm -rf "/Applications/Vibe XASR.app"; cp -R "$APP" /Applications/
  open -a "/Applications/Vibe XASR.app"; sleep 2
  pgrep -fl "MacOS/VibeIME" >/dev/null && echo "运行中 ✓" || echo "未运行 ✗"
  exit 0
fi

echo "== 3/5 notarize app =="
ZIP="native/dist/VibeXASR-${VER}.zip"
rm -f "$ZIP"
ditto -c -k --keepParent "$APP" "$ZIP"
xcrun notarytool submit "$ZIP" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$APP"
rm -f "$ZIP"

echo "== 4/5 build dmg =="
rm -f "$DMG"
STAGE="$(mktemp -d)"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -volname "Vibe XASR" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null
rm -rf "$STAGE"
codesign --force --timestamp -s "$ID" "$DMG"

echo "== 5/5 notarize dmg =="
xcrun notarytool submit "$DMG" --keychain-profile "$PROFILE" --wait
xcrun stapler staple "$DMG"
xcrun stapler validate "$DMG" && echo "✅ 公证已装订: $DMG"
echo "完成: $(cd "$(dirname "$DMG")" && pwd)/$(basename "$DMG")  (v$VER)"
