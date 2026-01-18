import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  ArrowDownTrayIcon,
  FunnelIcon,
} from '@heroicons/react/24/outline';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Input from '../components/common/Input';
import Button from '../components/common/Button';
import type { AuditLog, AuditLogFilter } from '../types/audit';
import api from '../services/api';

const CATEGORIES = [
  'Authentication',
  'User',
  'Security',
  'Token',
  'Client',
  'Role',
  'Resource',
  'Settings',
];

export default function AuditLogsPage() {
  const [filter, setFilter] = useState<AuditLogFilter>({
    pageNumber: 1,
    pageSize: 50,
  });
  const [searchTerm, setSearchTerm] = useState('');

  const { data, isLoading, refetch } = useQuery({
    queryKey: ['audit-logs', filter],
    queryFn: async () => {
      const params = {
        ...filter,
        search: filter.search || undefined,
        page: filter.pageNumber,
        pageSize: filter.pageSize,
      };
      const response = await api.get('/auditlogs', { params });
      return response.data;
    },
  });

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setFilter({ ...filter, search: searchTerm, pageNumber: 1 });
  };

  const handleExport = async (format: 'json' | 'csv') => {
    try {
      const params = {
        ...filter,
        format,
        page: undefined,
        pageSize: undefined,
        pageNumber: undefined,
      };
      const response = await api.get('/auditlogs/export', {
        params,
        responseType: 'blob',
      });

      const blob = new Blob([response.data], {
        type: format === 'csv' ? 'text/csv' : 'application/json',
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `activity-logs-${new Date().toISOString().split('T')[0]}.${format}`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      console.error('Export failed:', error);
    }
  };

  const clearFilters = () => {
    setFilter({ pageNumber: 1, pageSize: 50 });
    setSearchTerm('');
  };

  const columns = [
    {
      key: 'timestamp',
      header: 'Timestamp',
      render: (log: AuditLog) => (
        <span className="text-sm text-gray-600">
          {new Date(log.timestamp).toLocaleString()}
        </span>
      ),
    },
    {
      key: 'action',
      header: 'Action',
      render: (log: AuditLog) => (
        <span className="font-medium text-gray-900">{log.action}</span>
      ),
    },
    {
      key: 'category',
      header: 'Category',
      render: (log: AuditLog) => (
        <span className="inline-flex rounded-full bg-gray-100 px-2 py-0.5 text-xs font-medium text-gray-700">
          {log.category}
        </span>
      ),
    },
    {
      key: 'subjectName',
      header: 'User',
      render: (log: AuditLog) => (
        <span className="text-sm text-gray-600">
          {log.subjectName || log.subjectId || '-'}
        </span>
      ),
    },
    { key: 'clientId', header: 'Client' },
    {
      key: 'success',
      header: 'Status',
      render: (log: AuditLog) => (
        <span
          className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
            log.success ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
          }`}
        >
          {log.success ? 'Success' : 'Failed'}
        </span>
      ),
    },
    { key: 'ipAddress', header: 'IP Address' },
  ];

  const totalCount = data?.totalCount || 0;
  const totalPages = data?.totalPages || 0;

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Activity Logs</h1>
          <p className="mt-1 text-sm text-gray-500">
            Security and compliance audit trail for all system events
          </p>
        </div>
        <div className="flex items-center gap-2">
          <div className="relative">
            <Button
              variant="secondary"
              onClick={() => handleExport('csv')}
            >
              <ArrowDownTrayIcon className="h-4 w-4 mr-1" />
              Export CSV
            </Button>
          </div>
          <Button
            variant="secondary"
            onClick={() => refetch()}
            disabled={isLoading}
          >
            <ArrowPathIcon className={`h-4 w-4 mr-1 ${isLoading ? 'animate-spin' : ''}`} />
            Refresh
          </Button>
        </div>
      </div>

      {/* Filters */}
      <Card>
        <form onSubmit={handleSearch} className="space-y-4">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-6">
            <div className="lg:col-span-2">
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Search
              </label>
              <div className="relative">
                <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-gray-400" />
                <input
                  type="text"
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                  placeholder="Search by user, action, or details..."
                  className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-md focus:ring-primary-500 focus:border-primary-500"
                />
              </div>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                Category
              </label>
              <select
                value={filter.category || ''}
                onChange={(e) => setFilter({ ...filter, category: e.target.value || undefined, pageNumber: 1 })}
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
              >
                <option value="">All Categories</option>
                {CATEGORIES.map((cat) => (
                  <option key={cat} value={cat}>
                    {cat}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                From Date
              </label>
              <input
                type="date"
                value={filter.from || ''}
                onChange={(e) => setFilter({ ...filter, from: e.target.value || undefined, pageNumber: 1 })}
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">
                To Date
              </label>
              <input
                type="date"
                value={filter.to || ''}
                onChange={(e) => setFilter({ ...filter, to: e.target.value || undefined, pageNumber: 1 })}
                className="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-primary-500 focus:border-primary-500"
              />
            </div>
            <div className="flex items-end gap-2">
              <Button type="submit" className="flex-1">
                <FunnelIcon className="h-4 w-4 mr-1" />
                Apply
              </Button>
              <Button type="button" variant="secondary" onClick={clearFilters}>
                Clear
              </Button>
            </div>
          </div>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <Input
              placeholder="Filter by action"
              value={filter.action || ''}
              onChange={(e) => setFilter({ ...filter, action: e.target.value || undefined, pageNumber: 1 })}
            />
            <Input
              placeholder="Filter by user ID"
              value={filter.subjectId || ''}
              onChange={(e) => setFilter({ ...filter, subjectId: e.target.value || undefined, pageNumber: 1 })}
            />
            <Input
              placeholder="Filter by client ID"
              value={filter.clientId || ''}
              onChange={(e) => setFilter({ ...filter, clientId: e.target.value || undefined, pageNumber: 1 })}
            />
          </div>
        </form>
      </Card>

      {/* Results info */}
      <div className="flex justify-between items-center">
        <p className="text-sm text-gray-500">
          {totalCount > 0 ? (
            <>
              Showing {((filter.pageNumber - 1) * filter.pageSize) + 1} -{' '}
              {Math.min(filter.pageNumber * filter.pageSize, totalCount)} of{' '}
              {totalCount.toLocaleString()} records
            </>
          ) : (
            'No activity logs found'
          )}
        </p>
      </div>

      {/* Table */}
      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(log) => log.id}
          loading={isLoading}
          emptyMessage="No activity logs found matching your filters"
        />
      </Card>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-sm text-gray-500">
            Page {filter.pageNumber} of {totalPages}
          </p>
          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              disabled={filter.pageNumber === 1}
              onClick={() => setFilter({ ...filter, pageNumber: filter.pageNumber - 1 })}
            >
              Previous
            </Button>
            <Button
              variant="secondary"
              size="sm"
              disabled={filter.pageNumber === totalPages}
              onClick={() => setFilter({ ...filter, pageNumber: filter.pageNumber + 1 })}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
