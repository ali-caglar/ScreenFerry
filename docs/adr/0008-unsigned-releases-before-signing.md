# 0008. Unsigned releases before signing

- Status: Accepted; the Windows zips are superseded by 0010
- Date: 2026-10-11

## Context

Phase 5 ends with signed, notarized builds and package-manager listings. Signing needs an
Apple Developer Program membership and a Windows code-signing certificate, which the project
doesn't have yet. Phases 1–4 work, and people can use them now if a release ships something
they can install.

The release workflow already attached zips to each GitHub release, but they were not usable
as is:

- The macOS app was built with `CODE_SIGNING_ALLOWED=NO`. A downloaded, quarantined bundle
  without any signature is reported as "damaged" and has no "Open Anyway".
- The Windows build was framework-dependent, so users also had to install the .NET 10 Desktop
  Runtime.

## Decision

Until signing material exists, every release ships:

- `ScreenFerry-X.Y.0-macos-unsigned.zip`: the universal app, **ad-hoc signed**
  (`codesign --sign -`), so Gatekeeper offers "Open Anyway" in System Settings.
- `ScreenFerry-X.Y.0-win-x64-unsigned.zip` and `-win-arm64-unsigned.zip`: one
  **self-contained** single-file `ScreenFerry.exe` each, so no runtime install is needed.

`docs/install.md` explains how to install them, including the Gatekeeper and SmartScreen
steps. Homebrew and winget wait for signed builds; no update channel offers CI builds
(ADR 0004).

## Consequences

- Users see one Gatekeeper or SmartScreen warning on first launch.
- An ad-hoc signature changes with every build. macOS ties the agent's keychain item to it,
  so after an update macOS asks once whether the new build may use the key. Allowing it keeps
  the pairing; denying it makes the agent fail to start.
- The Windows zips grow to a self-contained .NET app (tens of MB).
- Signing later replaces the ad-hoc signature and adds notarization in the same jobs, and
  this ADR is superseded.
