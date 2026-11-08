#!/bin/bash
# Updates the version in all frontend library package.json files.
# Usage: ./scripts/set-version.sh <version>
# Example: ./scripts/set-version.sh 1.2.3

set -euo pipefail

VERSION="${1:?Usage: set-version.sh <version>}"

# Validate semver-ish format
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+ ]]; then
  echo "Error: Version must be in semver format (e.g. 1.2.3)" >&2
  exit 1
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"

echo "Setting version to $VERSION"

# Every frontend workspace's libs folder in the repository.
LIB_DIRS=()
while IFS= read -r dir; do
  LIB_DIRS+=("$dir")
done < <(find "$ROOT_DIR" -maxdepth 3 -type d -path '*/frontend/libs' -not -path '*/node_modules/*')

# Update all lib package.json versions, and the JavaScript SDK's: it is released with them.
# Use sed -i'' (no space) which works on both macOS and Linux
find "${LIB_DIRS[@]}" "$ROOT_DIR/sdk/js" \
  -maxdepth 4 \
  -name package.json \
  -not -path '*/node_modules/*' \
  -exec perl -pi -e "s/^  \"version\": \".*?\"/  \"version\": \"$VERSION\"/" {} \;

# Libs that need other Oluso packages (e.g. the SDK) ask for this same release of them.
find "${LIB_DIRS[@]}" \
  -name package.json \
  -not -path '*/node_modules/*' \
  -exec perl -pi -e "s/\"(\@oluso\/[a-z0-9-]+)\": \"\*\"/\"\$1\": \"^$VERSION\"/g" {} \;

echo "Done. Updated all frontend lib and SDK package.json files to version $VERSION"
