# ScreenFerry — Project Brief

> Working name. Status: Phase 3 (agent, discovery, pairing); Phase 0 completed 2026-10-09, Phases 1–2 2026-10-10. Owner/maintainer: Ali.
> This brief is the source of truth for scope and architecture. Decisions that change it
> are recorded as ADRs in `docs/adr/` and then reflected here.

## 1. Problem

People who run two or more computers on a shared set of monitors (2 Macs, or a Mac + a
Windows PC) switch monitor inputs with buttons on the back of the monitor or a remote.
That is slow and annoying. Worse, when a monitor is switched to the other computer, the
first computer often still believes the monitor is connected — especially over HDMI — so
windows, dialogs and the cursor disappear onto a screen nobody can see.

## 2. What ScreenFerry does

A small agent runs on every computer. With one hotkey (or a click in the menu bar / tray),
the user activates a **scene** such as "both monitors → Mac", "left → Mac, right → PC" or
"both → PC". The agents then, in the right order:

1. switch each monitor's input source over **DDC/CI** (VCP code `0x60`), and
2. **detach** the monitor from the desktop of the machine that no longer owns it,
   and **re-attach** it on the machine that now does.

### Goals (v1)
- macOS (Apple Silicon first) and Windows 10/11.
- 2–4 computers, 1–4 external monitors, all on one local network.
- Scenes, global hotkeys, menu-bar / tray UI.
- Secure pairing; agents only obey paired peers.
- Works offline on the LAN. No accounts, no cloud, no telemetry.

### Non-goals (v1)
- Keyboard/mouse sharing (integration with Deskflow / Input Leap is a later idea).
- Linux agent (design must not prevent it).
- Brightness/volume control UI (DDC makes it easy; out of scope until v1 ships).
- USB peripheral switching.

## 3. How it works

### 3.1 Monitor identity
The same physical monitor is seen by several computers. Agents identify it by EDID
(manufacturer + product code + serial number, falling back to a hash of the EDID block).

### 3.2 Input codes are learned, not hard-coded
`0x60` values differ per monitor model. While a machine is the active source of a monitor,
its agent reads the current `0x60` value: that is "my input code" for that monitor.
Agents share these codes with paired peers, so nobody needs a per-model table.

### 3.3 The handoff order matters
A machine generally cannot send DDC to a monitor it has detached, and some monitors only
accept DDC from the active input. Therefore the **current owner** performs the switch:

1. Owner A sends DDC `0x60 = B's input code` to monitor X.
2. A detaches X from its own desktop (moving windows off it first).
3. B attaches X to its desktop.
4. Ownership of X is updated and broadcast to the group.

If step 1 fails, nothing else happens and the user is told why.

Monitors without DDC/CI but with an "auto source switch" setting use the reverse order
(ADR 0005): the new owner attaches first, the monitor follows the new signal, then the old
owner detaches. Non-owners keep such monitors detached.

### 3.4 Groups, pairing and conflicts
- Agents discover each other with DNS-SD/mDNS (`_screenferry._tcp`).
- Pairing: user confirms a short code shown on both machines. Agents exchange long-term
  keys; all later traffic is mutually authenticated and encrypted. Unpaired agents on the
  same network are ignored. (Exact mechanism: ADR in Phase 3; no home-made crypto.)
- Every scene change carries a monotonically increasing sequence number; the group applies
  changes one at a time and the newest wins. A short lock prevents overlapping handoffs.

### 3.5 Platform notes
| Concern | macOS | Windows |
|---|---|---|
| DDC/CI | Apple Silicon: IOAVService I2C (private API, see `m1ddc` as reference). Intel: IOFramebuffer I2C. | Monitor Configuration API (`dxva2`: `GetPhysicalMonitorsFromHMONITOR`, `Get/SetVCPFeature`). |
| Detach / attach display | No public API. Private CoreGraphics/SkyLight display-enable calls (the approach BetterDisplay uses). Must be isolated + feature-checked. | CCD API (`QueryDisplayConfig` / `SetDisplayConfig`), same as "Disconnect this display" in Settings. |
| EDID | IORegistry | Registry / WMI (`WmiMonitorID`) |
| UI | Menu-bar app (SwiftUI `MenuBarExtra`) | System tray app |
| Autostart | Login item (`SMAppService`) | Startup registration |

## 4. Monorepo layout

