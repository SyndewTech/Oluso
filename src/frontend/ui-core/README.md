# @oluso/ui-core

Core components and extension system for Oluso Admin and Account UIs. This package provides reusable UI components and a plugin system for building extensible interfaces for both administrator and end-user self-service portals.

## Installation

```bash
npm install @oluso/ui-core
```

## Quick Start

```tsx
import {
  PluginProvider,
  ThemeProvider,
  registerPlugin,
  Button,
  Card,
} from '@oluso/ui-core';

// Register a plugin
registerPlugin({
  id: 'billing',
  name: 'Billing Module',
  version: '1.0.0',
  navigation: [
    {
      id: 'billing-nav',
      name: 'Billing',
      href: '/billing',
      icon: CreditCardIcon,
      group: 'billing',
    },
  ],
  routes: [
    {
      path: '/billing',
      component: BillingPage,
    },
  ],
});

// Wrap your app
function App() {
  return (
    <ThemeProvider>
      <PluginProvider>
        <YourAdminUI />
      </PluginProvider>
    </ThemeProvider>
  );
}
```

## Extension Points

### Plugins

Plugins can add navigation items, routes, page slots, and dashboard widgets:

```tsx
import { registerPlugin, type AdminUIPlugin } from '@oluso/ui-core';

const billingPlugin: AdminUIPlugin = {
  id: 'billing',
  name: 'Billing',
  version: '1.0.0',
  requiredFeatures: ['billing:enabled'],

  navigation: [
    {
      id: 'billing',
      name: 'Billing',
      href: '/billing',
      icon: CreditCardIcon,
      group: 'billing',
      order: 10,
    },
    {
      id: 'subscriptions',
      name: 'Subscriptions',
      href: '/billing/subscriptions',
      icon: DocumentIcon,
      group: 'billing',
      order: 20,
    },
  ],

  routes: [
    { path: '/billing', component: BillingDashboard },
    { path: '/billing/subscriptions', component: SubscriptionsPage },
    { path: '/billing/invoices', component: InvoicesPage },
  ],

  widgets: [
    {
      id: 'revenue-chart',
      name: 'Revenue',
      size: 'medium',
      component: RevenueWidget,
    },
  ],

  slots: [
    {
      id: 'user-billing-info',
      pages: ['/users/*'],
      position: 'after-content',
      component: UserBillingSection,
    },
  ],
};

registerPlugin(billingPlugin);
```

### Field Renderers

Add custom field types for forms:

```tsx
import { registerFieldRenderer, type FieldRendererProps } from '@oluso/ui-core';

function ColorPickerField({ value, onChange, label, error }: FieldRendererProps<string>) {
  return (
    <div>
      <label>{label}</label>
      <input
        type="color"
        value={value ?? '#000000'}
        onChange={(e) => onChange(e.target.value)}
      />
      {error && <span className="text-red-500">{error.message}</span>}
    </div>
  );
}

registerFieldRenderer({
  type: 'color',
  displayName: 'Color Picker',
  component: ColorPickerField,
});
```

### Cell Renderers

Add custom table column renderers:

```tsx
import { registerCellRenderer, type CellRendererProps } from '@oluso/ui-core';

function StatusBadge({ value }: CellRendererProps<string>) {
  const colors = {
    active: 'bg-green-100 text-green-800',
    inactive: 'bg-gray-100 text-gray-800',
    pending: 'bg-yellow-100 text-yellow-800',
  };

  return (
    <span className={`px-2 py-1 rounded-full text-xs ${colors[value] ?? colors.inactive}`}>
      {value}
    </span>
  );
}

registerCellRenderer({
  type: 'status',
  displayName: 'Status Badge',
  component: StatusBadge,
});
```

### Action Buttons

Add custom actions for entity types:

```tsx
import { registerActionButtons } from '@oluso/ui-core';

registerActionButtons('user', [
  {
    id: 'reset-password',
    label: 'Reset Password',
    icon: KeyIcon,
    onClick: async (users) => {
      // Handle action
    },
    requiresSelection: true,
  },
  {
    id: 'export',
    label: 'Export',
    icon: DownloadIcon,
    onClick: async () => {
      // Export all users
    },
  },
]);
```

### Detail Sections

Add custom sections to entity detail pages:

```tsx
import { registerDetailSection } from '@oluso/ui-core';

registerDetailSection({
  id: 'user-activity',
  title: 'Activity Log',
  entityTypes: ['user'],
  component: UserActivitySection,
  order: 50,
  collapsible: true,
});
```

## Hooks

### usePlugins

Access plugin data:

```tsx
import { usePlugins } from '@oluso/ui-core';

function Sidebar() {
  const { getNavigation } = usePlugins();
  const navItems = getNavigation({ isSuperAdmin: true });

  return (
    <nav>
      {navItems.map(item => (
        <NavLink key={item.id} to={item.href}>
          <item.icon />
          {item.name}
        </NavLink>
      ))}
    </nav>
  );
}
```

### useTheme

Access theme configuration:

```tsx
import { useTheme } from '@oluso/ui-core';

function ThemeSwitcher() {
  const { isDark, toggleMode, theme } = useTheme();

  return (
    <button onClick={toggleMode}>
      {isDark ? 'Light Mode' : 'Dark Mode'}
    </button>
  );
}
```

## Components

### Button

```tsx
import { Button } from '@oluso/ui-core';

<Button variant="primary" size="md" loading={isLoading}>
  Save
</Button>

<Button variant="danger" leftIcon={<TrashIcon />}>
  Delete
</Button>
```

### Card

```tsx
import { Card, CardHeader, CardContent, CardFooter } from '@oluso/ui-core';

<Card padding="lg">
  <CardHeader
    title="Settings"
    description="Manage your preferences"
    action={<Button size="sm">Edit</Button>}
  />
  <CardContent>
    {/* Content */}
  </CardContent>
  <CardFooter>
    <Button>Save Changes</Button>
  </CardFooter>
</Card>
```

## Theme Configuration

```tsx
import { ThemeProvider } from '@oluso/ui-core';

<ThemeProvider
  theme={{
    name: 'custom',
    mode: 'light',
    colors: {
      primary: {
        500: '#6366f1',
        600: '#4f46e5',
        700: '#4338ca',
      },
    },
    logo: {
      light: '/logo-light.svg',
      dark: '/logo-dark.svg',
      height: 32,
    },
  }}
>
  <App />
</ThemeProvider>
```

## License

MIT
