import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Input from '../components/common/Input';
import Button from '../components/common/Button';
import type { AuditLog, AuditLogFilter } from '../types/audit';
import api from '../services/api';

export default function AuditLogsPage() {
  const [filter, setFilter] = useState<AuditLogFilter>({
    pageNumber: 1,
    pageSize: 20,
  });

  const { data, isLoading } = useQuery({
    queryKey: ['audit-logs', filter],
    queryFn: async () => {
      const response = await api.get('/auditlogs', { params: filter });
      return response.data;
    },
  });

  const columns = [
    {
      key: 'timestamp',
      header: 'Timestamp',
      render: (log: AuditLog) => new Date(log.timestamp).toLocaleString(),
    },
    { key: 'action', header: 'Action' },
    { key: 'category', header: 'Category' },
    { key: 'subjectName', header: 'User' },
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

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Audit Logs</h1>
        <p className="mt-1 text-sm text-gray-500">View security and activity logs</p>
      </div>

      <Card>
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-4">
          <Input
            placeholder="Filter by action"
            value={filter.action || ''}
            onChange={(e) => setFilter({ ...filter, action: e.target.value || undefined })}
          />
          <Input
            placeholder="Filter by user"
            value={filter.subjectId || ''}
            onChange={(e) => setFilter({ ...filter, subjectId: e.target.value || undefined })}
          />
          <Input
            placeholder="Filter by client"
            value={filter.clientId || ''}
            onChange={(e) => setFilter({ ...filter, clientId: e.target.value || undefined })}
          />
          <Button variant="secondary" onClick={() => setFilter({ pageNumber: 1, pageSize: 20 })}>
            Clear Filters
          </Button>
        </div>
      </Card>

      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(log) => log.id}
          loading={isLoading}
          emptyMessage="No audit logs found"
        />
      </Card>

      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-sm text-gray-500">
            Showing page {filter.pageNumber} of {data.totalPages}
          </p>
          <div className="space-x-2">
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
              disabled={filter.pageNumber === data.totalPages}
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
