using ScreenFerry.Core;

namespace ScreenFerry.Windows;

/// <param name="Monitors">Monitors this PC sees, attached or detached, as reported to peers.</param>
/// <param name="Ambiguous">Identities shared by two connected monitors; they are left out (ADR 0007).</param>
public sealed record MonitorScan(IReadOnlyList<MonitorStatus> Monitors, IReadOnlyList<string> Ambiguous);

public static class LocalMonitors
{
    public static MonitorScan Scan()
    {
        var groups = DisplayTopology.Targets().Where(t => t.Edid is not null).GroupBy(t => t.Edid!.Identity).ToList();
        bool IsAmbiguous(IGrouping<string, DisplayTarget> g) => g.Select(t => t.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;
        return new MonitorScan(
            [.. groups.Where(g => !IsAmbiguous(g))
                .Select(g => new MonitorStatus(g.Key, g.Any(t => t.IsActive), g.First().Edid!.Name))
                .OrderBy(m => m.Identity, StringComparer.Ordinal)],
            [.. groups.Where(IsAmbiguous).Select(g => g.Key).Order(StringComparer.Ordinal)]);
    }
}

/// <summary>VCP 0x60 of a monitor by identity.</summary>
public static class InputSource
{
    public static int Read(string identity) => Use(identity, d => (int)(d.GetVcp(0x60).Current & 0xFF));

    public static void Set(string identity, int code) => Use(identity, d =>
    {
        d.SetVcp(0x60, (uint)code);
        return 0;
    });

    private static T Use<T>(string identity, Func<PhysicalDisplay, T> action)
    {
        var displays = PhysicalDisplay.All();
        try
        {
            var display = displays.FirstOrDefault(d => d.Edid?.Identity == identity)
                ?? throw new InvalidOperationException($"No attached monitor with identity {identity}.");
            return action(display);
        }
        finally
        {
            foreach (var display in displays)
            {
                display.Dispose();
            }
        }
    }
}
