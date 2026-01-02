import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import { clientService } from '../services/clientService';
import type { Client } from '../types/client';
import { PlusIcon } from '@heroicons/react/24/outline';

export default function ClientsPage() {
  const [page, setPage] = useState(1);
  const navigate = useNavigate();

  const { data, isLoading } = useQuery({
    queryKey: ['clients', page],
    queryFn: () => clientService.getAll(),
  });

  const columns = [
    { key: 'clientId', header: 'Client ID' },
    { key: 'clientName', header: 'Name' },
    {
      key: 'enabled',
      header: 'Status',
      render: (client: Client) => (
        <span
          className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
            client.enabled ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
          }`}
        >
          {client.enabled ? 'Enabled' : 'Disabled'}
        </span>
      ),
    },
    {
      key: 'allowedGrantTypes',
      header: 'Grant Types',
      render: (client: Client) => (
        <span className="text-xs text-gray-500">
          {client.allowedGrantTypes?.join(', ') || '-'}
        </span>
      ),
    },
    {
      key: 'created',
      header: 'Created',
      render: (client: Client) => new Date(client.created).toLocaleDateString(),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Clients</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage OAuth 2.0 / OpenID Connect client applications
          </p>
        </div>
        <Button onClick={() => navigate('/clients/new')}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add Client
        </Button>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(client) => client.clientId}
          onRowClick={(client) => navigate(`/clients/${encodeURIComponent(client.clientId)}`)}
          loading={isLoading}
          emptyMessage="No clients configured yet"
        />
      </Card>

      {data && data.totalPages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-sm text-gray-500">
            Showing page {page} of {data.totalPages}
          </p>
          <div className="space-x-2">
            <Button
              variant="secondary"
              size="sm"
              disabled={page === 1}
              onClick={() => setPage(page - 1)}
            >
              Previous
            </Button>
            <Button
              variant="secondary"
              size="sm"
              disabled={page === data.totalPages}
              onClick={() => setPage(page + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
