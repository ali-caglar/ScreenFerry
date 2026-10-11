using Microsoft.Win32;

namespace ScreenFerry.Windows;

/// <summary>Starts the app when the user signs in, through the per-user Run key.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenFerry";

    public static bool IsEnabled(string executable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string command && command == Quote(executable);
    }

    public static void Set(bool enabled, string executable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(ValueName, Quote(executable));
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string Quote(string path) => $"\"{path}\"";
}
