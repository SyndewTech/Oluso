import { useState, useEffect } from 'react';
import {
  TicketIcon,
  UserGroupIcon,
  ClockIcon,
  CheckCircleIcon,
  ExclamationCircleIcon,
  ArrowPathIcon,
} from '@heroicons/react/24/outline';
import { telemetryApi, type TelemetryMetrics, type MetricSeries } from '../api/telemetryApi';
import { MetricCard } from '../components/MetricCard';
import { SimpleBarChart, SimpleLineChart } from '../components/SimpleChart';

export default function TelemetryDashboardPage() {
  const [metrics, setMetrics] = useState<TelemetryMetrics | null>(null);
  const [tokenSeries, setTokenSeries] = useState<MetricSeries | null>(null);
  const [authSeries, setAuthSeries] = useState<MetricSeries | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [timeRange, setTimeRange] = useState('24h');
  const [autoRefresh, setAutoRefresh] = useState(false);

  const loadData = async () => {
    try {
      setLoading(true);
      const [metricsData, tokens, auth] = await Promise.all([
        telemetryApi.getOverviewMetrics(timeRange),
        telemetryApi.getMetricSeries('tokens_issued', timeRange, getInterval(timeRange)),
        telemetryApi.getMetricSeries('auth_success', timeRange, getInterval(timeRange)),
      ]);
      setMetrics(metricsData);
      setTokenSeries(tokens);
      setAuthSeries(auth);
      setError(null);
    } catch (err) {
      setError('Failed to load telemetry data');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadData();
  }, [timeRange]);

  useEffect(() => {
    if (autoRefresh) {
      const interval = setInterval(loadData, 30000); // 30 seconds
      return () => clearInterval(interval);
    }
  }, [autoRefresh, timeRange]);

  const getInterval = (range: string) => {
    switch (range) {
      case '1h': return '5m';
      case '24h': return '1h';
      case '7d': return '6h';
      case '30d': return '1d';
      default: return '1h';
    }
  };

  if (loading && !metrics) {
    return (
      <div className="p-6 flex items-center justify-center">
        <ArrowPathIcon className="h-8 w-8 animate-spin text-gray-400" />
      </div>
    );
  }

  return (
    <div className="p-6">
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">Telemetry Dashboard</h1>
          <p className="text-gray-500 mt-1">
            Monitor system health, performance, and security metrics
          </p>
        </div>
        <div className="flex items-center gap-4">
          <label className="flex items-center gap-2 text-sm text-gray-600">
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
              className="rounded"
            />
            Auto-refresh
          </label>
          <select
            value={timeRange}
            onChange={(e) => setTimeRange(e.target.value)}
            className="border rounded-md px-3 py-2 text-sm"
          >
            <option value="1h">Last 1 hour</option>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
            <option value="30d">Last 30 days</option>
          </select>
          <button
            onClick={loadData}
            disabled={loading}
            className="inline-flex items-center px-3 py-2 bg-gray-100 rounded-md hover:bg-gray-200 text-sm"
          >
            <ArrowPathIcon className={`h-4 w-4 mr-1 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 text-red-700 p-4 rounded-md mb-6">{error}</div>
      )}

      {/* Overview Metrics */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
        <MetricCard
          title="Tokens Issued"
          value={metrics?.tokensIssued.toLocaleString() ?? '-'}
          icon={TicketIcon}
          color="blue"
        />
        <MetricCard
          title="Active Users"
          value={metrics?.activeUsers.toLocaleString() ?? '-'}
          icon={UserGroupIcon}
          color="green"
        />
        <MetricCard
          title="Auth Success Rate"
          value={`${(metrics?.authSuccessRate ?? 0).toFixed(1)}%`}
          icon={CheckCircleIcon}
          color={metrics && metrics.authSuccessRate >= 95 ? 'green' : 'yellow'}
        />
        <MetricCard
          title="Avg Response Time"
          value={`${(metrics?.avgRequestDuration ?? 0).toFixed(0)}`}
          unit="ms"
          icon={ClockIcon}
          color={metrics && metrics.avgRequestDuration < 200 ? 'green' : 'yellow'}
        />
      </div>

      {/* Secondary Metrics */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6 mb-8">
        <MetricCard
          title="Token Failures"
          value={metrics?.tokensFailed.toLocaleString() ?? '-'}
          icon={ExclamationCircleIcon}
          color="red"
        />
        <MetricCard
          title="Active Sessions"
          value={metrics?.activeSessions.toLocaleString() ?? '-'}
          icon={UserGroupIcon}
          color="purple"
        />
        <MetricCard
          title="System Health"
          value={metrics && metrics.authSuccessRate >= 95 && metrics.avgRequestDuration < 500 ? 'Healthy' : 'Degraded'}
          icon={CheckCircleIcon}
          color={metrics && metrics.authSuccessRate >= 95 ? 'green' : 'yellow'}
        />
      </div>

      {/* Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {tokenSeries && (
          <SimpleBarChart
            title="Tokens Issued Over Time"
            data={tokenSeries.data.map((d) => ({
              label: new Date(d.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
              value: d.value,
            }))}
            color="#3b82f6"
            height={200}
          />
        )}
        {authSeries && (
          <SimpleLineChart
            title="Authentication Success Over Time"
            data={authSeries.data.map((d) => ({
              label: new Date(d.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
              value: d.value,
            }))}
            color="#10b981"
            height={200}
          />
        )}
      </div>

      {/* Quick Links */}
      <div className="mt-8 grid grid-cols-1 md:grid-cols-3 gap-4">
        <a
          href="/telemetry/logs"
          className="block p-4 bg-white rounded-lg shadow hover:shadow-md transition-shadow"
        >
          <h3 className="font-medium text-gray-900">View Logs</h3>
          <p className="text-sm text-gray-500 mt-1">
            Search and filter application logs
          </p>
        </a>
        <a
          href="/telemetry/traces"
          className="block p-4 bg-white rounded-lg shadow hover:shadow-md transition-shadow"
        >
          <h3 className="font-medium text-gray-900">View Traces</h3>
          <p className="text-sm text-gray-500 mt-1">
            Distributed tracing for requests
          </p>
        </a>
        <a
          href="/telemetry/audit"
          className="block p-4 bg-white rounded-lg shadow hover:shadow-md transition-shadow"
        >
          <h3 className="font-medium text-gray-900">Audit Logs</h3>
          <p className="text-sm text-gray-500 mt-1">
            Security and compliance audit trail
          </p>
        </a>
      </div>
    </div>
  );
}
