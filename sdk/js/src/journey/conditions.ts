import type { JourneyFormCondition } from '../types';

const ALIASES: Record<string, string> = {
  eq: 'eq', equals: 'eq', '==': 'eq', '=': 'eq',
  ne: 'ne', neq: 'ne', notequals: 'ne', '!=': 'ne',
  gt: 'gt', '>': 'gt', greaterthan: 'gt',
  gte: 'gte', '>=': 'gte', greaterthanorequals: 'gte',
  lt: 'lt', '<': 'lt', lessthan: 'lt',
  lte: 'lte', '<=': 'lte', lessthanorequals: 'lte',
  contains: 'contains', notcontains: 'not_contains', '!contains': 'not_contains',
  startswith: 'starts_with', endswith: 'ends_with',
  exists: 'exists', notnull: 'exists', isset: 'exists',
  notexists: 'not_exists', null: 'not_exists', isnotset: 'not_exists',
  empty: 'empty', notempty: 'not_empty',
  in: 'in', notin: 'not_in', '!in': 'not_in',
  regex: 'regex', matches: 'regex',
  true: 'true', false: 'false',
};

/** The canonical operator, as the server names it; undefined for one no condition supports. */
export function normalizeOperator(op: string | undefined): string | undefined {
  if (!op) return undefined;
  return ALIASES[op.trim().toLowerCase().replace(/_/g, '')];
}

const same = (a: string, b: string) => a.localeCompare(b, undefined, { sensitivity: 'accent' }) === 0;

function order(actual: string, expected: string | undefined): number | undefined {
  if (expected === undefined) return undefined;
  const a = Number(actual);
  const e = Number(expected);
  if (actual.trim() !== '' && expected.trim() !== '' && !Number.isNaN(a) && !Number.isNaN(e)) return a === e ? 0 : a < e ? -1 : 1;
  const ad = Date.parse(actual);
  const ed = Date.parse(expected);
  if (!Number.isNaN(ad) && !Number.isNaN(ed)) return ad === ed ? 0 : ad < ed ? -1 : 1;
  return actual.localeCompare(expected, undefined, { sensitivity: 'accent' });
}

function compare(actual: string, op: string, expected: string | undefined): boolean {
  const lower = actual.toLowerCase();
  const want = (expected ?? '').toLowerCase();
  switch (op) {
    case 'eq': return same(actual, expected ?? '');
    case 'ne': return !same(actual, expected ?? '');
    case 'gt': { const o = order(actual, expected); return o !== undefined && o > 0; }
    case 'gte': { const o = order(actual, expected); return o !== undefined && o >= 0; }
    case 'lt': { const o = order(actual, expected); return o !== undefined && o < 0; }
    case 'lte': { const o = order(actual, expected); return o !== undefined && o <= 0; }
    case 'contains': return lower.includes(want);
    case 'not_contains': return !lower.includes(want);
    case 'starts_with': return lower.startsWith(want);
    case 'ends_with': return lower.endsWith(want);
    case 'regex':
      if (!expected) return false;
      try { return new RegExp(expected, 'i').test(actual); } catch { return false; }
    default: return false;
  }
}

function expectedList(expected: string | undefined): string[] {
  if (!expected || !expected.trim()) return [];
  const trimmed = expected.trim();
  if (trimmed.startsWith('[')) {
    try {
      const parsed = JSON.parse(trimmed);
      if (Array.isArray(parsed)) return parsed.map(String);
    } catch {
      return [];
    }
  }
  return trimmed.split(',').map((v) => v.trim()).filter(Boolean);
}

const truthy = (v: string) => v.toLowerCase() !== 'false' && v !== '0';

/**
 * Whether a field's current values meet a showWhen condition, with the server's semantics: positive operators pass
 * when any value matches, negative ones when none does.
 */
export function conditionMet(condition: JourneyFormCondition, values: string[]): boolean {
  const op = normalizeOperator(condition.operator);
  const present = values.filter((v) => v !== '');
  const members = () => present.flatMap((v) => v.split(',').map((p) => p.trim()).filter(Boolean));
  switch (op) {
    case 'exists':
    case 'not_empty': return present.length > 0;
    case 'not_exists':
    case 'empty': return present.length === 0;
    case 'ne': return !present.some((v) => compare(v, 'eq', condition.value));
    case 'not_contains': return !present.some((v) => compare(v, 'contains', condition.value));
    case 'in': { const list = expectedList(condition.value); return members().some((m) => list.some((e) => same(m, e))); }
    case 'not_in': { const list = expectedList(condition.value); return !members().some((m) => list.some((e) => same(m, e))); }
    case 'true': return present.some(truthy);
    case 'false': return !present.some(truthy);
    case undefined: return false;
    default: return present.some((v) => compare(v, op, condition.value));
  }
}
