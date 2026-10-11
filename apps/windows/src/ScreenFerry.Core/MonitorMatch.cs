namespace ScreenFerry.Core;

/// <summary>Finds a computer's own monitor for one reported by another computer (ADR 0007, <c>protocol/test-vectors/monitor-matching.json</c>).</summary>
public static class MonitorMatch
{
    public static MonitorStatus? Find(MonitorStatus reference, IEnumerable<MonitorStatus> candidates)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var list = candidates.ToList();
        if (list.FirstOrDefault(c => c.Identity == reference.Identity) is { } exact)
        {
            return exact;
        }
        if (LooseKey(reference) is not { } key)
        {
            return null;
        }
        var matches = list.Where(c => LooseKey(c) == key).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>Manufacturer, serial part and name; null for hash identities and unnamed monitors.</summary>
    private static string? LooseKey(MonitorStatus monitor)
    {
        if (string.IsNullOrEmpty(monitor.Name))
        {
            return null;
        }
        var parts = monitor.Identity.Split('-', 3);
        if (parts.Length != 3 || parts[0].Length != 3 || !parts[0].All(char.IsAsciiLetterUpper)
            || parts[1].Length != 4 || !parts[1].All(char.IsAsciiHexDigit) || parts[2].Length == 0)
        {
            return null;
        }
        return $"{parts[0]}\n{parts[2]}\n{monitor.Name}";
    }
}
