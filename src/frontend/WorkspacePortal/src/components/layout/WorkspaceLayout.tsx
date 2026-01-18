import { Fragment, useState, type ReactNode } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Dialog, Transition, Menu } from '@headlessui/react';
import {
  Bars3Icon,
  XMarkIcon,
  HomeIcon,
  UserIcon,
  BellIcon,
  Cog6ToothIcon,
  ArrowRightOnRectangleIcon,
  ChevronDownIcon,
} from '@heroicons/react/24/outline';
import type { WorkspaceNavItem, WorkspaceUIPlugin } from '@oluso/ui-core';
import { useAuth } from '../../contexts/AuthContext';
import clsx from 'clsx';

interface WorkspaceLayoutProps {
  children: ReactNode;
  plugins?: WorkspaceUIPlugin[];
}

// Default navigation
const defaultNavigation: WorkspaceNavItem[] = [
  { id: 'dashboard', label: 'Dashboard', path: '/', icon: HomeIcon, group: 'dashboard', order: 0 },
  { id: 'profile', label: 'My Profile', path: '/profile', icon: UserIcon, group: 'self-service', order: 0 },
];

// Navigation group labels
const groupLabels: Record<string, string> = {
  dashboard: '',
  'self-service': 'Self Service',
  team: 'My Team',
  approvals: 'Approvals',
  tools: 'Tools',
  settings: 'Settings',
};

// Group order
const groupOrder = ['dashboard', 'self-service', 'team', 'approvals', 'tools', 'settings'];

function filterNavigation(
  items: WorkspaceNavItem[],
  isManager: boolean,
  hasPermission: (p: string) => boolean,
  hasAnyPermission: (p: string[]) => boolean,
  hasFeature: (f: string) => boolean
): WorkspaceNavItem[] {
  return items.filter((item) => {
    // Manager-only check
    if (item.managerOnly && !isManager) return false;

    // Permission check
    if (item.permission && !hasPermission(item.permission)) return false;
    if (item.anyPermission && !hasAnyPermission(item.anyPermission)) return false;

    // Feature flag check
    if (item.feature && !hasFeature(item.feature)) return false;

    return true;
  });
}

function groupNavigation(items: WorkspaceNavItem[]): Record<string, WorkspaceNavItem[]> {
  const grouped: Record<string, WorkspaceNavItem[]> = {};

  for (const item of items) {
    const group = item.group || 'dashboard';
    if (!grouped[group]) {
      grouped[group] = [];
    }
    grouped[group].push(item);
  }

  // Sort items within each group
  for (const group of Object.keys(grouped)) {
    grouped[group].sort((a, b) => (a.order || 0) - (b.order || 0));
  }

  return grouped;
}

