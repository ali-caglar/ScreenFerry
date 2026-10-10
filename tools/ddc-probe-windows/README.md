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

Please report results with the
[monitor compatibility form](https://github.com/ali-caglar/ScreenFerry/issues/new?template=monitor_compatibility.yml).
