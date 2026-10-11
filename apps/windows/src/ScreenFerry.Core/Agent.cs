using System.Net;
using System.Net.Sockets;

namespace ScreenFerry.Core;

public sealed record AgentPeer(
    string Id,
    string Name,
    AgentPlatform? Platform,
    bool IsPaired,
    bool IsConnected,
    bool IsPairable,
    IReadOnlyList<MonitorStatus> Monitors);

public sealed record PairingPrompt(string PeerName, string Code);

/// <summary>A computer found by DNS-SD; its TXT record is untrusted until TLS proves the key.</summary>
public sealed record DiscoveredPeer(string Id, string Name, bool IsPairable, Func<CancellationToken, Task<IPEndPoint?>> Resolve);

/// <summary>DNS-SD for <c>_screenferry._tcp</c>.</summary>
public interface IPeerDiscovery : IDisposable
{
    event Action<IReadOnlyList<DiscoveredPeer>>? Changed;

    void Advertise(string name, int port, IReadOnlyDictionary<string, string> txt);

    void Browse();
}

/// <summary>Finds, pairs with and stays connected to the other computers' agents (ADR 0006). Thread-safe.</summary>
public sealed class Agent : IDisposable
{
    public static readonly TimeSpan PairingWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan s_pingInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan s_silenceLimit = TimeSpan.FromSeconds(45);

    private readonly Lock _gate = new();
    private readonly string _appVersion;
    private readonly PairedPeerStore _store;
    private readonly IPeerDiscovery? _discovery;
    private readonly Dictionary<string, PairedPeer> _paired;
    private readonly Dictionary<string, IReadOnlyList<MonitorStatus>> _peerMonitors = [];
    private readonly List<Link> _links = [];
    private readonly HashSet<string> _resolving = [];
    private Dictionary<string, (DiscoveredPeer Peer, DateTimeOffset Since)> _discovered = [];
    private IReadOnlyList<MonitorStatus> _localMonitors = [];
    private DateTimeOffset? _pairingDeadline;
    private TcpListener? _listener;
    private Timer? _timer;

    public Agent(AgentIdentity identity, string name, string appVersion, PairedPeerStore store, IPeerDiscovery? discovery = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        Identity = identity;
        Name = name;
        _appVersion = appVersion;
        _store = store;
        _discovery = discovery;
        _paired = store.Load().ToDictionary(p => p.KeyId);
        Peers = Snapshot();
    }

    /// <summary>Raised from a background thread after any public state changed.</summary>
    public event Action? Changed;

    public AgentIdentity Identity { get; }

    public string Name { get; }

    public IReadOnlyList<AgentPeer> Peers { get; private set; }

    public bool IsPairingMode { get; private set; }

    public PairingPrompt? PairingPrompt { get; private set; }

    /// <summary>Outcome of the last pairing attempt, for the UI.</summary>
    public string? PairingResult { get; private set; }

    public int? ListenerPort => (_listener?.LocalEndpoint as IPEndPoint)?.Port;

    public void Start(int port = 0)
    {
        _listener = new TcpListener(IPAddress.IPv6Any, port);
        _listener.Server.DualMode = true;
        _listener.Start();
        _ = AcceptAsync(_listener);
        if (_discovery is not null)
        {
            _discovery.Changed += OnDiscovered;
            Advertise();
            _discovery.Browse();
        }
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _listener?.Stop();
        _discovery?.Dispose();
        lock (_gate)
        {
            foreach (var link in _links)
            {
                link.Connection.Close();
            }
        }
    }

    public void SetPairingMode(bool on) => Mutate(() => SetPairingModeLocked(on));

    /// <summary>Starts pairing with a discovered computer; both must be in pairing mode.</summary>
    public async Task PairAsync(string peerId)
    {
        DiscoveredPeer? peer;
        lock (_gate)
        {
            peer = _discovered.TryGetValue(peerId, out var found) ? found.Peer : null;
        }
        if (peer is not null && await peer.Resolve(CancellationToken.None).ConfigureAwait(false) is { } endpoint)
        {
            Pair(endpoint);
        }
    }

    public void Pair(IPEndPoint endpoint) => Mutate(() =>
    {
        if (!IsPairingMode)
        {
            SetPairingModeLocked(true);
        }
        Dial(endpoint, wantsToPair: true);
    });

    /// <summary>Connects to a paired peer, e.g. one not found by DNS-SD.</summary>
    public void Connect(IPEndPoint endpoint) => Mutate(() => Dial(endpoint, wantsToPair: false));

    public void ConfirmPairing(bool accepted) => Mutate(() =>
    {
        if (PairingPrompt is not null && _links.FirstOrDefault(l => l.Pairing is not null) is { } link)
        {
            Handle(link.Pairing!.UserDecided(accepted), link);
        }
    });

    public void Unpair(string peerId) => Mutate(() =>
    {
        _paired.Remove(peerId);
        _store.Save(_paired.Values);
        foreach (var link in _links.Where(l => l.PeerId == peerId))
        {
            link.Connection.Close();
        }
    });

