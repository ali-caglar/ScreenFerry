# 0003. MIT license and DCO sign-off

- Status: Accepted
- Date: 2026-10-09

## Context

ScreenFerry is a small community utility. We want the lowest possible friction for users
and contributors, while still having a clear record that contributors had the right to
submit their code.

## Decision

- License: **MIT**. Apache-2.0's explicit patent grant was considered; for a project of
  this size and kind, MIT's simplicity wins.
- Contributions: **Developer Certificate of Origin** (`Signed-off-by:` trailer, added with
  `git commit -s`), enforced on PRs by the [DCO GitHub App](https://github.com/apps/dco).
  No CLA.
- `.github/dco.yml` sets `require.members: false`, which lets the repository owner's
  verified commits skip sign-off. This is what lets release-please PRs (opened with the
  owner's token) pass; the owner still signs off ordinary commits. Bot commits
  (Dependabot) are exempt by the app's default.

## Consequences

- Every commit in a PR needs a valid sign-off matching the author email, or the DCO check
  blocks the merge. CONTRIBUTING.md explains how to fix a missing sign-off.
- Relicensing later would need agreement from contributors; MIT keeps that unlikely to matter.
