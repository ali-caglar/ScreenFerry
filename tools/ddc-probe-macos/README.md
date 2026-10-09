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

## Reading the results

- `IOAVServiceWriteI2C failed (IOReturn 0xe0114000)` — nothing answered on the I2C bus. Common
  causes: DDC/CI turned off in the monitor menu; a dock, hub, KVM or adapter that doesn't pass
  DDC; the built-in HDMI port of Apple Silicon MacBook Pros / Mac minis (use USB-C to
  DisplayPort or USB-C to HDMI instead).
- `IOAVServiceReadI2C failed` with a successful write — the monitor (or something in between)
  accepts commands but doesn't answer. Writes may still work: try `set <n> 0x10 30`.
- `reports VCP 0x.. as unsupported` — DDC works; the monitor doesn't implement that code.

Please report results with the
[monitor compatibility form](https://github.com/ali-caglar/ScreenFerry/issues/new?template=monitor_compatibility.yml).
