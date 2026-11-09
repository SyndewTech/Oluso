#!/usr/bin/env node
// ──────────────────────────────────────────────────────────────────────────────
// osv-scanner.mjs — Query OSV.dev for known vulnerabilities in npm & NuGet deps
//
// Reads package-lock.json files (npm) and *.csproj files (NuGet), batches them
// against the OSV.dev API, and reports any known vulnerabilities or malware.
//
// Usage:
//   node scripts/osv-scanner.mjs [--npm-only | --nuget-only] [--severity <level>]
//                                [--fail-severity <level>] [--json] [--update-blocklist]
//
// Options:
//   --npm-only             Only scan npm dependencies
//   --nuget-only           Only scan NuGet dependencies
//   --severity LEVEL       Minimum severity to report (low|medium|high|critical)
//                          Default: high
//   --fail-severity LEVEL  Minimum severity to fail the build (low|medium|high|critical)
//                          Default: critical
//   --json                 Output results as JSON (for CI parsing)
//   --update-blocklist     Merge malware findings into security/blocked-packages.json
//
// Exit codes:
//   0  No vulnerabilities found at or above threshold
//   1  Vulnerabilities found
// ──────────────────────────────────────────────────────────────────────────────
import { readFileSync, writeFileSync, readdirSync } from 'fs';
import { join, dirname, resolve } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(__dirname, '..');
const BLOCKLIST_PATH = join(REPO_ROOT, 'security', 'blocked-packages.json');
const OSV_API = 'https://api.osv.dev/v1/querybatch';
const BATCH_SIZE = 1000; // OSV limit per batch

// ── Severity ordering ────────────────────────────────────────────────────────
const SEVERITY_ORDER = { low: 0, medium: 1, high: 2, critical: 3 };

// ── Parse args ───────────────────────────────────────────────────────────────
const args = process.argv.slice(2);
let scanNpm = true;
let scanNuget = true;
let minSeverity = 'high';
let failSeverity = 'critical';
let jsonOutput = false;
let updateBlocklist = false;

for (let i = 0; i < args.length; i++) {
  switch (args[i]) {
    case '--npm-only':    scanNuget = false; break;
    case '--nuget-only':  scanNpm = false; break;
    case '--severity':    minSeverity = args[++i]; break;
    case '--fail-severity': failSeverity = args[++i]; break;
    case '--json':        jsonOutput = true; break;
    case '--update-blocklist': updateBlocklist = true; break;
  }
}

// ── Helpers ──────────────────────────────────────────────────────────────────
function log(msg) { if (!jsonOutput) console.log(msg); }
function logError(msg) { console.error(msg); }

function findFiles(dir, pattern, ignoreDirs = []) {
  const results = [];
  try {
    const entries = readdirSync(dir, { withFileTypes: true });
    for (const entry of entries) {
      if (ignoreDirs.includes(entry.name)) continue;
      const fullPath = join(dir, entry.name);
      if (entry.isDirectory()) {
        results.push(...findFiles(fullPath, pattern, ignoreDirs));
      } else if (entry.name.match(pattern)) {
        results.push(fullPath);
      }
    }
  } catch { /* permission errors, etc. */ }
  return results;
}

