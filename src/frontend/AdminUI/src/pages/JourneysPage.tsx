import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import { journeyService, JourneyPolicyListItem } from '../services/journeyService';
import {
  PlusIcon,
  DocumentDuplicateIcon,
  GlobeAltIcon,
  BuildingOfficeIcon
} from '@heroicons/react/24/outline';

const policyTypeColors: Record<string, string> = {
  // Authentication types
  SignIn: 'bg-blue-100 text-blue-800',
  SignUp: 'bg-green-100 text-green-800',
  SignInSignUp: 'bg-purple-100 text-purple-800',
  PasswordReset: 'bg-yellow-100 text-yellow-800',
  ProfileEdit: 'bg-orange-100 text-orange-800',
  LinkAccount: 'bg-indigo-100 text-indigo-800',
  Consent: 'bg-teal-100 text-teal-800',
  // Data collection types
  Waitlist: 'bg-pink-100 text-pink-800',
  ContactForm: 'bg-cyan-100 text-cyan-800',
  Survey: 'bg-lime-100 text-lime-800',
  Feedback: 'bg-amber-100 text-amber-800',
  DataCollection: 'bg-rose-100 text-rose-800',
  // Other
  Custom: 'bg-gray-100 text-gray-800',
};

export default function JourneysPage() {
  const [includeGlobal, setIncludeGlobal] = useState(true);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data: policies, isLoading } = useQuery({
    queryKey: ['journeys', includeGlobal],
    queryFn: () => journeyService.getPolicies(includeGlobal),
  });

  const cloneMutation = useMutation({
    mutationFn: ({ policyId, newName }: { policyId: string; newName?: string }) =>
      journeyService.clonePolicy(policyId, undefined, newName),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['journeys'] });
    },
  });

  const toggleMutation = useMutation({
    mutationFn: ({ policyId, enabled }: { policyId: string; enabled: boolean }) =>
      journeyService.setStatus(policyId, enabled),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['journeys'] });
    },
  });

  const handleClone = (policy: JourneyPolicyListItem, e: React.MouseEvent) => {
    e.stopPropagation();
    cloneMutation.mutate({
      policyId: policy.id,
      newName: `${policy.name} (Custom)`
    });
  };

  const handleToggle = (policy: JourneyPolicyListItem, e: React.MouseEvent) => {
    e.stopPropagation();
    if (!policy.isGlobal) {
      toggleMutation.mutate({ policyId: policy.id, enabled: !policy.enabled });
    }
  };

  const columns = [
    {
      key: 'name',
      header: 'Policy Name',
      render: (policy: JourneyPolicyListItem) => (
        <div className="flex items-center">
          {policy.isGlobal ? (
            <GlobeAltIcon className="h-4 w-4 text-gray-400 mr-2" title="Global Policy" />
          ) : (
            <BuildingOfficeIcon className="h-4 w-4 text-blue-400 mr-2" title="Tenant Policy" />
          )}
          <div>
            <div className="font-medium text-gray-900">{policy.name}</div>
            {policy.description && (
              <div className="text-xs text-gray-500">{policy.description}</div>
            )}
          </div>
        </div>
      ),
    },
    {
      key: 'type',
      header: 'Type',
      render: (policy: JourneyPolicyListItem) => (
        <span className={`inline-flex rounded-full px-2 py-1 text-xs font-semibold ${policyTypeColors[policy.type] || 'bg-gray-100 text-gray-800'}`}>
          {policy.type}
        </span>
      ),
    },
    {
      key: 'enabled',
      header: 'Status',
      render: (policy: JourneyPolicyListItem) => (
        <button
          onClick={(e) => handleToggle(policy, e)}
          disabled={policy.isGlobal}
          className={`inline-flex rounded-full px-2 py-1 text-xs font-semibold ${
            policy.enabled
              ? 'bg-green-100 text-green-800'
              : 'bg-red-100 text-red-800'
          } ${policy.isGlobal ? 'cursor-not-allowed opacity-60' : 'cursor-pointer hover:opacity-80'}`}
        >
          {policy.enabled ? 'Enabled' : 'Disabled'}
        </button>
      ),
    },
    {
      key: 'stepCount',
      header: 'Steps',
      render: (policy: JourneyPolicyListItem) => (
        <span className="text-sm text-gray-600">{policy.stepCount}</span>
      ),
    },
    {
      key: 'priority',
      header: 'Priority',
      render: (policy: JourneyPolicyListItem) => (
        <span className="text-sm text-gray-600">{policy.priority}</span>
      ),
    },
    {
      key: 'actions',
      header: '',
      render: (policy: JourneyPolicyListItem) => (
        <div className="flex items-center space-x-2">
          <button
            onClick={(e) => handleClone(policy, e)}
            className="text-gray-400 hover:text-gray-600"
            title={policy.isGlobal ? 'Customize for your tenant' : 'Clone policy'}
          >
            <DocumentDuplicateIcon className="h-4 w-4" />
          </button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">User Journeys</h1>
          <p className="mt-1 text-sm text-gray-500">
            Configure authentication flows for your users (like Azure AD B2C)
          </p>
        </div>
        <div className="flex items-center space-x-4">
          <label className="flex items-center text-sm text-gray-600">
            <input
              type="checkbox"
              checked={includeGlobal}
              onChange={(e) => setIncludeGlobal(e.target.checked)}
              className="mr-2 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
            />
            Show global policies
          </label>
          <Button onClick={() => navigate('/journeys/new')}>
            <PlusIcon className="h-4 w-4 mr-2" />
            Create Journey
          </Button>
        </div>
      </div>

      <Card padding="none">
        <Table
          columns={columns}
          data={policies || []}
          keyExtractor={(policy) => policy.id}
          onRowClick={(policy) => navigate(`/journeys/${policy.id}`)}
          loading={isLoading}
          emptyMessage="No user journeys configured yet"
        />
      </Card>

      <div className="bg-blue-50 border border-blue-200 rounded-lg p-4">
        <h3 className="text-sm font-medium text-blue-800">About User Journeys</h3>
        <p className="mt-1 text-sm text-blue-600">
          User journeys define the authentication flow for your users. You can create custom
          journeys with different steps like local login, social login, MFA, consent, and more.
          Clone a global policy to customize it for your tenant.
        </p>
      </div>
    </div>
  );
}
