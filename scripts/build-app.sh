#!/bin/bash
# Builds "Code Caller.app" into ./build and signs it with the sandbox entitlements.
#
#   scripts/build-app.sh            ad-hoc signature, for running on this Mac
#   SIGN_IDENTITY="Developer ID Application: ..." scripts/build-app.sh
set -euo pipefail

cd "$(dirname "$0")/.."

identity="${SIGN_IDENTITY:--}"
app="build/Code Caller.app"

swift build -c release --product CodeCaller
binary="$(swift build -c release --show-bin-path)/CodeCaller"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$binary" "$app/Contents/MacOS/CodeCaller"
cp Resources/Info.plist "$app/Contents/Info.plist"
if [[ -f Resources/AppIcon.icns ]]; then
	cp Resources/AppIcon.icns "$app/Contents/Resources/AppIcon.icns"
fi

codesign --force --options runtime --timestamp=none \
	--entitlements Resources/CodeCaller.entitlements \
	--sign "$identity" "$app"
codesign --verify --strict "$app"

echo "Built $app"
