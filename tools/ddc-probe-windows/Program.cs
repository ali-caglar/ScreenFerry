using System.Globalization;
using ScreenFerry.Core;
using ScreenFerry.Windows;

const string Usage = """
    Usage: ddc-probe <command>

      list                          Monitors, their identity, input and brightness
      edid <display>                Hex dump of the monitor's EDID
      caps <display>                MCCS capabilities string and supported VCP codes
      get  <display> <vcp>          Read a VCP code, e.g. `get 1 0x60`
      set  <display> <vcp> <value>  Write a VCP code, e.g. `set 1 0x10 50`

    <display> is the number shown by `list`. Numbers are decimal or 0x-prefixed hex.
    Useful codes: 0x10 brightness (harmless test), 0x60 input source.
    """;

if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "/?")
{
    Console.WriteLine(Usage);
    return 0;
}

var displays = PhysicalDisplay.All();
try
{
    switch (args[0])
    {
        case "list":
            List(displays);
            break;

        case "edid":
            var edid = Display(args, 1).RawEdid;
            for (var start = 0; start < edid.Length; start += 16)
            {
                var row = edid.AsSpan(start, Math.Min(16, edid.Length - start)).ToArray();
                Console.WriteLine($"{start:x4}  {string.Join(' ', row.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)))}");
            }
            break;

        case "caps":
            var caps = Display(args, 1).Capabilities();
            Console.WriteLine(caps.Raw);
            Console.WriteLine();
            foreach (var (code, values) in caps.Vcp.OrderBy(pair => pair.Key))
            {
                Console.WriteLine(values.Count == 0 ? Hex(code) : $"{Hex(code)}: {string.Join(' ', values.Select(v => Hex(v)))}");
            }
            break;

        case "get":
            var getCode = VcpCode(args, 2);
            Console.WriteLine(Describe(getCode, Display(args, 1).GetVcp(getCode)));
            break;

        case "set":
            var target = Display(args, 1);
            var setCode = VcpCode(args, 2);
            var value = Number(args, 3, "<value>");
            if (value > 0xFFFF)
            {
                throw new UsageException("Values are 0–65535.");
            }
            if (setCode == 0x60)
            {
                Console.WriteLine($"Switching input to {Hex(value)}. If that input isn't this PC, the monitor leaves this PC now.");
            }
            target.SetVcp(setCode, value);
            Console.WriteLine($"Sent {Hex(setCode)} = {value} ({Hex(value, 4)}).");
            break;

        default:
            throw new UsageException($"Unknown command: {args[0]}");
    }
    return 0;
}
catch (UsageException e)
{
    Console.Error.WriteLine($"{e.Message}\n\n{Usage}");
    return 2;
}
catch (DdcException e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}
finally
{
    foreach (var display in displays)
    {
        display.Dispose();
    }
}

void List(IReadOnlyList<PhysicalDisplay> all)
{
    if (all.Count == 0)
    {
        Console.WriteLine("No monitors with a DDC/CI handle found.");
        return;
    }
    for (var i = 0; i < all.Count; i++)
    {
        var display = all[i];
        var edid = display.Edid;
        var title = edid is null
            ? display.Description
            : $"{edid.Name ?? display.Description} — {edid.ManufacturerId} {Hex(edid.ProductCode, 4)}";
        Console.WriteLine($"{i + 1}. {title}  [{display.DeviceName}]");
        Console.WriteLine($"   identity:   {edid?.Identity ?? $"unreadable EDID ({display.RawEdid.Length} bytes)"}");
        if (edid is not null)
        {
            var serial = string.Join(" / ", new[] { edid.SerialString, edid.SerialNumber == 0 ? null : edid.SerialNumber.ToString(CultureInfo.InvariantCulture) }.OfType<string>());
            Console.WriteLine($"   serial:     {(serial.Length == 0 ? "none" : serial)}, made {edid.ManufactureYear}");
        }
        foreach (var (label, code) in new[] { ("input", (byte)0x60), ("brightness", (byte)0x10) })
        {
            string result;
            try
            {
                result = Describe(code, display.GetVcp(code));
            }
            catch (DdcException e)
            {
                result = $"error: {e.Message}";
            }
            Console.WriteLine($"   {label,-11} {result}");
        }
    }
}

PhysicalDisplay Display(string[] arguments, int position)
{
    var index = Number(arguments, position, "<display>");
    if (index < 1 || index > displays.Count)
    {
        throw new UsageException($"No display {index}; `list` shows {displays.Count}.");
    }
    return displays[(int)index - 1];
}

byte VcpCode(string[] arguments, int position)
{
    var code = Number(arguments, position, "<vcp>");
    return code <= 0xFF ? (byte)code : throw new UsageException("VCP codes are 0x00–0xFF.");
}

static uint Number(string[] arguments, int position, string name)
{
    if (arguments.Length <= position)
    {
        throw new UsageException($"Missing {name}.");
    }
    var text = arguments[position];
    var ok = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        ? uint.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
        : uint.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    return ok ? value : throw new UsageException($"Not a number: {text}");
}

static string Hex(uint value, int width = 2) => "0x" + value.ToString($"X{width}", CultureInfo.InvariantCulture);

static string Describe(byte code, (uint Current, uint Maximum) value)
{
    if (code != 0x60)
    {
        return $"{value.Current} of {value.Maximum}";
    }
    var name = MccsCapabilities.InputSourceName(value.Current);
    return $"{Hex(value.Current & 0xFF)}{(name is null ? "" : $" ({name})")}, raw {Hex(value.Current, 4)}";
}

internal sealed class UsageException(string message) : Exception(message);
