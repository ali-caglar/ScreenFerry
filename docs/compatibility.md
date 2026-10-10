# Monitor compatibility

Community-reported results for DDC/CI input switching (VCP `0x60`). To add a result, open a
[monitor compatibility report](https://github.com/ali-caglar/ScreenFerry/issues/new?template=monitor_compatibility.yml);
maintainers copy confirmed reports here.

Legend: ✅ works · ⚠️ partial (see notes) · ❌ fails · — not tested

| Monitor | Firmware | Connection | OS | Read `0x60` | Write `0x60` | Input codes | Notes | Report |
|---|---|---|---|---|---|---|---|---|
| Samsung Odyssey OLED G8 (G80SD) | — | DisplayPort | Windows 11 | ❌ | — | — | No I2C acknowledgement (`0xC0262582`) on the active input, PC as only source. Tizen "smart" model. Works with **Auto Source Switch+**: follows a newly attached source in 1–2 s (software re-attach on Windows and macOS), but not the loss of one (ADR 0005). | maintainer, 2026-10-10 |
| Samsung Odyssey OLED G8 (G80SD) | — | HDMI (MacBook Pro built-in port) | macOS 27, M2 Pro | ❌ | ❌ | — | Write fails (`0xe0114000`). The built-in HDMI port of Apple Silicon MacBook Pros doesn't pass DDC, so this says nothing about the monitor. | maintainer, 2026-10-09 |
| ASUS VG279Q1A | — | DisplayPort | Windows 11 | ✅ | — | — | | maintainer, 2026-10-10 |
| ASUS VG279Q1A | — | USB-C adapter → monitor | macOS 27, M2 Pro | ❌ | ⚠️ | — | Write accepted, read fails (`0xe0114000`). Likely the adapter. | maintainer, 2026-10-09 |
