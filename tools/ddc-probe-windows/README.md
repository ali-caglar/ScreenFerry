# ddc-probe-windows

Phase 1 feasibility tool: lists monitors with their EDID identity and reads or writes VCP
codes over DDC/CI through the Windows Monitor Configuration API (`dxva2`). It uses the same
`ScreenFerry.Core` / `ScreenFerry.Windows` code as the agent, and prints the same identity
as `ddc-probe-macos` for the same monitor.

## Getting it

- **Prebuilt:** every CI run uploads `ddc-probe-windows-x64` (a self-contained `ddc-probe.exe`,
  no .NET install needed) under the run's **Artifacts**. It is unsigned, so SmartScreen may
  warn: **More info → Run anyway**.
- **From source** (.NET 10 SDK):
  ```sh
  dotnet run --project tools/ddc-probe-windows -- list
  ```

## Commands

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
| `displays` | Monitors Windows knows about, attached or detached, numbered `<n>`. |
| `detach <n> [seconds]` | Detach, then re-attach after `seconds` (default 10). Ctrl+C re-attaches now. |
| `attach <n>` | Re-attach without a saved layout (recovery). |
| `release <monitor>` | Detach and record it in `%LOCALAPPDATA%\ScreenFerry\detached-displays.json`, with the layout. |
| `take <monitor> [T]` | Wait until it has been released for `T` s (default 25, ADR 0005), attach, restore the layout. |
| `released` | What this PC has released. |
| `reconcile` | Detach released monitors that came back. |
| `cycle <monitor> <count> [T]` | `release` + `take` repeatedly, checking each step (Phase 2 acceptance). |

`<monitor>` is a number from `displays` or an identity such as `SAM-E030-H1AK500000`.
Uses the CCD API (`SetDisplayConfig`), like "Disconnect this display" in Settings, but works
with any number of monitors, and saves to Windows' display database so a released monitor
stays detached after replugging. A layout saved before a reboot no longer applies (adapter
ids change); Windows then picks one. The last active monitor can't be detached.
