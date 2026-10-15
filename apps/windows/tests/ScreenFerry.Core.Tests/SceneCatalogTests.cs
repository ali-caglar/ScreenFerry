using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class SceneCatalogTests
{
    private static readonly Computer[] s_computers = [new("pc", "PC"), new("mac", "Mac")];

    [Fact]
    public void BuiltInScenesMoveSharedMonitorsOnly()
    {
        var seen = new Dictionary<string, IReadOnlyList<MonitorStatus>>
        {
            ["pc"] = [new("G8", true, "Odyssey"), new("ASUS", true)],
            ["mac"] = [new("G8", false)],
        };
        var custom = new Scene("1", "Split", new Dictionary<string, string> { ["G8"] = "mac", ["ASUS"] = "mac" });

        var entries = SceneCatalog.Build(s_computers, seen, [custom]);

        Assert.Equal(["All Monitors → PC", "All Monitors → Mac", "Split"], entries.Select(e => e.Scene.Name));
        Assert.Equal(new Dictionary<string, string> { ["G8"] = "pc" }, entries[0].Scene.Owners);
        Assert.Equal([1, 2, 3], entries.Select(e => e.Hotkey!.Value));
        Assert.True(entries[1].IsAvailable);
        Assert.False(entries[2].IsAvailable);
        Assert.Equal(["mac"], SceneCatalog.Monitors(s_computers, seen).Single(m => m.Identity == "G8").SeenBy.Where(id => id == "mac"));
    }

    [Fact]
    public void OneMonitorWithAnIdentityPerInputIsShared()
    {
        var seen = new Dictionary<string, IReadOnlyList<MonitorStatus>>
        {
            ["pc"] = [new("SAM-E035-H1AK500000", true, "Odyssey G80SD")],
            ["mac"] = [new("SAM-E030-H1AK500000", false, "Odyssey G80SD")],
        };
        var custom = new Scene("1", "Mac", new Dictionary<string, string> { ["SAM-E035-H1AK500000"] = "mac" });

        var entries = SceneCatalog.Build(s_computers, seen, [custom]);

        Assert.All(entries, e => Assert.True(e.IsAvailable));
        Assert.Equal(new Dictionary<string, string> { ["SAM-E035-H1AK500000"] = "mac" }, entries[1].Scene.Owners);
    }

    [Fact]
    public void ReleasedMonitorComesBackWhileItsOwnerIsOffline()
    {
        var seen = new Dictionary<string, IReadOnlyList<MonitorStatus>> { ["pc"] = [new("G8", false), new("ASUS", true)] };

        var entries = SceneCatalog.Build(s_computers, seen, []);

        Assert.True(entries[0].IsAvailable);
        Assert.Equal(new Dictionary<string, string> { ["G8"] = "pc" }, entries[0].Scene.Owners);
        Assert.False(entries[1].IsAvailable);
    }

    [Fact]
    public void NothingSharedMeansUnavailable()
    {
        var seen = new Dictionary<string, IReadOnlyList<MonitorStatus>> { ["pc"] = [new("G8", true)] };
        Assert.All(SceneCatalog.Build(s_computers, seen, []), e => Assert.False(e.IsAvailable));
    }
}
