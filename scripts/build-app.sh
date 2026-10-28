#!/bin/bash
# Builds a universal "Copy 2FA.app" (Apple silicon and Intel) into ./build and signs it
# with the sandbox entitlements.
#
#   scripts/build-app.sh            ad-hoc signature, for running on this Mac
#   SIGN_IDENTITY="Developer ID Application: ..." scripts/build-app.sh
set -euo pipefail

cd "$(dirname "$0")/.."

identity="${SIGN_IDENTITY:--}"
app="build/Copy 2FA.app"

# Each architecture is built on its own and then combined, which works with just the
# Command Line Tools. Building both in one step needs Xcode.
binaries=()
for arch in arm64 x86_64; do
	triple="$arch-apple-macosx14.0"
	swift build -c release --product Copy2FA --triple "$triple"
	binaries+=("$(swift build -c release --triple "$triple" --show-bin-path)/Copy2FA")
done

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
lipo -create "${binaries[@]}" -output "$app/Contents/MacOS/Copy2FA"
cp Resources/Info.plist "$app/Contents/Info.plist"
if [[ -f Resources/AppIcon.icns ]]; then
	cp Resources/AppIcon.icns "$app/Contents/Resources/AppIcon.icns"
fi

codesign --force --options runtime --timestamp=none \
	--entitlements Resources/Copy2FA.entitlements \
	--sign "$identity" "$app"
codesign --verify --strict "$app"

echo "Built $app"
