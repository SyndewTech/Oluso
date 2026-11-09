#!/bin/bash
# Sets up npm trusted publishing for every @syndew/oluso-* package this repository publishes: the JavaScript SDK and the
# frontend libraries and plugins. Run it from the repository root with your own npm login (npm login), once now and
# again whenever a new package is added.
#
# For each package:
#   1. if the name isn't on npm yet, publishes it from your machine (npm only lets a package that exists have a
#      trusted publisher);
#   2. adds GitHub Actions trusted publishing for SyndewTech/Oluso's publish-npm.yml, unless it is there already.
#
# After it has run, set the repository variable NPM_TRUSTED_PUBLISHING to true; v* tags then publish with no token.
#
# Usage: ./scripts/npm-trust.sh [--dry-run]
#   --dry-run   build, then list what would be published and configured, changing nothing on npm
set -euo pipefail

DRY_RUN=false
[ "${1:-}" = "--dry-run" ] && DRY_RUN=true

REPOSITORY="SyndewTech/Oluso"
WORKFLOW="publish-npm.yml"
ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR"

npm trust --help > /dev/null 2>&1 || { echo "npm trust needs a newer npm: npm install -g npm@latest" >&2; exit 1; }
NPM_USER="$(npm whoami 2>/dev/null)" || { echo "Not logged in to npm. Run: npm login" >&2; exit 1; }
echo "npm user: $NPM_USER"

echo "Building the JavaScript SDK..."
(cd sdk/js && npm ci --no-audit --no-fund && npm run build)
echo "Building the frontend libraries..."
(cd src/frontend && npm ci --no-audit --no-fund && npx nx run-many --target=build --all --configuration=production --outputStyle=static)

PACKAGES=(sdk/js)
for dir in src/frontend/dist/libs/*/ src/frontend/dist/libs/plugins/*/; do
    [ -f "$dir/package.json" ] && PACKAGES+=("${dir%/}")
done

for dir in "${PACKAGES[@]}"; do
    name=$(node -p "require('./$dir/package.json').name")
    echo ""
    echo "== $name"
    if npm view "$name" name > /dev/null 2>&1; then
        echo "   on npm already"
    else
        echo "   first publish ($(node -p "require('./$dir/package.json').version"))"
        $DRY_RUN || npm publish "$dir" --access public
    fi
    if npm trust list "$name" 2>/dev/null | grep -q "$WORKFLOW"; then
        echo "   trusted publisher already set"
    else
        $DRY_RUN || npm trust github "$name" --file "$WORKFLOW" --repository "$REPOSITORY" --yes
        echo "   trusted publisher $($DRY_RUN && echo "to add" || echo "added"): $REPOSITORY $WORKFLOW"
    fi
done

echo ""
echo "Done. Set the repository variable NPM_TRUSTED_PUBLISHING to true on $REPOSITORY."
