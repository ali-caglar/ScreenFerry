# Installing ScreenFerry

Download the files for your computers from the
[latest release](https://github.com/ali-caglar/ScreenFerry/releases/latest). Builds are not
signed yet ([ADR 0008](adr/0008-unsigned-releases-before-signing.md)), so each OS warns
once, on first launch or when you run the installer. Install ScreenFerry on every computer that shares the monitors.

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

1. Download `ScreenFerry-X.Y.0-windows-x64-setup.exe` (`windows-arm64` on an ARM PC such as a
   Snapdragon laptop) and run it. SmartScreen says it protected your PC; click
   **More info → Run anyway**.
2. Keep **Start ScreenFerry when I sign in** checked if you want it to start with Windows, and
   click **Install**. No administrator rights are needed; it installs for your user only.
3. Windows Firewall asks about network access; allow it on **private networks**.
4. ScreenFerry lives in the notification area. Open it again from the Start menu; turn
   autostart on or off with **Start with Windows** in its icon's menu.

Uninstall it in **Settings → Apps → Installed apps**. Pairings and scenes stay, so a
reinstall picks up where you left off.

Coming from the 0.1.0 zip? Quit that ScreenFerry from its tray menu first, run the installer,
then delete the old `ScreenFerry.exe`.

## Updates

Once installed, ScreenFerry checks GitHub for a new release at launch and once a day,
verifies its signature, installs it while no monitors are moving, and restarts itself
([ADR 0009](adr/0009-automatic-updates.md)). **Check for Updates…** in the menu (macOS) or
**Check for Updates** in the tray menu (Windows) checks right away. That check is the only
connection ScreenFerry makes outside your local network.

## Pair and move monitors

1. On both computers, click **Add Computer…**, then **Pair** next to the other one.
2. Check that both show the same six-digit code and click **Codes Match** on both.
3. Each computer gets a built-in scene per computer ("All Monitors → …"), bound to ⌃⌥⌘1–9 on
   a Mac and Ctrl+Alt+Shift+1–9 on Windows, in the order the scenes are listed.

A monitor without DDC/CI needs its own *auto source switch* setting turned on (Samsung calls
it Auto Source Switch+). Such a monitor only follows a source after it has been gone for about
25 seconds, so moving it back quickly takes a moment.
