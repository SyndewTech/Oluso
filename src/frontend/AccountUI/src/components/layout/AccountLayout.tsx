import { ReactNode, Fragment, useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Dialog, Transition, Menu } from '@headlessui/react';
import {
  Bars3Icon,
  UserCircleIcon,
  ShieldCheckIcon,
  DevicePhoneMobileIcon,
  LinkIcon,
  BuildingOfficeIcon,
  ArrowRightOnRectangleIcon,
  ChevronDownIcon,
  BellAlertIcon,
} from '@heroicons/react/24/outline';
import { clsx } from 'clsx';
import { useAuth } from '../../contexts/AuthContext';
import { useTenant } from '../../contexts/TenantContext';
import { AccountUIPlugin } from '../../types/plugin';

interface AccountLayoutProps {
  children: ReactNode;
  plugins?: AccountUIPlugin[];
}

const coreNavigation = [
  { name: 'Profile', href: '/profile', icon: UserCircleIcon },
  { name: 'Security', href: '/security', icon: ShieldCheckIcon },
  { name: 'Sessions', href: '/sessions', icon: DevicePhoneMobileIcon },
  { name: 'Connected Apps', href: '/connected-apps', icon: LinkIcon },
  { name: 'Auth Requests', href: '/authentication-requests', icon: BellAlertIcon },
];

