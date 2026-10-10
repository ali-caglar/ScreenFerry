using ScreenFerry.Core;
using static ScreenFerry.Windows.DisplayConfigNative;

namespace ScreenFerry.Windows;

/// <summary>A monitor output known to the CCD API, attached to the desktop or not.</summary>
public sealed record DisplayTarget(string FriendlyName, string DevicePath, bool IsActive, Edid? Edid)
{
    internal Luid AdapterId { get; init; }

    internal uint TargetId { get; init; }
}

/// <summary>The active display configuration captured before a detach, used to restore it exactly.</summary>
/// <remarks>Adapter LUIDs change across reboots, so a layout saved before a reboot no longer applies.</remarks>
public sealed class DisplayLayout
{
    internal DisplayLayout(PathInfo[] paths, ModeInfo[] modes)
    {
        Paths = paths;
        Modes = modes;
    }

    internal PathInfo[] Paths { get; }

    internal ModeInfo[] Modes { get; }

    public byte[] ToBytes()
    {
        var pathBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(Paths.AsSpan());
        var modeBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(Modes.AsSpan());
        var result = new byte[8 + pathBytes.Length + modeBytes.Length];
        BitConverter.TryWriteBytes(result.AsSpan(0), Paths.Length);
        BitConverter.TryWriteBytes(result.AsSpan(4), Modes.Length);
        pathBytes.CopyTo(result.AsSpan(8));
        modeBytes.CopyTo(result.AsSpan(8 + pathBytes.Length));
        return result;
    }

    public static unsafe DisplayLayout? FromBytes(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return null;
        }
        var pathCount = BitConverter.ToInt32(bytes, 0);
        var modeCount = BitConverter.ToInt32(bytes, 4);
        if (pathCount < 0 || modeCount < 0 || bytes.Length != 8 + (pathCount * sizeof(PathInfo)) + (modeCount * sizeof(ModeInfo)))
        {
            return null;
        }
        var paths = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, PathInfo>(bytes.AsSpan(8, pathCount * sizeof(PathInfo))).ToArray();
        var modes = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ModeInfo>(bytes.AsSpan(8 + (pathCount * sizeof(PathInfo)))).ToArray();
        return new DisplayLayout(paths, modes);
    }
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

    /// <summary>
    /// Removes the display from the desktop and saves that, so it stays detached across replugs.
    /// Returns the layout from before, for <see cref="Attach"/>; null if it was already detached.
    /// </summary>
    public static DisplayLayout? Detach(DisplayTarget target)
    {
        var (paths, modes) = Query(QdcOnlyActivePaths);
        var remaining = paths.Where(p => !IsTarget(p, target)).ToArray();
        if (remaining.Length == paths.Length)
        {
            return null;
        }
        if (remaining.Length == 0)
        {
            throw new InvalidOperationException("Refusing to detach the last active display.");
        }
        var before = new DisplayLayout(paths, modes.ToArray());
        KeepPrimaryAtOrigin(remaining, modes);
        Apply("Detaching the display", Supplied(remaining, modes), Topology(remaining));
        return before;
    }

    /// <summary>
    /// Adds the display back. Restores <paramref name="previous"/> exactly when it still applies,
    /// otherwise the layout Windows last used for these displays, otherwise one Windows picks.
    /// </summary>
    public static void Attach(DisplayTarget target, DisplayLayout? previous = null)
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

        var attempts = new List<(PathInfo[], ModeInfo[], uint)>();
        if (previous is not null && previous.Paths.Any(p => IsTarget(p, target)))
        {
            attempts.Add(Supplied(previous.Paths, previous.Modes));
        }
        attempts.Add(Topology([.. active]));
        attempts.Add(Supplied([.. active], modes));
        Apply("Attaching the display", [.. attempts]);
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

    /// <summary>The paths with their modes, saved to the database; keeps positions and primary.</summary>
    private static (PathInfo[], ModeInfo[], uint) Supplied(PathInfo[] paths, ModeInfo[] modes)
    {
        var (suppliedPaths, suppliedModes) = WithReferencedModes(paths, modes);
        return (suppliedPaths, suppliedModes, SdcUseSuppliedDisplayConfig | SdcAllowChanges | SdcSaveToDatabase);
    }

    /// <summary>Only which paths are active; Windows takes the modes from its database or picks them.</summary>
    private static (PathInfo[], ModeInfo[], uint) Topology(PathInfo[] paths)
    {
        var topologyPaths = paths.ToArray();
        for (var i = 0; i < topologyPaths.Length; i++)
        {
            topologyPaths[i].Source.ModeInfoIdx = ModeIdxInvalid;
            topologyPaths[i].Target.ModeInfoIdx = ModeIdxInvalid;
        }
        return (topologyPaths, [], SdcTopologySupplied | SdcAllowPathOrderChanges);
    }

    /// <summary>Tries each configuration in order until one validates and applies.</summary>
    private static void Apply(string operation, params (PathInfo[] Paths, ModeInfo[] Modes, uint Flags)[] attempts)
    {
        var errors = new List<int>();
        foreach (var (paths, modes, flags) in attempts)
        {
            var error = Set(paths, modes, flags);
            if (error == 0)
            {
                return;
            }
            errors.Add(error);
        }
        throw new DisplayTopologyException($"{operation} (attempts: {string.Join(", ", errors)})", errors[^1]);
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
