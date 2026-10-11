# 0006. Pairing and secure channel

- Status: Accepted
- Date: 2026-10-10

## Context

Agents act on commands from other machines: switch an input, detach a display. Anyone on
the LAN who can send such a command can blank the user's screens, so agents must only obey
peers the user paired on purpose (BRIEF §3.4). The brief fixes the user experience (confirm
a short code shown on both machines) and rules out home-made crypto.

Pinning a self-signed key is the usual way to authenticate peers without a CA. The hard
part is the short code. A code computed directly from the two public keys is not enough:
an attacker in the middle can generate keys until the codes in its two sessions match,
which takes about a million tries for six digits, i.e. seconds. Bluetooth's numeric
comparison (Secure Simple Pairing, LE Secure Connections) prevents that by having one side
commit to its random nonce before it sees the other's.

Platform TLS stacks: macOS Network.framework supports TLS 1.3, client certificates and a
custom verify block. Windows `SslStream` (SChannel) supports the same, but TLS 1.3 only on
Windows 11; Windows 10's SChannel stops at TLS 1.2.

## Decision

**Identity.** Each agent creates an ECDSA P-256 key pair once and a self-signed X.509
certificate for it. The key never leaves the machine: macOS Keychain; Windows user
certificate store (non-exportable CNG key). The agent's **key ID** is the SHA-256 of the
certificate's DER SubjectPublicKeyInfo. Only P-256 keys are accepted. Certificate fields
and expiry are ignored; only the key ID is trusted, so a renewed certificate with the same
key stays paired.

**Channel.** TCP with mutually authenticated TLS, one connection per pair of agents.
TLS 1.3; TLS 1.2 is accepted only because Windows 10 lacks 1.3, and then only with an
ECDHE key exchange and an AEAD cipher (checked after the handshake). Each side requests the
other's certificate and verifies it itself, without a CA: the peer's key ID must be paired,
or both sides must be in pairing mode. On a connection from an unpaired key only pairing
messages are allowed. If both peers dial each other, the connection opened by the lower key
ID survives.

**Discovery.** DNS-SD service `_screenferry._tcp` on a dynamic port. TXT record: `id` (key
ID, hex), `pv` (protocol version), `pairing=1` while in pairing mode. Discovery only finds
candidates; it is never trusted.

**Pairing (numeric comparison with commitment).** The user opens "Add computer" on both
machines, which turns on pairing mode for two minutes. On the initiator `I` they pick the
responder `R` from the discovered list. Over the TLS connection, where `PK_I` and `PK_R` are
the key IDs each side observed in the handshake and `N_I`, `N_R` fresh 32-byte random
nonces:

1. `R` sends `pair.commit` with `C = HMAC-SHA256(key: N_R, "ScreenFerry pair commit v1" ‖ PK_R ‖ PK_I)`.
2. `I` sends `pair.nonce` with `N_I`.
3. `R` sends `pair.nonce` with `N_R`. `I` recomputes `C` and aborts on mismatch.
4. Both show `code = BE32(SHA-256("ScreenFerry pair code v1" ‖ PK_I ‖ PK_R ‖ N_I ‖ N_R)[0..<4]) mod 10^6`
   as six digits.
5. The user confirms on each machine; each sends `pair.confirm`. A side stores the peer's
   key ID only after its own user and the peer accepted.

Messages out of this order abort the pairing. An attacker in the middle must fix its nonce
in one session before it learns the other session's code, so it succeeds with probability
10⁻⁶ per attempt, and every attempt needs the user to confirm.

Unpairing deletes the peer's key ID; the peer finds out the next time it connects.

## Consequences

- Only standard primitives and the platform TLS stacks; the pairing exchange copies a
  published design. Test vectors in `protocol/test-vectors/` pin the commitment and code
  computation for both agents.
- Pairing needs the user at both machines, which they are anyway to compare the codes.
- macOS needs a self-signed certificate for its keychain identity; the agent uses Apple's
  `swift-certificates` package to build it. The app needs `NSLocalNetworkUsageDescription`
  and `NSBonjourServices` in its Info.plist (macOS 15+ asks for local network access).
- Windows shows a firewall prompt the first time the agent listens. If inbound is blocked,
  the other peer still connects out to it.
- Windows has no managed DNS-SD API; the agent uses `DnsServiceRegister` / `DnsServiceBrowse`
  from `dnsapi.dll` (Windows 10 1903 and later).
- On Windows 10 the certificate a client presents in TLS 1.2 is visible on the LAN. It
  reveals only the key ID, which DNS-SD already advertises.
