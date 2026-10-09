using System.Text.Json;
using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class ProtocolFixtureTests
{
    private static readonly string FixturesDir = Path.Combine(AppContext.BaseDirectory, "fixtures");

    public static TheoryData<string> Fixtures()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixturesDir, "*.json", SearchOption.AllDirectories).Order())
        {
            data.Add(Path.GetRelativePath(FixturesDir, path));
        }
        return data;
    }

    [Fact]
    public void FixturesExist() =>
        Assert.NotEmpty(Directory.EnumerateFiles(FixturesDir, "*.json", SearchOption.AllDirectories));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EnvelopeDeserializes(string relativePath)
    {
        var json = File.ReadAllText(Path.Combine(FixturesDir, relativePath));
        var envelope = JsonSerializer.Deserialize<ProtocolEnvelope>(json);

        Assert.NotNull(envelope);
        Assert.True(envelope.ProtocolVersion >= 0);
        Assert.False(string.IsNullOrEmpty(envelope.Type));
    }
}