    public void SetLocalMonitors(IReadOnlyList<MonitorStatus> monitors) => Mutate(() =>
    {
        if (monitors.SequenceEqual(_localMonitors))
        {
            return;
        }
        _localMonitors = monitors;
        foreach (var link in _links.Where(l => l.IsPaired))
        {
            link.Send(new MonitorsMessage(monitors));
        }
    });

    private void Mutate(Action change)
    {
        lock (_gate)
        {
            change();
            Peers = Snapshot();
        }
        Changed?.Invoke();
    }

    private void SetPairingModeLocked(bool on)
    {
        IsPairingMode = on;
        _pairingDeadline = on ? DateTimeOffset.UtcNow + PairingWindow : null;
        if (on)
        {
            PairingResult = null;
        }
        Advertise();
    }

    private void Advertise()
    {
        if (_discovery is null || ListenerPort is not { } port)
        {
            return;
        }
        var txt = new Dictionary<string, string> { ["id"] = Identity.KeyIdHex, ["pv"] = ProtocolMessage.CurrentVersion.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        if (IsPairingMode)
        {
            txt["pairing"] = "1";
        }
        _discovery.Advertise(Name, port, txt);
    }

    private async Task AcceptAsync(TcpListener listener)
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is SocketException or ObjectDisposedException or InvalidOperationException)
            {
                return;
            }
            Mutate(() => Add(PeerConnection.Accepted(client), wantsToPair: false, remote: null));
        }
    }

    private void Dial(IPEndPoint endpoint, bool wantsToPair) => Add(PeerConnection.Dialing(), wantsToPair, endpoint);

    private void Add(PeerConnection connection, bool wantsToPair, IPEndPoint? remote)
    {
        var link = new Link(connection, wantsToPair);
        _links.Add(link);
        connection.Ready += _ => Mutate(() => OnReady(link));
        connection.MessageReceived += (_, message) => Mutate(() => Receive(message, link));
        connection.Closed += (_, _) => Mutate(() => OnClosed(link));
        _ = Task.Run(() => connection.RunAsync(Identity, keyId => Accepts(keyId, link), remote));
    }

    private bool Accepts(byte[] keyId, Link link)
    {
        lock (_gate)
        {
            var paired = _paired.ContainsKey(Convert.ToHexStringLower(keyId));
            return paired || (link.Connection.Outgoing ? link.WantsToPair : IsPairingMode);
        }
    }

    private void OnReady(Link link) =>
        link.Send(new HelloMessage(Identity.KeyId, Name, AgentPlatform.Windows, _appVersion));

    private void OnClosed(Link link)
    {
        _links.Remove(link);
        if (link.Pairing is not null)
        {
            PairingPrompt = null;
            PairingResult = $"Pairing with {link.Hello?.Name ?? "?"} was interrupted.";
        }
    }

    private void Receive(ProtocolMessage message, Link link)
    {
        if (link.Hello is null)
        {
            if (message is not HelloMessage hello || link.Connection.PeerKeyId is not { } keyId || !hello.KeyId.AsSpan().SequenceEqual(keyId))
            {
                Reject(link, ProtocolErrorCode.BadMessage);
                return;
            }
            link.Hello = hello;
            HelloReceived(link);
            return;
        }
        switch (message)
        {
            case PairCommitMessage or PairNonceMessage or PairConfirmMessage:
                if (link.Pairing is null)
                {
                    Reject(link, ProtocolErrorCode.BadMessage);
                    return;
                }
                Handle(link.Pairing.Receive(message), link);
                break;
            case MonitorsMessage monitors:
                if (!link.IsPaired)
                {
                    Reject(link, ProtocolErrorCode.NotPaired);
                    return;
                }
                _peerMonitors[link.PeerId!] = monitors.Monitors;
                break;
            case ErrorMessage:
                link.Connection.Close();
                break;
        }
    }

    private void HelloReceived(Link link)
    {
        var peerId = link.PeerId!;
        var hello = link.Hello!;
        if (_paired.TryGetValue(peerId, out var peer))
        {
            if (peer.Name != hello.Name || peer.Platform != hello.Platform)
            {
                _paired[peerId] = peer with { Name = hello.Name, Platform = hello.Platform };
                _store.Save(_paired.Values);
            }
            BecamePaired(link);
            return;
        }
        var pairingElsewhere = _links.Any(l => l != link && l.Pairing is not null);
        if (!pairingElsewhere && (link.Connection.Outgoing ? link.WantsToPair : IsPairingMode))
        {
            link.Pairing = new PairingSession(link.Connection.Outgoing ? PairingRole.Initiator : PairingRole.Responder,
                Identity.KeyId, link.Connection.PeerKeyId!);
            Handle(link.Pairing.Start(), link);
            return;
        }
        Reject(link, ProtocolErrorCode.NotPaired);
    }

    private void BecamePaired(Link link)
    {
        var peerId = link.PeerId!;
        // Both may have dialled; keep the connection opened by the lower key ID.
        if (_links.FirstOrDefault(l => l != link && l.IsPaired && l.PeerId == peerId) is { } other)
        {
            var keepThis = link.Connection.Outgoing == (string.CompareOrdinal(Identity.KeyIdHex, peerId) < 0);
            (keepThis ? other : link).Connection.Close();
            if (!keepThis)
            {
                return;
            }
        }
        link.IsPaired = true;
        link.Send(new MonitorsMessage(_localMonitors));
    }

    private void Handle(IReadOnlyList<PairingEvent> events, Link link)
    {
        foreach (var pairingEvent in events)
        {
            switch (pairingEvent)
            {
                case PairingSend send:
                    link.Send(send.Message);
                    break;
                case PairingShowCode show:
                    PairingPrompt = new PairingPrompt(link.Hello?.Name ?? "?", show.Code);
                    break;
                case PairingPaired:
                    var hello = link.Hello!;
                    _paired[link.PeerId!] = new PairedPeer(link.PeerId!, hello.Name, hello.Platform, DateTimeOffset.UtcNow);
                    _store.Save(_paired.Values);
                    link.Pairing = null;
                    PairingPrompt = null;
                    PairingResult = $"Paired with {hello.Name}.";
                    SetPairingModeLocked(false);
                    BecamePaired(link);
                    break;
                case PairingFailed failed:
                    link.Pairing = null;
                    PairingPrompt = null;
                    PairingResult = $"Pairing with {link.Hello?.Name ?? "?"} failed: {failed.Failure}.";
                    link.Connection.Finish();
                    break;
            }
        }
    }

    private static void Reject(Link link, ProtocolErrorCode code)
    {
        link.Send(new ErrorMessage(code));
        link.Connection.Finish();
    }

    private void Tick() => Mutate(() =>
    {
        var now = DateTimeOffset.UtcNow;
        if (_pairingDeadline is { } deadline && now > deadline)
        {
            SetPairingModeLocked(false);
        }
        foreach (var link in _links)
        {
            if (now - link.Connection.LastReceived > s_silenceLimit)
            {
                link.Connection.Close();
            }
            else if (link.Hello is not null && now - link.LastSent >= s_pingInterval)
            {
                link.Send(new PingMessage());
            }
        }
        foreach (var (peerId, (peer, since)) in _discovered)
        {
            // The lower key ID dials; the other waits a little in case it can't reach us.
            if (_paired.ContainsKey(peerId) && !_resolving.Contains(peerId)
                && !_links.Any(l => l.PeerId == peerId || l.DiscoveredId == peerId)
                && (string.CompareOrdinal(Identity.KeyIdHex, peerId) < 0 || now - since > TimeSpan.FromSeconds(10)))
            {
                _resolving.Add(peerId);
                _ = ResolveAndDialAsync(peer);
            }
        }
    });

    private async Task ResolveAndDialAsync(DiscoveredPeer peer)
    {
        var endpoint = await peer.Resolve(CancellationToken.None).ConfigureAwait(false);
        Mutate(() =>
        {
            _resolving.Remove(peer.Id);
            if (endpoint is not null)
            {
                Dial(endpoint, wantsToPair: false);
                _links[^1].DiscoveredId = peer.Id;
            }
        });
    }

    private void OnDiscovered(IReadOnlyList<DiscoveredPeer> peers) => Mutate(() =>
    {
        var now = DateTimeOffset.UtcNow;
        _discovered = peers
            .Where(p => p.Id != Identity.KeyIdHex)
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => (g.First(), _discovered.TryGetValue(g.Key, out var known) ? known.Since : now));
    });

    private List<AgentPeer> Snapshot()
    {
        var all = new Dictionary<string, AgentPeer>();
        foreach (var (id, (peer, _)) in _discovered)
        {
            all[id] = new AgentPeer(id, peer.Name, null, false, false, peer.IsPairable, []);
        }
        foreach (var (id, peer) in _paired)
        {
            all[id] = (all.TryGetValue(id, out var found) ? found : new AgentPeer(id, peer.Name, null, true, false, false, []))
                with { Name = peer.Name, Platform = peer.Platform, IsPaired = true };
        }
        foreach (var link in _links.Where(l => l.IsPaired && l.PeerId is not null))
        {
            if (all.TryGetValue(link.PeerId!, out var peer))
            {
                all[link.PeerId!] = peer with { IsConnected = true, Monitors = _peerMonitors.GetValueOrDefault(link.PeerId!, []) };
            }
        }
        return [.. all.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id, StringComparer.Ordinal)];
    }

    private sealed class Link(PeerConnection connection, bool wantsToPair)
    {
        public PeerConnection Connection { get; } = connection;

        public bool WantsToPair { get; } = wantsToPair;

        /// <summary>Set while dialling a discovered peer, to avoid dialling it twice.</summary>
        public string? DiscoveredId { get; set; }

        public HelloMessage? Hello { get; set; }

        public PairingSession? Pairing { get; set; }

        public bool IsPaired { get; set; }

        public DateTimeOffset LastSent { get; private set; } = DateTimeOffset.UtcNow;

        public string? PeerId => Connection.PeerKeyId is { } keyId ? Convert.ToHexStringLower(keyId) : null;

        public void Send(ProtocolMessage message)
        {
            LastSent = DateTimeOffset.UtcNow;
            Connection.Send(message);
        }
    }
}
