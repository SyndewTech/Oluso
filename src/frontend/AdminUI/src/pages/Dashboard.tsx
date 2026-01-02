import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Card, CardHeader, CardContent } from '../components/common/Card';
import {
  KeyIcon,
  UsersIcon,
  ServerStackIcon,
  ShieldCheckIcon,
  ClockIcon,
  PlusIcon,
  DocumentTextIcon,
} from '@heroicons/react/24/outline';
import api from '../services/api';

interface DashboardStats {
  clientsCount: number;
  usersCount: number;
  apiResourcesCount: number;
  identityResourcesCount: number;
  activeSessionsCount: number;
  recentLoginsCount: number;
}

export default function Dashboard() {
  const navigate = useNavigate();

  const { data: stats, isLoading } = useQuery<DashboardStats>({
    queryKey: ['dashboard-stats'],
    queryFn: async () => {
      const response = await api.get('/dashboard/stats');
      return response.data;
    },
  });

  const quickActions = [
    {
      label: 'Create new client',
      icon: PlusIcon,
      onClick: () => navigate('/clients/new'),
    },
    {
      label: 'Manage users',
      icon: UsersIcon,
      onClick: () => navigate('/users'),
    },
    {
      label: 'Configure API resource',
      icon: ServerStackIcon,
      onClick: () => navigate('/api-resources/new'),
    },
    {
      label: 'View audit logs',
      icon: DocumentTextIcon,
      onClick: () => navigate('/audit-logs'),
    },
  ];

  const statCards = [
    {
      name: 'Total Clients',
      value: stats?.clientsCount ?? 0,
      icon: KeyIcon,
      color: 'bg-blue-500',
    },
    {
      name: 'Total Users',
      value: stats?.usersCount ?? 0,
      icon: UsersIcon,
      color: 'bg-green-500',
    },
    {
      name: 'API Resources',
      value: stats?.apiResourcesCount ?? 0,
      icon: ServerStackIcon,
      color: 'bg-purple-500',
    },
    {
      name: 'Identity Resources',
      value: stats?.identityResourcesCount ?? 0,
      icon: ShieldCheckIcon,
      color: 'bg-yellow-500',
    },
    {
      name: 'Active Sessions',
      value: stats?.activeSessionsCount ?? 0,
      icon: ClockIcon,
      color: 'bg-indigo-500',
    },
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Dashboard</h1>
        <p className="mt-1 text-sm text-gray-500">
          Overview of your identity server configuration
        </p>
      </div>

      <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-3">
        {statCards.map((stat) => (
          <Card key={stat.name}>
            <CardContent className="flex items-center">
              <div className={`flex-shrink-0 rounded p-1.5 ${stat.color}`}>
                <stat.icon className="h-4 w-4 text-white" />
              </div>
              <div className="ml-3">
                <p className="text-sm font-medium text-gray-500">{stat.name}</p>
                <p className="text-2xl font-semibold text-gray-900">
                  {isLoading ? '-' : stat.value}
                </p>
              </div>
            </CardContent>
          </Card>
        ))}
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <Card>
          <CardHeader title="Recent Activity" description="Latest actions in your identity server" />
          <CardContent>
            <p className="text-sm text-gray-500">Activity feed will be displayed here</p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader title="Quick Actions" description="Common administrative tasks" />
          <CardContent className="space-y-2">
            {quickActions.map((action) => (
              <button
                key={action.label}
                onClick={action.onClick}
                className="w-full flex items-center gap-3 text-left px-4 py-2 text-sm text-gray-700 hover:bg-gray-50 rounded-md transition-colors"
              >
                <action.icon className="h-5 w-5 text-gray-400" />
                {action.label}
              </button>
            ))}
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
