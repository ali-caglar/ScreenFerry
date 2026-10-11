using System.IO;
using System.Text.Json;
using ScreenFerry.Core;
using ScreenFerry.Windows;

namespace ScreenFerry.App;

/// <summary>This PC's monitors, scenes and handoff steps. Thread-safe; <see cref="Changed"/> fires on any thread.</summary>
internal sealed class Controller : IHandoffExecutor
{
    private readonly Agent _agent;
    private readonly DisplayHandoff _displays = new();
    private readonly JsonFileStore<List<Scene>> _sceneStore = AppDataStores.Scenes();
    private readonly JsonFileStore<Dictionary<string, int>> _codeStore = AppDataStores.InputCodes();
    private readonly Lock _gate = new();

    /// <summary>Attached monitors whose input code this PC already tried to read.</summary>
    private readonly HashSet<string> _probed = [];
    private readonly List<Scene> _custom;
    private readonly Dictionary<string, int> _codes;
    private (string Scene, string Message)? _hotkeyError;

    public Controller(Agent agent)
    {
        _agent = agent;
        _custom = Load(_sceneStore);
        _codes = Load(_codeStore);
        agent.Executor = this;
        try
        {
            _displays.Reconcile();
        }
        catch (Exception e) when (e is DisplayTopologyException or InvalidOperationException or IOException)
        {
            SceneError = $"Couldn't keep released monitors detached: {e.Message}";
        }
    }

    public event Action? Changed;

    public IReadOnlyList<MonitorStatus> LocalMonitors { get; private set; } = [];

    public IReadOnlyList<string> Ambiguous { get; private set; } = [];

    public string? SceneError { get; private set; }

    public IReadOnlyList<Computer> Computers => SceneCatalog.Computers(_agent);

    public IReadOnlyList<KnownMonitor> KnownMonitors() => SceneCatalog.Monitors(Computers, Seen());

    public IReadOnlyList<SceneEntry> Scenes()
    {
        List<Scene> custom;
        lock (_gate)
        {
            custom = [.. _custom];
        }
        return SceneCatalog.Build(Computers, Seen(), custom);
    }

    public void Activate(SceneEntry entry)
    {
        SceneError = entry.IsAvailable ? null : $"{entry.Scene.Name} needs computers that aren't connected.";
        if (entry.IsAvailable)
        {
            _agent.Activate(entry.Scene.Owners);
        }
        Changed?.Invoke();
    }

    public void ActivateHotkey(int digit)
    {
        if (Scenes().FirstOrDefault(e => e.Hotkey == digit) is { } entry)
        {
            if (!entry.IsAvailable)
            {
                _hotkeyError = (entry.Scene.Name, "Needs computers that aren't connected.");
            }
            Activate(entry);
        }
    }

    /// <summary>The unavailable scene a hotkey asked for since the last call, if any.</summary>
    public (string Scene, string Message)? TakeHotkeyError()
    {
        var error = _hotkeyError;
        _hotkeyError = null;
        return error;
    }

    /// <summary>The EDID name of a monitor reported under this identity by any computer.</summary>
    public string MonitorName(string identity) =>
        LocalMonitors.Concat(_agent.Peers.SelectMany(p => p.Monitors)).FirstOrDefault(m => m.Identity == identity)?.Name ?? identity;

    public void AddScene(string name, IReadOnlyDictionary<string, string> owners) =>
        ChangeScenes(scenes => scenes.Add(new Scene(Guid.NewGuid().ToString(), name, owners)));

    public void DeleteScene(string id) => ChangeScenes(scenes => scenes.RemoveAll(s => s.Id == id));

    /// <summary>Rescans this PC's monitors and tells the agent.</summary>
    public void Refresh()
    {
        MonitorScan scan;
        try
        {
            scan = Windows.LocalMonitors.Scan();
        }
        catch (DisplayTopologyException)
        {
            return;
        }
        List<MonitorStatus> monitors;
        lock (_gate)
        {
            _probed.IntersectWith(scan.Monitors.Where(m => m.Attached).Select(m => m.Identity));
            monitors = [.. scan.Monitors.Select(m => m with { InputCode = _codes.TryGetValue(m.Identity, out var code) ? code : null })];
            LocalMonitors = monitors;
            Ambiguous = scan.Ambiguous;
        }
        _agent.SetLocalMonitors(monitors);
        LearnInputCodes(monitors);
        Changed?.Invoke();
    }

    public async Task PerformAsync(HandoffStep action, string identity)
    {
        int? ownCode;
        lock (_gate)
        {
            ownCode = _codes.TryGetValue(identity, out var code) ? code : null;
        }
        try
        {
            await Task.Run(() =>
            {
                switch (action.Kind)
                {
                    case HandoffStepKind.Attach:
                        _displays.Take(identity);
                        if (ownCode is { } code)
                        {
                            TrySetInput(identity, code);
                        }
                        break;
                    case HandoffStepKind.SwitchAndRelease:
                        InputSource.Set(identity, action.InputCode!.Value);
                        _displays.Release(identity);
                        break;
                    case HandoffStepKind.ReleaseAfterSettle:
                        _displays.Release(identity);
                        break;
                }
            }).ConfigureAwait(false);
        }
        finally
        {
            Refresh();
        }
    }

    private static void TrySetInput(string identity, int code)
    {
        try
        {
            InputSource.Set(identity, code);
        }
        catch (Exception e) when (e is DdcException or InvalidOperationException)
        {
        }
    }

    private static T Load<T>(JsonFileStore<T> store)
        where T : new()
    {
        try
        {
            return store.Load();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Key ID → monitors, for this PC and the connected peers.</summary>
    private Dictionary<string, IReadOnlyList<MonitorStatus>> Seen()
    {
        var seen = new Dictionary<string, IReadOnlyList<MonitorStatus>> { [_agent.Identity.KeyIdHex] = LocalMonitors };
        foreach (var peer in _agent.Peers.Where(p => p.IsConnected))
        {
            seen[peer.Id] = peer.Monitors;
        }
        return seen;
    }

    private void ChangeScenes(Action<List<Scene>> change)
    {
        lock (_gate)
        {
            change(_custom);
            try
            {
                _sceneStore.Save(_custom);
                SceneError = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                SceneError = $"Couldn't save scenes: {e.Message}";
            }
        }
        Changed?.Invoke();
    }

    /// <summary>Reads VCP 0x60 once per attach of each monitor: that value selects this PC (BRIEF §3.2).</summary>
    private void LearnInputCodes(List<MonitorStatus> monitors)
    {
        // While another computer has it attached too, the monitor may be showing that one.
        var elsewhere = _agent.Peers.Where(p => p.IsConnected).SelectMany(p => p.Monitors.Where(m => m.Attached).Select(m => m.Identity)).ToHashSet();
        foreach (var identity in monitors.Where(m => m.Attached && !elsewhere.Contains(m.Identity)).Select(m => m.Identity))
        {
            lock (_gate)
            {
                if (!_probed.Add(identity))
                {
                    continue;
                }
            }
            _ = Task.Run(() =>
            {
                int code;
                try
                {
                    code = InputSource.Read(identity);
                }
                catch (Exception e) when (e is DdcException or InvalidOperationException)
                {
                    return;
                }
                lock (_gate)
                {
                    if (_codes.TryGetValue(identity, out var known) && known == code)
                    {
                        return;
                    }
                    _codes[identity] = code;
                    try
                    {
                        _codeStore.Save(_codes);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                    }
                }
                Refresh();
            });
        }
    }
}
