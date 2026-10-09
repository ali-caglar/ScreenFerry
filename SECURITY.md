# Security Policy

ScreenFerry agents accept commands from other machines on your network, so we take
security reports seriously.

## Reporting a vulnerability

**Please do not open a public issue.** Report it privately through GitHub:
[Report a vulnerability](https://github.com/ali-caglar/ScreenFerry/security/advisories/new)
(the repository's **Security** tab → **Report a vulnerability**).

Include what you found, how to reproduce it, and the impact you expect. You should get a
first response within 7 days. We will keep you updated while we work on a fix and credit
you in the advisory unless you prefer otherwise.

## Supported versions

Until 1.0, only the latest release receives security fixes.

## Scope

Especially interesting: anything that lets an unpaired machine make an agent switch inputs,
detach displays, or read pairing keys; weaknesses in pairing or the encrypted channel; and
supply-chain issues in the build and release workflows.
