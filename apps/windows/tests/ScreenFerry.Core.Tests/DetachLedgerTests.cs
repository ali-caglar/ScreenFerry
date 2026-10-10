using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public sealed class DetachLedgerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"screenferry-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void MissingFileLoadsEmpty() =>
        Assert.Empty(new DetachLedgerStore(Path.Combine(_directory, "ledger.json")).Load().Monitors);

    [Fact]
    public void RoundTripsThroughDisk()
    {
        var store = new DetachLedgerStore(Path.Combine(_directory, "ledger.json"));
        var entry = new DetachLedgerEntry(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), "bGF5b3V0");

        store.Update(l => l.Monitors["SAM-E030-H1AK500000"] = entry);
        Assert.Equal(entry, store.Load().Monitors["SAM-E030-H1AK500000"]);

        store.Update(l => l.Monitors.Remove("SAM-E030-H1AK500000"));
        Assert.Empty(store.Load().Monitors);
    }

    [Fact]
    public void WaitsOutTheRemainingAbsence()
    {
        var detached = DateTimeOffset.FromUnixTimeSeconds(1_000);
        var minimum = TimeSpan.FromSeconds(25);

        Assert.Equal(TimeSpan.FromSeconds(15), MinimumAbsence.RemainingWait(detached, detached.AddSeconds(10), minimum));
        Assert.Equal(TimeSpan.Zero, MinimumAbsence.RemainingWait(detached, detached.AddSeconds(40), minimum));
        Assert.Equal(TimeSpan.Zero, MinimumAbsence.RemainingWait(null, detached, minimum));
        Assert.Equal(TimeSpan.Zero, MinimumAbsence.RemainingWait(detached, detached, TimeSpan.Zero));
    }
}
