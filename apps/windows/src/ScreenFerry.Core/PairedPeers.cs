using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenFerry.Core;

/// <summary>A computer the user paired with, trusted by its key ID (ADR 0006).</summary>
public sealed record PairedPeer(
    [property: JsonPropertyName("keyID")] string KeyId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("platform"), JsonConverter(typeof(JsonStringEnumConverter<AgentPlatform>))] AgentPlatform Platform,
    [property: JsonPropertyName("pairedAt")] DateTimeOffset PairedAt);

public sealed class PairedPeerStore(string path)
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public string Path { get; } = path;

    /// <summary><c>%LOCALAPPDATA%\ScreenFerry\paired-peers.json</c></summary>
    public static PairedPeerStore Standard() => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenFerry", "paired-peers.json"));

    public IReadOnlyList<PairedPeer> Load() =>
        File.Exists(Path) ? JsonSerializer.Deserialize<List<PairedPeer>>(File.ReadAllText(Path), s_options) ?? [] : [];

    public void Save(IEnumerable<PairedPeer> peers)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(peers.OrderBy(p => p.KeyId, StringComparer.Ordinal).ToList(), s_options));
        File.Move(temporary, Path, overwrite: true);
    }
}
