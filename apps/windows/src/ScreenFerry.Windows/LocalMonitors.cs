using ScreenFerry.Core;

namespace ScreenFerry.Windows;

public static class LocalMonitors
{
    /// <summary>Monitors this PC sees, attached or detached, as reported to peers.</summary>
    public static IReadOnlyList<MonitorStatus> Current() =>
        [.. DisplayTopology.Targets()
            .Where(t => t.Edid is not null)
            .GroupBy(t => t.Edid!.Identity)
            .Select(g => new MonitorStatus(g.Key, g.Any(t => t.IsActive), g.First().Edid!.Name))
            .OrderBy(m => m.Identity, StringComparer.Ordinal)];
}
