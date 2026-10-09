# macOS agent

Swift 6 (language mode 6), SwiftUI `MenuBarExtra`, minimum macOS 13 (ADR 0002).

## Layout

- `ScreenFerryKit/` — Swift package with all logic and its tests. Put new code here unless it
  needs the app bundle.
- `ScreenFerry/` — app target sources (menu-bar UI, app lifecycle).
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
