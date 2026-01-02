import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import { apiResourceService } from '../services/resourceService';
import type { ApiResource } from '../types/resources';
import { PlusIcon } from '@heroicons/react/24/outline';

export default function ApiResourcesPage() {
  const [page] = useState(1);
  const navigate = useNavigate();

  const { data, isLoading } = useQuery({
    queryKey: ['api-resources', page],
    queryFn: () => apiResourceService.getAll(page),
  });

  const columns = [
    {
      key: 'name',
      header: 'Name',
      render: (resource: ApiResource) => (
        <button
          onClick={() => navigate(`/api-resources/${resource.id}`)}
          className="text-blue-600 hover:text-blue-800 hover:underline font-medium"
        >
          {resource.name}
        </button>
      ),
    },
    { key: 'displayName', header: 'Display Name' },
    {
      key: 'enabled',
      header: 'Status',
      render: (resource: ApiResource) => (
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
      key: 'scopes',
      header: 'Scopes',
      render: (resource: ApiResource) => (
        <span className="text-xs text-gray-500">{resource.scopes?.length || 0} scopes</span>
      ),
    },
    {
      key: 'created',
      header: 'Created',
      render: (resource: ApiResource) => new Date(resource.created).toLocaleDateString(),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">API Resources</h1>
          <p className="mt-1 text-sm text-gray-500">Manage protected API resources</p>
        </div>
        <Button onClick={() => navigate('/api-resources/new')}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add API Resource
        </Button>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(resource) => resource.id}
          loading={isLoading}
          emptyMessage="No API resources configured yet"
        />
      </Card>
    </div>
  );
}
