using Microsoft.Win32;

namespace ScreenFerry.Windows;

internal static class MonitorRegistry
{
    /// <summary>Reads EDID for a monitor device path like <c>\\?\DISPLAY#SAM7400#5&amp;1a2b&amp;0&amp;UID256#{guid}</c>.</summary>
    public static byte[] ReadEdid(string devicePath)
    {
        var parts = devicePath.Split('#');
        if (parts.Length < 3)
        {
            return [];
        }
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters");
        return key?.GetValue("EDID") as byte[] ?? [];
    }
}
