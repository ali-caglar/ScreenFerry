using System.Runtime.InteropServices;
using Microsoft.Win32;
using ScreenFerry.Core;
using static ScreenFerry.Windows.NativeMethods;

namespace ScreenFerry.Windows;

/// <summary>A monitor reachable over DDC/CI through the Monitor Configuration API (dxva2).</summary>
public sealed unsafe class PhysicalDisplay : IDisposable
{
    private const int Attempts = 3;
    private readonly Lock _lock = new();
    private nint _handle;

    private PhysicalDisplay(nint handle, string description, string deviceName, string? deviceInterface)
    {
        _handle = handle;
        Description = description;
        DeviceName = deviceName;
        RawEdid = deviceInterface is null ? [] : ReadEdid(deviceInterface);
        Edid = Edid.TryParse(RawEdid, out var edid) ? edid : null;
    }

    public string Description { get; }

    /// <summary>GDI device name, e.g. <c>\\.\DISPLAY1</c>.</summary>
    public string DeviceName { get; }

    public byte[] RawEdid { get; }

    public Edid? Edid { get; }

    public static IReadOnlyList<PhysicalDisplay> All()
    {
        var monitors = new List<nint>();
        var handle = GCHandle.Alloc(monitors);
        try
        {
            EnumDisplayMonitors(0, 0, &CollectMonitor, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        var displays = new List<PhysicalDisplay>();
        foreach (var monitor in monitors)
        {
            var info = new MonitorInfoEx { Size = (uint)sizeof(MonitorInfoEx) };
            if (!GetMonitorInfo(monitor, &info))
            {
                continue;
            }
            var deviceName = new string(info.Device);
            var interfaces = MonitorInterfaces(deviceName);

            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count) || count == 0)
            {
                continue;
            }
            var physical = new PhysicalMonitor[count];
            fixed (PhysicalMonitor* pointer = physical)
            {
                if (!GetPhysicalMonitorsFromHMONITOR(monitor, count, pointer))
                {
                    continue;
                }
                for (var i = 0; i < count; i++)
                {
                    var description = new string(pointer[i].Description).TrimEnd('\0');
                    displays.Add(new PhysicalDisplay(pointer[i].Handle, description, deviceName, i < interfaces.Count ? interfaces[i] : null));
                }
            }
        }
        return displays;
    }

    public (uint Current, uint Maximum) GetVcp(byte code)
    {
        lock (_lock)
        {
            var error = 0;
            for (var attempt = 0; attempt < Attempts; attempt++)
            {
                if (GetVCPFeatureAndVCPFeatureReply(_handle, code, out _, out var current, out var maximum))
                {
                    return (current, maximum);
                }
                error = Marshal.GetLastPInvokeError();
                Thread.Sleep(50);
            }
            throw new DdcException($"Reading VCP 0x{code:X2}", error);
        }
    }

    public void SetVcp(byte code, uint value)
    {
        lock (_lock)
        {
            if (!SetVCPFeature(_handle, code, value))
            {
                throw new DdcException($"Writing VCP 0x{code:X2}", Marshal.GetLastPInvokeError());
            }
        }
    }

    public MccsCapabilities Capabilities()
    {
        lock (_lock)
        {
            if (!GetCapabilitiesStringLength(_handle, out var length))
            {
                throw new DdcException("Reading the capabilities length", Marshal.GetLastPInvokeError());
            }
            var buffer = new byte[length];
            fixed (byte* pointer = buffer)
            {
                if (!CapabilitiesRequestAndCapabilitiesReply(_handle, pointer, length))
                {
                    throw new DdcException("Reading the capabilities string", Marshal.GetLastPInvokeError());
                }
            }
            return new MccsCapabilities(System.Text.Encoding.ASCII.GetString(buffer).TrimEnd('\0'));
        }
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            DestroyPhysicalMonitor(_handle);
            _handle = 0;
        }
    }

    [UnmanagedCallersOnly]
    private static int CollectMonitor(nint monitor, nint hdc, Rect* rect, nint data)
    {
        ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(monitor);
        return 1;
    }

    /// <summary>Device interface paths of the active monitors on a display output, in enumeration order.</summary>
    private static List<string> MonitorInterfaces(string deviceName)
    {
        var result = new List<string>();
        var device = new DisplayDevice { Size = (uint)sizeof(DisplayDevice) };
        for (uint i = 0; EnumDisplayDevices(deviceName, i, &device, EddGetDeviceInterfaceName); i++)
        {
            if ((device.StateFlags & DisplayDeviceActive) != 0)
            {
                result.Add(new string(device.DeviceId));
            }
            device = new DisplayDevice { Size = (uint)sizeof(DisplayDevice) };
        }
        return result;
    }

    /// <summary>Reads EDID for an interface path like <c>\\?\DISPLAY#SAM7400#5&amp;1a2b&amp;0&amp;UID256#{guid}</c>.</summary>
    private static byte[] ReadEdid(string deviceInterface)
    {
        var parts = deviceInterface.Split('#');
        if (parts.Length < 3)
        {
            return [];
        }
        var keyPath = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{parts[1]}\{parts[2]}\Device Parameters";
        using var key = Registry.LocalMachine.OpenSubKey(keyPath);
        return key?.GetValue("EDID") as byte[] ?? [];
    }
}
