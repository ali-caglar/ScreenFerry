#!/usr/bin/env bash
# Prints the version to stamp on a build of HEAD (see docs/adr/0004-versioning.md):
# X.Y.0 on a release tag, otherwise X.(Y+1).N where N >= 1 counts commits since the last release.
set -euo pipefail

root=$(git rev-parse --show-toplevel)
released=$(sed -nE 's/.*"\."[[:space:]]*:[[:space:]]*"([0-9]+\.[0-9]+\.[0-9]+)".*/\1/p' "$root/.release-please-manifest.json")
if [[ -z "$released" ]]; then
  echo "version.sh: cannot read the root version from .release-please-manifest.json" >&2
  exit 1
fi

tag="v$released"
if git rev-parse -q --verify "refs/tags/$tag" >/dev/null; then
  if [[ "$(git rev-list -n 1 "$tag")" == "$(git rev-parse HEAD)" ]]; then
    echo "$released"
    exit 0
  fi
  count=$(git rev-list --count "$tag..HEAD")
else
  count=$(git rev-list --count HEAD)
fi

IFS=. read -r major minor _ <<<"$released"
echo "$major.$((minor + 1)).$count"
