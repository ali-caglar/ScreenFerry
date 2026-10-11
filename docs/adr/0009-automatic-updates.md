# 0009. Automatic updates

- Status: Accepted
- Date: 2026-10-11

## Context

Releases ship as zips on GitHub (ADR 0008), and nothing tells a user that a new one exists.
The maintainer wants the agents to update themselves. Until now no agent made a network
call outside the LAN.

Two threats matter: a tampered download, and a build that isn't a release (ADR 0004: a CI
build `0.3.4` is older than the release `0.3.0`). The builds are not code-signed yet, so
code signing can't vouch for an update.

## Decision

Both agents check for a new release at launch and once a day, download it, verify it, and
install it without asking. Each has a **Check for Updates…** menu item too. These checks are
the agents' only network calls outside the LAN. They send nothing but the HTTP request for a
file in the latest GitHub release.

- **Signed updates.** Each release carries a manifest that names the zip, its SHA-256 and a
  signature by an update key held only as a CI secret. The agents embed the public key and
  install nothing that doesn't verify.
  - macOS: [Sparkle](https://sparkle-project.org) 2, the standard updater for Mac apps.
    `appcast.xml` with an EdDSA (Ed25519) signature; public key in `SUPublicEDKey`. Sparkle
    accepts a changed code signature when the EdDSA signature is valid, which covers ad-hoc
    builds.
  - Windows: `windows-update.json` with an ECDSA P-256 / SHA-256 signature over each zip,
    checked with .NET's built-in `ECDsa`.
- **Feeds.** `https://github.com/ali-caglar/ScreenFerry/releases/latest/download/<manifest>`:
  only full releases, never CI builds.
- **Versions.** A release `X.Y.0` is newer than the running `A.B.C` if `X.Y > A.B`, or if
  `X.Y = A.B` and `C ≠ 0` (a pre-release build of that version). Sparkle compares
  `CFBundleVersion`, the release workflow's run number, which only grows.
- **When.** An agent installs an update only while no handoff is in progress, then restarts
  itself. Windows replaces its own exe: it renames the running file, puts the new one in its
  place and starts it.
- **Keys.** `SPARKLE_ED_PRIVATE_KEY` and `WINDOWS_UPDATE_KEY` are repository secrets; the
  maintainer keeps offline backups. Without them, the release workflow still ships the zips
  but no manifests, so agents see no update.

## Consequences

- Losing a private key means shipping one manual-install release with a new public key.
  Leaking it lets an attacker who can also publish GitHub releases push code to every agent.
  Developer ID and Authenticode signing later add a second check (Phase 5).
- On macOS each ad-hoc update still asks once for the keychain key (ADR 0008).
- Windows can only update an exe in a folder the user can write to, which `docs/install.md`
  already recommends.
- `CLAUDE.md` and BRIEF allow this one network destination.