// ── Extract npm dependencies from package-lock.json ──────────────────────────
function extractNpmDeps() {
  const lockfiles = findFiles(REPO_ROOT, /^package-lock\.json$/, ['node_modules']);
  const deps = new Map(); // name@version -> { name, version }

  for (const lockfile of lockfiles) {
    try {
      const lock = JSON.parse(readFileSync(lockfile, 'utf8'));
      const packages = lock.packages || {};

      for (const [pkgPath, pkgInfo] of Object.entries(packages)) {
        if (!pkgPath || !pkgInfo.version) continue;
        const name = pkgPath.replace(/^.*node_modules\//, '');
        if (!name) continue;
        const key = `${name}@${pkgInfo.version}`;
        if (!deps.has(key)) {
          deps.set(key, { name, version: pkgInfo.version, ecosystem: 'npm', source: lockfile });
        }
      }
    } catch (e) {
      logError(`Warning: Could not parse ${lockfile}: ${e.message}`);
    }
  }

  return [...deps.values()];
}

// ── Extract NuGet dependencies from *.csproj files ───────────────────────────
function extractNugetDeps() {
  const csprojFiles = findFiles(REPO_ROOT, /\.csproj$/, ['bin', 'obj', 'node_modules']);
  const deps = new Map();

  const re = /PackageReference\s+Include="([^"]+)"\s+Version="([^"]+)"/gi;

  for (const csproj of csprojFiles) {
    try {
      const content = readFileSync(csproj, 'utf8');
      let match;
      while ((match = re.exec(content)) !== null) {
        const name = match[1];
        const version = match[2];
        const key = `${name}@${version}`;
        if (!deps.has(key)) {
          deps.set(key, { name, version, ecosystem: 'NuGet', source: csproj });
        }
      }
      re.lastIndex = 0;
    } catch (e) {
      logError(`Warning: Could not parse ${csproj}: ${e.message}`);
    }
  }

  return [...deps.values()];
}

// ── Query OSV.dev batch API ──────────────────────────────────────────────────
async function queryOSV(deps) {
  const allVulns = [];

  for (let i = 0; i < deps.length; i += BATCH_SIZE) {
    const batch = deps.slice(i, i + BATCH_SIZE);
    const queries = batch.map(dep => ({
      version: dep.version,
      package: {
        name: dep.name,
        ecosystem: dep.ecosystem,
      },
    }));

    try {
      const response = await fetch(OSV_API, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ queries }),
      });

      if (!response.ok) {
        logError(`OSV API error: ${response.status} ${response.statusText}`);
        continue;
      }

      const data = await response.json();

      for (let j = 0; j < data.results.length; j++) {
        const result = data.results[j];
        if (result.vulns && result.vulns.length > 0) {
          const dep = batch[j];
          for (const vuln of result.vulns) {
            allVulns.push({
              id: vuln.id,
              summary: vuln.summary || 'No summary available',
              severity: extractSeverity(vuln),
              package: dep.name,
              version: dep.version,
              ecosystem: dep.ecosystem,
              source: dep.source,
              aliases: vuln.aliases || [],
              isMalware: vuln.id.startsWith('MAL-'),
              details: vuln.details || '',
              references: (vuln.references || []).map(r => r.url).slice(0, 3),
            });
          }
        }
      }
    } catch (e) {
      logError(`OSV API request failed: ${e.message}`);
    }

    // Rate limiting: small delay between batches
    if (i + BATCH_SIZE < deps.length) {
      await new Promise(r => setTimeout(r, 200));
    }
  }

  return allVulns;
}

function extractSeverity(vuln) {
  // Try CVSS from database_specific or severity array
  if (vuln.severity && vuln.severity.length > 0) {
    for (const s of vuln.severity) {
      if (s.type === 'CVSS_V3' || s.type === 'CVSS_V4') {
        const vec = s.score || '';
        if (vec.includes(':9.') || vec.includes(':10.')) return 'critical';
        if (vec.includes(':7.') || vec.includes(':8.')) return 'high';
        if (vec.includes(':4.') || vec.includes(':5.') || vec.includes(':6.')) return 'medium';
        return 'low';
      }
    }
  }

  // Malware is always critical
  if (vuln.id.startsWith('MAL-')) return 'critical';

  // Default to high for unknowns
  return 'high';
}

// ── Filter by severity ───────────────────────────────────────────────────────
function filterBySeverity(vulns, minLevel) {
  const threshold = SEVERITY_ORDER[minLevel] ?? 2;
  return vulns.filter(v => (SEVERITY_ORDER[v.severity] ?? 2) >= threshold);
}

// ── Update blocklist with malware findings ───────────────────────────────────
function mergeIntoBlocklist(vulns) {
  const malwareVulns = vulns.filter(v => v.isMalware);
  if (malwareVulns.length === 0) return;

  let blocklist;
  try {
    blocklist = JSON.parse(readFileSync(BLOCKLIST_PATH, 'utf8'));
  } catch {
    logError(`Could not read blocklist at ${BLOCKLIST_PATH}`);
    return;
  }

  const today = new Date().toISOString().split('T')[0];
  let updated = false;

  for (const vuln of malwareVulns) {
    const ecosystem = vuln.ecosystem === 'npm' ? 'npm' : 'nuget';
    const existing = blocklist[ecosystem].find(e => e.name === vuln.package);

    if (existing) {
      if (!existing.versions.includes(vuln.version) && !existing.versions.includes('*')) {
        existing.versions.push(vuln.version);
        updated = true;
      }
    } else {
      blocklist[ecosystem].push({
        name: vuln.package,
        versions: [vuln.version],
        reason: `[AUTO] ${vuln.id}: ${vuln.summary}`,
        advisory: vuln.references[0] || `https://osv.dev/vulnerability/${vuln.id}`,
        dateAdded: today,
      });
      updated = true;
    }
  }

  if (updated) {
    writeFileSync(BLOCKLIST_PATH, JSON.stringify(blocklist, null, 2) + '\n');
    log(`Updated blocklist with ${malwareVulns.length} malware finding(s)`);
  }
}

