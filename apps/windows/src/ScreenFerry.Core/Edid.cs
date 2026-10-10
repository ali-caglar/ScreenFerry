using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ScreenFerry.Core;

/// <summary>The fields of an EDID base block that ScreenFerry uses.</summary>
public sealed class Edid
{
    public const int BlockSize = 128;
    private static readonly byte[] s_header = [0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00];

    private Edid(byte[] baseBlock)
    {
        BaseBlock = baseBlock;
        var packed = (baseBlock[8] << 8) | baseBlock[9];
        ManufacturerId = new string([(char)(((packed >> 10) & 0x1F) + 64), (char)(((packed >> 5) & 0x1F) + 64), (char)((packed & 0x1F) + 64)]);
        ProductCode = (ushort)(baseBlock[10] | (baseBlock[11] << 8));
        SerialNumber = BitConverter.ToUInt32(baseBlock, 12);
        ManufactureYear = 1990 + baseBlock[17];

        for (var offset = 54; offset <= 108; offset += 18)
        {
            if (baseBlock[offset] != 0 || baseBlock[offset + 1] != 0 || baseBlock[offset + 2] != 0)
            {
                continue;
            }
            var text = DescriptorText(baseBlock.AsSpan(offset + 5, 13));
            switch (baseBlock[offset + 3])
            {
                case 0xFF: SerialString = text; break;
                case 0xFC: Name = text; break;
            }
        }
    }

    /// <summary>The 128-byte base block, without extension blocks.</summary>
    public byte[] BaseBlock { get; }

    /// <summary>Three-letter PNP ID, e.g. <c>SAM</c>.</summary>
    public string ManufacturerId { get; }

    public ushort ProductCode { get; }

    /// <summary>0 when the monitor doesn't set it.</summary>
    public uint SerialNumber { get; }

    /// <summary>Display descriptor <c>0xFF</c>, if present.</summary>
    public string? SerialString { get; }

    /// <summary>Display descriptor <c>0xFC</c>, if present.</summary>
    public string? Name { get; }

    public int ManufactureYear { get; }

    /// <summary>Stable identity shared by every computer that sees this monitor; see <c>protocol/README.md</c>.</summary>
    public string Identity
    {
        get
        {
            var prefix = $"{ManufacturerId}-{ProductCode:X4}";
            if (SerialString is not null)
            {
                return $"{prefix}-{SerialString}";
            }
            if (SerialNumber != 0)
            {
                return $"{prefix}-{SerialNumber.ToString(CultureInfo.InvariantCulture)}";
            }
            return "edid-" + Convert.ToHexStringLower(SHA256.HashData(BaseBlock).AsSpan(0, 8));
        }
    }

    public static Edid Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < BlockSize)
        {
            throw new FormatException($"EDID is {data.Length} bytes; need at least {BlockSize}.");
        }
        var block = data[..BlockSize].ToArray();
        if (!block.AsSpan(0, 8).SequenceEqual(s_header))
        {
            throw new FormatException("EDID header is invalid.");
        }
        byte sum = 0;
        foreach (var b in block)
        {
            sum += b;
        }
        if (sum != 0)
        {
            throw new FormatException("EDID checksum is invalid.");
        }
        return new Edid(block);
    }

    public static bool TryParse(ReadOnlySpan<byte> data, out Edid? edid)
    {
        try
        {
            edid = Parse(data);
            return true;
        }
        catch (FormatException)
        {
            edid = null;
            return false;
        }
    }

    private static string? DescriptorText(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOfAny((byte)0x0A, (byte)0x00);
        var text = Encoding.ASCII.GetString(end >= 0 ? bytes[..end] : bytes).Trim(' ');
        return text.Length == 0 ? null : text;
    }
}
