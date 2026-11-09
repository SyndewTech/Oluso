import { AdminSearchBadgeTone, AdminSearchProvider, PLATFORM_MODULE } from '../../services/admin-search.service';
import { workspaceUrl } from '../../config';

/** Module accent keys from the module registry mapped to badge classes. */
export const MODULE_TONES: Record<string, string> = {
  indigo: 'bg-indigo-100 text-indigo-700',
  blue: 'bg-blue-100 text-blue-700',
  sky: 'bg-sky-100 text-sky-700',
  cyan: 'bg-cyan-100 text-cyan-700',
  teal: 'bg-teal-100 text-teal-700',
  emerald: 'bg-emerald-100 text-emerald-700',
  green: 'bg-green-100 text-green-700',
  amber: 'bg-amber-100 text-amber-700',
  orange: 'bg-orange-100 text-orange-700',
  rose: 'bg-rose-100 text-rose-700',
  pink: 'bg-pink-100 text-pink-700',
  purple: 'bg-purple-100 text-purple-700',
  violet: 'bg-violet-100 text-violet-700',
  red: 'bg-red-100 text-red-700',
};

export const BADGE_TONES: Record<AdminSearchBadgeTone, string> = {
  neutral: 'bg-gray-100 text-gray-600',
  info: 'bg-blue-50 text-blue-700',
  success: 'bg-green-50 text-green-700',
  warning: 'bg-amber-50 text-amber-700',
  danger: 'bg-red-50 text-red-700',
};

/** Labels for search modules that are not module descriptors in the registry. */
export const MODULE_LABEL_FALLBACKS: Record<string, string> = {
  [PLATFORM_MODULE]: 'Platform',
  workflows: 'Automations',
};

export function moduleTone(color: string | undefined): string {
  return (color && MODULE_TONES[color]) || 'bg-gray-100 text-gray-600';
}

export function badgeTone(tone: AdminSearchBadgeTone): string {
  return BADGE_TONES[tone] ?? BADGE_TONES.neutral;
}

/** Absolute workspace link for records that have no admin page; null when no workspace host is configured. */
export function workspaceHref(route: string | undefined): string | null {
  if (!route || !workspaceUrl) return null;
  return workspaceUrl.replace(/\/+$/, '') + route;
}

/** Query parameters for the results page. */
export function searchPageParams(query: string, providerId: string | null, filters: Record<string, string>): Record<string, string> {
  const params: Record<string, string> = { q: query };
  if (providerId) params['provider'] = providerId;
  for (const [key, value] of Object.entries(filters)) {
    if (value) params[`f.${key}`] = value;
  }
  return params;
}

export interface ProviderModuleGroup {
  module: string;
  label: string;
  providers: AdminSearchProvider[];
}

/** Providers grouped by module, platform first then alphabetical, so like-named kinds stay distinguishable. */
export function groupProvidersByModule(
  providers: AdminSearchProvider[],
  labelFor: (module: string) => string,
): ProviderModuleGroup[] {
  const groups = new Map<string, ProviderModuleGroup>();
  for (const provider of providers) {
    let group = groups.get(provider.module);
    if (!group) {
      group = { module: provider.module, label: labelFor(provider.module), providers: [] };
      groups.set(provider.module, group);
    }
    group.providers.push(provider);
  }
  return [...groups.values()].sort((a, b) => {
    if (a.module === PLATFORM_MODULE) return -1;
    if (b.module === PLATFORM_MODULE) return 1;
    return a.label.localeCompare(b.label);
  });
}
