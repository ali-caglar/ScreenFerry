using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenFerry.Core;

/// <summary>Monitors this machine has released, persisted so they can be re-attached after a restart.</summary>
public sealed class DetachLedger
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>Keyed by monitor identity.</summary>
    [JsonPropertyName("monitors")]
    public Dictionary<string, DetachLedgerEntry> Monitors { get; init; } = [];
}

public sealed record DetachLedgerEntry(
    [property: JsonPropertyName("detachedAt")] DateTimeOffset DetachedAt,
    [property: JsonPropertyName("platformData")] string? PlatformData = null);

public sealed class DetachLedgerStore(string path)
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public string Path { get; } = path;

    /// <summary><c>%LOCALAPPDATA%\ScreenFerry\detached-displays.json</c></summary>
    public static DetachLedgerStore Standard() => new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenFerry", "detached-displays.json"));

    public DetachLedger Load() =>
        File.Exists(Path) ? JsonSerializer.Deserialize<DetachLedger>(File.ReadAllText(Path), s_options) ?? new() : new();

    public void Save(DetachLedger ledger)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(ledger, s_options));
        File.Move(temporary, Path, overwrite: true);
    }

    public void Update(Action<DetachLedger> change)
    {
        var ledger = Load();
        change(ledger);
        Save(ledger);
    }
}

/// <summary>Monitors that follow a newly appearing source only notice it after it was gone for a while (ADR 0005).</summary>
public static class MinimumAbsence
{
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(25);

    /// <summary>How long to wait before attaching a monitor released at <paramref name="detachedAt"/>.</summary>
    public static TimeSpan RemainingWait(DateTimeOffset? detachedAt, DateTimeOffset now, TimeSpan minimum)
    {
        if (detachedAt is null || minimum <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }
        var remaining = minimum - (now - detachedAt.Value);
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }
}
