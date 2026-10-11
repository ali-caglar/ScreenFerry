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

    private const string G8 = "SAM-E030-H1AK500000";

    private async Task<(Agent Mac, Agent Pc)> PairedAgents()
    {
        var mac = Start("Mac", Identity(), Store("mac"));
        var pc = Start("PC", Identity(), Store("pc"));
        pc.SetPairingMode(true);
        mac.Pair(Endpoint(pc));
        await Eventually("codes shown", () => mac.PairingPrompt is not null && pc.PairingPrompt is not null);
        pc.ConfirmPairing(true);
        mac.ConfirmPairing(true);
        await Eventually("connected", () => Connected(mac, pc) is not null && Connected(pc, mac) is not null);
        return (mac, pc);
    }

    private static bool Done(params Agent[] agents) => agents.All(a => a.CurrentHandoff?.Pending.Count == 0);

    [Fact]
    public async Task AutoSourceAttachesFirstThenReleases()
    {
        var (mac, pc) = await PairedAgents();
        var macDisplays = new FakeDisplays(mac, new MonitorStatus(G8, Attached: true));
        var pcDisplays = new FakeDisplays(pc, new MonitorStatus(G8, Attached: false));
        await Eventually("monitors exchanged", () => Connected(mac, pc)?.Monitors.Count == 1);

        mac.Activate(new Dictionary<string, string> { [G8] = pc.Identity.KeyIdHex });
        await Eventually("moved to the PC", () => Done(mac, pc));
        Assert.Equal([new HandoffStep(HandoffStepKind.Attach)], pcDisplays.Steps);
        Assert.Equal([new HandoffStep(HandoffStepKind.ReleaseAfterSettle)], macDisplays.Steps);

        pc.Activate(new Dictionary<string, string> { [G8] = mac.Identity.KeyIdHex });
        await Eventually("moved back", () => Done(mac, pc) && mac.CurrentHandoff?.Seq == 2);
        Assert.True(macDisplays.Monitor.Attached);
        Assert.False(pcDisplays.Monitor.Attached);
    }

    [Fact]
    public async Task DdcSwitchesThenAttaches()
    {
        var (mac, pc) = await PairedAgents();
        var macDisplays = new FakeDisplays(mac, new MonitorStatus(G8, Attached: true, InputCode: 17));
        var pcDisplays = new FakeDisplays(pc, new MonitorStatus(G8, Attached: false, InputCode: 15));
        await Eventually("monitors exchanged", () => Connected(pc, mac)?.Monitors.Count == 1);

        pc.Activate(new Dictionary<string, string> { [G8] = pc.Identity.KeyIdHex });
        await Eventually("moved to the PC", () => Done(mac, pc));
        Assert.Equal([new HandoffStep(HandoffStepKind.SwitchAndRelease, 15)], macDisplays.Steps);
        Assert.Equal([new HandoffStep(HandoffStepKind.Attach)], pcDisplays.Steps);
    }

    [Fact]
    public async Task MatchesAMonitorWithAnotherIdentityPerInput()
    {
        var (mac, pc) = await PairedAgents();
        var macDisplays = new FakeDisplays(mac, new MonitorStatus("SAM-E030-H1AK500000", Attached: true, Name: "Odyssey G80SD"));
        var pcDisplays = new FakeDisplays(pc, new MonitorStatus("SAM-E035-H1AK500000", Attached: false, Name: "Odyssey G80SD"));
        await Eventually("monitors exchanged", () => Connected(mac, pc)?.Monitors.Count == 1);

        mac.Activate(new Dictionary<string, string> { ["SAM-E030-H1AK500000"] = pc.Identity.KeyIdHex });
        await Eventually("moved to the PC", () => Done(mac, pc));
        Assert.Equal(["SAM-E035-H1AK500000"], pcDisplays.Identities);
        Assert.Equal(["SAM-E030-H1AK500000"], macDisplays.Identities);
    }

    [Fact]
    public async Task FailureReachesThePeer()
    {
        var (mac, pc) = await PairedAgents();
        _ = new FakeDisplays(mac, new MonitorStatus(G8, Attached: true));
        _ = new FakeDisplays(pc, new MonitorStatus(G8, Attached: false)) { Failing = true };
        await Eventually("monitors exchanged", () => Connected(mac, pc)?.Monitors.Count == 1);

        mac.Activate(new Dictionary<string, string> { [G8] = pc.Identity.KeyIdHex });
        await Eventually("failure reported", () => mac.CurrentHandoff?.Failures.GetValueOrDefault(G8) == "PC: no");
        Assert.True(mac.CurrentHandoff!.IsFinished);
    }

    private sealed class FakeDisplays : IHandoffExecutor
    {
        private readonly Agent _agent;

        public FakeDisplays(Agent agent, MonitorStatus monitor)
        {
            _agent = agent;
            Monitor = monitor;
            agent.Executor = this;
            agent.Settle = TimeSpan.FromMilliseconds(200);
            agent.SetLocalMonitors([monitor]);
        }

        public MonitorStatus Monitor { get; private set; }

        public List<HandoffStep> Steps { get; } = [];

        public List<string> Identities { get; } = [];

        public bool Failing { get; init; }

        public Task PerformAsync(HandoffStep action, string identity)
        {
            Steps.Add(action);
            Identities.Add(identity);
            if (Failing)
            {
                throw new InvalidOperationException("no");
            }
            Monitor = Monitor with { Attached = action.Kind == HandoffStepKind.Attach };
            _agent.SetLocalMonitors([Monitor]);
            return Task.CompletedTask;
        }
    }
}
