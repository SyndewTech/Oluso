#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# audit-dependencies.sh — Dependency security audit for npm and NuGet
#
# Checks:
#   1. Blocklist  — rejects known-compromised package versions (security/blocked-packages.json)
#   2. npm audit  — runs `npm audit` against the npm advisory database
#   3. NuGet vulnerable — runs `dotnet list package --vulnerable`
#   4. OSV.dev    — queries the OSV.dev API for known vulnerabilities + malware
#
# Usage:
#   ./scripts/audit-dependencies.sh [--npm-only | --nuget-only] [--severity <level>] [--fail-severity <level>] [--prod-only]
#
# Options:
#   --npm-only             Skip NuGet checks
#   --nuget-only           Skip npm checks
#   --severity LEVEL       Minimum severity to report (low|moderate|high|critical)
#                          Default: high
#   --fail-severity LEVEL  Minimum severity to fail the build (low|moderate|high|critical)
#                          Default: critical
#   --prod-only            Scope the npm FAIL gate to production dependencies
#                          (npm audit --omit=dev). Dev/build-time advisories are
#                          still reported, just not blocking. Default: off.
#
# Exit codes:
#   0  All checks passed
#   1  Blocked package found or vulnerability above threshold
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BLOCKLIST="$REPO_ROOT/security/blocked-packages.json"
SEVERITY="high"
FAIL_SEVERITY="critical"
CHECK_NPM=true
CHECK_NUGET=true
PROD_ONLY=false
FAILURES=0

# ── Parse args ────────────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case "$1" in
    --npm-only)        CHECK_NUGET=false; shift ;;
    --nuget-only)      CHECK_NPM=false; shift ;;
    --severity)        SEVERITY="$2"; shift 2 ;;
    --fail-severity)   FAIL_SEVERITY="$2"; shift 2 ;;
    # Scope the npm FAIL gate to production (non-dev) dependencies. The full
    # report still lists dev advisories for visibility; we just don't block a
    # release on vulnerabilities in build-time tooling that never ships.
    --prod-only)       PROD_ONLY=true; shift ;;
    *)                 echo "Unknown option: $1"; exit 1 ;;
  esac
done

# ── Helpers ───────────────────────────────────────────────────────────────────
red()    { printf '\033[0;31m%s\033[0m\n' "$*"; }
green()  { printf '\033[0;32m%s\033[0m\n' "$*"; }
yellow() { printf '\033[0;33m%s\033[0m\n' "$*"; }
bold()   { printf '\033[1m%s\033[0m\n' "$*"; }

fail() { red "FAIL: $*"; FAILURES=$((FAILURES + 1)); }
pass() { green "PASS: $*"; }
info() { echo "  $*"; }

# ── Check: blocklist file exists ──────────────────────────────────────────────
if [[ ! -f "$BLOCKLIST" ]]; then
  fail "Blocklist not found at $BLOCKLIST"
  exit 1
fi

