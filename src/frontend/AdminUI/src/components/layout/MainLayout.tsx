import { useState, useMemo } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { useAuthStore } from '../../store/slices/authSlice';
import { useTenantFeatures } from '../../contexts/TenantFeaturesContext';
import { usePlugins, type AdminNavItem } from '../../contexts/PluginContext';
import { FEATURE_KEYS } from '../../types/features';
import {
  HomeIcon,
  UsersIcon,
  KeyIcon,
  ShieldCheckIcon,
  DocumentTextIcon,
  ClockIcon,
  Cog6ToothIcon,
  ArrowRightOnRectangleIcon,
  Bars3Icon,
  XMarkIcon,
  ServerStackIcon,
  Square3Stack3DIcon,
  UserGroupIcon,
  ArrowPathIcon,
  LockClosedIcon,
  GlobeAltIcon,
  PuzzlePieceIcon,
  BuildingOfficeIcon,
  BellAlertIcon,
  ChevronDownIcon,
  ChevronRightIcon,
  InboxStackIcon,
} from '@heroicons/react/24/outline';

// Core navigation items with optional feature requirements
// Additional items come from registered plugins (e.g., @identityserver/billing-ui)
const coreNavigation: AdminNavItem[] = [
  { id: 'dashboard', name: 'Dashboard', href: '/', icon: HomeIcon, order: 0 },
  { id: 'tenants', name: 'Tenants', href: '/tenants', icon: BuildingOfficeIcon, order: 10, superAdminOnly: true },
  { id: 'webhooks', name: 'Webhooks', href: '/webhooks', icon: BellAlertIcon, feature: FEATURE_KEYS.WEBHOOKS, order: 15 },
  { id: 'clients', name: 'Clients', href: '/clients', icon: KeyIcon, order: 20 },
  { id: 'api-resources', name: 'API Resources', href: '/api-resources', icon: ServerStackIcon, order: 21 },
  { id: 'api-scopes', name: 'API Scopes', href: '/api-scopes', icon: Square3Stack3DIcon, order: 22 },
  { id: 'identity-resources', name: 'Identity Resources', href: '/identity-resources', icon: ShieldCheckIcon, order: 23 },
  { id: 'identity-providers', name: 'Identity Providers', href: '/identity-providers', icon: GlobeAltIcon, order: 30 },
  { id: 'journeys', name: 'User Journeys', href: '/journeys', icon: ArrowPathIcon, order: 40 },
  { id: 'submissions', name: 'Data Collection', href: '/submissions', icon: InboxStackIcon, order: 42 },
  { id: 'plugins', name: 'Plugins', href: '/plugins', icon: PuzzlePieceIcon, order: 45 },
  { id: 'signing-keys', name: 'Signing Keys', href: '/signing-keys', icon: LockClosedIcon, order: 46 },
  { id: 'users', name: 'Users', href: '/users', icon: UsersIcon, order: 60 },
  { id: 'roles', name: 'Roles', href: '/roles', icon: UserGroupIcon, order: 61 },
  { id: 'grants', name: 'Grants', href: '/grants', icon: ClockIcon, order: 62 },
  { id: 'audit-logs', name: 'Audit Logs', href: '/audit-logs', icon: DocumentTextIcon, feature: FEATURE_KEYS.AUDIT_LOGS, order: 70 },
  { id: 'settings', name: 'Settings', href: '/settings', icon: Cog6ToothIcon, order: 100 },
];

interface MainLayoutProps {
  children: React.ReactNode;
}

interface NavItemProps {
  item: AdminNavItem;
  location: ReturnType<typeof useLocation>;
  depth?: number;
  expandedItems: Set<string>;
  onToggle: (id: string) => void;
  isSuperAdmin: boolean;
  hasFeature: (key: string) => boolean;
  billingEnabled: boolean;
}

