# 0002. Technology stack

- Status: Accepted
- Date: 2026-10-09

## Context

Nearly all the hard work in ScreenFerry is platform API calls: DDC/CI over IOAVService or
`dxva2`, display attach/detach through private CoreGraphics/SkyLight calls or the CCD API,
EDID from the IORegistry or WMI. A cross-platform framework would wrap each of those in
interop and make them harder to debug. What the agents must share is behaviour on the wire,
not code.

## Decision

**Two native agents, one protocol.**

macOS agent (`apps/macos/`)
- Swift 6 (language mode 6), SwiftUI `MenuBarExtra`, minimum **macOS 13**.
- Logic lives in the local Swift package `ScreenFerryKit` (built and tested with
  `swift test`); the Xcode project `ScreenFerry.xcodeproj` contains only the app target.
- `SMAppService` for the login item.

Windows agent (`apps/windows/`)
- C# on **.NET 10 (LTS)**, **WPF** for windows (scene editor, settings, pairing).
- The tray icon uses `System.Windows.Forms.NotifyIcon` (`UseWindowsForms` in the app
  project only). WPF has no tray icon; this avoids a third-party dependency.
- Logic lives in `ScreenFerry.Core` (`net10.0`, no Windows dependency) so it can be tested
  on any OS; Windows API code lives in the app (later: a `ScreenFerry.Windows` library).
- Central package management and NuGet lock files; CI restores in locked mode.

Protocol (`protocol/`)
- JSON messages over an authenticated, encrypted TCP channel (mechanism: Phase 3 ADR).
- JSON Schema draft 2020-12 files are normative. Golden fixtures are validated against
  them in CI (Node + Ajv) and parsed by both agents' test suites.

Tooling and CI
- GitHub Actions on `macos-26` and `windows-2025` runners.
- CodeQL in **advanced setup** (`.github/workflows/codeql.yml`) because Swift needs an
  explicit build; GitHub's default setup must stay off.

## Consequences

- Every feature is implemented twice. The protocol fixtures are the guard against drift:
  a message is not done until both test suites parse it.
- Contributors need Xcode for macOS work and the .NET 10 SDK for Windows work.
  `ScreenFerry.Core` and its tests also build on macOS/Linux; the WPF app builds on non-Windows
  hosts because the project sets `EnableWindowsTargeting`, but only runs on Windows.
- A Linux agent later means a third native codebase against the same protocol.
