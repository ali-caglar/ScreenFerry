# ddc-probe-macos

Phase 1 feasibility tool: lists external monitors with their EDID identity and reads or
writes VCP codes over DDC/CI. It uses the same `ScreenFerryKit` / `ScreenFerryDDC` code
as the agent.

Apple Silicon only (it uses the private `IOAVService` I2C API, like
[m1ddc](https://github.com/waydabber/m1ddc)). Intel Macs are not supported yet.

```sh
swift build -c release --package-path tools/ddc-probe-macos
tools/ddc-probe-macos/.build/release/ddc-probe list
```

| Command | What it does |
|---|---|
| `list` | Monitors, identity, current input (`0x60`) and brightness (`0x10`). Read-only. |
| `edid <n>` | Hex dump of the EDID. |
| `caps <n>` | MCCS capabilities string; shows which inputs the monitor says it has. Read-only. |
| `get <n> <vcp>` | Read a VCP code, e.g. `get 1 0x60`. |
| `set <n> <vcp> <value>` | Write a VCP code, e.g. `set 1 0x10 30`. |

`<n>` is the number from `list`. Try brightness (`0x10`) before input (`0x60`): switching the
input of the only monitor you are looking at sends it to the other computer.

### Detach and attach (Phase 2)

| Command | What it does |
|---|---|
| `displays` | Displays macOS knows, with their CoreGraphics `<id>`; detached ones included. |
| `detach <id> [seconds]` | Detach, then re-attach after `seconds` (default 10). Ctrl+C re-attaches now. |
| `attach <id>` | Re-attach by id (recovery). |
| `release <monitor>` | Detach and record it in `~/Library/Application Support/ScreenFerry/detached-displays.json`. |
| `take <monitor> [T]` | Wait until it has been released for `T` s (default 25, ADR 0005), attach, verify. |
| `released` | What this Mac has released. |
| `reconcile` | Detach released monitors that came back, e.g. after logging in again. |
| `cycle <monitor> <count> [T]` | `release` + `take` repeatedly, checking each step (Phase 2 acceptance). |

`<monitor>` is an id from `displays` or an identity such as `SAM-E030-H1AK500000`.
Uses the private SkyLight call `SLSConfigureDisplayEnabled` for the login session only:
logging out restores every display, which is what `reconcile` is for. The built-in display
is never touched, and the last active display can't be detached. One monitor can show up
under several ids; `take` tries them all and checks the monitor by identity.
