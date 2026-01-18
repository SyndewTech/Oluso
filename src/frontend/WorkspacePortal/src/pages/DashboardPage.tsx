import { useMemo } from 'react';
import { Link } from 'react-router-dom';
import {
  CalendarIcon,
  DocumentIcon,
  ClockIcon,
  CheckCircleIcon,
  ArrowRightIcon,
  BanknotesIcon,
  UserGroupIcon,
  InboxIcon,
} from '@heroicons/react/24/outline';
import type {
  WorkspaceUIPlugin,
  WorkspaceQuickAction,
  WorkspaceDashboardWidget,
  WorkspaceStatItem,
  WorkspaceWidgetProps,
} from '@oluso/ui-core';
import { useAuth } from '../contexts/AuthContext';
import clsx from 'clsx';

interface DashboardPageProps {
  plugins?: WorkspaceUIPlugin[];
}

// ============ Core Widgets ============

// Welcome Widget - slot: 'welcome', order: 0
function WelcomeWidget({ isManager }: WorkspaceWidgetProps) {
  const { user } = useAuth();
  const greeting = useMemo(() => {
    const hour = new Date().getHours();
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }, []);

  return (
    <div className="bg-gradient-to-r from-primary-600 to-primary-700 rounded-xl p-6 text-white">
      <h1 className="text-2xl font-bold">
        {greeting}, {user?.firstName || user?.displayName}!
      </h1>
      <p className="mt-1 text-primary-100">
        {isManager
          ? "Here's what's happening with your team today."
          : "Here's your workspace for today."}
      </p>
    </div>
  );
}

