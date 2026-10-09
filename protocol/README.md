# ScreenFerry protocol

The contract between agents. The macOS and Windows agents share no code; they share this.

- `schemas/` — one JSON Schema (draft 2020-12) per message type. **Normative.**
- `fixtures/<schema>/` — golden messages. Each must validate against `schemas/<schema>.schema.json`,
  and both agents' test suites must parse (and, from Phase 3, produce) them.
- `scripts/validate-fixtures.mjs` — the CI check.

```sh
cd protocol
npm ci
npm run validate
```

The macOS tests (`apps/macos/ScreenFerryKit`) and Windows tests (`apps/windows/tests`) read
every file under `fixtures/` directly.

## Changing the protocol

1. Change or add the schema.
2. Add or update fixtures covering the change.
3. Update both agents so their tests pass.
4. Bump `protocolVersion` if the change is not backward compatible and record it below.

## Messages

Only the common envelope exists so far; message types are designed in Phase 3.

| Field | Type | Meaning |
|---|---|---|
| `protocolVersion` | integer ≥ 0 | Protocol version of the sender. |
| `type` | string | Message type, e.g. `scene.activate`. |

Transport (Phase 3): JSON messages over a mutually authenticated, encrypted TCP channel
between paired agents; discovery via DNS-SD `_screenferry._tcp`.

## Version history

| `protocolVersion` | Date | Changes |
|---|---|---|
| 0 | 2026-10-09 | Draft envelope. No compatibility guarantees until 1. |
