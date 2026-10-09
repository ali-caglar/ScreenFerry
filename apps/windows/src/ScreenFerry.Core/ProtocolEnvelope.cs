using System.Text.Json.Serialization;

namespace ScreenFerry.Core;

/// <summary>Fields shared by every protocol message; see <c>protocol/schemas/envelope.schema.json</c>.</summary>
public sealed record ProtocolEnvelope(
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("type")] string Type);
