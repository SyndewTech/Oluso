import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import { resourceService } from '../services/resourceService';
import type { Resource } from '../types/resources';
import { PlusIcon } from '@heroicons/react/24/outline';

export default function ResourcesPage() {
  const [page] = useState(1);
  const navigate = useNavigate();

  const { data, isLoading } = useQuery({
    queryKey: ['resources', page],
    queryFn: () => resourceService.getAll(page),
  });

  const columns = [
    {
      key: 'uri',
      header: 'Resource URI',
      render: (resource: Resource) => (
        <button
          onClick={() => navigate(`/resources/${resource.id}`)}
          className="text-blue-600 hover:text-blue-800 hover:underline font-medium text-left break-all"
        >
          {resource.uri}
        </button>
      ),
    },
    { key: 'displayName', header: 'Display Name' },
    {
      key: 'enabled',
      header: 'Status',
      render: (resource: Resource) => (
        <span
          className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
            resource.enabled ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
          }`}
        >
          {resource.enabled ? 'Enabled' : 'Disabled'}
        </span>
      ),
    },
    {
      key: 'allowedScopes',
      header: 'Scopes',
      render: (resource: Resource) => (
        <span className="text-xs text-gray-500">{resource.allowedScopes?.length || 0} scopes</span>
      ),
    },
    {
      key: 'created',
      header: 'Created',
      render: (resource: Resource) => new Date(resource.created).toLocaleDateString(),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Resources</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage protected resources (RFC 8707). Resources are identified by absolute URIs.
          </p>
        </div>
        <Button onClick={() => navigate('/resources/new')}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add Resource
        </Button>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(resource) => resource.id}
          loading={isLoading}
          emptyMessage="No resources configured yet"
        />
      </Card>
    </div>
  );
}
