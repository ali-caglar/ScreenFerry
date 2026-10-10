# macOS agent

Swift 6 (language mode 6), SwiftUI `MenuBarExtra`, minimum macOS 13 (ADR 0002).

## Layout

- `ScreenFerryKit/` — Swift package with all logic and its tests. Put new code here unless it
  needs the app bundle. Targets: `ScreenFerryKit` (portable logic: protocol, pairing, EDID,
  DDC/CI packets), `ScreenFerryDDC` (IOKit/private-API DDC transport), `ScreenFerryDisplays`
  (private SkyLight detach/attach), `ScreenFerryNet` (agent identity, TLS, DNS-SD, pairing
  over the wire; depends on Apple's `swift-certificates`) and `ScreenFerryAgent` (the menu-bar
  model and SwiftUI views; the app target links only this). Keep private APIs out of the
  portable targets. `Package.resolved` is committed.
- `ScreenFerry/` — app target: `ScreenFerryApp.swift` and `Info.plist` (Bonjour service type;
  the rest of the plist is generated from build settings).
- `ScreenFerry.xcodeproj` — app target only; depends on `ScreenFerryKit` as a local package.
  Edit it in Xcode. It was generated once with XcodeGen; there is no `project.yml`.
- `Config/Version.xcconfig` — local default version; CI overrides it (never set `x.y.0` here).

## Build and test

```sh
swift test --package-path apps/macos/ScreenFerryKit

xcodebuild build -project apps/macos/ScreenFerry.xcodeproj -scheme ScreenFerry \
  -configuration Release -destination 'generic/platform=macOS' \
  -derivedDataPath build/DerivedData CODE_SIGNING_ALLOWED=NO \
  MARKETING_VERSION="$(scripts/version.sh)"
```

The app is `LSUIElement` (no Dock icon). Protocol fixture tests read `protocol/fixtures/`
relative to `#filePath`, so they need the full repository checkout.

## Notes

- Tests use Swift Testing (`import Testing`), not XCTest.
- Private APIs (IOAVService DDC on Apple Silicon, SkyLight display enable/disable) must live
  behind a small protocol with a runtime availability check, so the agent can fall back to
  "switch input only" when they break on an OS update.
- Signing: CI builds are unsigned (`CODE_SIGNING_ALLOWED=NO`). Developer ID signing and
  notarization arrive in Phase 5 via CI secrets only.
