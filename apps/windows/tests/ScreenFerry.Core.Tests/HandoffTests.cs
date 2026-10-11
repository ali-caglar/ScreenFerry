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

public class MonitorMatchTests
{
    private static JsonElement Cases()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "test-vectors", "monitor-matching.json")));
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

    private static MonitorStatus Monitor(JsonElement json) =>
        new(json.GetProperty("identity").GetString()!, false, json.TryGetProperty("name", out var name) ? name.GetString() : null);

    [Theory]
    [MemberData(nameof(Vectors))]
    public void MatchesVector(string description)
    {
        var v = Cases().EnumerateArray().Single(c => c.GetProperty("description").GetString() == description);
        var match = MonitorMatch.Find(Monitor(v.GetProperty("reference")), v.GetProperty("candidates").EnumerateArray().Select(Monitor));
        var expected = v.GetProperty("match");
        Assert.Equal(expected.ValueKind == JsonValueKind.Null ? null : expected.GetString(), match?.Identity);
    }
}

public class HandoffClockTests
{
    [Fact]
    public void NewestWins()
    {
        var clock = new HandoffClock();
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(1, clock.Start(new Dictionary<string, string> { ["G8"] = "a" }, "a", now).Seq);
        Assert.False(clock.Receive(1, new Dictionary<string, string> { ["G8"] = "b" }, "a", now));
        Assert.True(clock.Receive(1, new Dictionary<string, string> { ["G8"] = "b" }, "b", now));
        Assert.False(clock.Receive(1, new Dictionary<string, string> { ["G8"] = "a" }, "a", now));
        Assert.True(clock.Receive(4, new Dictionary<string, string> { ["G8"] = "a" }, "a", now));
        Assert.Equal("a", clock.Target!.Owners["G8"]);
        Assert.Equal(5, clock.Start(new Dictionary<string, string>(), "b", now).Seq);
    }
}
