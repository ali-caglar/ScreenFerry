using ScreenFerry.Core;
using static ScreenFerry.Windows.DisplayConfigNative;

namespace ScreenFerry.Windows;

/// <summary>A monitor output known to the CCD API, attached to the desktop or not.</summary>
public sealed record DisplayTarget(string FriendlyName, string DevicePath, bool IsActive, Edid? Edid)
{
    internal Luid AdapterId { get; init; }

    internal uint TargetId { get; init; }
}

public sealed class DisplayTopologyException(string operation, int error)
    : Exception($"{operation} failed (Win32 error {error}: {new System.ComponentModel.Win32Exception(error).Message}).");

/// <summary>Detaches and attaches individual displays, like "Disconnect this display" in Settings.</summary>
public static unsafe class DisplayTopology
{
    /// <summary>Every connected monitor, including detached ones, in a stable order.</summary>
    public static IReadOnlyList<DisplayTarget> Targets()
    {
        var (paths, _) = Query(QdcAllPaths);
        return paths
            .Where(p => p.Target.TargetAvailable != 0)
            .GroupBy(p => (p.Target.AdapterId, p.Target.Id))
            .Select(group =>
            {
                var first = group.First();
                var name = TargetName(first.Target.AdapterId, first.Target.Id);
                var devicePath = name.Path;
                return new DisplayTarget(
                    name.FriendlyName,
                    devicePath,
                    group.Any(p => (p.Flags & PathActive) != 0),
                    Edid.TryParse(MonitorRegistry.ReadEdid(devicePath), out var edid) ? edid : null)
                {
                    AdapterId = first.Target.AdapterId,
                    TargetId = first.Target.Id,
                };
            })
            .OrderBy(t => t.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Removes the display from the desktop and saves that, so it stays detached across replugs.</summary>
    public static void Detach(DisplayTarget target)
    {
        var (paths, modes) = Query(QdcOnlyActivePaths);
        var remaining = paths.Where(p => !IsTarget(p, target)).ToArray();
        if (remaining.Length == paths.Length)
        {
            return;
        }
        if (remaining.Length == 0)
        {
            throw new InvalidOperationException("Refusing to detach the last active display.");
        }
        KeepPrimaryAtOrigin(remaining, modes);
        Apply(remaining, modes, "Detaching the display");
    }

    /// <summary>Adds the display back to the desktop, extending it.</summary>
    public static void Attach(DisplayTarget target)
    {
        var (all, modes) = Query(QdcAllPaths);
        var active = all.Where(p => (p.Flags & PathActive) != 0).ToList();
        if (active.Any(p => IsTarget(p, target)))
        {
            return;
        }
        var usedSources = active.Select(p => (p.Source.AdapterId, p.Source.Id)).ToHashSet();
        var candidate = all
            .Where(p => IsTarget(p, target) && p.Target.TargetAvailable != 0)
            .Where(p => !usedSources.Contains((p.Source.AdapterId, p.Source.Id)))
            .Cast<PathInfo?>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException("No free display source to attach this display to.");

        var path = candidate;
        path.Flags |= PathActive;
        path.Source.ModeInfoIdx = ModeIdxInvalid;
        path.Target.ModeInfoIdx = ModeIdxInvalid;
        active.Add(path);
        Apply([.. active], modes, "Attaching the display");
    }

    private static bool IsTarget(PathInfo path, DisplayTarget target) =>
        path.Target.AdapterId.Equals(target.AdapterId) && path.Target.Id == target.TargetId;

    private static (PathInfo[] Paths, ModeInfo[] Modes) Query(uint flags)
    {
        while (true)
        {
            var error = GetDisplayConfigBufferSizes(flags, out var pathCount, out var modeCount);
            if (error != 0)
            {
                throw new DisplayTopologyException("GetDisplayConfigBufferSizes", error);
            }
            var paths = new PathInfo[pathCount];
            var modes = new ModeInfo[modeCount];
            fixed (PathInfo* pathPointer = paths)
            fixed (ModeInfo* modePointer = modes)
            {
                error = QueryDisplayConfig(flags, ref pathCount, pathPointer, ref modeCount, modePointer, 0);
            }
            if (error == ErrorInsufficientBuffer)
            {
                continue;
            }
            if (error != 0)
            {
                throw new DisplayTopologyException("QueryDisplayConfig", error);
            }
            return (paths[..(int)pathCount], modes[..(int)modeCount]);
        }
    }

    /// <summary>The primary display must sit at (0, 0); if it was removed, shift the layout to the nearest remaining one.</summary>
    private static void KeepPrimaryAtOrigin(PathInfo[] paths, ModeInfo[] modes)
    {
        var sourceModes = paths
            .Select(p => p.Source.ModeInfoIdx)
            .Where(i => i < modes.Length && modes[i].InfoType == ModeInfoTypeSource)
            .Distinct()
            .ToList();
        if (sourceModes.Count == 0 || sourceModes.Any(i => Position(ref modes[i]) == (0, 0)))
        {
            return;
        }
        var origin = sourceModes.Select(i => Position(ref modes[i])).MinBy(p => Math.Abs((long)p.X) + Math.Abs((long)p.Y));
        foreach (var i in sourceModes)
        {
            var (x, y) = Position(ref modes[i]);
            SetPosition(ref modes[i], x - origin.X, y - origin.Y);
        }
    }

    // DISPLAYCONFIG_SOURCE_MODE: width, height, pixelFormat, then POINTL position at byte 12.
    private static (int X, int Y) Position(ref ModeInfo mode)
    {
        fixed (ulong* payload = mode.Payload)
        {
            var bytes = (byte*)payload;
            return (*(int*)(bytes + 12), *(int*)(bytes + 16));
        }
    }

    private static void SetPosition(ref ModeInfo mode, int x, int y)
    {
        fixed (ulong* payload = mode.Payload)
        {
            var bytes = (byte*)payload;
            *(int*)(bytes + 12) = x;
            *(int*)(bytes + 16) = y;
        }
    }

    /// <summary>
    /// Applies the given active paths: first with their modes (keeps the layout, saved to the database),
    /// then as a bare topology for Windows to complete. Each is validated before it is applied.
    /// </summary>
    private static void Apply(PathInfo[] paths, ModeInfo[] modes, string operation)
    {
        var (suppliedPaths, suppliedModes) = WithReferencedModes(paths, modes);
        var supplied = Set(suppliedPaths, suppliedModes, SdcUseSuppliedDisplayConfig | SdcAllowChanges | SdcSaveToDatabase);
        if (supplied == 0)
        {
            return;
        }

        var topologyPaths = paths.ToArray();
        for (var i = 0; i < topologyPaths.Length; i++)
        {
            topologyPaths[i].Source.ModeInfoIdx = ModeIdxInvalid;
            topologyPaths[i].Target.ModeInfoIdx = ModeIdxInvalid;
        }
        var topology = Set(topologyPaths, [], SdcTopologySupplied | SdcAllowPathOrderChanges);
        if (topology == 0)
        {
            return;
        }
        throw new DisplayTopologyException($"{operation} (supplied config: {supplied}, supplied topology: {topology})", topology);
    }

    /// <summary>Validates, then applies; returns the Win32 error, 0 on success.</summary>
    private static int Set(PathInfo[] paths, ModeInfo[] modes, uint flags)
    {
        fixed (PathInfo* pathPointer = paths)
        fixed (ModeInfo* modePointer = modes)
        {
            var modeArg = modes.Length == 0 ? null : modePointer;
            var error = SetDisplayConfig((uint)paths.Length, pathPointer, (uint)modes.Length, modeArg, flags | SdcValidate);
            return error != 0 ? error : SetDisplayConfig((uint)paths.Length, pathPointer, (uint)modes.Length, modeArg, flags | SdcApply);
        }
    }

    /// <summary>Copies the paths with their mode indices remapped to a compact array of only the modes they use.</summary>
    private static (PathInfo[] Paths, ModeInfo[] Modes) WithReferencedModes(PathInfo[] paths, ModeInfo[] modes)
    {
        var result = paths.ToArray();
        var used = new List<ModeInfo>();
        var remap = new Dictionary<uint, uint>();
        uint Remap(uint index)
        {
            if (index == ModeIdxInvalid || index >= modes.Length)
            {
                return ModeIdxInvalid;
            }
            if (!remap.TryGetValue(index, out var mapped))
            {
                mapped = (uint)used.Count;
                used.Add(modes[index]);
                remap[index] = mapped;
            }
            return mapped;
        }
        for (var i = 0; i < result.Length; i++)
        {
            result[i].Source.ModeInfoIdx = Remap(result[i].Source.ModeInfoIdx);
            result[i].Target.ModeInfoIdx = Remap(result[i].Target.ModeInfoIdx);
        }
        return (result, [.. used]);
    }

    private static (string FriendlyName, string Path) TargetName(Luid adapterId, uint targetId)
    {
        var name = new TargetDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = DeviceInfoGetTargetName,
                Size = (uint)sizeof(TargetDeviceName),
                AdapterId = adapterId,
                Id = targetId,
            },
        };
        if (DisplayConfigGetDeviceInfo(&name.Header) != 0)
        {
            return ("", "");
        }
        return (new string(name.MonitorFriendlyDeviceName), new string(name.MonitorDevicePath));
    }
}