export function AccountLayout({ children, plugins = [] }: AccountLayoutProps) {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const location = useLocation();
  const { user, logout } = useAuth();
  const { currentTenant, tenants, isMultiTenant, switchTenant } = useTenant();

  // Build navigation from core + plugins
  const navigation = [
    ...coreNavigation,
    ...plugins.flatMap(plugin =>
      (plugin.navigation || []).map(nav => ({
        name: nav.label,
        href: nav.path,
        icon: nav.icon || LinkIcon,
        badge: nav.badge,
      }))
    ),
  ];

  // Add organizations link if multi-tenant
  if (isMultiTenant) {
    navigation.push({
      name: 'Organizations',
      href: '/organizations',
      icon: BuildingOfficeIcon,
    });
  }

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
                <div className="flex grow flex-col gap-y-5 overflow-y-auto bg-white px-6 pb-4">
                  <div className="flex h-16 shrink-0 items-center">
                    <span className="text-xl font-bold text-indigo-600">My Account</span>
                  </div>
                  <nav className="flex flex-1 flex-col">
                    <ul role="list" className="flex flex-1 flex-col gap-y-7">
                      <li>
                        <ul role="list" className="-mx-2 space-y-1">
                          {navigation.map((item) => (
                            <li key={item.name}>
                              <Link
                                to={item.href}
                                onClick={() => setSidebarOpen(false)}
                                className={clsx(
                                  location.pathname === item.href
                                    ? 'bg-indigo-50 text-indigo-600'
                                    : 'text-gray-700 hover:text-indigo-600 hover:bg-gray-50',
                                  'group flex gap-x-3 rounded-md p-2 text-sm leading-6 font-semibold'
                                )}
                              >
                                <item.icon className="h-6 w-6 shrink-0" />
                                {item.name}
                              </Link>
                            </li>
                          ))}
                        </ul>
                      </li>
                    </ul>
                  </nav>
                </div>
              </Dialog.Panel>
            </Transition.Child>
          </div>
        </Dialog>
      </Transition.Root>

      {/* Desktop sidebar */}
      <div className="hidden lg:fixed lg:inset-y-0 lg:z-50 lg:flex lg:w-72 lg:flex-col">
        <div className="flex grow flex-col gap-y-5 overflow-y-auto border-r border-gray-200 bg-white px-6 pb-4">
          <div className="flex h-16 shrink-0 items-center">
            <span className="text-xl font-bold text-indigo-600">My Account</span>
          </div>

          {/* Tenant switcher */}
          {isMultiTenant && currentTenant && (
            <Menu as="div" className="relative">
              <Menu.Button className="flex w-full items-center gap-x-3 rounded-md bg-gray-50 p-3 text-sm font-semibold text-gray-900 hover:bg-gray-100">
                {currentTenant.logoUrl ? (
                  <img src={currentTenant.logoUrl} alt="" className="h-8 w-8 rounded-lg" />
                ) : (
                  <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-indigo-600 text-white">
                    {currentTenant.name.charAt(0).toUpperCase()}
                  </div>
                )}
                <span className="flex-1 text-left truncate">
                  {currentTenant.displayName || currentTenant.name}
                </span>
                <ChevronDownIcon className="h-5 w-5 text-gray-400" />
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
                <Menu.Items className="absolute left-0 right-0 z-10 mt-2 origin-top-left rounded-md bg-white shadow-lg ring-1 ring-black ring-opacity-5 focus:outline-none">
                  <div className="py-1">
                    {tenants.map((tenant) => (
                      <Menu.Item key={tenant.id}>
                        {({ active }) => (
                          <button
                            onClick={() => switchTenant(tenant.id)}
                            className={clsx(
                              active ? 'bg-gray-100' : '',
                              tenant.isCurrent ? 'bg-indigo-50' : '',
                              'flex w-full items-center gap-x-3 px-4 py-2 text-sm text-gray-700'
                            )}
                          >
                            {tenant.logoUrl ? (
                              <img src={tenant.logoUrl} alt="" className="h-6 w-6 rounded" />
                            ) : (
                              <div className="flex h-6 w-6 items-center justify-center rounded bg-gray-200 text-xs font-medium">
                                {tenant.name.charAt(0).toUpperCase()}
                              </div>
                            )}
                            <span className="truncate">{tenant.displayName || tenant.name}</span>
                            {tenant.isCurrent && (
                              <span className="ml-auto text-indigo-600">Current</span>
                            )}
                          </button>
                        )}
                      </Menu.Item>
                    ))}
                  </div>
                </Menu.Items>
              </Transition>
            </Menu>
          )}

          <nav className="flex flex-1 flex-col">
            <ul role="list" className="flex flex-1 flex-col gap-y-7">
              <li>
                <ul role="list" className="-mx-2 space-y-1">
                  {navigation.map((item) => (
                    <li key={item.name}>
                      <Link
                        to={item.href}
                        className={clsx(
                          location.pathname === item.href
                            ? 'bg-indigo-50 text-indigo-600'
                            : 'text-gray-700 hover:text-indigo-600 hover:bg-gray-50',
                          'group flex gap-x-3 rounded-md p-2 text-sm leading-6 font-semibold'
                        )}
                      >
                        <item.icon className="h-6 w-6 shrink-0" />
                        {item.name}
                        {'badge' in item && item.badge && (
                          <span className="ml-auto inline-flex items-center rounded-full bg-indigo-100 px-2 py-0.5 text-xs font-medium text-indigo-600">
                            {item.badge}
                          </span>
                        )}
                      </Link>
                    </li>
                  ))}
                </ul>
              </li>

              {/* User menu at bottom */}
              <li className="mt-auto -mx-2">
                <div className="flex items-center gap-x-4 px-2 py-3 text-sm font-semibold text-gray-900">
                  {user?.picture ? (
                    <img src={user.picture} alt="" className="h-8 w-8 rounded-full" />
                  ) : (
                    <UserCircleIcon className="h-8 w-8 text-gray-400" />
                  )}
                  <span className="flex-1 truncate">{user?.name || user?.email}</span>
                  <button
                    onClick={logout}
                    className="p-1 text-gray-400 hover:text-gray-600"
                    title="Sign out"
                  >
                    <ArrowRightOnRectangleIcon className="h-5 w-5" />
                  </button>
                </div>
              </li>
            </ul>
          </nav>
        </div>
      </div>

      {/* Main content */}
      <div className="lg:pl-72">
        {/* Top bar for mobile */}
        <div className="sticky top-0 z-40 flex h-16 shrink-0 items-center gap-x-4 border-b border-gray-200 bg-white px-4 shadow-sm sm:gap-x-6 sm:px-6 lg:hidden">
          <button
            type="button"
            className="-m-2.5 p-2.5 text-gray-700 lg:hidden"
            onClick={() => setSidebarOpen(true)}
          >
            <span className="sr-only">Open sidebar</span>
            <Bars3Icon className="h-6 w-6" />
          </button>

          <div className="flex flex-1 justify-end gap-x-4">
            <div className="flex items-center gap-x-4">
              {user?.picture ? (
                <img src={user.picture} alt="" className="h-8 w-8 rounded-full" />
              ) : (
                <UserCircleIcon className="h-8 w-8 text-gray-400" />
              )}
            </div>
          </div>
        </div>

        <main className="py-10">
          <div className="px-4 sm:px-6 lg:px-8">{children}</div>
        </main>
      </div>
    </div>
  );
}
