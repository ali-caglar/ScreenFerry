using System.Text;
using System.Text.Json;
using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class EdidTests
{
    private static readonly string s_vectorsPath = Path.Combine(AppContext.BaseDirectory, "test-vectors", "monitor-identity.json");

    public static TheoryData<string> IdentityVectors()
    {
        var data = new TheoryData<string>();
        using var json = JsonDocument.Parse(File.ReadAllText(s_vectorsPath));
        foreach (var vector in json.RootElement.EnumerateArray())
        {
            data.Add(vector.GetProperty("description").GetString()!);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(IdentityVectors))]
    public void MatchesSharedIdentityVector(string description)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(s_vectorsPath));
        var vector = json.RootElement.EnumerateArray().Single(v => v.GetProperty("description").GetString() == description);
        var edid = Edid.Parse(Convert.FromHexString(vector.GetProperty("edid").GetString()!));

        Assert.Equal(vector.GetProperty("identity").GetString(), edid.Identity);
    }

    [Fact]
    public void ParsesIdentityFields()
    {
        var edid = Edid.Parse(MakeEdid(serial: 0x0100_0E00, descriptors: [(0xFC, "Odyssey G80SD"), (0xFF, "H1AK500000")]));

        Assert.Equal("SAM", edid.ManufacturerId);
        Assert.Equal(0xE030, edid.ProductCode);
        Assert.Equal(0x0100_0E00u, edid.SerialNumber);
        Assert.Equal("Odyssey G80SD", edid.Name);
        Assert.Equal("H1AK500000", edid.SerialString);
        Assert.Equal(2024, edid.ManufactureYear);
    }

    [Fact]
    public void RejectsInvalidData()
    {
        var corrupt = MakeEdid();
        corrupt[20]++;

        Assert.Throws<FormatException>(() => Edid.Parse(corrupt));
        Assert.Throws<FormatException>(() => Edid.Parse(new byte[10]));
        Assert.False(Edid.TryParse(corrupt, out _));
    }

    private static byte[] MakeEdid(string manufacturer = "SAM", ushort product = 0xE030, uint serial = 0, (byte Tag, string Text)[]? descriptors = null)
    {
        var bytes = new byte[128];
        new byte[] { 0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00 }.CopyTo(bytes, 0);
        var packed = ((manufacturer[0] - 64) << 10) | ((manufacturer[1] - 64) << 5) | (manufacturer[2] - 64);
        bytes[8] = (byte)(packed >> 8);
        bytes[9] = (byte)packed;
        bytes[10] = (byte)product;
        bytes[11] = (byte)(product >> 8);
        BitConverter.GetBytes(serial).CopyTo(bytes, 12);
        bytes[17] = 34;
        for (var i = 0; i < (descriptors?.Length ?? 0); i++)
        {
            var offset = 54 + (i * 18);
            bytes[offset + 3] = descriptors![i].Tag;
            var field = Encoding.ASCII.GetBytes(descriptors[i].Text).Take(13).ToList();
            if (field.Count < 13)
            {
                field.Add(0x0A);
            }
            while (field.Count < 13)
            {
                field.Add(0x20);
            }
            field.ToArray().CopyTo(bytes, offset + 5);
        }
        byte sum = 0;
        for (var i = 0; i < 127; i++)
        {
            sum += bytes[i];
        }
        bytes[127] = (byte)(0 - sum);
        return bytes;
    }
}
