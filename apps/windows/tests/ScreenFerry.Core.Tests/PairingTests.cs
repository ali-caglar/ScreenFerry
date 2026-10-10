using System.Security.Cryptography;
using System.Text.Json;
using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class PairingTests
{
    private static readonly byte[] s_initiator = Enumerable.Repeat((byte)1, 32).ToArray();
    private static readonly byte[] s_responder = Enumerable.Repeat((byte)2, 32).ToArray();

    private static JsonElement Vectors(string section)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", "pairing.json")));
        return json.RootElement.GetProperty(section).Clone();
    }

    private static TheoryData<string> Descriptions(string section)
    {
        var data = new TheoryData<string>();
        foreach (var vector in Vectors(section).EnumerateArray())
        {
            data.Add(vector.GetProperty("description").GetString()!);
        }
        return data;
    }

    private static Func<string, byte[]> Vector(string section, string description)
    {
        var vector = Vectors(section).EnumerateArray().Single(v => v.GetProperty("description").GetString() == description);
        return name => Convert.FromHexString(vector.GetProperty(name).GetString()!);
    }

    public static TheoryData<string> KeyIdVectors() => Descriptions("keyIds");

    public static TheoryData<string> ExchangeVectors() => Descriptions("pairing");

    [Theory]
    [MemberData(nameof(KeyIdVectors))]
    public void KeyIdMatchesVector(string description)
    {
        var v = Vector("keyIds", description);
        var point = v("publicKey");
        using var key = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = point[1..33], Y = point[33..] },
        });

        Assert.Equal(v("keyId"), Pairing.KeyId(key));
    }

    [Theory]
    [MemberData(nameof(ExchangeVectors))]
    public void ExchangeMatchesVector(string description)
    {
        var v = Vector("pairing", description);
        var code = Vectors("pairing").EnumerateArray()
            .Single(e => e.GetProperty("description").GetString() == description).GetProperty("code").GetString();

        Assert.Equal(v("commitment"), Pairing.Commitment(v("responderNonce"), v("responderKeyId"), v("initiatorKeyId")));
        Assert.Equal(code, Pairing.Code(v("initiatorKeyId"), v("responderKeyId"), v("initiatorNonce"), v("responderNonce")));
    }

    private static (PairingSession Initiator, PairingSession Responder) Sessions() =>
        (new PairingSession(PairingRole.Initiator, s_initiator, s_responder), new PairingSession(PairingRole.Responder, s_responder, s_initiator));

    private static ProtocolMessage Sent(IReadOnlyList<PairingEvent> events) => events.OfType<PairingSend>().Single().Message;

    private static string? Code(IReadOnlyList<PairingEvent> events) => events.OfType<PairingShowCode>().SingleOrDefault()?.Code;

    [Fact]
    public void BothSidesShowTheSameCodeAndPair()
    {
        var (initiator, responder) = Sessions();
        Assert.Empty(initiator.Start());
        var nonce = Sent(initiator.Receive(Sent(responder.Start())));
        var responderEvents = responder.Receive(nonce);
        var initiatorEvents = initiator.Receive(Sent(responderEvents));

        Assert.NotNull(Code(initiatorEvents));
        Assert.Equal(Code(initiatorEvents), Code(responderEvents));

        Assert.Empty(initiator.Receive(Sent(responder.UserDecided(true))));
        var initiatorDone = initiator.UserDecided(true);
        Assert.IsType<PairingPaired>(initiatorDone[^1]);
        Assert.IsType<PairingPaired>(Assert.Single(responder.Receive(Sent(initiatorDone))));
    }

    [Fact]
    public void WrongCommitmentFails()
    {
        var (initiator, responder) = Sessions();
        initiator.Start();
        responder.Start();
        var nonce = Sent(initiator.Receive(new PairCommitMessage(new byte[32])));
        var events = initiator.Receive(Sent(responder.Receive(nonce)));

        Assert.Equal(new PairingFailed(PairingFailure.CommitmentMismatch), events[^1]);
        Assert.Null(Code(events));
    }

    [Fact]
    public void ResponderMustCommitFirst()
    {
        var (initiator, _) = Sessions();
        initiator.Start();

        Assert.Equal(new PairingFailed(PairingFailure.OutOfOrder), initiator.Receive(new PairNonceMessage(s_responder))[^1]);
    }

    [Fact]
    public void ConfirmBeforeCodeFails()
    {
        var (_, responder) = Sessions();
        responder.Start();

        Assert.Equal(new PairingFailed(PairingFailure.OutOfOrder), responder.Receive(new PairConfirmMessage(true))[^1]);
        Assert.Equal(new PairingFailed(PairingFailure.OutOfOrder), Assert.Single(responder.UserDecided(true)));
    }

    [Fact]
    public void RejectionOnEitherSideFails()
    {
        var (initiator, responder) = Sessions();
        initiator.Start();
        var nonce = Sent(initiator.Receive(Sent(responder.Start())));
        initiator.Receive(Sent(responder.Receive(nonce)));
        var rejection = responder.UserDecided(false);

        Assert.Equal(new PairConfirmMessage(false), Sent(rejection));
        Assert.Equal(new PairingFailed(PairingFailure.RejectedHere), rejection[^1]);
        Assert.Equal(new PairingFailed(PairingFailure.RejectedByPeer), Assert.Single(initiator.Receive(Sent(rejection))));
    }
}
