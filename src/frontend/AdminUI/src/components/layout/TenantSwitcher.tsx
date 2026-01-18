import { Fragment, useState, useMemo } from 'react';
import { Menu, Transition } from '@headlessui/react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ChevronDownIcon,
  BuildingOfficeIcon,
  CheckIcon,
  MagnifyingGlassIcon,
  ArrowPathIcon,
} from '@heroicons/react/24/outline';
import { clsx } from 'clsx';
import { useAuthStore } from '../../store/slices/authSlice';
import { contextService } from '../../services/contextService';
import type { TenantSwitch } from '../../types/context';
import { Badge } from '../common/Badge';

const environmentColors: Record<string, 'default' | 'success' | 'warning' | 'error' | 'info'> = {
  Production: 'success',
  Staging: 'warning',
  Development: 'info',
  Testing: 'default',
};

export function TenantSwitcher() {
  const { user, currentTenantId, setCurrentTenantId } = useAuthStore();
  const queryClient = useQueryClient();
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedOrgId, setSelectedOrgId] = useState<string | null>(null);

  const isSuperAdmin = user?.roles?.includes('SuperAdmin') || false;

  // Fetch available organizations
  const { data: organizations = [], isLoading: orgsLoading } = useQuery({
    queryKey: ['context', 'organizations'],
    queryFn: () => contextService.getAvailableOrganizations(),
    enabled: true,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });

  // Fetch available tenants (optionally filtered by org)
  const { data: tenants = [], isLoading: tenantsLoading } = useQuery({
    queryKey: ['context', 'tenants', selectedOrgId],
    queryFn: () => contextService.getAvailableTenants(selectedOrgId || undefined),
    enabled: true,
    staleTime: 5 * 60 * 1000, // 5 minutes
  });

  // Find current tenant
  const currentTenant = useMemo(() => {
    return tenants.find((t) => t.id === currentTenantId) || null;
  }, [tenants, currentTenantId]);

  // Filter tenants by search query
  const filteredTenants = useMemo(() => {
    if (!searchQuery) return tenants;
    const query = searchQuery.toLowerCase();
    return tenants.filter(
      (t) =>
        t.name.toLowerCase().includes(query) ||
        t.identifier.toLowerCase().includes(query) ||
        (t.displayName && t.displayName.toLowerCase().includes(query))
    );
  }, [tenants, searchQuery]);

  // Group tenants by organization
  const groupedTenants = useMemo(() => {
    if (selectedOrgId || !isSuperAdmin) {
      // No grouping if filtering by org or not super admin
      return { ungrouped: filteredTenants };
    }

    const groups: Record<string, TenantSwitch[]> = {};
    const orgMap = new Map(organizations.map((o) => [o.id, o]));

    filteredTenants.forEach((tenant) => {
      const orgId = tenant.organizationId || 'none';
      const org = orgMap.get(orgId);
      const groupKey = org?.name || (tenant.organizationId ? 'Unknown Organization' : 'No Organization');

      if (!groups[groupKey]) {
        groups[groupKey] = [];
      }
      groups[groupKey].push(tenant);
    });

    return groups;
  }, [filteredTenants, organizations, selectedOrgId, isSuperAdmin]);

  const handleTenantSwitch = (tenant: TenantSwitch) => {
    setCurrentTenantId(tenant.id);
    // Invalidate queries that depend on tenant context
    queryClient.invalidateQueries();
    setSearchQuery('');
  };

  const handleClearTenant = () => {
    setCurrentTenantId(null);
    queryClient.invalidateQueries();
  };

  const isLoading = orgsLoading || tenantsLoading;

  // Don't show if user has no tenants and is not super admin
  if (!isSuperAdmin && tenants.length === 0 && !isLoading) {
    return null;
  }

  return (
    <Menu as="div" className="relative">
      <Menu.Button
        className={clsx(
          'flex items-center gap-2 rounded-lg px-3 py-2 text-sm font-medium transition-colors',
          'hover:bg-gray-100 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2',
          currentTenant ? 'text-gray-900' : 'text-gray-500'
        )}
      >
        <BuildingOfficeIcon className="h-5 w-5 text-gray-400" />
        <span className="max-w-[180px] truncate">
          {currentTenant ? (currentTenant.displayName || currentTenant.name) : 'Select Tenant'}
        </span>
        {currentTenant && (
          <Badge variant={environmentColors[currentTenant.environment] || 'default'} size="sm">
            {currentTenant.environment}
          </Badge>
        )}
        <ChevronDownIcon className="h-4 w-4 text-gray-400" />
      </Menu.Button>

      <Transition
        as={Fragment}
        enter="transition ease-out duration-100"
        enterFrom="transform opacity-0 scale-95"
        enterTo="transform opacity-100 scale-100"
        leave="transition ease-in duration-75"
        leaveFrom="transform opacity-100 scale-100"
        leaveTo="transform opacity-0 scale-95"
      >
        <Menu.Items className="absolute right-0 z-50 mt-2 w-80 origin-top-right rounded-lg bg-white shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none">
          <div className="p-3">
            {/* Search input */}
            <div className="relative mb-3">
              <MagnifyingGlassIcon className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder="Search tenants..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full rounded-md border border-gray-300 py-2 pl-9 pr-3 text-sm placeholder-gray-400 focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500"
              />
            </div>

            {/* Organization filter (for super admins with multiple orgs) */}
            {isSuperAdmin && organizations.length > 1 && (
              <div className="mb-3">
                <select
                  value={selectedOrgId || ''}
                  onChange={(e) => setSelectedOrgId(e.target.value || null)}
                  className="w-full rounded-md border border-gray-300 py-2 px-3 text-sm focus:border-primary-500 focus:outline-none focus:ring-1 focus:ring-primary-500"
                >
                  <option value="">All Organizations</option>
                  {organizations.map((org) => (
                    <option key={org.id} value={org.id}>
                      {org.name} ({org.tenantCount} tenants)
                    </option>
                  ))}
                </select>
              </div>
            )}

            {/* Clear tenant option for super admins */}
            {isSuperAdmin && currentTenantId && (
              <Menu.Item>
                {({ active }) => (
                  <button
                    onClick={handleClearTenant}
                    className={clsx(
                      'flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm',
                      active ? 'bg-gray-100' : ''
                    )}
                  >
                    <ArrowPathIcon className="h-4 w-4 text-gray-400" />
                    <span className="text-gray-600">Clear tenant context</span>
                  </button>
                )}
              </Menu.Item>
            )}

            {/* Divider */}
            {isSuperAdmin && currentTenantId && <div className="my-2 border-t border-gray-100" />}

            {/* Loading state */}
            {isLoading && (
              <div className="py-4 text-center text-sm text-gray-500">Loading tenants...</div>
            )}

            {/* Empty state */}
            {!isLoading && filteredTenants.length === 0 && (
              <div className="py-4 text-center text-sm text-gray-500">
                {searchQuery ? 'No tenants match your search' : 'No tenants available'}
              </div>
            )}

            {/* Tenant list */}
            {!isLoading && filteredTenants.length > 0 && (
              <div className="max-h-64 overflow-y-auto">
                {Object.entries(groupedTenants).map(([groupName, groupTenants]) => (
                  <div key={groupName}>
                    {groupName !== 'ungrouped' && (
                      <div className="px-3 py-1.5 text-xs font-medium uppercase tracking-wider text-gray-500">
                        {groupName}
                      </div>
                    )}
                    {groupTenants.map((tenant) => (
                      <Menu.Item key={tenant.id}>
                        {({ active }) => (
                          <button
                            onClick={() => handleTenantSwitch(tenant)}
                            disabled={!tenant.enabled}
                            className={clsx(
                              'flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm',
                              active ? 'bg-gray-100' : '',
                              !tenant.enabled && 'cursor-not-allowed opacity-50',
                              tenant.id === currentTenantId && 'bg-primary-50'
                            )}
                          >
                            <div className="flex-1 text-left">
                              <div className="flex items-center gap-2">
                                <span className="font-medium text-gray-900">
                                  {tenant.displayName || tenant.name}
                                </span>
                                {tenant.id === currentTenantId && (
                                  <CheckIcon className="h-4 w-4 text-primary-600" />
                                )}
                              </div>
                              <div className="flex items-center gap-2 text-xs text-gray-500">
                                <span>{tenant.identifier}</span>
                                <Badge
                                  variant={environmentColors[tenant.environment] || 'default'}
                                  size="sm"
                                >
                                  {tenant.environment}
                                </Badge>
                                {tenant.userRole && tenant.userRole !== 'superadmin' && (
                                  <Badge variant="info" size="sm">
                                    {tenant.userRole}
                                  </Badge>
                                )}
                              </div>
                            </div>
                          </button>
                        )}
                      </Menu.Item>
                    ))}
                  </div>
                ))}
              </div>
            )}
          </div>
        </Menu.Items>
      </Transition>
    </Menu>
  );
}

export default TenantSwitcher;
