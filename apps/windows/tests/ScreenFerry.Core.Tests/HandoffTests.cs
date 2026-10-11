using System.Text.Json;
using ScreenFerry.Core;

namespace ScreenFerry.Core.Tests;

public class HandoffTests
{
    private static JsonElement Cases()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", "handoff.json")));
        return json.RootElement.GetProperty("cases").Clone();
    }

    public static TheoryData<string> Vectors()
    {
        var data = new TheoryData<string>();
        foreach (var vector in Cases().EnumerateArray())
        {
            data.Add(vector.GetProperty("description").GetString()!);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void MatchesVector(string description)
    {
        var v = Cases().EnumerateArray().Single(c => c.GetProperty("description").GetString() == description);
        var states = v.GetProperty("states").EnumerateObject().ToDictionary(
            p => p.Name,
            p => new HandoffState(
                p.Value.GetProperty("attached").GetBoolean(),
                p.Value.TryGetProperty("inputCode", out var code) ? code.GetInt32() : null));
        var target = v.GetProperty("target").GetString()!;
        var expected = v.GetProperty("expected");

        var step = Handoff.Step(v.GetProperty("self").GetString()!, target, states);

        Assert.Equal(expected.GetProperty("step").GetString(), JsonNamingPolicy.CamelCase.ConvertName(step.Kind.ToString()));
        Assert.Equal(expected.TryGetProperty("inputCode", out var c) ? c.GetInt32() : null, step.InputCode);
        Assert.Equal(expected.GetProperty("done").GetBoolean(), Handoff.IsDone(target, states));
    }
}
