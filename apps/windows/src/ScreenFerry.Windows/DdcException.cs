using System.ComponentModel;

namespace ScreenFerry.Windows;

public sealed class DdcException : Exception
{
    public DdcException(string operation, int win32Error)
        : base($"{operation} failed (0x{win32Error:X8}: {new Win32Exception(win32Error).Message}). "
            + "Check that DDC/CI is on in the monitor's menu and that no dock, hub or adapter is in between.")
    {
        Win32Error = win32Error;
    }

    public DdcException()
    {
    }

    public DdcException(string message)
        : base(message)
    {
    }

    public DdcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public int Win32Error { get; }
}
