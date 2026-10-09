using System.Globalization;

namespace ScreenFerry.Core;

/// <summary>Parses an MCCS capabilities string such as <c>(prot(monitor)vcp(10 60(0F 11 12))mccs_ver(2.1))</c>.</summary>
public sealed class MccsCapabilities
{
    public MccsCapabilities(string raw)
    {
        Raw = raw;
        var section = Section("vcp", raw);
        Vcp = section is null ? new Dictionary<byte, IReadOnlyList<byte>>() : ParseVcp(section);
    }

    public string Raw { get; }

    /// <summary>VCP codes the monitor reports, with their allowed values when listed.</summary>
    public IReadOnlyDictionary<byte, IReadOnlyList<byte>> Vcp { get; }

    /// <summary>Standard MCCS names for input source (VCP 0x60) values. Monitors often deviate.</summary>
    public static string? InputSourceName(uint value) => (value & 0xFF) switch
    {
        0x01 => "VGA 1",
        0x02 => "VGA 2",
        0x03 => "DVI 1",
        0x04 => "DVI 2",
        0x0F => "DisplayPort 1",
        0x10 => "DisplayPort 2",
        0x11 => "HDMI 1",
        0x12 => "HDMI 2",
        0x1B => "USB-C",
        _ => null,
    };

    private static string? Section(string name, string raw)
    {
        var start = raw.IndexOf(name + "(", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }
        start += name.Length + 1;
        var depth = 1;
        for (var i = start; i < raw.Length; i++)
        {
            if (raw[i] == '(')
            {
                depth++;
            }
            else if (raw[i] == ')' && --depth == 0)
            {
                return raw[start..i];
            }
        }
        return raw[start..];
    }

    private static Dictionary<byte, IReadOnlyList<byte>> ParseVcp(string body)
    {
        var result = new Dictionary<byte, IReadOnlyList<byte>>();
        byte? lastCode = null;
        List<byte>? values = null;
        var token = new System.Text.StringBuilder();

        void Flush()
        {
            if (byte.TryParse(token.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                if (values is not null)
                {
                    values.Add(b);
                }
                else
                {
                    result[b] = [];
                    lastCode = b;
                }
            }
            token.Clear();
        }

        foreach (var c in body)
        {
            switch (c)
            {
                case '(':
                    Flush();
                    values = [];
                    break;
                case ')':
                    Flush();
                    if (lastCode is { } code && values is not null)
                    {
                        result[code] = values;
                    }
                    values = null;
                    break;
                case ' ':
                    Flush();
                    break;
                default:
                    token.Append(c);
                    break;
            }
        }
        Flush();
        if (lastCode is { } last && values is not null)
        {
            result[last] = values;
        }
        return result;
    }
}
