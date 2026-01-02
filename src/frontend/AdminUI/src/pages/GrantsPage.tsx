import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import { grantService } from '../services/grantService';
import type { PersistedGrant, ServerSideSession } from '../types/grants';
import toast from 'react-hot-toast';

export default function GrantsPage() {
  const [activeTab, setActiveTab] = useState<'grants' | 'sessions'>('grants');
  const [page] = useState(1);
  const queryClient = useQueryClient();

  const { data: grantsData, isLoading: grantsLoading } = useQuery({
    queryKey: ['persisted-grants', page],
    queryFn: () => grantService.getPersistedGrants({}, page),
    enabled: activeTab === 'grants',
  });

  const { data: sessionsData, isLoading: sessionsLoading } = useQuery({
    queryKey: ['sessions', page],
    queryFn: () => grantService.getSessions(undefined, page),
    enabled: activeTab === 'sessions',
  });

  const revokeGrantMutation = useMutation({
    mutationFn: (key: string) => grantService.deletePersistedGrant(key),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['persisted-grants'] });
      toast.success('Grant revoked successfully');
    },
    onError: () => {
      toast.error('Failed to revoke grant');
    },
  });

  const terminateSessionMutation = useMutation({
    mutationFn: (key: string) => grantService.deleteSession(key),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
      toast.success('Session terminated successfully');
    },
    onError: () => {
      toast.error('Failed to terminate session');
    },
  });

  const grantColumns = [
    { key: 'type', header: 'Type' },
    { key: 'clientId', header: 'Client' },
    { key: 'subjectId', header: 'Subject' },
    {
      key: 'creationTime',
      header: 'Created',
      render: (grant: PersistedGrant) => new Date(grant.creationTime).toLocaleString(),
    },
    {
      key: 'expiration',
      header: 'Expires',
      render: (grant: PersistedGrant) =>
        grant.expiration ? new Date(grant.expiration).toLocaleString() : 'Never',
    },
    {
      key: 'actions',
      header: '',
      render: (grant: PersistedGrant) => (
        <Button
          variant="danger"
          size="sm"
          onClick={() => revokeGrantMutation.mutate(grant.key)}
          loading={revokeGrantMutation.isPending}
        >
          Revoke
        </Button>
      ),
    },
  ];

  const sessionColumns = [
    { key: 'subjectId', header: 'Subject' },
    { key: 'displayName', header: 'Display Name' },
    { key: 'scheme', header: 'Scheme' },
    {
      key: 'created',
      header: 'Created',
      render: (session: ServerSideSession) => new Date(session.created).toLocaleString(),
    },
    {
      key: 'renewed',
      header: 'Renewed',
      render: (session: ServerSideSession) => new Date(session.renewed).toLocaleString(),
    },
    {
      key: 'expires',
      header: 'Expires',
      render: (session: ServerSideSession) =>
        session.expires ? new Date(session.expires).toLocaleString() : 'Never',
    },
    {
      key: 'actions',
      header: '',
      render: (session: ServerSideSession) => (
        <Button
          variant="danger"
          size="sm"
          onClick={() => terminateSessionMutation.mutate(session.key)}
          loading={terminateSessionMutation.isPending}
        >
          Terminate
        </Button>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Grants & Sessions</h1>
        <p className="mt-1 text-sm text-gray-500">Manage persisted grants and active sessions</p>
      </div>

      <div className="border-b border-gray-200">
        <nav className="-mb-px flex space-x-8">
          <button
            onClick={() => setActiveTab('grants')}
            className={`py-4 px-1 border-b-2 font-medium text-sm ${
              activeTab === 'grants'
                ? 'border-primary-500 text-primary-600'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            Persisted Grants
          </button>
          <button
            onClick={() => setActiveTab('sessions')}
            className={`py-4 px-1 border-b-2 font-medium text-sm ${
              activeTab === 'sessions'
                ? 'border-primary-500 text-primary-600'
                : 'border-transparent text-gray-500 hover:text-gray-700 hover:border-gray-300'
            }`}
          >
            Active Sessions
          </button>
        </nav>
      </div>

      <Card padding="none">
        {activeTab === 'grants' ? (
          <Table
            columns={grantColumns}
            data={grantsData?.items || []}
            keyExtractor={(grant) => grant.id}
            loading={grantsLoading}
            emptyMessage="No persisted grants found"
          />
        ) : (
          <Table
            columns={sessionColumns}
            data={sessionsData?.items || []}
            keyExtractor={(session) => session.id}
            loading={sessionsLoading}
            emptyMessage="No active sessions found"
          />
        )}
      </Card>
    </div>
  );
}
