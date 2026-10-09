# 0001. Record architecture decisions

- Status: Accepted
- Date: 2026-10-09

## Context

ScreenFerry is two native codebases, a protocol and a set of repository rules, maintained
in public. Decisions about any of them need to be findable later by contributors (and by
coding agents) without digging through PR threads.

## Decision

We record architecturally significant decisions as ADRs in `docs/adr/`, using the short
format in `template.md` (context, decision, consequences). `docs/BRIEF.md` is the source of
truth for scope and architecture; a decision that changes it gets an ADR first, and the
brief is updated in the same PR.

## Consequences

- Changes to stack, protocol transport, security model, licensing, release process or
  supported platforms need an ADR in the PR that makes them.
- ADRs are immutable once accepted; changing course means a new ADR that supersedes the old one.
