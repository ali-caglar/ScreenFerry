# Windows agent

C# on .NET 10, WPF, tray icon via `System.Windows.Forms.NotifyIcon` (ADR 0002).

## Layout

- `src/ScreenFerry.Core/` — `net10.0`, no Windows dependency: protocol models, pairing, and
  the agent's networking (`AgentIdentity`, `PeerConnection` over `SslStream`, `Agent`).
  Builds and tests on any OS; DNS-SD plugs in through `IPeerDiscovery`.
- `src/ScreenFerry.App/` — `net10.0-windows` WPF tray app (`ScreenFerry.exe`); CI publishes it
  as the `screenferry-windows-x64` artifact. `UseWindowsForms`
  is on only for `NotifyIcon`; the implicit `System.Windows.Forms` using is removed to avoid
  clashes with WPF types, so alias it (`using Forms = System.Windows.Forms;`).
- `src/ScreenFerry.Windows/` — `net10.0-windows` library for Windows APIs (P/Invoke via
  `LibraryImport`): DDC/CI through `dxva2`, EDID from the registry, CCD detach/attach, DNS-SD
  through `dnsapi.dll` (`DnsSdDiscovery`), the agent key in the user certificate store. Used by the app and
  `tools/ddc-probe-windows` (which is also in `ScreenFerry.sln`).
- `tests/ScreenFerry.Core.Tests/` — xUnit. Protocol fixtures and test vectors are copied into
  the test output under `fixtures/` and `test-vectors/`.
- `Directory.Build.props` — shared settings (nullable, warnings as errors, analyzers,
  local default version — never `x.y.0`). `Directory.Packages.props` — all package versions.

## Build and test

```sh
dotnet restore apps/windows/ScreenFerry.sln --locked-mode
dotnet build   apps/windows/ScreenFerry.sln -c Release --no-restore -p:Version="$(scripts/version.sh)"
dotnet test    apps/windows/ScreenFerry.sln -c Release --no-build
dotnet publish apps/windows/src/ScreenFerry.App -c Release -r win-x64 --self-contained false
```

`EnableWindowsTargeting` lets the whole solution build on macOS/Linux; the app only runs on
Windows.

## Notes

- NuGet lock files (`packages.lock.json`) are committed and CI restores in locked mode. After
  changing a package, run `dotnet restore` (without `--locked-mode`) and commit the lock files.
- Add package versions to `Directory.Packages.props`, not to `.csproj` files.
- A self-contained `dotnet publish -r <rid>` adds RID entries to the lock files. Don't commit
  those changes; CI enforces the lock files with `dotnet restore --locked-mode` before publishing.
- Windows APIs (Monitor Configuration API in `dxva2`, CCD `QueryDisplayConfig`/`SetDisplayConfig`)
  go in `ScreenFerry.Windows`, never in Core.
