using System.Runtime.InteropServices;

namespace ScreenFerry.Windows;

/// <summary>Ctrl+Alt+Shift+1 … 9, system-wide. WM_HOTKEY arrives in the registering thread's message queue.</summary>
public static partial class GlobalHotKeys
{
    public const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkDigit1 = 0x31;
    private const int Count = 9;

    /// <summary>Registers the hotkeys for scenes 1 … <paramref name="count"/> on the calling thread; id = scene digit.</summary>
    public static void Register(int count)
    {
        Unregister();
        for (var digit = 1; digit <= Math.Min(count, Count); digit++)
        {
            _ = RegisterHotKey(0, digit, ModControl | ModAlt | ModShift | ModNoRepeat, VkDigit1 + (uint)digit - 1);
        }
    }

    public static void Unregister()
    {
        for (var digit = 1; digit <= Count; digit++)
        {
            _ = UnregisterHotKey(0, digit);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint window, int id);
}
