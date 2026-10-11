using System.Net;
using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public sealed class AgentTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"screenferry-tests-{Guid.NewGuid()}");
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            disposable.Dispose();
        }
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private PairedPeerStore Store(string name) => new(Path.Combine(_directory, name, "paired-peers.json"));

    private AgentIdentity Identity() => Track(AgentIdentity.Ephemeral());

    private Agent Start(string name, AgentIdentity identity, PairedPeerStore store)
    {
        var agent = Track(new Agent(identity, name, "0.2.1", store));
        agent.Start();
        return agent;
    }

    private T Track<T>(T disposable) where T : IDisposable
    {
        _disposables.Add(disposable);
        return disposable;
    }

    private static IPEndPoint Endpoint(Agent agent) => new(IPAddress.Loopback, agent.ListenerPort!.Value);

    private static AgentPeer? Connected(Agent agent, Agent other) =>
        agent.Peers.FirstOrDefault(p => p.Id == other.Identity.KeyIdHex && p.IsConnected);

    private static async Task Eventually(string what, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for: {what}");
            await Task.Delay(20);
        }
    }

    [Fact]
    public async Task PairsShowsMonitorsAndReconnects()
    {
        var macIdentity = Identity();
        var pcIdentity = Identity();
        var macStore = Store("mac");
        var pcStore = Store("pc");
        var mac = Start("Mac", macIdentity, macStore);
        var pc = Start("PC", pcIdentity, pcStore);

        pc.SetPairingMode(true);
        mac.Pair(Endpoint(pc));
        await Eventually("codes shown", () => mac.PairingPrompt is not null && pc.PairingPrompt is not null);
        Assert.Equal(mac.PairingPrompt!.Code, pc.PairingPrompt!.Code);
        Assert.Equal("PC", mac.PairingPrompt.PeerName);
        Assert.Equal("Mac", pc.PairingPrompt.PeerName);

        pc.ConfirmPairing(true);
        mac.ConfirmPairing(true);
        await Eventually("paired and connected", () => Connected(mac, pc) is not null && Connected(pc, mac) is not null);
        Assert.False(mac.IsPairingMode || pc.IsPairingMode);
        Assert.Equal([pcIdentity.KeyIdHex], macStore.Load().Select(p => p.KeyId));

        var g8 = new MonitorStatus("SAM-E030-H1AK500000", true, "Odyssey G80SD");
        pc.SetLocalMonitors([g8]);
        await Eventually("monitors arrive", () => Connected(mac, pc)?.Monitors.SequenceEqual([g8]) == true);

        mac.Dispose();
        pc.Dispose();
        mac = Start("Mac", macIdentity, macStore);
        pc = Start("PC", pcIdentity, pcStore);
        pc.SetLocalMonitors([g8]);
        mac.Connect(Endpoint(pc));
        await Eventually("reconnected without pairing", () => Connected(mac, pc)?.Monitors.SequenceEqual([g8]) == true);
        Assert.Null(mac.PairingPrompt);
    }

    [Fact]
    public async Task RejectionStoresNothing()
    {
        var mac = Start("Mac", Identity(), Store("mac"));
        var pc = Start("PC", Identity(), Store("pc"));
        pc.SetPairingMode(true);
        mac.Pair(Endpoint(pc));
        await Eventually("codes shown", () => mac.PairingPrompt is not null && pc.PairingPrompt is not null);

        pc.ConfirmPairing(false);
        await Eventually("both gave up", () => mac.PairingPrompt is null && pc.PairingPrompt is null);
        Assert.Empty(Store("mac").Load());
        Assert.Empty(Store("pc").Load());
        Assert.Contains("RejectedByPeer", mac.PairingResult, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnpairedPeerIsRefusedOutsidePairingMode()
    {
        var mac = Start("Mac", Identity(), Store("mac"));
        var pc = Start("PC", Identity(), Store("pc"));
        mac.Pair(Endpoint(pc));
        await Task.Delay(1000);

        Assert.Null(mac.PairingPrompt);
        Assert.Null(pc.PairingPrompt);
        Assert.Empty(mac.Peers);
        Assert.Empty(pc.Peers);
    }
}
