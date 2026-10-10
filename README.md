# ScreenFerry

[![CI](https://github.com/ali-caglar/ScreenFerry/actions/workflows/ci.yml/badge.svg)](https://github.com/ali-caglar/ScreenFerry/actions/workflows/ci.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/ali-caglar/ScreenFerry/badge)](https://scorecard.dev/viewer/?uri=github.com/ali-caglar/ScreenFerry)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

Move your monitors between computers with one hotkey.

If two or more computers (Macs, Windows PCs) share the same monitors, ScreenFerry switches
each monitor's input over **DDC/CI** and detaches it from the computer that no longer owns it,
so windows and the cursor don't vanish onto a screen nobody can see. Then the computer that
now owns the monitor attaches it.

> **Status: early development.** Nothing is usable yet. Input switching and detaching
> monitors work from command-line probes (Phases 1–2); the agents that talk to each other
> come next (Phase 3).
> See the [roadmap](docs/BRIEF.md#6-roadmap).

## Planned for v1

- macOS (Apple Silicon first) and Windows 10/11 agents with a menu-bar / tray UI.
- **Scenes** such as "both monitors → Mac" or "left → Mac, right → PC", bound to global hotkeys.
- 2–4 computers and 1–4 external monitors on one local network.
- Secure pairing: agents only take commands from paired peers.
- Works offline. No accounts, no cloud, no telemetry.

Not in v1: keyboard/mouse sharing, Linux, brightness controls, USB switching.

## How it works

Every computer runs a small agent. Agents find each other on the LAN with DNS-SD, pair once
with a short confirmation code, and then talk over an encrypted channel. When you activate a
scene, the computer that currently owns a monitor sends the DDC "switch input" command,
detaches the monitor from its desktop, and the new owner attaches it. Each monitor's input
codes are learned automatically, so no per-model tables are needed.

Details: [docs/BRIEF.md](docs/BRIEF.md) · Decisions: [docs/adr/](docs/adr/) ·
Protocol: [protocol/](protocol/)

## Monitor compatibility

DDC/CI support varies by monitor, cable, dock and adapter. Results reported by users are in
[docs/compatibility.md](docs/compatibility.md). Please
[add yours](https://github.com/ali-caglar/ScreenFerry/issues/new?template=monitor_compatibility.yml).

## Building from source

| Part | Requirements | Build |
|---|---|---|
| macOS app | macOS 13+ to run; Xcode 26+ to build | see [apps/macos/CLAUDE.md](apps/macos/CLAUDE.md) |
| Windows app | Windows 10/11 to run; .NET 10 SDK to build | see [apps/windows/CLAUDE.md](apps/windows/CLAUDE.md) |
| Protocol fixtures | Node.js 24+ | `cd protocol && npm ci && npm run validate` |

Release builds are currently unsigned, so macOS Gatekeeper and Windows SmartScreen will warn.
Signing and notarization come in Phase 5.

## Versioning

Releases are always `x.y.0` (`v0.1.0`, `v0.2.0`, …). Builds in between carry the *next*
version with a build counter (`0.2.1`, `0.2.2`, … on the way to `0.2.0`). Only releases are
meant for users. See [ADR 0004](docs/adr/0004-versioning.md).

## Contributing

Contributions are welcome — especially monitor compatibility reports. Read
[CONTRIBUTING.md](CONTRIBUTING.md) first: PR titles follow Conventional Commits and every
commit needs a DCO sign-off (`git commit -s`). Please follow the
[Code of Conduct](CODE_OF_CONDUCT.md), and report security issues privately as described in
[SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)
