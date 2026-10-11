# Installing ScreenFerry

Download the files for your computers from the
[latest release](https://github.com/ali-caglar/ScreenFerry/releases/latest). Builds are not
signed yet ([ADR 0008](adr/0008-unsigned-releases-before-signing.md)), so each OS warns
once on first launch. Install ScreenFerry on every computer that shares the monitors.

## macOS

Requires macOS 13 or later. Apple silicon is needed for DDC/CI input switching; on Intel Macs
only monitors with auto source switching work (ADR 0005).

1. Download `ScreenFerry-X.Y.0-macos-unsigned.zip`, open it, and move **ScreenFerry** to
   **Applications**.
2. Open it. macOS says it can't verify the developer; click **Done**.
3. Open **System Settings → Privacy & Security**, scroll to *Security*, and click
   **Open Anyway** next to ScreenFerry. Confirm with your password.
4. Allow **local network** access when asked; ScreenFerry finds the other computers on your
   network with Bonjour.
5. ScreenFerry lives in the menu bar (two-display icon). To start it with your Mac, turn on
   **Open at Login** in its menu.

After an update, macOS asks once whether ScreenFerry may use its key in your keychain. Click
**Always Allow**; denying it stops ScreenFerry from starting.

## Windows

Requires Windows 10 version 1903 or later, or Windows 11.

1. Download `ScreenFerry-X.Y.0-win-x64-unsigned.zip` (or `win-arm64` on ARM), and extract
   `ScreenFerry.exe` to a folder it can stay in, for example
   `%LOCALAPPDATA%\Programs\ScreenFerry`.
2. Run it. SmartScreen says it protected your PC; click **More info → Run anyway**.
3. Windows Firewall asks about network access; allow it on **private networks**.
4. ScreenFerry lives in the notification area. To start it with Windows, right-click its icon
   and check **Start with Windows**.

## Pair and move monitors

1. On both computers, click **Add Computer…**, then **Pair** next to the other one.
2. Check that both show the same six-digit code and click **Codes Match** on both.
3. Each computer gets a built-in scene per computer ("All Monitors → …"), bound to ⌃⌥⌘1–9 on
   a Mac and Ctrl+Alt+Shift+1–9 on Windows, in the order the scenes are listed.

A monitor without DDC/CI needs its own *auto source switch* setting turned on (Samsung calls
it Auto Source Switch+). Such a monitor only follows a source after it has been gone for about
25 seconds, so moving it back quickly takes a moment.
