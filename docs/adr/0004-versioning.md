# 0004. Releases are always `x.y.0`

- Status: Accepted
- Date: 2026-10-09

## Context

The maintainer's rule for every Xcode project: marketing versions are three-part `x.y.z`;
`x.y.0` is reserved for the release of that version, and every build before it uses
`x.y.1`, `x.y.2`, … — a pre-release build is never `x.y.0`.

release-please by default emits patch releases (`x.y.1`) for `fix:` commits, which would
collide with pre-release build numbers. The brief (§7.4) originally proposed
`bump-minor-pre-major`.

## Decision

- release-please uses `"versioning": "always-bump-minor"`. Every release is `x.y.0`, tagged
  `vX.Y.0`, with one lockstep version for both apps.
- Builds of any other commit get `X.(Y+1).N`, where `X.Y.0` is the last release in
  `.release-please-manifest.json` and `N ≥ 1` is the number of commits since its tag
  (since the first commit before any release). `scripts/version.sh` computes this; CI passes
  it to `xcodebuild` (`MARKETING_VERSION`) and `dotnet` (`-p:Version`).
- The checked-in defaults (`apps/macos/Config/Version.xcconfig`, `apps/windows/Directory.Build.props`)
  are for local builds only and are never `x.y.0`.
- A major version (including `1.0.0`) is cut deliberately with a `Release-As: X.0.0` footer
  on a commit, since `always-bump-minor` ignores breaking-change markers.

## Consequences

- Conventional Commit types still drive the changelog, but not the size of the bump.
- Within a minor, version order is not build order: pre-release `0.2.5` sorts above release
  `0.2.0` although it was built earlier. Update channels (Homebrew, winget, any in-app
  updater) must only ever offer releases, never CI builds.
- `CURRENT_PROJECT_VERSION` / build number is the CI run number.