```
screenferry/
├── apps/
│   ├── macos/            # ScreenFerryKit Swift package + Xcode project, own CLAUDE.md with build notes
│   └── windows/          # .NET solution (Core, App, tests), own CLAUDE.md with build notes
├── protocol/
│   ├── README.md         # spec + protocolVersion history
│   ├── schemas/          # JSON Schema per message type
│   ├── fixtures/         # golden messages; both agents' tests must parse/produce these
│   └── scripts/          # fixture validation (CI check)
├── tools/
│   ├── ddc-probe-macos/  # CLI: list monitors, EDID, read/write VCP
│   └── ddc-probe-windows/
├── scripts/version.sh    # build version (ADR 0004)
├── docs/
│   ├── BRIEF.md
│   ├── adr/              # 0001-record-architecture-decisions.md, ..., template.md
│   └── compatibility.md  # community-reported monitor results
├── .github/
│   ├── workflows/
│   ├── ISSUE_TEMPLATE/
│   ├── PULL_REQUEST_TEMPLATE.md
│   ├── CODEOWNERS
│   └── dependabot.yml
├── CLAUDE.md  README.md  LICENSE  CONTRIBUTING.md  CODE_OF_CONDUCT.md  SECURITY.md
├── .editorconfig  .gitattributes  .gitignore
└── release-please-config.json  .release-please-manifest.json
```

`.gitattributes` must normalize line endings (`* text=auto eol=lf`, with `*.sln`, `*.csproj`,
`*.ps1`, `*.cmd` as `eol=crlf`) because contributors work on both macOS and Windows.

## 5. Stack (ADR 0002)
- macOS agent: Swift 6, SwiftUI, Swift Package Manager; minimum macOS 13.
- Windows agent: C#, .NET 10 (LTS), WPF; tray icon via WinForms `NotifyIcon`.
- Protocol: JSON messages over an authenticated, encrypted TCP channel; JSON Schemas
  in `protocol/` are normative.
- Why two native codebases instead of one cross-platform one: nearly all hard work is
  platform API calls; native code keeps those simple and debuggable. The shared contract is
  the protocol, enforced by golden fixtures.

## 6. Roadmap

Each phase ends with its acceptance criteria met and merged to `main`.

**Phase 0 — Repository bootstrap**
- All files in §4 skeleton, community files (§7.5), CI building both apps on every PR,
  PR-title check, release-please, Dependabot, CodeQL, Scorecard.
- ADR 0001 (use ADRs), ADR 0002 (stack), ADR 0003 (license + DCO).
- Done when: an empty-but-building app exists for each platform, CI is green, and a test PR
  with a bad title is rejected.

**Phase 1 — DDC probes (feasibility gate)**
- `tools/ddc-probe-*`: list monitors with EDID identity; read and write VCP `0x60`
  (and `0x10` brightness as a harmless test).
- Done when: maintainer can switch his monitor's input from each OS using the CLI.
  Maintainer test hardware: Samsung Odyssey OLED G8 (Samsung DDC support is known to be
  inconsistent — if it fails, document it and evaluate fallbacks before Phase 2).

**Phase 2 — Attach/detach displays**
- Platform module that detaches/re-attaches a given monitor and moves windows off it first.
- Done when: a monitor can be detached and restored repeatedly, surviving sleep/wake.
- Result: `DisplayHandoff` in both agents (release ledger, minimum absence, reconcile). The OS
  moves windows off a detached display. Acceptance on the G8 is recorded in ADR 0005.

**Phase 3 — Agent, discovery, pairing, protocol v1**
- Background agent, DNS-SD discovery, pairing flow, encrypted channel, ownership state.
- Done when: a Mac and a Windows PC pair, show each other's monitors, and survive restarts.

**Phase 4 — Scenes, hotkeys, UI**
- Scene editor, global hotkeys, menu-bar/tray UI, the handoff sequence from §3.3.
- Done when: one hotkey moves both monitors from Mac to PC and back, 20 times in a row.

**Phase 5 — Distribution**
- macOS: Developer ID signing + notarization; Windows: signed installer, winget manifest;
  Homebrew cask. Signing material only as CI secrets, never in the repo.

## 7. Repository standards (public repo)

These are the rules behind `CLAUDE.md`, with the reasoning.

### 7.1 Branching — GitHub Flow
`main` is always releasable. Work happens on short-lived branches named
`<type>/<description>` and lands via PR. No `develop`/`release` branches: release-please
handles releases from `main`.

### 7.2 Commits — Conventional Commits 1.0.0
`feat` maps to a SemVer minor bump, `fix` to a patch, and `!` or a `BREAKING CHANGE:` footer
to a major bump; that is what lets release-please compute versions and changelogs.
Allowed types follow `@commitlint/config-conventional` (`build`, `chore`, `ci`, `docs`,
`feat`, `fix`, `perf`, `refactor`, `revert`, `style`, `test`). Scopes are listed in CLAUDE.md.

### 7.3 Merging
Squash-merge only, PR title = final commit message. This keeps `main` linear and means
contributors don't need perfect intermediate commits — only the PR title is enforced
(CI check, e.g. `amannn/action-semantic-pull-request`).

### 7.4 Releases
release-please in manifest mode with a single root package (lockstep version, tags `vX.Y.0`,
`versioning: always-bump-minor`, ADR 0004). Every release is `x.y.0`; builds between releases
are `x.(y+1).N` with `N ≥ 1` (`scripts/version.sh`). Majors are cut with a `Release-As:` footer.
The release jobs build both apps and attach artifacts to the GitHub Release. Publishing never
happens inside the release-please job itself.

