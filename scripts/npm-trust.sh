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
    if [ -f "$dir/package.json" ]; then
        # The packages say "SEE LICENSE IN LICENSE"; the SDK copies its own in prepack.
        cp LICENSE "$dir"
        PACKAGES+=("${dir%/}")
    fi
done

# A failure on one package is reported and the rest still run; rerun the script for what failed
# (it skips what is already done).
FAILED=()
for dir in "${PACKAGES[@]}"; do
    name=$(node -p "require('./$dir/package.json').name")
    echo ""
    echo "== $name"
    if npm view "$name" name > /dev/null 2>&1; then
        echo "   on npm already"
    else
        echo "   first publish ($(node -p "require('./$dir/package.json').version"))"
        # ./ so npm reads sdk/js as a folder, not a GitHub user/repo.
        if ! $DRY_RUN && ! npm publish "./$dir" --access public; then
            FAILED+=("$name: first publish")
            continue
        fi
    fi
    if npm trust list "$name" 2>/dev/null | grep -q "$WORKFLOW"; then
        echo "   trusted publisher already set"
    elif $DRY_RUN; then
        echo "   trusted publisher to add: $REPOSITORY $WORKFLOW"
    elif npm trust github "$name" --file "$WORKFLOW" --repository "$REPOSITORY" --yes; then
        echo "   trusted publisher added: $REPOSITORY $WORKFLOW"
    else
        FAILED+=("$name: trusted publisher")
    fi
done

if [ ${#FAILED[@]} -gt 0 ]; then
    echo ""
    echo "Not done:" >&2
    printf '  %s\n' "${FAILED[@]}" >&2
    echo "A new package can take a minute to show on npm: rerun the script. Adding a trusted publisher needs a" >&2
    echo "login without a 2FA-bypassing token (npm logout, then npm login), or set it on npmjs.com:" >&2
    echo "package > Settings > Trusted publishing > GitHub Actions: $REPOSITORY, workflow $WORKFLOW." >&2
    exit 1
fi

echo ""
echo "Done. Set the repository variable NPM_TRUSTED_PUBLISHING to true on $REPOSITORY."
