# 0007. Scenes and handoff coordination

- Status: Accepted
- Date: 2026-10-11

## Context

Phase 4 moves monitors between computers with a hotkey. A move involves at least two agents
(the monitor's current owner and its new owner) and, if the hotkey was pressed on a third
computer, a third one. BRIEF §3.3 and ADR 0005 fix the order of the steps for `ddc` and
`autoSource` monitors; this ADR decides how agents agree on what to do and who does which
step.

Forces:

- 2–4 computers, each connected to every other (ADR 0006). Any of them can go away
  mid-handoff, restart, or sleep.
- `autoSource` handoffs take up to the minimum absence (25 s by default) plus a settle time,
  so a handoff is a process, not a call.
- Agents already broadcast which monitors are attached on their desktop (`monitors`), and
  each agent keeps monitors it released detached across restarts (`DetachLedger`).
- The protocol must stay simple enough to implement twice and test against shared vectors.

## Decision

### Ownership is derived, not stored

The **owner** of a monitor is the computer that has it attached. Every other computer keeps
it detached (ADR 0005's invariant, now for every monitor). Agents learn each other's
attached state from `monitors`; no separate ownership record exists.

### A handoff is a target, not a script

Activating a scene broadcasts a `handoff` message to every connected paired peer and applies
it locally:

```json
{ "protocolVersion": 1, "type": "handoff", "seq": 7,
  "owners": [ { "identity": "SAM-E030-H1AK500000", "owner": "<key ID>" } ] }
```

Each agent then drives its own part of the change towards that target, re-evaluating
whenever its own or a peer's monitors change, until every listed monitor is attached only on
its target owner or the handoff expires (2 minutes). Agents send `monitors` as soon as a
step changes their desktop. There is no coordinator and no acknowledgement.

- **Newest wins.** `seq` is a Lamport counter: a new handoff gets one more than the highest
  `seq` the agent has seen. An agent replaces its current target with a received one if
  `(seq, sender key ID)` is greater, and ignores it otherwise. Monitors missing from the new
  target are left where they are.
- **Not persisted.** A restarted agent has no target; its `DetachLedger` keeps released
  monitors detached, and the user activates the scene again if needed.

### Who does what

For each monitor `X` in the target, with target owner `T` and current owners `O` (every
connected computer other than `T` reporting `X` attached; normally one, possibly none):

- The handoff is `ddc` if there is an `O`, and `T` and every `O` report an `inputCode` for
  `X`; otherwise `autoSource`. Every agent computes this from the same `monitors` data. An agent reports
  `inputCode` once it has read VCP `0x60` while `X` was attached on its desktop. This
  replaces ADR 0005's user confirmation of the switch method.
- `ddc`: each `O` sets `0x60` to `T`'s `inputCode`, then releases `X`. `T` waits until no
  `O` is left, then attaches it.
- `autoSource`: `T` attaches `X` (after the minimum absence). Each `O` waits until `T`
  reports `X` attached, waits the settle time (5 s), then releases `X`.
- No owner: `T` attaches `X`.
- After attaching, `T` sets `0x60` to its own `inputCode` if it has one.

The decision is a pure function of the target, the local agent's key ID and every agent's
latest `monitors`; `protocol/test-vectors/handoff.json` pins it for both agents.

A failing step is reported to every peer with `handoff.failed` (`seq`, `identity`,
`message`), so the computer where the hotkey was pressed can show it. Nothing is retried
automatically.

### Scenes and hotkeys are local

A scene maps monitors to owners. Each agent offers one built-in scene per computer ("All
monitors → <computer>", for every monitor shared with at least one peer) and lets the user
add scenes. Scenes and their hotkeys are stored per computer; only the resulting `handoff`
goes over the wire. A scene is available only while every computer it names is connected.

### One monitor, several identities

A monitor can report a different EDID on each input: the maintainer's Samsung G8 is
`SAM-E030-H1AK500000` over HDMI and `SAM-E035-H1AK500000` over DisplayPort. Agents therefore
match a monitor reported by another computer to their own like this:

1. a monitor with the identical identity, else
2. the only monitor with the same manufacturer, the same serial part of the identity
   (`MMM-PPPP-S` without `PPPP`) and the same EDID name.

Hash identities (`edid-…`) and monitors without a name match only exactly. A `handoff` names
monitors by any one of their identities; each agent resolves it to its own monitor and acts
on that. `protocol/test-vectors/monitor-matching.json` pins the rule. It can only merge
monitors that the vendor's placeholder serials already make indistinguishable.

### Ambiguous identities

If two monitors connected to one computer share an identity (vendors that repeat a placeholder
serial, `protocol/README.md`), that agent leaves both out of `monitors`, so they cannot be
part of a scene, and its UI says why.

## Consequences

- Adding `handoff` and `handoff.failed` is backward compatible (unknown types are ignored),
  so `protocolVersion` stays 1.
- Handoffs survive a slow peer and a lost message: whatever arrives later moves the state
  further, and the next `monitors` update triggers the next step.
- A peer that disconnects mid-handoff stalls only the monitors it had to act on; they stay
  where they are and the handoff expires. The UI shows the handoff as incomplete.
- The minimum absence and settle time are local settings of the agent that waits; they are
  not on the wire.
- Agents now read VCP `0x60` while a monitor is attached and persist the value as their input
  code for it.
- Scenes have to be set up on each computer. Syncing them can come later as a new message
  type without breaking this one.
