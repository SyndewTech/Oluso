import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import { apiScopeService } from '../services/resourceService';
import type { ApiScope } from '../types/resources';
import { PlusIcon } from '@heroicons/react/24/outline';

export default function ApiScopesPage() {
  const [page] = useState(1);
  const navigate = useNavigate();

  const { data, isLoading } = useQuery({
    queryKey: ['api-scopes', page],
    queryFn: () => apiScopeService.getAll(page),
  });

  const columns = [
    {
      key: 'name',
      header: 'Name',
      render: (scope: ApiScope) => (
        <button
          onClick={() => navigate(`/api-scopes/${scope.id}`)}
          className="text-blue-600 hover:text-blue-800 hover:underline font-medium"
        >
          {scope.name}
        </button>
      ),
    },
    { key: 'displayName', header: 'Display Name' },
    {
      key: 'enabled',
      header: 'Status',
      render: (scope: ApiScope) => (
        <span
          className={`inline-flex rounded-full px-2 text-xs font-semibold leading-5 ${
            scope.enabled ? 'bg-green-100 text-green-800' : 'bg-red-100 text-red-800'
          }`}
        >
          {scope.enabled ? 'Enabled' : 'Disabled'}
        </span>
      ),
    },
    {
      key: 'apiResourceNames',
      header: 'Resources',
      render: (scope: ApiScope) => (
        <div className="flex flex-wrap gap-1">
          {scope.apiResourceNames?.length > 0 ? (
            scope.apiResourceNames.map((name) => (
              <span
                key={name}
                className="inline-flex rounded bg-blue-100 px-1.5 py-0.5 text-xs text-blue-700"
              >
                {name}
              </span>
            ))
          ) : (
            <span className="text-xs text-gray-400">No resources</span>
          )}
        </div>
      ),
    },
    {
      key: 'required',
      header: 'Required',
      render: (scope: ApiScope) => (scope.required ? 'Yes' : 'No'),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">API Scopes</h1>
          <p className="mt-1 text-sm text-gray-500">Manage API scopes (permissions)</p>
        </div>
        <Button onClick={() => navigate('/api-scopes/new')}>
          <PlusIcon className="h-4 w-4 mr-2" />
          Add API Scope
        </Button>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={data?.items || []}
          keyExtractor={(scope) => scope.id}
          loading={isLoading}
          emptyMessage="No API scopes configured yet"
        />
      </Card>
    </div>
  );
}
