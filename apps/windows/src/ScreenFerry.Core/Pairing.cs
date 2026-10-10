using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace ScreenFerry.Core;

/// <summary>Key IDs and the numeric-comparison pairing exchange of ADR 0006.</summary>
public static class Pairing
{
    /// <summary>SHA-256 of the DER SubjectPublicKeyInfo of a P-256 key.</summary>
    public static byte[] KeyId(ECDsa publicKey)
    {
        ArgumentNullException.ThrowIfNull(publicKey);
        if (publicKey.KeySize != 256)
        {
            throw new ArgumentException("Only P-256 keys are supported.", nameof(publicKey));
        }
        return SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo());
    }

    public static byte[] Commitment(byte[] responderNonce, byte[] responderKeyId, byte[] initiatorKeyId)
    {
        byte[] message = [.. "ScreenFerry pair commit v1"u8, .. responderKeyId, .. initiatorKeyId];
        return HMACSHA256.HashData(responderNonce, message);
    }

    /// <summary>Six digits, zero-padded.</summary>
    public static string Code(byte[] initiatorKeyId, byte[] responderKeyId, byte[] initiatorNonce, byte[] responderNonce)
    {
        byte[] input = [.. "ScreenFerry pair code v1"u8, .. initiatorKeyId, .. responderKeyId, .. initiatorNonce, .. responderNonce];
        var hash = SHA256.HashData(input);
        return (BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(32);
}

public enum PairingRole
{
    Initiator,
    Responder,
}

public enum PairingFailure
{
    OutOfOrder,
    CommitmentMismatch,
    RejectedHere,
    RejectedByPeer,
}

/// <summary>What a <see cref="PairingSession"/> wants done: send a message, show the code, or finish.</summary>
public abstract record PairingEvent;

public sealed record PairingSend(ProtocolMessage Message) : PairingEvent;

public sealed record PairingShowCode(string Code) : PairingEvent;

public sealed record PairingPaired : PairingEvent;

public sealed record PairingFailed(PairingFailure Failure) : PairingEvent;

/// <summary>
/// One pairing attempt on one connection. Feed it the peer's <c>pair.*</c> messages and the user's decision;
/// it enforces the order and says what to send and show.
/// </summary>
public sealed class PairingSession
{
    private enum State
    {
        Idle,
        AwaitingCommit,
        AwaitingInitiatorNonce,
        AwaitingResponderNonce,
        Deciding,
        Done,
    }

    private readonly byte[] _initiatorKeyId;
    private readonly byte[] _responderKeyId;
    private readonly byte[] _nonce;
    private State _state = State.Idle;
    private byte[]? _commitment;
    private bool? _localAccepted;
    private bool? _peerAccepted;

    /// <summary>Key IDs are the ones observed in the TLS handshake.</summary>
    public PairingSession(PairingRole role, byte[] localKeyId, byte[] peerKeyId, byte[]? nonce = null)
    {
        Role = role;
        _initiatorKeyId = role == PairingRole.Initiator ? localKeyId : peerKeyId;
        _responderKeyId = role == PairingRole.Initiator ? peerKeyId : localKeyId;
        _nonce = nonce ?? Pairing.NewNonce();
    }

    public PairingRole Role { get; }

    public IReadOnlyList<PairingEvent> Start()
    {
        if (_state != State.Idle)
        {
            return Fail(PairingFailure.OutOfOrder);
        }
        if (Role == PairingRole.Initiator)
        {
            _state = State.AwaitingCommit;
            return [];
        }
        _state = State.AwaitingInitiatorNonce;
        return [new PairingSend(new PairCommitMessage(Pairing.Commitment(_nonce, _responderKeyId, _initiatorKeyId)))];
    }

    public IReadOnlyList<PairingEvent> Receive(ProtocolMessage message)
    {
        switch (_state, message)
        {
            case (State.AwaitingCommit, PairCommitMessage commit):
                _commitment = commit.Commitment;
                _state = State.AwaitingResponderNonce;
                return [new PairingSend(new PairNonceMessage(_nonce))];
            case (State.AwaitingInitiatorNonce, PairNonceMessage initiatorNonce):
                _state = State.Deciding;
                return [new PairingSend(new PairNonceMessage(_nonce)), new PairingShowCode(Code(initiatorNonce.Nonce, _nonce))];
            case (State.AwaitingResponderNonce, PairNonceMessage responderNonce):
                var expected = Pairing.Commitment(responderNonce.Nonce, _responderKeyId, _initiatorKeyId);
                if (!CryptographicOperations.FixedTimeEquals(expected, _commitment))
                {
                    return Fail(PairingFailure.CommitmentMismatch);
                }
                _state = State.Deciding;
                return [new PairingShowCode(Code(_nonce, responderNonce.Nonce))];
            case (State.Deciding, PairConfirmMessage confirm) when _peerAccepted is null:
                if (!confirm.Accepted)
                {
                    _state = State.Done;
                    return [new PairingFailed(PairingFailure.RejectedByPeer)];
                }
                _peerAccepted = true;
                return Decide();
            default:
                return Fail(PairingFailure.OutOfOrder);
        }
    }

    public IReadOnlyList<PairingEvent> UserDecided(bool accepted)
    {
        if (_state != State.Deciding || _localAccepted is not null)
        {
            return Fail(PairingFailure.OutOfOrder);
        }
        if (!accepted)
        {
            _state = State.Done;
            return [new PairingSend(new PairConfirmMessage(false)), new PairingFailed(PairingFailure.RejectedHere)];
        }
        _localAccepted = true;
        return [new PairingSend(new PairConfirmMessage(true)), .. Decide()];
    }

    private List<PairingEvent> Decide()
    {
        if (_localAccepted == true && _peerAccepted == true)
        {
            _state = State.Done;
            return [new PairingPaired()];
        }
        return [];
    }

    private string Code(byte[] initiatorNonce, byte[] responderNonce) =>
        Pairing.Code(_initiatorKeyId, _responderKeyId, initiatorNonce, responderNonce);

    private PairingEvent[] Fail(PairingFailure failure)
    {
        var wasDone = _state == State.Done;
        _state = State.Done;
        return wasDone
            ? [new PairingFailed(PairingFailure.OutOfOrder)]
            : [new PairingSend(new ErrorMessage(ProtocolErrorCode.PairingFailed)), new PairingFailed(failure)];
    }
}