# ══════════════════════════════════════════════════════════════════════════════
# npm checks
# ══════════════════════════════════════════════════════════════════════════════
check_npm_blocklist() {
  local lockfile="$1"
  local dir
  dir="$(dirname "$lockfile")"

  bold "── npm blocklist: $dir ──"

  local blocked_count
  blocked_count=$(node -e "
    const fs = require('fs');
    const blocklist = JSON.parse(fs.readFileSync('$BLOCKLIST', 'utf8'));
    const lock = JSON.parse(fs.readFileSync('$lockfile', 'utf8'));
    const packages = lock.packages || {};
    let found = 0;

    for (const entry of blocklist.npm) {
      const name = entry.name;
      const blockedVersions = entry.versions;

      for (const [pkgPath, pkgInfo] of Object.entries(packages)) {
        const pkgName = pkgPath.replace(/^node_modules\//, '').replace(/.*node_modules\//, '');
        if (pkgName !== name) continue;

        const version = pkgInfo.version;
        if (!version) continue;

        const isBlocked = blockedVersions.includes('*') || blockedVersions.includes(version);
        if (isBlocked) {
          console.error('BLOCKED: ' + name + '@' + version + ' — ' + entry.reason);
          found++;
        }
      }
    }
    process.exit(found > 0 ? 1 : 0);
  " 2>&1) || {
    echo "$blocked_count"
    fail "Blocked npm packages found in $dir"
    return
  }
  pass "No blocked npm packages in $dir"
}

check_npm_audit() {
  local dir="$1"

  bold "── npm audit: $dir (report >= $SEVERITY, fail >= $FAIL_SEVERITY) ──"

  if [[ ! -f "$dir/package-lock.json" ]]; then
    info "No package-lock.json, skipping"
    return
  fi

  # Report at the lower threshold so high vulns are visible. The report is
  # always full (incl. devDependencies) so nothing is hidden.
  local audit_output
  audit_output=$(cd "$dir" && npm audit --audit-level="$SEVERITY" 2>&1) || true
  # No pipe: under pipefail, head closing early would SIGPIPE (141) the job.
  sed -n '1,50p' <<< "$audit_output"

  # The fail gate can be scoped to production deps (--prod-only): a vulnerability
  # in build-time tooling (e.g. webpack-dev-server) never ships, so it shouldn't
  # block a release. It still appears in the full report above.
  local -a gate_args=(--audit-level="$FAIL_SEVERITY")
  local scope_note=""
  if [[ "$PROD_ONLY" == true ]]; then
    gate_args+=(--omit=dev)
    scope_note=" (production deps only)"
  fi

  # Only fail if vulnerabilities at or above FAIL_SEVERITY exist in scope
  if cd "$dir" && npm audit "${gate_args[@]}" > /dev/null 2>&1; then
    pass "npm audit — no $FAIL_SEVERITY+ vulnerabilities in $dir$scope_note"
  else
    fail "npm audit found $FAIL_SEVERITY+ vulnerabilities in $dir$scope_note"
  fi
}

if [[ "$CHECK_NPM" == true ]]; then
  bold ""
  bold "=============================="
  bold "  npm Dependency Audit"
  bold "=============================="

  # Find all package-lock.json files (skip node_modules)
  while IFS= read -r lockfile; do
    check_npm_blocklist "$lockfile"
  done < <(find "$REPO_ROOT" -name "package-lock.json" -not -path "*/node_modules/*" -not -path "*/.claude/*")

  while IFS= read -r lockfile; do
    dir="$(dirname "$lockfile")"
    check_npm_audit "$dir"
  done < <(find "$REPO_ROOT" -name "package-lock.json" -not -path "*/node_modules/*" -not -path "*/.claude/*")
fi

# ══════════════════════════════════════════════════════════════════════════════
# NuGet checks
# ══════════════════════════════════════════════════════════════════════════════
check_nuget_blocklist() {
  local dir="$1"

  bold "── NuGet blocklist: $dir ──"

  # Collect all PackageReference entries from csproj files
  local found_blocked=false

  while IFS= read -r csproj; do
    local blocked
    blocked=$(node -e "
      const fs = require('fs');
      const blocklist = JSON.parse(fs.readFileSync('$BLOCKLIST', 'utf8'));
      const content = fs.readFileSync('$csproj', 'utf8');

      // Simple XML regex — good enough for PackageReference Include=\"Name\" Version=\"X.Y.Z\"
      const re = /PackageReference\s+Include=\"([^\"]+)\"\s+Version=\"([^\"]+)\"/gi;
      let match;
      let found = 0;

      while ((match = re.exec(content)) !== null) {
        const name = match[1];
        const version = match[2];

        for (const entry of blocklist.nuget) {
          if (entry.name.toLowerCase() !== name.toLowerCase()) continue;
          const isBlocked = entry.versions.includes('*') || entry.versions.includes(version);
          if (isBlocked) {
            console.error('BLOCKED: ' + name + '@' + version + ' in $csproj — ' + entry.reason);
            found++;
          }
        }
      }
      process.exit(found > 0 ? 1 : 0);
    " 2>&1) || {
      echo "$blocked"
      found_blocked=true
    }
  done < <(find "$dir" -name "*.csproj" -not -path "*/bin/*" -not -path "*/obj/*" -not -path "*/node_modules/*" -not -path "*/.claude/*")

  if [[ "$found_blocked" == true ]]; then
    fail "Blocked NuGet packages found"
  else
    pass "No blocked NuGet packages"
  fi
}

check_nuget_vulnerable() {
  bold "── NuGet vulnerable packages ──"

  local sln="$REPO_ROOT/Oluso.sln"
  if [[ ! -f "$sln" ]]; then
    info "No Oluso.sln found, skipping dotnet list"
    return
  fi

  local vuln_output
  if ! vuln_output=$(dotnet list "$sln" package --vulnerable 2>&1); then
    yellow "WARNING: dotnet list package --vulnerable failed (may need restore first)"
    info "$vuln_output" | head -10
    return
  fi

  if ! echo "$vuln_output" | grep -q "has the following vulnerable packages"; then
    pass "No known vulnerable NuGet packages"
    return
  fi

  # Always show the report so users see what's flagged.
  echo "$vuln_output"

  # Honour --fail-severity. dotnet emits a Severity column with values
  # Low / Moderate / High / Critical; we fail only when one of the rows is
  # at or above the configured threshold (default: critical).
  local -a severities_to_fail=()
  case "$FAIL_SEVERITY" in
    low)      severities_to_fail=(Low Moderate High Critical) ;;
    moderate) severities_to_fail=(Moderate High Critical) ;;
    high)     severities_to_fail=(High Critical) ;;
    critical) severities_to_fail=(Critical) ;;
    *)        severities_to_fail=(Critical) ;;
  esac

  local severity_pattern
  severity_pattern=$(IFS='|'; echo "${severities_to_fail[*]}")

  # Only the data rows (starting with '   >') have the Severity column we
  # want — skip the 'has the following vulnerable packages' header lines.
  if echo "$vuln_output" | grep -E "^[[:space:]]+>" | grep -qE "[[:space:]](${severity_pattern})[[:space:]]"; then
    fail "NuGet vulnerable packages found at severity >= ${FAIL_SEVERITY}"
  else
    yellow "NuGet vulnerable packages reported below --fail-severity=${FAIL_SEVERITY} threshold; not failing the build."
    pass "No blocking NuGet vulnerabilities at severity ${FAIL_SEVERITY}+"
  fi
}

