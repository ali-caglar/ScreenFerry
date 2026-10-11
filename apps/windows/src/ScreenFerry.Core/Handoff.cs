namespace ScreenFerry.Core;

public enum HandoffStepKind
{
    None,
    Attach,
    WaitForRelease,

    /// <summary>Set VCP 0x60 to the step's input code, then release.</summary>
    SwitchAndRelease,
    WaitForAttach,
    ReleaseAfterSettle,

    /// <summary>This agent is the target but doesn't see the monitor.</summary>
    Missing,
}

public readonly record struct HandoffStep(HandoffStepKind Kind, int? InputCode = null);

public readonly record struct HandoffState(bool Attached, int? InputCode = null);

/// <summary>What one agent does next for one monitor of a handoff (ADR 0007, <c>protocol/test-vectors/handoff.json</c>).</summary>
public static class Handoff
{
    public static readonly TimeSpan Expiry = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);

    /// <param name="states">What each connected agent, including <paramref name="local"/>, reports for the monitor.</param>
    public static HandoffStep Step(string local, string target, IReadOnlyDictionary<string, HandoffState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        var owners = states.Where(s => s.Key != target && s.Value.Attached).ToDictionary();
        var targetCode = states.TryGetValue(target, out var t) ? t.InputCode : null;
        var isDdc = owners.Count > 0 && targetCode is not null && owners.Values.All(s => s.InputCode is not null);
        if (local == target)
        {
            if (!states.TryGetValue(local, out var state))
            {
                return new(HandoffStepKind.Missing);
            }
            return state.Attached ? new(HandoffStepKind.None) : new(isDdc ? HandoffStepKind.WaitForRelease : HandoffStepKind.Attach);
        }
        if (!owners.ContainsKey(local))
        {
            return new(HandoffStepKind.None);
        }
        if (isDdc)
        {
            return new(HandoffStepKind.SwitchAndRelease, targetCode);
        }
        return new(t.Attached ? HandoffStepKind.ReleaseAfterSettle : HandoffStepKind.WaitForAttach);
    }

    /// <summary>Attached on the target and nowhere else.</summary>
    public static bool IsDone(string target, IReadOnlyDictionary<string, HandoffState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        return states.TryGetValue(target, out var t) && t.Attached && !states.Any(s => s.Key != target && s.Value.Attached);
    }
}