export function WorkspaceLayout({ children, plugins = [] }: WorkspaceLayoutProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const location = useLocation();
  const { user, logout, isManager, hasPermission, hasAnyPermission } = useAuth();

  // Stub feature check - in real app would come from context
  const hasFeature = (_: string) => true;

  // Merge plugin navigation
  const allNavigation: WorkspaceNavItem[] = [
    ...defaultNavigation,
    ...plugins.flatMap((p) => p.navigation || []),
  ];

  // Filter and group navigation
  const filteredNav = filterNavigation(allNavigation, isManager, hasPermission, hasAnyPermission, hasFeature);
  const groupedNav = groupNavigation(filteredNav);

  const isActive = (path: string) => {
    if (path === '/') return location.pathname === '/';
    return location.pathname.startsWith(path);
  };

  const NavItems = () => (
    <>
      {groupOrder.map((groupKey) => {
        const items = groupedNav[groupKey];
        if (!items || items.length === 0) return null;

        return (
          <div key={groupKey} className="space-y-1">
            {groupLabels[groupKey] && (
              <h3 className="px-3 text-xs font-semibold text-gray-500 uppercase tracking-wider mt-6 mb-2">
                {groupLabels[groupKey]}
              </h3>
            )}
            {items.map((item) => {
              const Icon = item.icon;
              const active = isActive(item.path);
              const badge = typeof item.badge === 'function' ? item.badge() : item.badge;

              return (
                <Link
                  key={item.id}
                  to={item.path}
                  onClick={() => setSidebarOpen(false)}
                  className={clsx(
                    active
                      ? 'bg-primary-50 text-primary-700 border-primary-500'
                      : 'text-gray-700 hover:bg-gray-50 hover:text-gray-900 border-transparent',
                    'group flex items-center px-3 py-2 text-sm font-medium border-l-4 transition-colors'
                  )}
                >
                  {Icon && (
                    <Icon
                      className={clsx(
                        active ? 'text-primary-500' : 'text-gray-400 group-hover:text-gray-500',
                        'mr-3 h-5 w-5 flex-shrink-0'
                      )}
                    />
                  )}
                  <span className="flex-1">{item.label}</span>
                  {badge !== undefined && (
                    <span
                      className={clsx(
                        active ? 'bg-primary-100 text-primary-700' : 'bg-gray-100 text-gray-600',
                        'ml-2 inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium'
                      )}
                    >
                      {badge}
                    </span>
                  )}
                </Link>
              );
            })}
          </div>
        );
      })}
    </>
  );

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Mobile sidebar */}
      <Transition.Root show={sidebarOpen} as={Fragment}>
        <Dialog as="div" className="relative z-50 lg:hidden" onClose={setSidebarOpen}>
          <Transition.Child
            as={Fragment}
            enter="transition-opacity ease-linear duration-300"
            enterFrom="opacity-0"
            enterTo="opacity-100"
            leave="transition-opacity ease-linear duration-300"
            leaveFrom="opacity-100"
            leaveTo="opacity-0"
          >
            <div className="fixed inset-0 bg-gray-900/80" />
          </Transition.Child>

          <div className="fixed inset-0 flex">
            <Transition.Child
              as={Fragment}
              enter="transition ease-in-out duration-300 transform"
              enterFrom="-translate-x-full"
              enterTo="translate-x-0"
              leave="transition ease-in-out duration-300 transform"
              leaveFrom="translate-x-0"
              leaveTo="-translate-x-full"
            >
              <Dialog.Panel className="relative mr-16 flex w-full max-w-xs flex-1">
                <Transition.Child
                  as={Fragment}
                  enter="ease-in-out duration-300"
                  enterFrom="opacity-0"
                  enterTo="opacity-100"
                  leave="ease-in-out duration-300"
                  leaveFrom="opacity-100"
                  leaveTo="opacity-0"
                >
                  <div className="absolute left-full top-0 flex w-16 justify-center pt-5">
                    <button type="button" className="-m-2.5 p-2.5" onClick={() => setSidebarOpen(false)}>
                      <XMarkIcon className="h-6 w-6 text-white" />
                    </button>
                  </div>
                </Transition.Child>

                <div className="flex grow flex-col gap-y-5 overflow-y-auto bg-white px-6 pb-4">
                  <div className="flex h-16 shrink-0 items-center">
                    <span className="text-xl font-bold text-primary-600">Workspace</span>
                  </div>
                  <nav className="flex flex-1 flex-col">
                    <NavItems />
                  </nav>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </Dialog>
      </Transition.Root>

      {/* Desktop sidebar */}
      <div className="hidden lg:fixed lg:inset-y-0 lg:z-50 lg:flex lg:w-64 lg:flex-col">
        <div className="flex grow flex-col gap-y-5 overflow-y-auto border-r border-gray-200 bg-white px-6 pb-4">
          <div className="flex h-16 shrink-0 items-center">
            <span className="text-xl font-bold text-primary-600">Workspace</span>
          </div>
          <nav className="flex flex-1 flex-col">
            <NavItems />
          </nav>
        </div>
      </div>

      {/* Main content area */}
      <div className="lg:pl-64">
        {/* Top header */}
        <div className="sticky top-0 z-40 flex h-16 shrink-0 items-center gap-x-4 border-b border-gray-200 bg-white px-4 shadow-sm sm:gap-x-6 sm:px-6 lg:px-8">
          <button
            type="button"
            className="-m-2.5 p-2.5 text-gray-700 lg:hidden"
            onClick={() => setSidebarOpen(true)}
          >
            <Bars3Icon className="h-6 w-6" />
          </button>

          {/* Separator */}
          <div className="h-6 w-px bg-gray-200 lg:hidden" />

          <div className="flex flex-1 gap-x-4 self-stretch lg:gap-x-6">
            {/* Search placeholder */}
            <div className="flex flex-1 items-center">
              {/* Add search here if needed */}
            </div>

            <div className="flex items-center gap-x-4 lg:gap-x-6">
              {/* Notifications */}
              <button className="relative p-2 text-gray-400 hover:text-gray-500">
                <BellIcon className="h-6 w-6" />
                {/* Notification badge */}
              </button>

              {/* Profile dropdown */}
              <Menu as="div" className="relative">
                <Menu.Button className="-m-1.5 flex items-center p-1.5">
                  {user?.profilePictureUrl ? (
                    <img
                      className="h-8 w-8 rounded-full bg-gray-50"
                      src={user.profilePictureUrl}
                      alt=""
                    />
                  ) : (
                    <div className="h-8 w-8 rounded-full bg-primary-100 flex items-center justify-center">
                      <span className="text-primary-600 text-sm font-medium">
                        {user?.firstName?.[0] || user?.displayName?.[0] || '?'}
                      </span>
                    </div>
                  )}
                  <span className="hidden lg:flex lg:items-center">
                    <span className="ml-4 text-sm font-semibold text-gray-900">
                      {user?.displayName || user?.userName}
                    </span>
                    <ChevronDownIcon className="ml-2 h-5 w-5 text-gray-400" />
                  </span>
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
                  <Menu.Items className="absolute right-0 z-10 mt-2.5 w-48 origin-top-right rounded-md bg-white py-2 shadow-lg ring-1 ring-gray-900/5 focus:outline-none">
                    <Menu.Item>
                      {({ active }) => (
                        <Link
                          to="/profile"
                          className={clsx(
                            active ? 'bg-gray-50' : '',
                            'flex items-center px-3 py-2 text-sm text-gray-900'
                          )}
                        >
                          <UserIcon className="mr-3 h-5 w-5 text-gray-400" />
                          My Profile
                        </Link>
                      )}
                    </Menu.Item>
                    <Menu.Item>
                      {({ active }) => (
                        <Link
                          to="/settings"
                          className={clsx(
                            active ? 'bg-gray-50' : '',
                            'flex items-center px-3 py-2 text-sm text-gray-900'
                          )}
                        >
                          <Cog6ToothIcon className="mr-3 h-5 w-5 text-gray-400" />
                          Settings
                        </Link>
                      )}
                    </Menu.Item>
                    <div className="border-t border-gray-100 my-1" />
                    <Menu.Item>
                      {({ active }) => (
                        <button
                          onClick={logout}
                          className={clsx(
                            active ? 'bg-gray-50' : '',
                            'flex w-full items-center px-3 py-2 text-sm text-gray-900'
                          )}
                        >
                          <ArrowRightOnRectangleIcon className="mr-3 h-5 w-5 text-gray-400" />
                          Sign out
                        </button>
                      )}
                    </Menu.Item>
                  </Menu.Items>
                </Transition>
              </Menu>
            </div>
          </div>
        </div>

        {/* Page content */}
        <main className="py-6">
          <div className="px-4 sm:px-6 lg:px-8">{children}</div>
        </main>
      </div>
    </div>
  );
}

export default WorkspaceLayout;
