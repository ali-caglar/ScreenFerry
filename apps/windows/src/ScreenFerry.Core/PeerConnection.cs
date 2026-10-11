using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using System.Threading.Channels;

namespace ScreenFerry.Core;

/// <summary>Framed protocol messages over one mutually authenticated TLS connection (ADR 0006).</summary>
[SuppressMessage("Reliability", "CA1001", Justification = "RunAsync releases the socket; Close may be called at any time after, so the token source stays alive.")]
public sealed class PeerConnection
{
    private static readonly TlsCipherSuite[] s_tls12Suites =
    [
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384,
        TlsCipherSuite.TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256,
    ];

    private readonly TcpClient _client;
    private readonly Channel<byte[]?> _outgoing = Channel.CreateUnbounded<byte[]?>(new() { SingleReader = true });
    private readonly CancellationTokenSource _closed = new();

    private PeerConnection(TcpClient client, bool outgoing)
    {
        _client = client;
        Outgoing = outgoing;
    }

    public bool Outgoing { get; }

    public byte[]? PeerKeyId { get; private set; }

    public DateTimeOffset LastReceived { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Raised from a background thread once TLS is up and the peer's key is known.</summary>
    public event Action<PeerConnection>? Ready;

    public event Action<PeerConnection, ProtocolMessage>? MessageReceived;

    /// <summary>Raised exactly once, with the reason if it failed.</summary>
    public event Action<PeerConnection, string?>? Closed;

    public static PeerConnection Accepted(TcpClient client) => new(client, outgoing: false);

    public static PeerConnection Dialing() => new(new TcpClient(), outgoing: true);

    /// <summary>Runs the connection until it closes. <paramref name="accept"/> decides on the peer's key ID during the handshake.</summary>
    public async Task RunAsync(AgentIdentity identity, Func<byte[], bool> accept, IPEndPoint? remote = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string? reason = null;
        try
        {
            if (Outgoing)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_closed.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                await _client.ConnectAsync(remote!, timeout.Token).ConfigureAwait(false);
            }
            await using var stream = new SslStream(_client.GetStream(), leaveInnerStreamOpen: false);
            var context = SslStreamCertificateContext.Create(identity.Certificate, additionalCertificates: null, offline: true);
            bool Validate(object sender, System.Security.Cryptography.X509Certificates.X509Certificate? certificate,
                System.Security.Cryptography.X509Certificates.X509Chain? chain, SslPolicyErrors errors) =>
                AgentIdentity.KeyIdOf(certificate) is { } keyId && accept(keyId);
#pragma warning disable CA5398 // TLS 1.2 only for Windows 10, which lacks 1.3 (ADR 0006).
            const SslProtocols protocols = SslProtocols.Tls12 | SslProtocols.Tls13;
#pragma warning restore CA5398
            if (Outgoing)
            {
                await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = "screenferry",
                    ClientCertificateContext = context,
                    RemoteCertificateValidationCallback = Validate,
                    EnabledSslProtocols = protocols,
                    CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                }, _closed.Token).ConfigureAwait(false);
            }
            else
            {
                await stream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificateContext = context,
                    ClientCertificateRequired = true,
                    RemoteCertificateValidationCallback = Validate,
                    EnabledSslProtocols = protocols,
                    CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck,
                }, _closed.Token).ConfigureAwait(false);
            }
            if (stream.SslProtocol != SslProtocols.Tls13 && !s_tls12Suites.Contains(stream.NegotiatedCipherSuite))
            {
                throw new AuthenticationException($"Refusing {stream.SslProtocol} with {stream.NegotiatedCipherSuite}.");
            }
            PeerKeyId = AgentIdentity.KeyIdOf(stream.RemoteCertificate) ?? throw new AuthenticationException("No peer key.");
            LastReceived = DateTimeOffset.UtcNow;
            Ready?.Invoke(this);

            var writing = WriteAsync(stream);
            await ReadAsync(stream).ConfigureAwait(false);
            _outgoing.Writer.TryComplete();
            await writing.ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or SocketException or AuthenticationException or JsonException
            or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
            reason = _closed.IsCancellationRequested ? null : e.Message;
        }
        finally
        {
            await _closed.CancelAsync().ConfigureAwait(false);
            _client.Dispose();
            Closed?.Invoke(this, reason);
        }
    }

    public void Send(ProtocolMessage message) => _outgoing.Writer.TryWrite(MessageFramer.Frame(message));

    /// <summary>Closes once everything sent so far is out.</summary>
    public void Finish() => _outgoing.Writer.TryWrite(null);

    public void Close() => _closed.Cancel();

    private async Task ReadAsync(SslStream stream)
    {
        var framer = new MessageFramer();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, _closed.Token).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }
            LastReceived = DateTimeOffset.UtcNow;
            foreach (var frame in framer.Append(buffer.AsSpan(0, read)))
            {
                MessageReceived?.Invoke(this, ProtocolMessage.Decode(frame));
            }
        }
    }

    private async Task WriteAsync(SslStream stream)
    {
        try
        {
            await foreach (var frame in _outgoing.Reader.ReadAllAsync(_closed.Token).ConfigureAwait(false))
            {
                if (frame is null)
                {
                    await stream.ShutdownAsync().ConfigureAwait(false);
                    _client.Client.Shutdown(SocketShutdown.Send);
                    // The peer closes in turn, which ends ReadAsync; don't wait forever.
                    _closed.CancelAfter(TimeSpan.FromSeconds(2));
                    return;
                }
                await stream.WriteAsync(frame, _closed.Token).ConfigureAwait(false);
            }
        }
        catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
        {
            await _closed.CancelAsync().ConfigureAwait(false);
        }
    }
}
