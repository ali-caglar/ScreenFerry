# Contributing to ScreenFerry

Thanks for helping. This page covers setup, the branch/commit/PR rules, and the sign-off
every commit needs. The reasoning behind these rules is in [docs/BRIEF.md](docs/BRIEF.md) §7.

## Ways to help

- **Monitor compatibility reports** — the most useful thing early on. Use the
  [compatibility issue form](https://github.com/ali-caglar/ScreenFerry/issues/new?template=monitor_compatibility.yml).
- Bug reports and feature requests through the issue forms.
- Code: please open an issue first for anything larger than a small fix, so we can agree on
  the approach before you spend time on it.

## Setup

```sh
git clone https://github.com/ali-caglar/ScreenFerry.git
cd ScreenFerry
```

- **macOS app** — Xcode 26 or newer. Build notes: [apps/macos/CLAUDE.md](apps/macos/CLAUDE.md).
- **Windows app** — .NET 10 SDK (Visual Studio 2026 or Rider optional). Build notes:
  [apps/windows/CLAUDE.md](apps/windows/CLAUDE.md).
- **Protocol** — Node.js 24+: `cd protocol && npm ci && npm run validate`.

Line endings are normalized by `.gitattributes` (LF everywhere, CRLF for `.sln`, `.csproj`,
`.ps1`, `.cmd`). Your editor should pick up `.editorconfig`.

## Workflow

We use GitHub Flow: `main` is always releasable, and all work lands through pull requests.

1. Branch from `main` as `<type>/<short-description>`, e.g. `feat/windows-tray-menu`,
   `fix/macos-edid-serial`.
2. Commit with sign-off (`git commit -s`, see below).
3. Open a PR. CI must be green; fill in the PR template checklist.
4. PRs are **squash-merged**: the PR title becomes the commit on `main`.

### PR titles (Conventional Commits)

Only the PR title is enforced — your intermediate commits can be anything, as long as they
are signed off. Format:

```
type(scope): subject in lowercase imperative
```

**Types:** `build`, `chore`, `ci`, `docs`, `feat`, `fix`, `perf`, `refactor`, `revert`,
`style`, `test`.

**Scopes (optional):**

| Scope | Covers |
|---|---|
| `macos` | `apps/macos/` |
| `windows` | `apps/windows/` |
| `protocol` | `protocol/` |
| `probe` | `tools/ddc-probe-*` |
| `adr` | `docs/adr/` |
| `ci` | `.github/workflows/`, `scripts/` |
| `deps` | dependency updates |
| `repo` | repository-wide files and settings |

Examples: `feat(windows): add tray menu`, `fix(macos): read edid serial on intel macs`,
`docs(adr): record pairing mechanism`.

`feat` and `fix` appear in the changelog. Every release bumps the minor version
([ADR 0004](docs/adr/0004-versioning.md)).

## Developer Certificate of Origin (DCO)

We use the [DCO](https://developercertificate.org/) instead of a CLA. By signing off you
certify that you wrote the change or otherwise have the right to submit it under the
project's MIT license. Every commit in a PR needs a trailer that matches the commit author:

```
Signed-off-by: Your Name <you@example.com>
```

`git commit -s` adds it. To fix commits that are missing it:

```sh
git commit --amend -s --no-edit        # last commit only
git rebase --signoff main              # every commit on your branch
git push --force-with-lease
```

The DCO check on the PR turns green once all commits are signed off.

## Changes that need more than code

- **Protocol:** update `protocol/schemas/`, add or update `protocol/fixtures/`, make both
  agents' tests pass, and record any `protocolVersion` change in `protocol/README.md`.
- **Architecture, scope, security model, licensing, release process:** add an ADR in
  `docs/adr/` (copy `template.md`) and update `docs/BRIEF.md` in the same PR.

## Style

- Follow `.editorconfig`. Windows builds treat warnings as errors.
- Keep comments short: say why, not what.
- Never commit binaries, secrets or signing material.
