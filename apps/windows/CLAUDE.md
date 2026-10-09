# Windows agent

C# on .NET 10, WPF, tray icon via `System.Windows.Forms.NotifyIcon` (ADR 0002).

## Layout

- `src/ScreenFerry.Core/` — `net10.0`, no Windows dependency: protocol models and logic.
  Builds and tests on any OS.
- `src/ScreenFerry.App/` — `net10.0-windows` WPF app (`ScreenFerry.exe`). `UseWindowsForms`
  is on only for `NotifyIcon`; the implicit `System.Windows.Forms` using is removed to avoid
  clashes with WPF types, so alias it (`using Forms = System.Windows.Forms;`).
- `tests/ScreenFerry.Core.Tests/` — xUnit. Protocol fixtures are copied into the test output
  under `fixtures/`.
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
- Windows APIs (Monitor Configuration API in `dxva2`, CCD `QueryDisplayConfig`/`SetDisplayConfig`)
  go through P/Invoke in the app or a future `ScreenFerry.Windows` library, never in Core.
