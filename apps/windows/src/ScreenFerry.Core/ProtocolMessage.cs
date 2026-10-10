using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace ScreenFerry.Core;

/// <summary>A message between agents; see <c>protocol/schemas/</c>.</summary>
public abstract record ProtocolMessage
{
    public const int CurrentVersion = 0;

    private static readonly JsonSerializerOptions s_options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>Decodes one message; a type this version doesn't know becomes <see cref="UnknownMessage"/>.</summary>
    public static ProtocolMessage Decode(ReadOnlySpan<byte> json)
    {
        var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(json, s_options)
            ?? throw new JsonException("Empty message.");
        return envelope.Type switch
        {
            "hello" => Read<HelloMessage>(json),
            "pair.commit" => Read<PairCommitMessage>(json),
            "pair.nonce" => Read<PairNonceMessage>(json),
            "pair.confirm" => Read<PairConfirmMessage>(json),
            "monitors" => Read<MonitorsMessage>(json),
            "ping" => new PingMessage(),
            "error" => Read<ErrorMessage>(json),
            _ => new UnknownMessage(envelope.Type),
        };
    }

    public byte[] Encode()
    {
        var body = JsonSerializer.SerializeToNode(this, GetType(), s_options)!.AsObject();
        var message = new JsonObject { ["protocolVersion"] = CurrentVersion, ["type"] = Type };
        foreach (var (name, value) in body.ToList())
        {
            body.Remove(name);
            message[name] = value;
        }
        return JsonSerializer.SerializeToUtf8Bytes(message, s_options);
    }

    private static T Read<T>(ReadOnlySpan<byte> json) =>
        JsonSerializer.Deserialize<T>(json, s_options) ?? throw new JsonException("Empty message.");
}

public enum AgentPlatform
{
    [JsonStringEnumMemberName("macos")] MacOS,
    [JsonStringEnumMemberName("windows")] Windows,
}

public enum ProtocolErrorCode
{
    [JsonStringEnumMemberName("notPaired")] NotPaired,
    [JsonStringEnumMemberName("pairingFailed")] PairingFailed,
    [JsonStringEnumMemberName("protocolVersion")] ProtocolVersion,
    [JsonStringEnumMemberName("badMessage")] BadMessage,
}

public sealed record HelloMessage(
    [property: JsonPropertyName("keyId"), JsonConverter(typeof(Hex32Converter))] byte[] KeyId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("platform"), JsonConverter(typeof(JsonStringEnumConverter<AgentPlatform>))] AgentPlatform Platform,
    [property: JsonPropertyName("appVersion")] string AppVersion) : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => "hello";
}

public sealed record PairCommitMessage(
    [property: JsonPropertyName("commitment"), JsonConverter(typeof(Hex32Converter))] byte[] Commitment) : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => "pair.commit";
}

public sealed record PairNonceMessage(
    [property: JsonPropertyName("nonce"), JsonConverter(typeof(Hex32Converter))] byte[] Nonce) : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => "pair.nonce";
}

public sealed record PairConfirmMessage([property: JsonPropertyName("accepted")] bool Accepted) : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => "pair.confirm";
}

public sealed record MonitorStatus(
    [property: JsonPropertyName("identity")] string Identity,
    [property: JsonPropertyName("attached")] bool Attached,
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("inputCode")] int? InputCode = null);

public sealed record MonitorsMessage([property: JsonPropertyName("monitors")] IReadOnlyList<MonitorStatus> Monitors) : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => "monitors";
}

public sealed record PingMessage : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => "ping";
}

public sealed record ErrorMessage(
    [property: JsonPropertyName("code"), JsonConverter(typeof(JsonStringEnumConverter<ProtocolErrorCode>))] ProtocolErrorCode Code,
    [property: JsonPropertyName("message")] string? Message = null) : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => "error";
}

/// <summary>A type this version doesn't know; receivers ignore it.</summary>
public sealed record UnknownMessage(string UnknownType) : ProtocolMessage
{
    [JsonIgnore]
    public override string Type => UnknownType;
}

/// <summary>32 bytes as 64 lowercase hex digits.</summary>
public sealed class Hex32Converter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is not { Length: 64 } || text.Any(char.IsUpper))
        {
            throw new JsonException("Expected 64 lowercase hex digits.");
        }
        try
        {
            return Convert.FromHexString(text);
        }
        catch (FormatException e)
        {
            throw new JsonException("Expected 64 lowercase hex digits.", e);
        }
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) =>
        writer.WriteStringValue(Convert.ToHexStringLower(value));
}

/// <summary>Splits a byte stream into messages: each is a 4-byte big-endian length and that many bytes of JSON.</summary>
public sealed class MessageFramer
{
    public const int MaxLength = 65_536;

    private readonly List<byte> _buffer = [];

    public static byte[] Frame(ProtocolMessage message)
    {
        var json = message.Encode();
        if (json.Length > MaxLength)
        {
            throw new InvalidDataException($"Message of {json.Length} bytes exceeds {MaxLength}.");
        }
        var frame = new byte[4 + json.Length];
        BinaryPrimitives.WriteUInt32BigEndian(frame, (uint)json.Length);
        json.CopyTo(frame, 4);
        return frame;
    }

    /// <summary>Adds received bytes and returns the complete messages' JSON, in order.</summary>
    public IReadOnlyList<byte[]> Append(ReadOnlySpan<byte> bytes)
    {
        _buffer.AddRange(bytes);
        var frames = new List<byte[]>();
        while (_buffer.Count >= 4)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian([.. _buffer.GetRange(0, 4)]);
            if (length > MaxLength)
            {
                throw new InvalidDataException($"Frame of {length} bytes exceeds {MaxLength}.");
            }
            if (_buffer.Count < 4 + (int)length)
            {
                break;
            }
            frames.Add([.. _buffer.GetRange(4, (int)length)]);
            _buffer.RemoveRange(0, 4 + (int)length);
        }
        return frames;
    }
}