if [[ "$CHECK_NUGET" == true ]]; then
  bold ""
  bold "=============================="
  bold "  NuGet Dependency Audit"
  bold "=============================="

  check_nuget_blocklist "$REPO_ROOT"
  check_nuget_vulnerable
fi

# ══════════════════════════════════════════════════════════════════════════════
# OSV.dev live vulnerability scan
# ══════════════════════════════════════════════════════════════════════════════
bold ""
bold "=============================="
bold "  OSV.dev Live Scan"
bold "=============================="

OSV_ARGS=("--severity" "$SEVERITY" "--fail-severity" "$FAIL_SEVERITY" "--update-blocklist")
if [[ "$CHECK_NPM" == true && "$CHECK_NUGET" == false ]]; then
  OSV_ARGS+=("--npm-only")
elif [[ "$CHECK_NPM" == false && "$CHECK_NUGET" == true ]]; then
  OSV_ARGS+=("--nuget-only")
fi

if node "$REPO_ROOT/scripts/osv-scanner.mjs" "${OSV_ARGS[@]}"; then
  pass "OSV.dev scan clean"
else
  fail "OSV.dev found vulnerabilities"
fi

# ══════════════════════════════════════════════════════════════════════════════
# Summary
# ══════════════════════════════════════════════════════════════════════════════
echo ""
bold "=============================="
if [[ $FAILURES -gt 0 ]]; then
  red "  AUDIT FAILED — $FAILURES issue(s) found"
  bold "=============================="
  exit 1
else
  green "  AUDIT PASSED — all checks clean"
  bold "=============================="
  exit 0
fi
