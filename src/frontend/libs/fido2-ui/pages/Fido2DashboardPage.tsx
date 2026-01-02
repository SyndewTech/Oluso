import { useEffect, useState } from 'react';
import {
  FingerPrintIcon,
  UserGroupIcon,
  DevicePhoneMobileIcon,
  KeyIcon,
} from '@heroicons/react/24/outline';
import { fido2Api, type Fido2Stats } from '../api/fido2Api';

interface StatCardProps {
  title: string;
  value: string | number;
  subtitle?: string;
  icon: React.ComponentType<{ className?: string }>;
}

function StatCard({ title, value, subtitle, icon: Icon }: StatCardProps) {
  return (
    <div className="bg-white overflow-hidden shadow rounded-lg">
      <div className="p-5">
        <div className="flex items-center">
          <div className="flex-shrink-0">
            <Icon className="h-8 w-8 text-primary-600" />
          </div>
          <div className="ml-4 flex-1">
            <p className="text-sm font-medium text-gray-500">{title}</p>
            <div className="flex items-baseline">
              <p className="text-2xl font-semibold text-gray-900">{value}</p>
            </div>
            {subtitle && <p className="mt-1 text-sm text-gray-500">{subtitle}</p>}
          </div>
        </div>
      </div>
    </div>
  );
}

export default function Fido2DashboardPage() {
  const [stats, setStats] = useState<Fido2Stats | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    async function loadStats() {
      try {
        setLoading(true);
        const data = await fido2Api.getStats();
        setStats(data);
        setError(null);
      } catch (err) {
        setError('Failed to load FIDO2 statistics');
        console.error('Error loading FIDO2 stats:', err);
      } finally {
        setLoading(false);
      }
    }
    loadStats();
  }, []);

  if (loading) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-gray-900">FIDO2 / WebAuthn</h1>
        <div className="animate-pulse">
          <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4">
            {[1, 2, 3, 4].map((i) => (
              <div key={i} className="bg-gray-200 h-24 rounded-lg" />
            ))}
          </div>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-gray-900">FIDO2 / WebAuthn</h1>
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <p className="text-red-700">{error}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">FIDO2 / WebAuthn Dashboard</h1>
      </div>

      <div className="grid grid-cols-1 gap-5 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          title="Total Credentials"
          value={stats?.totalCredentials ?? 0}
          icon={FingerPrintIcon}
        />
        <StatCard
          title="Users with Passkeys"
          value={stats?.totalUsersWithCredentials ?? 0}
          icon={UserGroupIcon}
        />
        <StatCard
          title="Platform Authenticators"
          value={stats?.platformCredentials ?? 0}
          subtitle="Built-in (Touch ID, Windows Hello)"
          icon={DevicePhoneMobileIcon}
        />
        <StatCard
          title="Security Keys"
          value={stats?.crossPlatformCredentials ?? 0}
          subtitle="External (YubiKey, etc.)"
          icon={KeyIcon}
        />
      </div>

      <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
        <div className="bg-white overflow-hidden shadow rounded-lg">
          <div className="px-4 py-5 sm:p-6">
            <h3 className="text-lg font-medium text-gray-900">Recent Activity</h3>
            <div className="mt-4">
              <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                <div>
                  <dt className="text-sm font-medium text-gray-500">
                    Registered (Last 30 Days)
                  </dt>
                  <dd className="mt-1 text-2xl font-semibold text-gray-900">
                    {stats?.credentialsRegisteredLast30Days ?? 0}
                  </dd>
                </div>
                <div>
                  <dt className="text-sm font-medium text-gray-500">
                    Used (Last 30 Days)
                  </dt>
                  <dd className="mt-1 text-2xl font-semibold text-gray-900">
                    {stats?.credentialsUsedLast30Days ?? 0}
                  </dd>
                </div>
              </dl>
            </div>
          </div>
        </div>

        <div className="bg-white overflow-hidden shadow rounded-lg">
          <div className="px-4 py-5 sm:p-6">
            <h3 className="text-lg font-medium text-gray-900">Quick Actions</h3>
            <div className="mt-4 space-y-3">
              <a
                href="/fido2/credentials"
                className="block w-full rounded-md border border-gray-300 bg-white px-4 py-2 text-center text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50"
              >
                View All Credentials
              </a>
              <a
                href="/users"
                className="block w-full rounded-md border border-gray-300 bg-white px-4 py-2 text-center text-sm font-medium text-gray-700 shadow-sm hover:bg-gray-50"
              >
                Manage Users
              </a>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
