using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenFerry.Core;

/// <summary>Monitors and the computers that should have them (ADR 0007). <paramref name="Owners"/>: identity → owner key ID (hex).</summary>
public sealed record Scene(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("owners")] IReadOnlyDictionary<string, string> Owners);

/// <param name="IsAvailable">Every computer the scene names is connected and sees its monitors.</param>
/// <param name="Hotkey">The digit of its hotkey, for the first nine scenes.</param>
public sealed record SceneEntry(Scene Scene, bool IsBuiltIn, bool IsAvailable, int? Hotkey);

/// <summary>A monitor some computer sees, and the key IDs of the computers that see it.</summary>
public sealed record KnownMonitor(string Identity, string Name, IReadOnlyList<string> SeenBy);

public sealed record Computer(string Id, string Name);

public static class SceneCatalog
{
    /// <summary>This computer first, then the paired peers.</summary>
    public static IReadOnlyList<Computer> Computers(Agent agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        return [new(agent.Identity.KeyIdHex, agent.Name), .. agent.Peers.Where(p => p.IsPaired).Select(p => new Computer(p.Id, p.Name))];
    }

    /// <summary>Physical monitors, each named by the identity of the first computer that sees it (ADR 0007 matching).</summary>
    public static IReadOnlyList<KnownMonitor> Monitors(IReadOnlyList<Computer> computers, IReadOnlyDictionary<string, IReadOnlyList<MonitorStatus>> seen)
    {
        ArgumentNullException.ThrowIfNull(computers);
        ArgumentNullException.ThrowIfNull(seen);
        var groups = new List<(MonitorStatus Reference, List<string> SeenBy)>();
        foreach (var computer in computers)
        {
            if (!seen.TryGetValue(computer.Id, out var monitors))
            {
                continue;
            }
            foreach (var monitor in monitors)
            {
                var group = groups.FirstOrDefault(g => !g.SeenBy.Contains(computer.Id) && MonitorMatch.Find(g.Reference, monitors) == monitor);
                if (group.SeenBy is null)
                {
                    groups.Add((monitor, [computer.Id]));
                }
                else
                {
                    group.SeenBy.Add(computer.Id);
                }
            }
        }
        return [.. groups
            .Select(g => new KnownMonitor(g.Reference.Identity, g.Reference.Name ?? g.Reference.Identity, g.SeenBy))
            .OrderBy(m => m.Identity, StringComparer.Ordinal)];
    }

    /// <summary>One built-in scene per computer, then <paramref name="custom"/> by name.</summary>
    /// <param name="seen">Key ID → the monitors that computer reports, for this computer and the connected peers.</param>
    public static IReadOnlyList<SceneEntry> Build(IReadOnlyList<Computer> computers, IReadOnlyDictionary<string, IReadOnlyList<MonitorStatus>> seen, IEnumerable<Scene> custom)
    {
        ArgumentNullException.ThrowIfNull(computers);
        ArgumentNullException.ThrowIfNull(seen);
        var known = Monitors(computers, seen);
        var entries = new List<SceneEntry>();
        foreach (var computer in computers)
        {
            var shared = known.Where(m => m.SeenBy.Contains(computer.Id) && m.SeenBy.Count > 1).ToList();
            var scene = new Scene($"all:{computer.Id}", $"All Monitors → {computer.Name}", shared.ToDictionary(m => m.Identity, _ => computer.Id));
            entries.Add(new SceneEntry(scene, true, shared.Count > 0, null));
        }
        foreach (var scene in custom.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var available = scene.Owners.Count > 0 && scene.Owners.All(o => Sees(seen, o.Value, o.Key));
            entries.Add(new SceneEntry(scene, false, available, null));
        }
        return [.. entries.Select((e, i) => i < 9 ? e with { Hotkey = i + 1 } : e)];
    }

    private static bool Sees(IReadOnlyDictionary<string, IReadOnlyList<MonitorStatus>> seen, string computer, string identity) =>
        seen.Values.SelectMany(m => m).FirstOrDefault(m => m.Identity == identity) is { } reference
        && seen.TryGetValue(computer, out var monitors) && MonitorMatch.Find(reference, monitors) is not null;
}

/// <summary>A JSON file under <c>%LOCALAPPDATA%\ScreenFerry\</c>.</summary>
public sealed class JsonFileStore<T>(string path)
    where T : new()
{
    private static readonly JsonSerializerOptions s_options = new() { WriteIndented = true };

    public string Path { get; } = path;

    public T Load() => File.Exists(Path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(Path), s_options) ?? new() : new();

    public void Save(T value)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, s_options));
        File.Move(temporary, Path, overwrite: true);
    }
}

public static class AppDataStores
{
    /// <summary>The user's scenes; built-in ones aren't stored.</summary>
    public static JsonFileStore<List<Scene>> Scenes() => new(PathFor("scenes.json"));

    /// <summary>Monitor identity → this computer's VCP 0x60 value.</summary>
    public static JsonFileStore<Dictionary<string, int>> InputCodes() => new(PathFor("input-codes.json"));

    private static string PathFor(string name) =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenFerry", name);
}