### 7.5 Community and legal files
`LICENSE`, `README.md`, `CONTRIBUTING.md` (setup, branch/commit rules, DCO), `CODE_OF_CONDUCT.md`
(Contributor Covenant), `SECURITY.md` (points to GitHub private vulnerability reporting),
issue forms: **bug report**, **feature request**, **monitor compatibility report**
(model, connection, OS, DDC result — feeds `docs/compatibility.md`), PR template with a
checklist, `CODEOWNERS`.

**Contributions: DCO, not a CLA.** Contributors add `Signed-off-by` with `git commit -s`,
enforced on PRs by a DCO GitHub App. Lower friction than a CLA, standard for community projects.

### 7.6 Supply-chain and security hygiene (aligned with OpenSSF Scorecard checks)
- Branch protection via rulesets (below), CI on every PR, code review through PRs.
- GitHub Actions pinned to full commit SHAs; workflow `permissions:` least-privilege.
- Dependabot version updates for GitHub Actions, NuGet and Swift packages.
- CodeQL (supports C# and Swift), secret scanning + push protection.
- Scorecard workflow + badge in README.
- No binaries committed to the repo.

## 8. GitHub settings checklist (maintainer, one-time)

Claude Code cannot change these through files; do them in GitHub (or via `gh api`):

- [x] General: allow **squash merging only**; default squash message = PR title + description;
      **automatically delete head branches**.
- [x] Ruleset for `main` (Settings → Rules → Rulesets):
  - [x] Restrict deletions; block force pushes.
  - [x] Require a pull request before merging. Required approvals: **0 while solo**
        (you cannot approve your own PR), raise to 1 when a second maintainer joins.
        Dismiss stale approvals; require conversation resolution.
  - [x] Require status checks (exact names): `macOS build/test`, `Windows build/test`,
        `Protocol fixtures`, `PR title`, `DCO`. Require branches to be up to date.
  - [x] Require linear history.
  - [x] **Do not** enable "Require signed commits" for now: with it, GitHub blocks
        squash-merging PRs you didn't author. Instead sign your own commits (SSH or GPG
        signing + vigilant mode).
- [x] Security: private vulnerability reporting **on**, Dependabot alerts + security updates,
      secret scanning + push protection. CodeQL runs from `.github/workflows/codeql.yml`
      (advanced setup, needed for Swift) — leave CodeQL **default setup off**.
- [x] Release PRs: pull requests opened with `GITHUB_TOKEN` do not trigger CI, so required
      checks would never run on them. Create a fine-grained PAT for this repo only
      (Contents: read/write, Pull requests: read/write) and save it as the Actions secret
      `RELEASE_PLEASE_TOKEN`. Also enable Settings → Actions → General →
      "Allow GitHub Actions to create and approve pull requests".
- [x] Install the DCO GitHub App.
- [x] Topics: `multi-monitor`, `kvm`, `ddc-ci`, `macos`, `windows`, `display`, `utility`.

## 9. Decisions

Resolved in Phase 0:
- **License** — MIT (ADR 0003).
- **Windows UI toolkit** — WPF, tray icon via WinForms `NotifyIcon` (ADR 0002).
- **Minimum macOS version** — 13 (ADR 0002).
- **Versioning** — releases are always `x.y.0` (ADR 0004).

Still open:
1. **Final name** — "ScreenFerry" checked: no conflicting project in this category; an
   unrelated QR file-transfer library uses the same name on GitHub/npm. Domain not yet checked.

## 10. Risks

- Monitors with weak or missing DDC/CI support (notably some Samsung models) → Phase 1 gate,
  public compatibility list, auto source switching (ADR 0005). Confirmed: the maintainer's
  G8 has no DDC/CI.
- macOS private display APIs can break on OS updates → isolation, feature checks, fallback
  ("switch input only, don't detach" — `ddc` monitors only; `autoSource` monitors need detach).
- Docks, KVMs and some USB-C adapters block DDC → document; detect and warn.
- Unsigned builds trigger Gatekeeper/SmartScreen warnings → Phase 5 signing.

## 11. References

- Conventional Commits 1.0.0 — https://www.conventionalcommits.org/en/v1.0.0/
- GitHub rulesets: available rules — https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets
- release-please manifest releaser — https://github.com/googleapis/release-please/blob/main/docs/manifest-releaser.md
- GitHub community health files — https://docs.github.com/en/communities/setting-up-your-project-for-healthy-contributions/creating-a-default-community-health-file
- OpenSSF Scorecard — https://github.com/ossf/scorecard
- DCO — https://developercertificate.org/
- m1ddc (DDC on Apple Silicon, reference) — https://github.com/waydabber/m1ddc
