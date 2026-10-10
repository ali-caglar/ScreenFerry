using System.Runtime.InteropServices;

namespace ScreenFerry.Windows;

/// <summary>CCD API (wingdi.h). Struct sizes must match: path 72, mode 64, target name 420 bytes.</summary>
internal static unsafe partial class DisplayConfigNative
{
    public const uint QdcAllPaths = 0x1;
    public const uint QdcOnlyActivePaths = 0x2;
    public const uint SdcTopologySupplied = 0x10;
    public const uint SdcValidate = 0x40;
    public const uint SdcApply = 0x80;
    public const uint SdcAllowPathOrderChanges = 0x2000;
    public const uint ModeInfoTypeSource = 1;
    public const uint SdcUseSuppliedDisplayConfig = 0x20;
    public const uint SdcSaveToDatabase = 0x200;
    public const uint SdcAllowChanges = 0x400;
    public const uint PathActive = 0x1;
    public const uint ModeIdxInvalid = 0xFFFFFFFF;
    public const uint DeviceInfoGetTargetName = 2;
    public const int ErrorInsufficientBuffer = 122;

    [StructLayout(LayoutKind.Sequential)]
    public struct Luid : IEquatable<Luid>
    {
        public uint LowPart;
        public int HighPart;

        public readonly bool Equals(Luid other) => LowPart == other.LowPart && HighPart == other.HighPart;

        public override readonly bool Equals(object? obj) => obj is Luid other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(LowPart, HighPart);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Rational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint OutputTechnology;
        public uint Rotation;
        public uint Scaling;
        public Rational RefreshRate;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PathInfo
    {
        public PathSourceInfo Source;
        public PathTargetInfo Target;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ModeInfo
    {
        public uint InfoType;
        public uint Id;
        public Luid AdapterId;
        public fixed ulong Payload[6];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DeviceInfoHeader
    {
        public uint Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TargetDeviceName
    {
        public DeviceInfoHeader Header;
        public uint Flags;
        public uint OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;
        public fixed char MonitorFriendlyDeviceName[64];
        public fixed char MonitorDevicePath[128];
    }

    [LibraryImport("user32.dll")]
    public static partial int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [LibraryImport("user32.dll")]
    public static partial int QueryDisplayConfig(uint flags, ref uint pathCount, PathInfo* paths, ref uint modeCount, ModeInfo* modes, nint currentTopologyId);

    [LibraryImport("user32.dll")]
    public static partial int SetDisplayConfig(uint pathCount, PathInfo* paths, uint modeCount, ModeInfo* modes, uint flags);

    [LibraryImport("user32.dll")]
    public static partial int DisplayConfigGetDeviceInfo(DeviceInfoHeader* request);
}
