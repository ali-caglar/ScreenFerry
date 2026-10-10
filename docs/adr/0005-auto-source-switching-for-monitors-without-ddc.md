# 0005. Auto source switching for monitors without DDC/CI

- Status: Accepted
- Date: 2026-10-10

## Context

The Phase 1 gate tested the maintainer's Samsung Odyssey OLED G8 (G80SD). It does not
answer DDC/CI at all: Windows reports `0xC0262582` (no I2C acknowledgement) over
DisplayPort, on the active input, with the PC as the only source. The ASUS VG279Q1A on the
same PC answers, so the setup is fine. The G8 is one of Samsung's Tizen "smart" monitors;
others in that family are widely reported to lack DDC/CI too.

The G8 has **Auto Source Switch+**. Tested with a Mac on HDMI and a PC on DisplayPort:

- When the PC re-attached the G8 in software (Windows display settings, i.e. the CCD API),
  the monitor switched from the Mac to the PC within 1–2 s.
- When the PC detached the G8 while the Mac was already sending a signal, the monitor did
  **not** switch to the Mac. It stayed on the now-empty DisplayPort input and started a
  60-second no-signal countdown.

So this monitor follows a *newly appearing* signal, not the loss of one.

Fallbacks considered: Samsung's LAN remote-control API (unverified on this model, needs
the monitor on the network), SmartThings (cloud, violates the no-cloud goal), IR (extra
hardware). Auto source switching needs nothing beyond what ScreenFerry already does.

## Decision

Each monitor has a **switch method**, learned per monitor and shared with peers:

- `ddc` — the monitor answers VCP `0x60`. Handoff as in BRIEF §3.3: the current owner
  switches the input, then detaches; the new owner attaches.
- `autoSource` — no DDC/CI; the monitor's own "auto source switch" setting must be on.
  Handoff:
  1. New owner B attaches the monitor. The new signal makes the monitor switch to B.
  2. After a short settle delay, owner A moves windows off the monitor and detaches it.
  3. Ownership is updated and broadcast.

  Invariant: **a machine that doesn't own an `autoSource` monitor keeps it detached.**
  Otherwise its signal is never "new" and step 1 can't work. This also fixes the original
  problem of a computer believing an invisible monitor is still connected.

An agent picks `ddc` if a `0x60` read succeeds while it is the active source, otherwise
proposes `autoSource`; the user confirms during setup, since the agent cannot see whether
the monitor's setting is on.

## Consequences

- Detach/attach (Phase 2) is now essential, not just a nicety: without it, `autoSource`
  monitors cannot move at all. The BRIEF §10 fallback "switch input only, don't detach"
  only applies to `ddc` monitors.
- Without DDC the agents cannot confirm the switch. If the monitor didn't switch (setting
  off), A's detach leaves it showing "no signal"; the UI must make undoing that easy.
- Verified 2026-10-10: a macOS software re-attach (`SLSConfigureDisplayEnabled`) also counts
  as a new signal; the G8 switched from the PC to the Mac.
- macOS drops a detached display from every display list, so the agent must persist the
  `CGDirectDisplayID`s it detached to be able to re-attach them.
- The settle delay and switch method become part of the protocol (Phase 3).

## Amendments

- 2026-10-10, **minimum absence.** The G8 only follows a source that has been gone for a
  while. Re-attaching after 5 s or 10 s did not switch it; after 20 s, 30 s and longer it
  did, from both Windows and macOS. Presumably it polls inactive inputs every ~10 s and
  misses a source that disappears and returns between polls. Rule: each `autoSource`
  monitor has a minimum absence `T` (default 25 s, stored per monitor). If the new owner
  detached the monitor less than `T` ago, its agent waits out the rest before attaching
  and the UI shows the handoff as in progress. The Phase 4 goal of 20 handoffs in a row
  is therefore paced by `T` for such monitors.
- 2026-10-10, **macOS details.** After a detach the HDMI link stays up and keeps
  retraining; enabling the display during a retrain fails with `kCGErrorFailure`, so
  attach retries for a few seconds. `SLSGetDisplayList` lists detached displays, and one
  monitor can appear under several `CGDirectDisplayID`s after its link drops, so displays
  are matched by EDID identity, not by id alone.
- 2026-10-10, **possible switch feedback (unconfirmed).** While the G8 showed the Mac,
  the Mac's HDMI link was steady; while it showed the PC (Mac detached), the link
  dropped about every 10 s. If that holds, an agent could tell whether an `autoSource`
  monitor is on its input. To be tested before relying on it.