function NavItem({ item, location, depth = 0, expandedItems, onToggle, isSuperAdmin, hasFeature, billingEnabled }: NavItemProps) {
  const hasChildren = item.children && item.children.length > 0;
  const isExpanded = expandedItems.has(item.id);

  // Check if any child is active (to auto-highlight parent)
  const isChildActive = hasChildren && item.children!.some(
    child => location.pathname === child.href || location.pathname.startsWith(child.href + '/')
  );

  const isActive = location.pathname === item.href || location.pathname.startsWith(item.href + '/');
  const Icon = item.icon;

  // Filter children based on features
  const filteredChildren = hasChildren
    ? item.children!.filter(child => {
        if (child.superAdminOnly && !isSuperAdmin) return false;
        if (!child.feature) return true;
        if (!billingEnabled) return true;
        return hasFeature(child.feature);
      }).sort((a, b) => (a.order ?? 100) - (b.order ?? 100))
    : [];

  if (hasChildren) {
    return (
      <div>
        <button
          onClick={() => onToggle(item.id)}
          className={`w-full group flex items-center justify-between rounded-md px-2 py-2 text-sm font-medium ${
            isChildActive
              ? 'bg-primary-50 text-primary-600'
              : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
          }`}
          style={{ paddingLeft: `${8 + depth * 12}px` }}
        >
          <span className="flex items-center">
            <Icon className={`mr-2.5 h-4 w-4 flex-shrink-0 ${isChildActive ? 'text-primary-600' : 'text-gray-400'}`} />
            {item.name}
            {item.badge && (
              <span className="ml-2 rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600">
                {item.badge}
              </span>
            )}
          </span>
          {isExpanded ? (
            <ChevronDownIcon className="h-4 w-4 text-gray-400" />
          ) : (
            <ChevronRightIcon className="h-4 w-4 text-gray-400" />
          )}
        </button>
        {isExpanded && filteredChildren.length > 0 && (
          <div className="mt-1 space-y-1">
            {filteredChildren.map((child) => (
              <NavItem
                key={child.id}
                item={child}
                location={location}
                depth={depth + 1}
                expandedItems={expandedItems}
                onToggle={onToggle}
                isSuperAdmin={isSuperAdmin}
                hasFeature={hasFeature}
                billingEnabled={billingEnabled}
              />
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <Link
      to={item.href}
      className={`group flex items-center rounded-md px-2 py-2 text-sm font-medium ${
        isActive
          ? 'bg-primary-50 text-primary-600'
          : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
      }`}
      style={{ paddingLeft: `${8 + depth * 12}px` }}
    >
      <Icon className={`mr-2.5 h-4 w-4 flex-shrink-0 ${isActive ? 'text-primary-600' : 'text-gray-400'}`} />
      {item.name}
      {item.badge && (
        <span className="ml-auto rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-600">
          {item.badge}
        </span>
      )}
    </Link>
  );
}

export default function MainLayout({ children }: MainLayoutProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [expandedItems, setExpandedItems] = useState<Set<string>>(new Set());
  const location = useLocation();
  const { user, logout } = useAuthStore();
  const { hasFeature, billingEnabled, plan } = useTenantFeatures();
  const { getNavigation } = usePlugins();

  const isSuperAdmin = user?.roles?.includes('SuperAdmin') || false;

  // Toggle expand/collapse for nav items with children
  const handleToggle = (id: string) => {
    setExpandedItems(prev => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  // Merge core navigation with plugin navigation
  const filteredNavigation = useMemo(() => {
    // Get plugin navigation items
    const pluginNav = getNavigation({ isSuperAdmin, hasFeature });

    // Combine core + plugin navigation
    const allNavigation = [...coreNavigation, ...pluginNav];

    // Filter based on feature availability and sort by order
    return allNavigation
      .filter((item) => {
        // Filter by super admin
        if (item.superAdminOnly && !isSuperAdmin) return false;
        // If no feature requirement, always show
        if (!item.feature) return true;
        // If billing not enabled, show all items
        if (!billingEnabled) return true;
        // Check if feature is available
        return hasFeature(item.feature);
      })
      .sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  }, [getNavigation, isSuperAdmin, hasFeature, billingEnabled]);

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Mobile sidebar */}
      <div className={`fixed inset-0 z-40 lg:hidden ${sidebarOpen ? '' : 'hidden'}`}>
        <div className="fixed inset-0 bg-gray-600 bg-opacity-75" onClick={() => setSidebarOpen(false)} />
        <div className="fixed inset-y-0 left-0 flex w-64 flex-col bg-white">
          <div className="flex h-16 items-center justify-between px-4">
            <span className="text-xl font-bold text-primary-600">Oluso</span>
            <button onClick={() => setSidebarOpen(false)}>
              <XMarkIcon className="h-5 w-5 text-gray-500" />
            </button>
          </div>
          <nav className="flex-1 space-y-1 px-2 py-4 overflow-y-auto">
            {filteredNavigation.map((item) => (
              <NavItem
                key={item.id}
                item={item}
                location={location}
                expandedItems={expandedItems}
                onToggle={handleToggle}
                isSuperAdmin={isSuperAdmin}
                hasFeature={hasFeature}
                billingEnabled={billingEnabled}
              />
            ))}
          </nav>
        </div>
      </div>

      {/* Desktop sidebar */}
      <div className="hidden lg:fixed lg:inset-y-0 lg:flex lg:w-64 lg:flex-col">
        <div className="flex min-h-0 flex-1 flex-col border-r border-gray-200 bg-white">
          <div className="flex h-16 items-center px-4">
            <span className="text-xl font-bold text-primary-600">Oluso</span>
            {billingEnabled && plan && (
              <span className="ml-2 rounded-full bg-primary-100 px-2 py-0.5 text-xs font-medium text-primary-700">
                {plan.displayName || plan.name}
              </span>
            )}
          </div>
          <nav className="flex-1 space-y-1 px-2 py-4 overflow-y-auto">
            {filteredNavigation.map((item) => (
              <NavItem
                key={item.id}
                item={item}
                location={location}
                expandedItems={expandedItems}
                onToggle={handleToggle}
                isSuperAdmin={isSuperAdmin}
                hasFeature={hasFeature}
                billingEnabled={billingEnabled}
              />
            ))}
          </nav>
          <div className="flex-shrink-0 border-t border-gray-200 p-4">
            <div className="flex items-center">
              <div className="flex-shrink-0">
                <div className="h-8 w-8 rounded-full bg-primary-600 flex items-center justify-center">
                  <span className="text-sm font-medium text-white">
                    {user?.displayName?.[0] || user?.userName?.[0] || 'U'}
                  </span>
                </div>
              </div>
              <div className="ml-3 flex-1">
                <p className="text-sm font-medium text-gray-700">{user?.displayName || user?.userName}</p>
                <p className="text-xs text-gray-500">{user?.email}</p>
              </div>
              <button
                onClick={logout}
                className="ml-2 rounded-md p-1 text-gray-400 hover:bg-gray-100 hover:text-gray-500"
              >
                <ArrowRightOnRectangleIcon className="h-4 w-4" />
              </button>
            </div>
          </div>
        </div>
      </div>

      {/* Main content */}
      <div className="lg:pl-64 flex flex-col min-h-screen">
        <div className="sticky top-0 z-10 flex h-16 items-center gap-x-4 border-b border-gray-200 bg-white px-4 shadow-sm lg:hidden">
          <button onClick={() => setSidebarOpen(true)} className="-m-2.5 p-2.5 text-gray-700">
            <Bars3Icon className="h-5 w-5" />
          </button>
          <span className="text-lg font-semibold text-primary-600">Identity Admin</span>
        </div>
        <main className="flex-1 flex flex-col py-6">
          <div className="flex-1 flex flex-col w-full px-4 sm:px-6 lg:px-8">{children}</div>
        </main>
      </div>
    </div>
  );
}
