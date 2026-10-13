# 0010. Windows installer

- Status: Accepted
- Date: 2026-10-13

## Context

v0.1.0 shipped Windows as a zip with one `ScreenFerry.exe` (ADR 0008). Users had to pick a
folder for it, the updater could only replace an exe in a folder they can write to
(ADR 0009), and nothing showed up in Start or in *Installed apps*. The maintainer wants a
normal installer.

## Decision

Each release ships `ScreenFerry-X.Y.0-windows-x64-setup.exe` and `-arm64-setup.exe`, built
with [Inno Setup](https://jrsoftware.org/isinfo.php) 6 from `apps/windows/installer/ScreenFerry.iss`.
They replace the Windows zips of ADR 0008.

- **Per user, no admin.** Installs to `%LOCALAPPDATA%\Programs\ScreenFerry`, adds a Start menu
  entry and an *Installed apps* entry, and offers "Start ScreenFerry when I sign in" (the same
  Run value as the app's **Start with Windows**). Uninstalling removes that value and keeps the
  pairing and scenes.
- **One installer per architecture.** The self-contained exe is left uncompressed so the
  installer's LZMA2 can pack it: about 49 MB instead of 68 MB. Both architectures in one
  installer would double every download.
- **Updates run the installer.** `windows-update.json` signs the setup exes instead of the
  zips. The agent downloads and verifies one, starts it with `/VERYSILENT /UPDATED=X.Y.0` and
  quits; the installer waits for the agent's `ScreenFerry.Instance` mutex to go away, replaces
  the exe, keeps the user's autostart choice and starts the new version.
- **One instance.** The agent holds that mutex; starting it again opens the running agent's
  window.
- CI builds the x64 installer for every PR, so the script can't break unnoticed.

## Consequences

- SmartScreen warns about the setup exe instead of the app until Authenticode signing exists;
  a silent update isn't downloaded by a browser, so it gets no warning.
- Agents on the v0.1.0 zip can't install the new manifest's setup exe; they need one manual
  install of the next release.
- The installer is the place to add Authenticode signing and the winget manifest later.
