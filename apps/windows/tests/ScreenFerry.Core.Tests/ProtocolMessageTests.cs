using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class ProtocolMessageTests
{
    private static readonly string s_fixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Theory]
    [MemberData(nameof(ProtocolFixtureTests.Fixtures), MemberType = typeof(ProtocolFixtureTests))]
    public void FixtureRoundTrips(string relativePath)
    {
        var json = File.ReadAllBytes(Path.Combine(s_fixturesDir, relativePath));
        var message = ProtocolMessage.Decode(json);

        Assert.IsNotType<UnknownMessage>(message);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(message.Encode())),
            $"{relativePath} re-encoded as {Encoding.UTF8.GetString(message.Encode())}");
    }

    [Fact]
    public void UnknownTypeIsKept() =>
        Assert.Equal(new UnknownMessage("scene.future"),
            ProtocolMessage.Decode("""{"protocolVersion":3,"type":"scene.future","x":1}"""u8));

    [Theory]
    [InlineData("""{"protocolVersion":0,"type":"pair.confirm"}""")]
    [InlineData("""{"protocolVersion":0,"type":"pair.nonce","nonce":"00"}""")]
    [InlineData("""{"protocolVersion":0,"type":"pair.nonce","nonce":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}""")]
    [InlineData("""{"protocolVersion":0,"type":"pair.nonce","nonce":7}""")]
    [InlineData("""{"type":"ping"}""")]
    public void RejectsMissingFieldsAndBadHex(string json) =>
        Assert.Throws<JsonException>(() => ProtocolMessage.Decode(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void FramerReassemblesSplitInput()
    {
        byte[] stream = [.. MessageFramer.Frame(new PingMessage()), .. MessageFramer.Frame(new PairConfirmMessage(true))];
        var framer = new MessageFramer();
        var messages = new List<ProtocolMessage>();
        foreach (var b in stream)
        {
            messages.AddRange(framer.Append([b]).Select(frame => ProtocolMessage.Decode(frame)));
        }

        Assert.Equal(new ProtocolMessage[] { new PingMessage(), new PairConfirmMessage(true) }, messages);
    }

    [Fact]
    public void FramerRejectsOversizedLength() =>
        Assert.Throws<InvalidDataException>(() => new MessageFramer().Append([1, 0, 0, 0]));
}
