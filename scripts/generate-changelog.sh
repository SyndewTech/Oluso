#!/bin/bash
set -euo pipefail

# Generates CHANGELOG.md from git tags and commit messages.
# Run after tagging a new version.
#
# Usage: ./scripts/generate-changelog.sh > CHANGELOG.md
#
# Commit format expected:
#   feat: Add custom domain provisioning            → Features
#   feat(approval,core): info-request loop          → Features (scope stripped)
#   fix: Correct tenant resolution cache            → Fixes
#   fix(approval): missing memberCount              → Fixes
#   breaking: Drop Configuration column             → Breaking Changes
#   perf/refactor/docs/chore: …                     → Other
#
# Output order: newest tag first.

# Tags in ASCENDING order so we can compute correct ranges as we walk
# forward; the section bodies are accumulated into per-version files
# and emitted in reverse order at the end so the output is newest-first.
ASC_TAGS=$(git tag -l 'v*' --sort=v:refname)
if [ -z "$ASC_TAGS" ]; then
  echo "# Changelog"
  echo ""
  echo "_No version tags yet._"
  exit 0
fi

# Use a tmp dir to hold per-version bodies so we can replay them
# newest-first at the end. mktemp + trap so a failure doesn't litter.
WORK_DIR=$(mktemp -d)
trap 'rm -rf "$WORK_DIR"' EXIT

PREV=""
ORDER_FILE="$WORK_DIR/order"
touch "$ORDER_FILE"

for TAG in $ASC_TAGS; do
  VERSION="${TAG#v}"
  DATE=$(git log -1 --format=%ai "$TAG" | cut -d' ' -f1)

  # Initial tag: every commit reachable from $TAG. Subsequent tags: the
  # half-open range PREV..TAG so each commit is attributed to exactly
  # one release.
  if [ -z "$PREV" ]; then
    RANGE="$TAG"
  else
    RANGE="$PREV..$TAG"
  fi

  FEATURES=""
  FIXES=""
  BREAKING=""
  OTHER=""

  # `--no-merges` keeps the squash-merged PR titles, dropping the empty
  # "Merge branch …" rows the maintainers don't want in release notes.
  while IFS= read -r line; do
    [ -z "$line" ] && continue
    SUBJECT=$(echo "$line" | sed 's/^[a-f0-9]* //')

    # Strip a leading "<type>(<scope>): " or "<type>: " prefix in one
    # pass so feat(approval,core): foo and feat: foo both land as "foo".
    STRIPPED=$(echo "$SUBJECT" | sed -E 's/^[a-zA-Z!]+(\([^)]*\))?!?:[[:space:]]*//')

    case "$SUBJECT" in
      breaking:*|breaking\(*|*\!:*)
        BREAKING="$BREAKING\n- $STRIPPED" ;;
      feat:*|feat\(*)
        FEATURES="$FEATURES\n- $STRIPPED" ;;
      fix:*|fix\(*)
        FIXES="$FIXES\n- $STRIPPED" ;;
      *)
        OTHER="$OTHER\n- $STRIPPED" ;;
    esac
  done < <(git log --oneline --no-merges "$RANGE")

  BODY_FILE="$WORK_DIR/$TAG.md"
  {
    echo "## $VERSION — $DATE"
    echo ""
    if [ -n "$BREAKING" ]; then
      echo "### Breaking Changes"
      echo -e "$BREAKING"
      echo ""
    fi
    if [ -n "$FEATURES" ]; then
      echo "### Features"
      echo -e "$FEATURES"
      echo ""
    fi
    if [ -n "$FIXES" ]; then
      echo "### Fixes"
      echo -e "$FIXES"
      echo ""
    fi
    if [ -n "$OTHER" ]; then
      echo "### Other Changes"
      echo -e "$OTHER"
      echo ""
    fi
    # No commits at all in this range (e.g. retag with no new commits)
    # — emit a placeholder so the version still shows in the wiki.
    if [ -z "$BREAKING" ] && [ -z "$FEATURES" ] && [ -z "$FIXES" ] && [ -z "$OTHER" ]; then
      echo "_No commit changes in this release._"
      echo ""
    fi
  } > "$BODY_FILE"

  echo "$TAG" >> "$ORDER_FILE"
  PREV="$TAG"
done

# Header + body — replay in REVERSE order (newest first). `tac` is
# GNU-only; macOS shells use `tail -r`. Pick whichever is available so
# the script works on CI (Linux) and during local maintainer runs.
reverse() {
  if command -v tac >/dev/null 2>&1; then
    tac "$1"
  else
    tail -r "$1"
  fi
}

echo "# Changelog"
echo ""
reverse "$ORDER_FILE" | while IFS= read -r TAG; do
  cat "$WORK_DIR/$TAG.md"
done