// ── Report ───────────────────────────────────────────────────────────────────
function printReport(vulns) {
  if (vulns.length === 0) {
    log('\n\x1b[32m  OSV SCAN PASSED — no known vulnerabilities found\x1b[0m\n');
    return;
  }

  // Group by severity
  const critical = vulns.filter(v => v.severity === 'critical');
  const high = vulns.filter(v => v.severity === 'high');
  const medium = vulns.filter(v => v.severity === 'medium');
  const low = vulns.filter(v => v.severity === 'low');

  log('\n\x1b[1m══════════════════════════════════════\x1b[0m');
  log('\x1b[1m  OSV.dev Vulnerability Report\x1b[0m');
  log('\x1b[1m══════════════════════════════════════\x1b[0m\n');

  if (critical.length) log(`\x1b[31m  CRITICAL: ${critical.length}\x1b[0m`);
  if (high.length)     log(`\x1b[33m  HIGH:     ${high.length}\x1b[0m`);
  if (medium.length)   log(`\x1b[36m  MEDIUM:   ${medium.length}\x1b[0m`);
  if (low.length)      log(`  LOW:      ${low.length}`);
  log('');

  for (const vuln of vulns) {
    const color = vuln.severity === 'critical' ? '\x1b[31m'
      : vuln.severity === 'high' ? '\x1b[33m'
      : '\x1b[36m';
    const malwareTag = vuln.isMalware ? ' \x1b[41m\x1b[37m MALWARE \x1b[0m' : '';

    log(`${color}[${vuln.severity.toUpperCase()}]\x1b[0m ${vuln.package}@${vuln.version} (${vuln.ecosystem})${malwareTag}`);
    log(`  ${vuln.id}: ${vuln.summary}`);
    if (vuln.references.length) {
      log(`  ${vuln.references[0]}`);
    }
    log('');
  }
}

// ── Main ─────────────────────────────────────────────────────────────────────
async function main() {
  log('\x1b[1m══════════════════════════════════════\x1b[0m');
  log('\x1b[1m  OSV.dev Dependency Scanner\x1b[0m');
  log('\x1b[1m══════════════════════════════════════\x1b[0m\n');

  // Collect dependencies
  let allDeps = [];

  if (scanNpm) {
    const npmDeps = extractNpmDeps();
    log(`  Found ${npmDeps.length} npm packages`);
    allDeps.push(...npmDeps);
  }

  if (scanNuget) {
    const nugetDeps = extractNugetDeps();
    log(`  Found ${nugetDeps.length} NuGet packages`);
    allDeps.push(...nugetDeps);
  }

  if (allDeps.length === 0) {
    log('  No dependencies found to scan.');
    process.exit(0);
  }

  log(`\n  Querying OSV.dev for ${allDeps.length} packages...`);

  // Query OSV
  const vulns = await queryOSV(allDeps);

  // Report at the lower threshold (--severity) so high vulns are visible
  const reported = filterBySeverity(vulns, minSeverity);

  // Only fail on the higher threshold (--fail-severity)
  const failing = filterBySeverity(vulns, failSeverity);

  // Auto-update blocklist with malware
  if (updateBlocklist) {
    mergeIntoBlocklist(vulns);
  }

  // Output
  if (jsonOutput) {
    console.log(JSON.stringify({
      total: reported.length,
      failing: failing.length,
      failSeverity,
      vulnerabilities: reported,
    }, null, 2));
  } else {
    printReport(reported);
    if (reported.length > 0 && failing.length === 0) {
      log(`\x1b[33m  ${reported.length} issue(s) reported but none at ${failSeverity} level — not failing build\x1b[0m\n`);
    }
  }

  process.exit(failing.length > 0 ? 1 : 0);
}

main().catch(e => {
  logError(`Fatal: ${e.message}`);
  process.exit(1);
});
