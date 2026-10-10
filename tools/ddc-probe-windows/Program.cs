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

      displays                      Monitors known to Windows, attached or detached
      detach <n> [seconds]          Detach a monitor, re-attach after `seconds` (default 10)
      attach <n>                    Re-attach a monitor (recovery; Windows picks the layout)

      release <monitor>             Detach and remember it (what the agent does for a non-owner)
      take <monitor> [T]            Wait until it has been released for T s (default 25), attach
      released                      Monitors this PC has released
      reconcile                     Detach released monitors that came back
      cycle <monitor> <count> [T] [hold]  Release and take repeatedly, holding hold s (default 10) after each take

    <display> is the number shown by `list`; <n> is the number shown by `displays`;
    <monitor> is a number from `displays` or an identity such as SAM-E030-H1AK500000.
    Numbers are decimal or 0x-prefixed hex.
    Useful codes: 0x10 brightness (harmless test), 0x60 input source.
    """;

if (args.Length == 0 || args[0] is "help" or "-h" or "--help" or "/?")
{
    Console.WriteLine(Usage);
    return 0;
}

switch (args[0])
{
    case "displays":
    case "detach":
    case "attach":
    case "release":
    case "take":
    case "released":
    case "reconcile":
    case "cycle":
        try
        {
            RunTopology(args);
            return 0;
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine($"{e.Message}\n\n{Usage}");
            return 2;
        }
        catch (Exception e) when (e is DisplayTopologyException or InvalidOperationException)
        {
            Console.Error.WriteLine($"error: {e.Message}");
            return 1;
        }
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

void RunTopology(string[] arguments)
{
    var targets = DisplayTopology.Targets();
    var handoff = new DisplayHandoff();
    switch (arguments[0])
    {
        case "release":
            var released = MonitorIdentity(arguments, targets);
            handoff.Release(released);
            Console.WriteLine($"Released {released}.");
            return;
        case "take":
            var taken = MonitorIdentity(arguments, targets);
            var minimum = arguments.Length > 2 ? TimeSpan.FromSeconds(Number(arguments, 2, "[T]")) : MinimumAbsence.Default;
            var wait = MinimumAbsence.RemainingWait(handoff.Released().GetValueOrDefault(taken)?.DetachedAt, DateTimeOffset.UtcNow, minimum);
            if (wait > TimeSpan.Zero)
            {
                Console.WriteLine($"Waiting {wait.TotalSeconds:F0} s so the monitor notices the change…");
            }
            handoff.Take(taken, minimum);
            Console.WriteLine($"Took {taken}.");
            return;
        case "released":
            var entries = handoff.Released();
            if (entries.Count == 0)
            {
                Console.WriteLine("Nothing released.");
            }
            foreach (var (identity, entry) in entries.OrderBy(e => e.Key, StringComparer.Ordinal))
            {
                Console.WriteLine($"{identity}  released {(DateTimeOffset.UtcNow - entry.DetachedAt).TotalSeconds:F0} s ago  (layout {(entry.PlatformData is null ? "none" : "saved")})");
            }
            return;
        case "reconcile":
            var reverted = handoff.Reconcile();
            Console.WriteLine(reverted.Count == 0 ? "Nothing to do." : $"Released again: {string.Join(", ", reverted)}");
            return;
        case "cycle":
            Cycle(handoff, MonitorIdentity(arguments, targets), (int)Number(arguments, 2, "<count>"),
                arguments.Length > 3 ? TimeSpan.FromSeconds(Number(arguments, 3, "[T]")) : MinimumAbsence.Default,
                TimeSpan.FromSeconds(arguments.Length > 4 ? Number(arguments, 4, "[hold]") : 10));
            return;
    }

    if (arguments[0] == "displays")
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            var name = t.Edid?.Name ?? (t.FriendlyName.Length > 0 ? t.FriendlyName : "Unknown monitor");
            Console.WriteLine($"{i + 1}. {(t.IsActive ? "attached" : "DETACHED")}  {name}  {t.Edid?.Identity ?? "identity unknown"}");
        }
        return;
    }

    var index = Number(arguments, 1, "<n>");
    if (index < 1 || index > targets.Count)
    {
        throw new UsageException($"No monitor {index}; `displays` shows {targets.Count}.");
    }
    var target = targets[(int)index - 1];

    if (arguments[0] == "attach")
    {
        DisplayTopology.Attach(target);
        Console.WriteLine("Attached.");
        return;
    }

    var layout = DisplayTopology.Detach(target);
    var seconds = arguments.Length > 2 ? Number(arguments, 2, "[seconds]") : 10;
    Console.WriteLine($"Detached monitor {index}. Re-attaching in {seconds} s; Ctrl+C re-attaches now.");
    using var interrupted = new ManualResetEventSlim();
    ConsoleCancelEventHandler onCancel = (_, e) =>
    {
        e.Cancel = true;
        interrupted.Set();
    };
    Console.CancelKeyPress += onCancel;
    interrupted.Wait(TimeSpan.FromSeconds(seconds));
    Console.CancelKeyPress -= onCancel;
    DisplayTopology.Attach(target, layout);
    Console.WriteLine($"Re-attached monitor {index}.");
}

static string MonitorIdentity(string[] arguments, IReadOnlyList<DisplayTarget> targets)
{
    if (arguments.Length <= 1)
    {
        throw new UsageException("Missing <monitor>.");
    }
    if (!uint.TryParse(arguments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
    {
        return arguments[1];
    }
    if (index < 1 || index > targets.Count || targets[(int)index - 1].Edid is not { } edid)
    {
        throw new UsageException($"No monitor {index} with a readable EDID; see `displays`.");
    }
    return edid.Identity;
}

static void Cycle(DisplayHandoff handoff, string identity, int count, TimeSpan minimum, TimeSpan hold)
{
    var failures = 0;
    for (var round = 1; round <= Math.Max(count, 1); round++)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            handoff.Release(identity);
            var waited = handoff.Take(identity, minimum);
            Console.WriteLine($"round {round}/{count}: attached (waited {waited.TotalSeconds:F0} s, total {(DateTimeOffset.UtcNow - started).TotalSeconds:F1} s); check the monitor shows it");
            // The monitor needs a few seconds to lock on; releasing at once hides whether it did.
            Thread.Sleep(hold);
        }
        catch (Exception e) when (e is DisplayTopologyException or InvalidOperationException)
        {
            failures++;
            Console.WriteLine($"round {round}/{count}: FAILED — {e.Message}");
            try
            {
                handoff.Take(identity, TimeSpan.Zero);
            }
            catch (Exception recovery) when (recovery is DisplayTopologyException or InvalidOperationException)
            {
                Console.WriteLine($"  recovery failed: {recovery.Message}");
            }
        }
    }
    Console.WriteLine($"{count - failures}/{count} rounds attached");
    if (failures > 0)
    {
        Environment.Exit(1);
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