// Pending Approvals Widget - slot: 'content', order: 10
function PendingApprovalsWidget({ isManager }: WorkspaceWidgetProps) {
  if (!isManager) return null;

  // Mock data - would come from API
  const pendingApprovals = [
    { id: '1', type: 'Leave', requester: 'John Smith', date: '2 hours ago' },
    { id: '2', type: 'Expense', requester: 'Jane Doe', date: '1 day ago' },
    { id: '3', type: 'Document', requester: 'Bob Wilson', date: '2 days ago' },
  ];

  return (
    <>
      <div className="flex items-center justify-between mb-4">
        <h2 className="text-lg font-semibold text-gray-900">Pending Approvals</h2>
        <Link to="/approvals" className="text-sm text-primary-600 hover:text-primary-700">
          View all
        </Link>
      </div>

      <div className="space-y-3">
        {pendingApprovals.map((item) => (
          <div key={item.id} className="flex items-center justify-between py-2 border-b border-gray-100 last:border-0">
            <div className="flex items-center gap-3">
              <div className="h-8 w-8 rounded-full bg-yellow-100 flex items-center justify-center">
                <ClockIcon className="h-4 w-4 text-yellow-600" />
              </div>
              <div>
                <p className="text-sm font-medium text-gray-900">{item.type} Request</p>
                <p className="text-xs text-gray-500">
                  {item.requester} • {item.date}
                </p>
              </div>
            </div>
            <div className="flex gap-2">
              <button className="p-1.5 text-green-600 hover:bg-green-50 rounded">
                <CheckCircleIcon className="h-5 w-5" />
              </button>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}

// Recent Activity Widget - slot: 'content', order: 20
function RecentActivityWidget() {
  // Mock data
  const activities = [
    { id: '1', message: 'Your leave request was approved', time: '1 hour ago', type: 'success' },
    { id: '2', message: 'Document "ID Card" uploaded', time: '3 hours ago', type: 'info' },
    { id: '3', message: 'Profile updated', time: 'Yesterday', type: 'info' },
  ];

  return (
    <>
      <h2 className="text-lg font-semibold text-gray-900 mb-4">Recent Activity</h2>
      <div className="space-y-3">
        {activities.map((activity) => (
          <div key={activity.id} className="flex items-start gap-3">
            <div
              className={clsx(
                'h-2 w-2 mt-2 rounded-full',
                activity.type === 'success' ? 'bg-green-500' : 'bg-blue-500'
              )}
            />
            <div>
              <p className="text-sm text-gray-900">{activity.message}</p>
              <p className="text-xs text-gray-500">{activity.time}</p>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}

// ============ Core Widget Definitions ============

const coreWidgets: WorkspaceDashboardWidget[] = [
  {
    id: 'core-welcome',
    name: 'Welcome',
    slot: 'welcome',
    order: 0,
    component: WelcomeWidget,
    noWrapper: true,
  },
  {
    id: 'core-pending-approvals',
    name: 'Pending Approvals',
    slot: 'content',
    size: 'medium',
    order: 10,
    managerOnly: true,
    component: PendingApprovalsWidget,
    category: 'approvals',
  },
  {
    id: 'core-recent-activity',
    name: 'Recent Activity',
    slot: 'content',
    size: 'medium',
    order: 20,
    component: RecentActivityWidget,
    category: 'personal',
  },
];

const coreStatItems: WorkspaceStatItem[] = [
  {
    id: 'core-leave-balance',
    label: 'Leave Balance',
    value: '12 days',
    icon: CalendarIcon,
    href: '/leave',
    order: 0,
  },
  {
    id: 'core-pending-requests',
    label: 'Pending',
    value: 2,
    icon: InboxIcon,
    href: '/requests',
    order: 10,
  },
  {
    id: 'core-team-members',
    label: 'Team Members',
    value: 8,
    icon: UserGroupIcon,
    href: '/team',
    order: 20,
    managerOnly: true,
  },
  {
    id: 'core-to-approve',
    label: 'To Approve',
    value: 3,
    icon: CheckCircleIcon,
    href: '/approvals',
    order: 30,
    managerOnly: true,
  },
];

const coreQuickActions: WorkspaceQuickAction[] = [
  {
    id: 'request-leave',
    label: 'Request Leave',
    icon: CalendarIcon,
    action: '/leave/request',
    description: 'Submit a new leave request',
    color: 'primary',
  },
  {
    id: 'upload-document',
    label: 'Upload Document',
    icon: DocumentIcon,
    action: '/documents/upload',
    description: 'Upload a new document',
    color: 'secondary',
  },
  {
    id: 'view-payslip',
    label: 'View Payslip',
    icon: BanknotesIcon,
    action: '/payroll/payslips',
    description: 'View your latest payslip',
    color: 'success',
  },
];

// ============ Helper Components ============

function QuickActionCard({ action }: { action: WorkspaceQuickAction }) {
  const Icon = action.icon;
  const isLink = typeof action.action === 'string';

  const colorClasses = {
    primary: 'bg-primary-50 text-primary-700 hover:bg-primary-100',
    secondary: 'bg-gray-50 text-gray-700 hover:bg-gray-100',
    success: 'bg-green-50 text-green-700 hover:bg-green-100',
    warning: 'bg-yellow-50 text-yellow-700 hover:bg-yellow-100',
    danger: 'bg-red-50 text-red-700 hover:bg-red-100',
  };

  const content = (
    <div className={clsx('p-4 rounded-lg transition-colors', colorClasses[action.color || 'secondary'])}>
      <div className="flex items-center gap-3">
        <Icon className="h-5 w-5 flex-shrink-0" />
        <div className="flex-1">
          <h3 className="font-medium">{action.label}</h3>
          {action.description && <p className="text-sm opacity-75">{action.description}</p>}
        </div>
        <ArrowRightIcon className="h-5 w-5 opacity-50" />
      </div>
    </div>
  );

  if (isLink) {
    return <Link to={action.action as string}>{content}</Link>;
  }

  return (
    <button onClick={action.action as () => void} className="w-full text-left">
      {content}
    </button>
  );
}

function StatCard({ stat }: { stat: WorkspaceStatItem }) {
  const Icon = stat.icon;
  const value = typeof stat.value === 'function' ? stat.value() : stat.value;

  const colorClasses = {
    default: 'bg-white/10',
    primary: 'bg-primary-500/20',
    success: 'bg-green-500/20',
    warning: 'bg-yellow-500/20',
    danger: 'bg-red-500/20',
  };

  const content = (
    <div className={clsx('rounded-lg p-3', colorClasses[stat.color || 'default'])}>
      <div className="flex items-center gap-2">
        {Icon && <Icon className="h-4 w-4 text-primary-100" />}
        <p className="text-sm text-primary-100">{stat.label}</p>
      </div>
      <p className="text-2xl font-bold mt-1">{value}</p>
    </div>
  );

  if (stat.href) {
    return <Link to={stat.href} className="block hover:opacity-90 transition-opacity">{content}</Link>;
  }

  return content;
}

// ============ Main Dashboard Component ============

export default function DashboardPage({ plugins = [] }: DashboardPageProps) {
  const { user, isManager, hasPermission, hasFeature } = useAuth();

  // Merge and filter widgets
  const allWidgets = useMemo(() => {
    const pluginWidgets = plugins.flatMap((p) => p.widgets || []);

    // Merge: plugin widgets with same ID replace core widgets
    const widgetMap = new Map<string, WorkspaceDashboardWidget>();
    for (const widget of coreWidgets) {
      widgetMap.set(widget.id, widget);
    }
    for (const widget of pluginWidgets) {
      widgetMap.set(widget.id, widget);
    }

    return Array.from(widgetMap.values())
      .filter((widget) => {
        if (widget.managerOnly && !isManager) return false;
        if (widget.permission && !hasPermission(widget.permission)) return false;
        if (widget.feature && !hasFeature(widget.feature)) return false;
        return true;
      })
      .sort((a, b) => (a.order || 0) - (b.order || 0));
  }, [plugins, isManager, hasPermission, hasFeature]);

  // Get widgets by slot
  const getWidgetsBySlot = (slot: string) =>
    allWidgets.filter((w) => (w.slot || 'content') === slot);

  // Merge and filter stat items
  const allStatItems = useMemo(() => {
    const pluginStats = plugins.flatMap((p) => p.statItems || []);

    const statMap = new Map<string, WorkspaceStatItem>();
    for (const stat of coreStatItems) {
      statMap.set(stat.id, stat);
    }
    for (const stat of pluginStats) {
      statMap.set(stat.id, stat);
    }

    return Array.from(statMap.values())
      .filter((stat) => {
        if (stat.managerOnly && !isManager) return false;
        if (stat.permission && !hasPermission(stat.permission)) return false;
        if (stat.feature && !hasFeature(stat.feature)) return false;
        return true;
      })
      .sort((a, b) => (a.order || 0) - (b.order || 0));
  }, [plugins, isManager, hasPermission, hasFeature]);

  // Merge and filter quick actions
  const allQuickActions = useMemo(() => {
    const pluginActions = plugins.flatMap((p) => p.quickActions || []);

    const actionMap = new Map<string, WorkspaceQuickAction>();
    for (const action of coreQuickActions) {
      actionMap.set(action.id, action);
    }
    for (const action of pluginActions) {
      actionMap.set(action.id, action);
    }

    return Array.from(actionMap.values())
      .filter((action) => {
        if (action.permission && !hasPermission(action.permission)) return false;
        if (action.feature && !hasFeature(action.feature)) return false;
        return true;
      });
  }, [plugins, hasPermission, hasFeature]);

  // Widgets by slot
  const welcomeWidgets = getWidgetsBySlot('welcome');
  const contentWidgets = getWidgetsBySlot('content');

  // Render widget with optional wrapper
  const renderWidget = (widget: WorkspaceDashboardWidget) => {
    const WidgetComponent = widget.component;
    const widgetProps: WorkspaceWidgetProps = { user: user || undefined, isManager };

    if (widget.noWrapper) {
      return <WidgetComponent key={widget.id} {...widgetProps} />;
    }

    return (
      <div
        key={widget.id}
        className={clsx(
          'bg-white rounded-xl shadow p-6',
          widget.size === 'full' && 'lg:col-span-2',
          widget.size === 'large' && 'lg:col-span-2'
        )}
      >
        <WidgetComponent {...widgetProps} />
      </div>
    );
  };

  return (
    <div className="space-y-6">
      {/* Welcome slot */}
      {welcomeWidgets.map((widget) => {
        const WidgetComponent = widget.component;
        const widgetProps: WorkspaceWidgetProps = { user: user || undefined, isManager };

        return (
          <div key={widget.id}>
            {widget.noWrapper ? (
              <WidgetComponent {...widgetProps} />
            ) : (
              <div className="bg-white rounded-xl shadow p-6">
                <WidgetComponent {...widgetProps} />
              </div>
            )}

            {/* Stats rendered inside welcome area */}
            {allStatItems.length > 0 && widget.id === 'core-welcome' && (
              <div className="mt-6 grid grid-cols-2 md:grid-cols-4 gap-4 -mb-6 px-6 pb-6 bg-gradient-to-r from-primary-600 to-primary-700 rounded-b-xl text-white"
                   style={{ marginTop: '-1.5rem' }}>
                {allStatItems.slice(0, 4).map((stat) => (
                  <StatCard key={stat.id} stat={stat} />
                ))}
              </div>
            )}
          </div>
        );
      })}

      {/* Quick Actions */}
      {allQuickActions.length > 0 && (
        <div>
          <h2 className="text-lg font-semibold text-gray-900 mb-4">Quick Actions</h2>
          <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
            {allQuickActions.slice(0, 6).map((action) => (
              <QuickActionCard key={action.id} action={action} />
            ))}
          </div>
        </div>
      )}

      {/* Content widgets grid */}
      {contentWidgets.length > 0 && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {contentWidgets.map(renderWidget)}
        </div>
      )}
    </div>
  );
}
