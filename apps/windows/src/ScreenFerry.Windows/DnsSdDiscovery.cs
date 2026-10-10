using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ScreenFerry.Core;

namespace ScreenFerry.Windows;

/// <summary>DNS-SD for <c>_screenferry._tcp</c> through <c>dnsapi.dll</c> (Windows 10 1903+).</summary>
public sealed unsafe partial class DnsSdDiscovery : IPeerDiscovery
{
    private const string ServiceType = "_screenferry._tcp.local";
    private const uint DnsRequestPending = 9506;
    private const ushort DnsTypePtr = 12;
    private static readonly TimeSpan s_rebrowse = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan s_forgetAfter = TimeSpan.FromSeconds(90);

    private readonly Lock _gate = new();
    private readonly GCHandle _self;
    private readonly Dictionary<string, Instance> _instances = new(StringComparer.OrdinalIgnoreCase);
    // Native requests stay allocated one round longer than needed, since late callbacks may still read them.
    private readonly List<Arena> _retired = [];
    private readonly List<Arena> _retiring = [];
    private Registration? _registration;
    private Browsing? _browse;
    private Timer? _timer;
    private bool _disposed;

    public DnsSdDiscovery() => _self = GCHandle.Alloc(this);

    public event Action<IReadOnlyList<DiscoveredPeer>>? Changed;

    /// <summary>Why advertising or browsing failed, if it did.</summary>
    public string? Error { get; private set; }

    public void Advertise(string name, int port, IReadOnlyDictionary<string, string> txt)
    {
        ArgumentNullException.ThrowIfNull(txt);
        lock (_gate)
        {
            if (_registration is { } old)
            {
                _ = DnsServiceDeRegister(old.Request, null);
                _retiring.Add(old.Arena);
            }
            _registration = Register(name, port, txt);
        }
    }

