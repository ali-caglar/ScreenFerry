# ScreenFerry

Agents on macOS and Windows that switch shared monitors between computers over DDC/CI and
detach/attach them from each desktop. `docs/BRIEF.md` is the source of truth for scope,
architecture and roadmap; check which phase is current before adding features.

## Layout

- `apps/macos/` — Swift agent. Build notes: `apps/macos/CLAUDE.md`.
- `apps/windows/` — .NET agent. Build notes: `apps/windows/CLAUDE.md`.
- `protocol/` — JSON Schemas (normative) and golden fixtures shared by both agents.
- `tools/ddc-probe-*` — Phase 1 CLIs.
- `docs/adr/` — decisions. `scripts/version.sh` — build version.

## Rules

- Branch from `main` as `<type>/<description>`; never commit directly to `main`.
- PR title = squash commit = `type(scope): subject` (Conventional Commits; start the subject
  lowercase — not enforced, since Dependabot capitalizes).
  Types: `build chore ci docs feat fix perf refactor revert style test`.
  Scopes: `macos windows protocol probe adr ci deps repo`. The lists live in
  `.github/workflows/pr-title.yml` and `CONTRIBUTING.md`; change all three together.
- Every commit is signed off: always `git commit -s`.
- A change to stack, scope, protocol transport, security model, license or release process
  needs a new ADR (`docs/adr/template.md`) and a matching `docs/BRIEF.md` edit in the same PR.
- Protocol changes: schema + fixtures + both agents' tests in the same PR; record
  `protocolVersion` changes in `protocol/README.md`.
- Versions: releases are always `x.y.0` (release-please, `always-bump-minor`); any other build
  is `x.(y+1).N`, N ≥ 1, from `scripts/version.sh`. Never hard-code `x.y.0` in the tree.
- GitHub Actions: pin by full commit SHA with a `# vX.Y.Z` comment; least-privilege
  `permissions:`; `persist-credentials: false` on checkout. Lint with `actionlint`.
- Required check names (`macOS build/test`, `Windows build/test`, `Protocol fixtures`,
  `PR title`) are referenced by the branch ruleset; renaming a job breaks merging.
- macOS private display APIs (SkyLight/CoreGraphics, IOAVService) stay isolated behind a
  feature check with a fallback; never call them from UI code directly.
- No binaries, secrets or signing material in the repo. No telemetry, no network calls
  outside the LAN.
- Comments: short, only for what the code cannot say.

## Checks before a PR

```sh
swift test --package-path apps/macos/ScreenFerryKit
dotnet test apps/windows/ScreenFerry.sln        # needs the .NET 10 SDK
(cd protocol && npm ci && npm run validate)
```
