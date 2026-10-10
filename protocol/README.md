# ScreenFerry protocol

The contract between agents. The macOS and Windows agents share no code; they share this.

- `schemas/` — one JSON Schema (draft 2020-12) per message type. **Normative.**
- `fixtures/<schema>/` — golden messages. Each must validate against `schemas/<schema>.schema.json`,
  and both agents' test suites must parse and re-encode them.
- `test-vectors/` — inputs and expected outputs for algorithms both agents implement
  (`monitor-identity.json`, `pairing.json`); both test suites check them.
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

## Transport

Mutually authenticated TLS over TCP between agents found with DNS-SD `_screenferry._tcp`
(ADR 0006). Each message is a frame: a 4-byte big-endian length, then that many bytes of
UTF-8 JSON, at most 65 536. Receivers ignore unknown fields and messages of unknown `type`.

## Messages

Every message carries the envelope fields:

| Field | Type | Meaning |
|---|---|---|
| `protocolVersion` | integer ≥ 0 | Protocol version of the sender. |
| `type` | string | Message type. |

| `type` | When | Fields |
|---|---|---|
| `hello` | First message on a connection, both directions. | `keyId`, `name`, `platform`, `appVersion` |
| `pair.commit` | Pairing step 1, responder. | `commitment` |
| `pair.nonce` | Pairing steps 2 (initiator) and 3 (responder). | `nonce` |
| `pair.confirm` | Pairing step 5, both, after the user compared the codes. | `accepted` |
| `monitors` | After `hello` on a paired connection, and whenever the list changes. | `monitors[]`: `identity`, `name?`, `attached`, `inputCode?` |
| `ping` | Every 15 s when nothing else was sent; a peer silent for 45 s is gone. | — |
| `error` | Before closing a connection or aborting a pairing. | `code`, `message?` |

Byte strings (`keyId`, `commitment`, `nonce`) are 32 bytes as 64 lowercase hex digits.
On a connection with an unpaired key, only `hello`, `pair.*`, `ping` and `error` are
allowed. The pairing exchange and the code computation are specified in ADR 0006 and
pinned by `test-vectors/pairing.json`.

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

## Version history

| `protocolVersion` | Date | Changes |
|---|---|---|
| 0 | 2026-10-09 | Draft envelope. No compatibility guarantees until 1. |
| 0 | 2026-10-10 | Draft `hello`, `pair.*`, `monitors`, `ping`, `error` and framing. Becomes 1 when Phase 3 ships. |