    public void Browse()
    {
        lock (_gate)
        {
            _timer ??= new Timer(_ => Rebrowse(), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _timer?.Dispose();
            if (_browse is { } browse)
            {
                _ = DnsServiceBrowseCancel(browse.Cancel);
            }
            if (_registration is { } registration)
            {
                _ = DnsServiceDeRegister(registration.Request, null);
            }
        }
    }

    private Registration Register(string name, int port, IReadOnlyDictionary<string, string> txt)
    {
        var arena = new Arena();
        var keys = (char**)arena.Alloc(txt.Count * sizeof(char*));
        var values = (char**)arena.Alloc(txt.Count * sizeof(char*));
        var index = 0;
        foreach (var (key, value) in txt)
        {
            keys[index] = arena.String(key);
            values[index] = arena.String(value);
            index++;
        }
        uint* ip4 = null;
        if (PrimaryIPv4() is { } address)
        {
            ip4 = (uint*)arena.Alloc(sizeof(uint));
            *ip4 = address;
        }
        var instance = DnsServiceConstructInstance(
            arena.String($"{name.Replace('.', '-')}.{ServiceType}"), arena.String($"{Environment.MachineName}.local"),
            ip4, null, (ushort)port, 0, 0, (uint)txt.Count, keys, values);
        arena.Instance = instance;

        var request = (DnsServiceRegisterRequest*)arena.Alloc(sizeof(DnsServiceRegisterRequest));
        request->Version = 1;
        request->ServiceInstance = instance;
        request->RegisterCompletionCallback = &OnRegistered;
        request->QueryContext = (void*)GCHandle.ToIntPtr(_self);
        var status = DnsServiceRegister(request, null);
        Error = status == DnsRequestPending ? null : $"DnsServiceRegister failed: {status}";
        return new Registration(request, arena);
    }

    private void Rebrowse()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }
            var now = DateTimeOffset.UtcNow;
            if (_browse is null || now - _browse.Started > s_rebrowse)
            {
                if (_browse is { } old)
                {
                    _ = DnsServiceBrowseCancel(old.Cancel);
                    _retiring.Add(old.Arena);
                }
                var arena = new Arena();
                var request = (DnsServiceBrowseRequest*)arena.Alloc(sizeof(DnsServiceBrowseRequest));
                var cancel = (DnsServiceCancel*)arena.Alloc(sizeof(DnsServiceCancel));
                request->Version = 1;
                request->QueryName = arena.String(ServiceType);
                request->BrowseCallback = &OnBrowsed;
                request->QueryContext = (void*)GCHandle.ToIntPtr(_self);
                var status = DnsServiceBrowse(request, cancel);
                Error = status == DnsRequestPending ? Error : $"DnsServiceBrowse failed: {status}";
                _browse = new Browsing(cancel, arena, now);
                FreeRetired();
            }
            foreach (var (fullName, instance) in _instances.ToList())
            {
                if (now - instance.Seen > s_forgetAfter)
                {
                    _instances.Remove(fullName);
                }
                else
                {
                    Resolve(fullName);
                }
            }
        }
        Publish();
    }

    private void Resolve(string fullName)
    {
        var op = new ResolveOp(this, fullName);
        var handle = GCHandle.Alloc(op);
        op.Request->Version = 1;
        op.Request->QueryName = op.Arena.String(fullName);
        op.Request->ResolveCompletionCallback = &OnResolved;
        op.Request->QueryContext = (void*)GCHandle.ToIntPtr(handle);
        if (DnsServiceResolve(op.Request, op.Cancel) != DnsRequestPending)
        {
            handle.Free();
            op.Arena.Free();
        }
    }

    private void Publish()
    {
        List<DiscoveredPeer> peers;
        lock (_gate)
        {
            peers = [.. _instances.Values
                .Where(i => i.Id is not null && i.Endpoint is not null)
                .Select(i =>
                {
                    var endpoint = i.Endpoint;
                    return new DiscoveredPeer(i.Id!, i.Label, i.IsPairable, _ => Task.FromResult(endpoint));
                })];
        }
        Changed?.Invoke(peers);
    }

    private void FreeRetired()
    {
        foreach (var arena in _retired)
        {
            arena.Free();
        }
        _retired.Clear();
        _retired.AddRange(_retiring);
        _retiring.Clear();
    }

    private static uint? PrimaryIPv4()
    {
        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .Select(n => n.GetIPProperties())
            .Where(p => p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .SelectMany(p => p.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        // IP4_ADDRESS is in network byte order, i.e. the address bytes as they are in memory.
        return address is null ? null : BitConverter.ToUInt32(address.GetAddressBytes());
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnRegistered(uint status, void* context, DnsServiceInstance* instance)
    {
        // The completion hands back a copy of the instance.
        if (instance is not null)
        {
            DnsServiceFreeInstance(instance);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnBrowsed(uint status, void* context, DnsRecord* records)
    {
        if (GCHandle.FromIntPtr((nint)context).Target is not DnsSdDiscovery self)
        {
            return;
        }
        var found = new List<string>();
        for (var record = records; record is not null; record = record->Next)
        {
            if (record->Type == DnsTypePtr && record->Ttl > 0 && record->PtrNameHost is not null)
            {
                found.Add(new string(record->PtrNameHost));
            }
        }
        if (records is not null)
        {
            DnsFree(records, 1);
        }
        lock (self._gate)
        {
            foreach (var fullName in found)
            {
                var isNew = !self._instances.ContainsKey(fullName);
                if (isNew)
                {
                    self._instances[fullName] = new Instance(fullName.Split('.')[0]);
                }
                self._instances[fullName].Seen = DateTimeOffset.UtcNow;
                if (isNew && !self._disposed)
                {
                    self.Resolve(fullName);
                }
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void OnResolved(uint status, void* context, DnsServiceInstance* resolved)
    {
        var handle = GCHandle.FromIntPtr((nint)context);
        var op = (ResolveOp)handle.Target!;
        handle.Free();
        op.Arena.Free();
        if (resolved is null)
        {
            return;
        }
        try
        {
            var txt = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < resolved->PropertyCount; i++)
            {
                if (resolved->Keys[i] is not null && resolved->Values[i] is not null)
                {
                    txt[new string(resolved->Keys[i])] = new string(resolved->Values[i]);
                }
            }
            IPAddress? address = resolved->Ip4Address is not null
                ? new IPAddress(new ReadOnlySpan<byte>(resolved->Ip4Address, 4))
                : resolved->Ip6Address is not null
                    ? new IPAddress(new ReadOnlySpan<byte>(resolved->Ip6Address, 16).ToArray(), resolved->InterfaceIndex)
                    : null;
            lock (op.Owner._gate)
            {
                if (op.Owner._instances.TryGetValue(op.FullName, out var instance))
                {
                    instance.Id = txt.GetValueOrDefault("id");
                    instance.IsPairable = txt.GetValueOrDefault("pairing") == "1";
                    instance.Endpoint = address is null ? null : new IPEndPoint(address, resolved->Port);
                }
            }
        }
        finally
        {
            DnsServiceFreeInstance(resolved);
        }
        op.Owner.Publish();
    }

    private sealed class Instance(string label)
    {
        public string Label { get; } = label;

        public DateTimeOffset Seen { get; set; } = DateTimeOffset.UtcNow;

        public string? Id { get; set; }

        public bool IsPairable { get; set; }

        public IPEndPoint? Endpoint { get; set; }
    }

    private sealed class Registration(DnsServiceRegisterRequest* request, Arena arena)
    {
        public DnsServiceRegisterRequest* Request { get; } = request;

        public Arena Arena { get; } = arena;
    }

    private sealed class Browsing(DnsServiceCancel* cancel, Arena arena, DateTimeOffset started)
    {
        public DnsServiceCancel* Cancel { get; } = cancel;

        public Arena Arena { get; } = arena;

        public DateTimeOffset Started { get; } = started;
    }

    private sealed class ResolveOp
    {
        public ResolveOp(DnsSdDiscovery owner, string fullName)
        {
            Owner = owner;
            FullName = fullName;
            Request = (DnsServiceResolveRequest*)Arena.Alloc(sizeof(DnsServiceResolveRequest));
            Cancel = (DnsServiceCancel*)Arena.Alloc(sizeof(DnsServiceCancel));
        }

        public DnsSdDiscovery Owner { get; }

        public string FullName { get; }

        public Arena Arena { get; } = new();

        public DnsServiceResolveRequest* Request { get; }

        public DnsServiceCancel* Cancel { get; }
    }

    /// <summary>Native memory freed together; owns a constructed service instance too.</summary>
    private sealed class Arena
    {
        private readonly List<nint> _blocks = [];

        public DnsServiceInstance* Instance { get; set; }

        public void* Alloc(int bytes)
        {
            var block = NativeMemory.AllocZeroed((nuint)bytes);
            _blocks.Add((nint)block);
            return block;
        }

        public char* String(string value)
        {
            var chars = (char*)Alloc((value.Length + 1) * sizeof(char));
            value.AsSpan().CopyTo(new Span<char>(chars, value.Length));
            return chars;
        }

        public void Free()
        {
            if (Instance is not null)
            {
                DnsServiceFreeInstance(Instance);
                Instance = null;
            }
            foreach (var block in _blocks)
            {
                NativeMemory.Free((void*)block);
            }
            _blocks.Clear();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceInstance
    {
        public char* InstanceName;
        public char* HostName;
        public byte* Ip4Address;
        public byte* Ip6Address;
        public ushort Port;
        public ushort Priority;
        public ushort Weight;
        public uint PropertyCount;
        public char** Keys;
        public char** Values;
        public uint InterfaceIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceRegisterRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public DnsServiceInstance* ServiceInstance;
        public delegate* unmanaged[Stdcall]<uint, void*, DnsServiceInstance*, void> RegisterCompletionCallback;
        public void* QueryContext;
        public nint Credentials;
        public int UnicastEnabled;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceBrowseRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public char* QueryName;
        public delegate* unmanaged[Stdcall]<uint, void*, DnsRecord*, void> BrowseCallback;
        public void* QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceResolveRequest
    {
        public uint Version;
        public uint InterfaceIndex;
        public char* QueryName;
        public delegate* unmanaged[Stdcall]<uint, void*, DnsServiceInstance*, void> ResolveCompletionCallback;
        public void* QueryContext;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DnsServiceCancel
    {
        public void* Reserved;
    }

    /// <summary>The head of <c>DNS_RECORDW</c>; only the PTR target is read from the data union.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct DnsRecord
    {
        public DnsRecord* Next;
        public char* Name;
        public ushort Type;
        public ushort DataLength;
        public uint Flags;
        public uint Ttl;
        public uint Reserved;
        public char* PtrNameHost;
    }

    [LibraryImport("dnsapi.dll")]
    private static partial DnsServiceInstance* DnsServiceConstructInstance(char* serviceName, char* hostName, uint* ip4, void* ip6,
        ushort port, ushort priority, ushort weight, uint propertiesCount, char** keys, char** values);

    [LibraryImport("dnsapi.dll")]
    private static partial void DnsServiceFreeInstance(DnsServiceInstance* instance);

    [LibraryImport("dnsapi.dll")]
    private static partial uint DnsServiceRegister(DnsServiceRegisterRequest* request, DnsServiceCancel* cancel);

    [LibraryImport("dnsapi.dll")]
    private static partial uint DnsServiceDeRegister(DnsServiceRegisterRequest* request, DnsServiceCancel* cancel);

    [LibraryImport("dnsapi.dll")]
    private static partial uint DnsServiceBrowse(DnsServiceBrowseRequest* request, DnsServiceCancel* cancel);

    [LibraryImport("dnsapi.dll")]
    private static partial uint DnsServiceBrowseCancel(DnsServiceCancel* cancel);

    [LibraryImport("dnsapi.dll")]
    private static partial uint DnsServiceResolve(DnsServiceResolveRequest* request, DnsServiceCancel* cancel);

    [LibraryImport("dnsapi.dll")]
    private static partial void DnsFree(void* data, int freeType);
}
