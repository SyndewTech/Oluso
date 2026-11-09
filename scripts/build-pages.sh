#!/bin/bash
# Builds one portal (admin, account or workspace) as a static site for Cloudflare Pages or any static host, with
# the runtime config.js that points it at your Oluso server. The portals read window.__OLUSO_CONFIG__ from it.
#
# Usage: scripts/build-pages.sh <admin|account|workspace>    (from the repository root)
#
# Environment:
#   OLUSO_SERVER_URL      required: the Oluso server's public URL, e.g. https://auth.example.com
#   OLUSO_API_URL         optional: the admin API, if not OLUSO_SERVER_URL/api/admin
#   OLUSO_ADMIN_URL       optional: the admin portal's public URL (links between the portals)
#   OLUSO_ACCOUNT_URL     optional: the account portal's public URL
#   OLUSO_WORKSPACE_URL   optional: the workspace portal's public URL
#
# The site is written to src/frontend/dist/apps/<app>-standalone/browser.
#
# Cloudflare Pages project settings:
#   Root directory:    (repository root)
#   Build command:     ./scripts/build-pages.sh admin        (or account / workspace)
#   Output directory:  src/frontend/dist/apps/admin-standalone/browser
#   Variables:         NODE_VERSION=22, OLUSO_SERVER_URL and any of the optional ones
set -euo pipefail

APP="${1:-}"
case "$APP" in
    admin|account|workspace) ;;
    *) echo "Usage: $0 <admin|account|workspace>" >&2; exit 1 ;;
esac
: "${OLUSO_SERVER_URL:?Set OLUSO_SERVER_URL to the public URL of the Oluso server, e.g. https://auth.example.com}"

ROOT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT_DIR/src/frontend"

npm ci --no-audit --no-fund
npx nx build "$APP-standalone" --configuration=production --outputStyle=static

OUT="dist/apps/$APP-standalone/browser"
[ -f "$OUT/index.html" ] || { echo "Build output not found in $OUT" >&2; exit 1; }

node -e '
const env = process.env;
const config = {
  serverUrl: env.OLUSO_SERVER_URL.replace(/\/+$/, ""),
  apiUrl: env.OLUSO_API_URL,
  adminUrl: env.OLUSO_ADMIN_URL,
  accountUrl: env.OLUSO_ACCOUNT_URL,
  workspaceUrl: env.OLUSO_WORKSPACE_URL,
};
for (const key of Object.keys(config)) if (!config[key]) delete config[key];
require("fs").writeFileSync(process.argv[1],
  "// Written by scripts/build-pages.sh\nwindow.__OLUSO_CONFIG__ = " + JSON.stringify(config, null, 2) + ";\n");
console.log("config.js:", JSON.stringify(config));
' "$OUT/config.js"

echo "Built $APP portal in src/frontend/$OUT"
