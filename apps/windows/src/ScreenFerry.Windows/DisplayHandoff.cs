using ScreenFerry.Core;

namespace ScreenFerry.Windows;

/// <summary>Releases and takes monitors by identity, remembering what this PC released (ADR 0005).</summary>
public sealed class DisplayHandoff(DetachLedgerStore store)
{
    public DisplayHandoff()
        : this(DetachLedgerStore.Standard())
    {
    }

    public DetachLedgerStore Store { get; } = store;

    public IReadOnlyDictionary<string, DetachLedgerEntry> Released() => Store.Load().Monitors;

    /// <summary>Detaches the monitor and records when, with the layout to restore on <see cref="Take"/>.</summary>
    public void Release(string identity, DateTimeOffset? now = null)
    {
        var target = Find(identity);
        var previous = Store.Load().Monitors.GetValueOrDefault(identity);
        var timestamp = now ?? DateTimeOffset.UtcNow;
        if (!target.IsActive)
        {
            if (previous is null)
            {
                Store.Update(l => l.Monitors[identity] = new DetachLedgerEntry(timestamp));
            }
            return;
        }

        var layout = DisplayTopology.Detach(target);
        var data = layout is null ? previous?.PlatformData : Convert.ToBase64String(layout.ToBytes());
        Store.Update(l => l.Monitors[identity] = new DetachLedgerEntry(timestamp, data));
    }

    /// <summary>Waits out the minimum absence, attaches the monitor and checks that it is active. Returns the time waited.</summary>
    public TimeSpan Take(string identity, TimeSpan? minimumAbsence = null, Func<DateTimeOffset>? now = null, Action<TimeSpan>? sleep = null)
    {
        var target = Find(identity);
        var entry = Store.Load().Monitors.GetValueOrDefault(identity);
        var wait = MinimumAbsence.RemainingWait(entry?.DetachedAt, (now ?? (() => DateTimeOffset.UtcNow))(), minimumAbsence ?? MinimumAbsence.Default);
        if (wait > TimeSpan.Zero)
        {
            (sleep ?? Thread.Sleep)(wait);
        }

        if (!target.IsActive)
        {
            var layout = entry?.PlatformData is { } data ? DisplayLayout.FromBytes(Convert.FromBase64String(data)) : null;
            DisplayTopology.Attach(target, layout);
        }
        if (!Find(identity).IsActive)
        {
            throw new InvalidOperationException($"Could not attach {identity}; it stays released.");
        }
        Store.Update(l => l.Monitors.Remove(identity));
        return wait;
    }

    /// <summary>Detaches released monitors that came back, e.g. after a replug. Returns their identities.</summary>
    public IReadOnlyList<string> Reconcile(DateTimeOffset? now = null)
    {
        var reverted = new List<string>();
        foreach (var identity in Store.Load().Monitors.Keys.Order(StringComparer.Ordinal))
        {
            var target = DisplayTopology.Targets().FirstOrDefault(t => t.Edid?.Identity == identity);
            if (target is { IsActive: true })
            {
                Release(identity, now);
                reverted.Add(identity);
            }
        }
        return reverted;
    }

    private static DisplayTarget Find(string identity) =>
        DisplayTopology.Targets().FirstOrDefault(t => t.Edid?.Identity == identity)
        ?? throw new InvalidOperationException($"No connected monitor with identity {identity}.");
}
