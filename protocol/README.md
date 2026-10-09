# ScreenFerry protocol

The contract between agents. The macOS and Windows agents share no code; they share this.

- `schemas/` — one JSON Schema (draft 2020-12) per message type. **Normative.**
- `fixtures/<schema>/` — golden messages. Each must validate against `schemas/<schema>.schema.json`,
  and both agents' test suites must parse (and, from Phase 3, produce) them.
- `test-vectors/` — inputs and expected outputs for algorithms both agents implement
  (e.g. `monitor-identity.json`); both test suites check them.
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

## Monitor identity

Both agents derive the same key for a physical monitor from its EDID base block
(the first 128 bytes; extension blocks are ignored):

1. `MMM-PPPP-S`, where `MMM` is the three-letter PNP manufacturer ID, `PPPP` the product
   code as four uppercase hex digits, and `S` the serial-number display descriptor (`0xFF`),
   trimmed — for example `SAM-E030-H1AK500000`.
2. Without that descriptor, `S` is the 32-bit serial number (bytes 12–15, little-endian) in
   decimal, if it is not 0 — for example `AUS-2703-153957`.
3. Otherwise `edid-` followed by the first 8 bytes of the SHA-256 of the base block, as
   lowercase hex.

Known issue: some vendors put the same placeholder serial in every unit (Samsung uses
`H1AK500000`), so two identical monitors can share a key. To be resolved in Phase 3.

Transport (Phase 3): JSON messages over a mutually authenticated, encrypted TCP channel
between paired agents; discovery via DNS-SD `_screenferry._tcp`.

## Version history

| `protocolVersion` | Date | Changes |
|---|---|---|
| 0 | 2026-10-09 | Draft envelope. No compatibility guarantees until 1. |
