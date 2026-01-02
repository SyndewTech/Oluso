import { useState, useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import { Badge } from '../components/common/Badge';
import Modal from '../components/common/Modal';
import {
  submissionService,
  JourneySubmission,
  SubmissionStatus
} from '../services/submissionService';
import {
  DocumentTextIcon,
  ArrowDownTrayIcon,
  TrashIcon,
  EyeIcon,
  CheckIcon,
  XMarkIcon,
  InboxIcon,
  ChevronRightIcon,
  LinkIcon,
  ClipboardDocumentIcon
} from '@heroicons/react/24/outline';

const statusColors: Record<SubmissionStatus, string> = {
  New: 'bg-blue-100 text-blue-800',
  Reviewed: 'bg-gray-100 text-gray-800',
  Processing: 'bg-yellow-100 text-yellow-800',
  Approved: 'bg-green-100 text-green-800',
  Rejected: 'bg-red-100 text-red-800',
  FollowUp: 'bg-orange-100 text-orange-800',
  Archived: 'bg-gray-100 text-gray-500'
};

const statusOptions: SubmissionStatus[] = [
  'New',
  'Reviewed',
  'Processing',
  'Approved',
  'Rejected',
  'FollowUp',
  'Archived'
];

function formatDate(dateString: string): string {
  return new Date(dateString).toLocaleString();
}

function formatRelativeDate(dateString: string): string {
  const date = new Date(dateString);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMs / 3600000);
  const diffDays = Math.floor(diffMs / 86400000);

  if (diffMins < 1) return 'Just now';
  if (diffMins < 60) return `${diffMins}m ago`;
  if (diffHours < 24) return `${diffHours}h ago`;
  if (diffDays < 7) return `${diffDays}d ago`;
  return date.toLocaleDateString();
}

export default function SubmissionsPage() {
  const [selectedPolicyId, setSelectedPolicyId] = useState<string | null>(null);
  const [selectedSubmission, setSelectedSubmission] = useState<JourneySubmission | null>(null);
  const [showDetailsModal, setShowDetailsModal] = useState(false);
  const [page, setPage] = useState(0);
  const [copiedUrl, setCopiedUrl] = useState(false);
  const pageSize = 25;
  const queryClient = useQueryClient();

  // Get the base URL for journey links
  // /journey/start/{policyId} starts a new journey from the policy
  // /journey/{journeyStateId} is for active journey sessions
  const getJourneyUrl = (policyId: string) => {
    // Derive identity server URL from API URL (remove /api/admin suffix)
    const apiUrl = import.meta.env.VITE_API_URL || 'http://localhost:5050/api/admin';
    const identityServerUrl = apiUrl.replace(/\/api\/admin\/?$/, '');
    return `${identityServerUrl}/journey/start/${policyId}`;
  };

  const copyToClipboard = async (text: string) => {
    try {
      await navigator.clipboard.writeText(text);
      setCopiedUrl(true);
      setTimeout(() => setCopiedUrl(false), 2000);
    } catch (err) {
      console.error('Failed to copy:', err);
    }
  };

  // Fetch data collection policies
  const { data: policies, isLoading: policiesLoading } = useQuery({
    queryKey: ['data-collection-policies'],
    queryFn: () => submissionService.getDataCollectionPolicies()
  });

  // Fetch submissions for selected policy
  const { data: submissionsData, isLoading: submissionsLoading } = useQuery({
    queryKey: ['submissions', selectedPolicyId, page],
    queryFn: () =>
      selectedPolicyId
        ? submissionService.getSubmissions(selectedPolicyId, page * pageSize, pageSize)
        : null,
    enabled: !!selectedPolicyId
  });

  // Update submission mutation
  const updateMutation = useMutation({
    mutationFn: ({
      submissionId,
      status
    }: {
      submissionId: string;
      status: SubmissionStatus;
    }) => submissionService.updateSubmission(submissionId, { status }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['submissions'] });
    }
  });

  // Delete submission mutation
  const deleteMutation = useMutation({
    mutationFn: (submissionId: string) =>
      submissionService.deleteSubmission(submissionId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['submissions'] });
      queryClient.invalidateQueries({ queryKey: ['data-collection-policies'] });
    }
  });

  const selectedPolicy = useMemo(
    () => policies?.find((p) => p.id === selectedPolicyId),
    [policies, selectedPolicyId]
  );

  const handleExport = async () => {
    if (!selectedPolicyId) return;
    try {
      const blob = await submissionService.exportSubmissions(selectedPolicyId, {
        format: 'csv'
      });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${selectedPolicy?.name || 'submissions'}-export.csv`;
      document.body.appendChild(a);
      a.click();
      window.URL.revokeObjectURL(url);
      document.body.removeChild(a);
    } catch (error) {
      console.error('Export failed:', error);
    }
  };

  const handleViewDetails = (submission: JourneySubmission) => {
    setSelectedSubmission(submission);
    setShowDetailsModal(true);
  };

  const handleStatusChange = (submission: JourneySubmission, status: SubmissionStatus) => {
    updateMutation.mutate({ submissionId: submission.id, status });
  };

  const handleDelete = (submission: JourneySubmission) => {
    if (confirm('Are you sure you want to delete this submission?')) {
      deleteMutation.mutate(submission.id);
    }
  };

  const columns = [
    {
      key: 'createdAt',
      header: 'Submitted',
      render: (submission: JourneySubmission) => (
        <div>
          <div className="text-sm font-medium text-gray-900">
            {formatRelativeDate(submission.createdAt)}
          </div>
          <div className="text-xs text-gray-500">
            {formatDate(submission.createdAt)}
          </div>
        </div>
      )
    },
    {
      key: 'data',
      header: 'Data Preview',
      render: (submission: JourneySubmission) => {
        const entries = Object.entries(submission.data).slice(0, 2);
        return (
          <div className="max-w-xs">
            {entries.map(([key, value]) => (
              <div key={key} className="text-sm truncate">
                <span className="text-gray-500">{key}:</span>{' '}
                <span className="text-gray-900">{String(value)}</span>
              </div>
            ))}
            {Object.keys(submission.data).length > 2 && (
              <div className="text-xs text-gray-400">
                +{Object.keys(submission.data).length - 2} more fields
              </div>
            )}
          </div>
        );
      }
    },
    {
      key: 'metadata',
      header: 'Source',
      render: (submission: JourneySubmission) => (
        <div className="text-sm">
          {submission.metadata.country && (
            <div className="text-gray-600">{submission.metadata.country}</div>
          )}
          {submission.metadata.ipAddress && (
            <div className="text-xs text-gray-400">{submission.metadata.ipAddress}</div>
          )}
        </div>
      )
    },
    {
      key: 'status',
      header: 'Status',
      render: (submission: JourneySubmission) => (
        <select
          value={submission.status}
          onChange={(e) =>
            handleStatusChange(submission, e.target.value as SubmissionStatus)
          }
          className={`text-xs font-semibold rounded-full px-2 py-1 border-0 cursor-pointer ${statusColors[submission.status]}`}
        >
          {statusOptions.map((status) => (
            <option key={status} value={status}>
              {status}
            </option>
          ))}
        </select>
      )
    },
    {
      key: 'actions',
      header: '',
      render: (submission: JourneySubmission) => (
        <div className="flex items-center space-x-2">
          <button
            onClick={() => handleViewDetails(submission)}
            className="text-gray-400 hover:text-blue-600"
            title="View details"
          >
            <EyeIcon className="h-4 w-4" />
          </button>
          <button
            onClick={() => handleDelete(submission)}
            className="text-gray-400 hover:text-red-600"
            title="Delete"
          >
            <TrashIcon className="h-4 w-4" />
          </button>
        </div>
      )
    }
  ];

  return (
    <div className="h-full flex flex-col">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900">Data Collection</h1>
        <p className="mt-1 text-sm text-gray-500">
          View and manage submissions from waitlists, surveys, and other data collection journeys
        </p>
      </div>

      <div className="flex-1 flex gap-6 min-h-0">
        {/* Left panel - Policy list */}
        <div className="w-80 flex-shrink-0">
          <Card className="h-full flex flex-col" padding="none">
            <div className="p-4 border-b border-gray-200">
              <h2 className="text-sm font-medium text-gray-700">Collection Policies</h2>
            </div>
            <div className="flex-1 overflow-y-auto">
              {policiesLoading ? (
                <div className="p-4 text-center text-gray-500">Loading...</div>
              ) : policies?.length === 0 ? (
                <div className="p-4 text-center">
                  <InboxIcon className="mx-auto h-12 w-12 text-gray-400" />
                  <h3 className="mt-2 text-sm font-medium text-gray-900">
                    No data collection policies
                  </h3>
                  <p className="mt-1 text-sm text-gray-500">
                    Create a waitlist or survey journey to start collecting data
                  </p>
                </div>
              ) : (
                <ul className="divide-y divide-gray-200">
                  {policies?.map((policy) => (
                    <li key={policy.id}>
                      <button
                        onClick={() => {
                          setSelectedPolicyId(policy.id);
                          setPage(0);
                        }}
                        className={`w-full px-4 py-3 text-left hover:bg-gray-50 flex items-center justify-between ${
                          selectedPolicyId === policy.id ? 'bg-blue-50' : ''
                        }`}
                      >
                        <div className="min-w-0 flex-1">
                          <div className="flex items-center">
                            <DocumentTextIcon className="h-5 w-5 text-gray-400 mr-2 flex-shrink-0" />
                            <div className="truncate">
                              <p className="text-sm font-medium text-gray-900 truncate">
                                {policy.name}
                              </p>
                              <p className="text-xs text-gray-500">
                                {policy.submissionCount} submissions
                                {policy.maxSubmissions > 0 &&
                                  ` / ${policy.maxSubmissions} max`}
                              </p>
                            </div>
                          </div>
                        </div>
                        <div className="ml-2 flex items-center">
                          <Badge
                            variant={policy.enabled ? 'success' : 'default'}
                            size="sm"
                          >
                            {policy.enabled ? 'Active' : 'Inactive'}
                          </Badge>
                          <ChevronRightIcon className="h-4 w-4 text-gray-400 ml-2" />
                        </div>
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </Card>
        </div>

        {/* Right panel - Submissions */}
        <div className="flex-1 min-w-0">
          <Card className="h-full flex flex-col" padding="none">
            {selectedPolicyId ? (
              <>
                <div className="p-4 border-b border-gray-200">
                  <div className="flex items-center justify-between">
                    <div>
                      <h2 className="text-lg font-medium text-gray-900">
                        {selectedPolicy?.name}
                      </h2>
                      <p className="text-sm text-gray-500">
                        {submissionsData?.total || 0} total submissions
                      </p>
                    </div>
                    <div className="flex items-center space-x-2">
                      <Button variant="secondary" size="sm" onClick={handleExport}>
                        <ArrowDownTrayIcon className="h-4 w-4 mr-1" />
                        Export CSV
                      </Button>
                    </div>
                  </div>
                  {/* Journey URL for embedding/sharing */}
                  <div className="mt-3 flex items-center gap-2 bg-gray-50 rounded-lg px-3 py-2">
                    <LinkIcon className="h-4 w-4 text-gray-400 flex-shrink-0" />
                    <code className="text-sm text-gray-600 flex-1 truncate">
                      {getJourneyUrl(selectedPolicyId)}
                    </code>
                    <button
                      onClick={() => copyToClipboard(getJourneyUrl(selectedPolicyId))}
                      className="flex items-center gap-1 text-sm text-blue-600 hover:text-blue-800 flex-shrink-0"
                      title="Copy URL"
                    >
                      <ClipboardDocumentIcon className="h-4 w-4" />
                      {copiedUrl ? 'Copied!' : 'Copy'}
                    </button>
                  </div>
                </div>
                <div className="flex-1 overflow-auto">
                  <Table
                    columns={columns}
                    data={submissionsData?.submissions || []}
                    keyExtractor={(s) => s.id}
                    loading={submissionsLoading}
                    emptyMessage="No submissions yet"
                  />
                </div>
                {submissionsData && submissionsData.total > pageSize && (
                  <div className="p-4 border-t border-gray-200 flex items-center justify-between">
                    <p className="text-sm text-gray-500">
                      Showing {page * pageSize + 1}-
                      {Math.min((page + 1) * pageSize, submissionsData.total)} of{' '}
                      {submissionsData.total}
                    </p>
                    <div className="flex space-x-2">
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => setPage((p) => Math.max(0, p - 1))}
                        disabled={page === 0}
                      >
                        Previous
                      </Button>
                      <Button
                        variant="secondary"
                        size="sm"
                        onClick={() => setPage((p) => p + 1)}
                        disabled={(page + 1) * pageSize >= submissionsData.total}
                      >
                        Next
                      </Button>
                    </div>
                  </div>
                )}
              </>
            ) : (
              <div className="flex-1 flex items-center justify-center">
                <div className="text-center">
                  <InboxIcon className="mx-auto h-12 w-12 text-gray-400" />
                  <h3 className="mt-2 text-sm font-medium text-gray-900">
                    Select a policy
                  </h3>
                  <p className="mt-1 text-sm text-gray-500">
                    Choose a data collection policy from the left to view submissions
                  </p>
                </div>
              </div>
            )}
          </Card>
        </div>
      </div>

      {/* Submission Details Modal */}
      <Modal
        isOpen={showDetailsModal}
        onClose={() => setShowDetailsModal(false)}
        title="Submission Details"
        size="lg"
      >
        {selectedSubmission && (
          <div className="space-y-6">
            {/* Status and metadata */}
            <div className="flex items-center justify-between">
              <Badge className={statusColors[selectedSubmission.status]}>
                {selectedSubmission.status}
              </Badge>
              <span className="text-sm text-gray-500">
                {formatDate(selectedSubmission.createdAt)}
              </span>
            </div>

            {/* Collected Data */}
            <div>
              <h4 className="text-sm font-medium text-gray-700 mb-2">
                Collected Data
              </h4>
              <div className="bg-gray-50 rounded-lg p-4">
                <dl className="space-y-2">
                  {Object.entries(selectedSubmission.data).map(([key, value]) => (
                    <div key={key} className="flex">
                      <dt className="w-1/3 text-sm font-medium text-gray-500">
                        {key}
                      </dt>
                      <dd className="w-2/3 text-sm text-gray-900">
                        {typeof value === 'object'
                          ? JSON.stringify(value)
                          : String(value)}
                      </dd>
                    </div>
                  ))}
                </dl>
              </div>
            </div>

            {/* Metadata */}
            <div>
              <h4 className="text-sm font-medium text-gray-700 mb-2">Metadata</h4>
              <div className="bg-gray-50 rounded-lg p-4">
                <dl className="space-y-2">
                  {selectedSubmission.metadata.ipAddress && (
                    <div className="flex">
                      <dt className="w-1/3 text-sm font-medium text-gray-500">
                        IP Address
                      </dt>
                      <dd className="w-2/3 text-sm text-gray-900">
                        {selectedSubmission.metadata.ipAddress}
                      </dd>
                    </div>
                  )}
                  {selectedSubmission.metadata.country && (
                    <div className="flex">
                      <dt className="w-1/3 text-sm font-medium text-gray-500">
                        Country
                      </dt>
                      <dd className="w-2/3 text-sm text-gray-900">
                        {selectedSubmission.metadata.country}
                      </dd>
                    </div>
                  )}
                  {selectedSubmission.metadata.locale && (
                    <div className="flex">
                      <dt className="w-1/3 text-sm font-medium text-gray-500">
                        Locale
                      </dt>
                      <dd className="w-2/3 text-sm text-gray-900">
                        {selectedSubmission.metadata.locale}
                      </dd>
                    </div>
                  )}
                  {selectedSubmission.metadata.referrer && (
                    <div className="flex">
                      <dt className="w-1/3 text-sm font-medium text-gray-500">
                        Referrer
                      </dt>
                      <dd className="w-2/3 text-sm text-gray-900 truncate">
                        {selectedSubmission.metadata.referrer}
                      </dd>
                    </div>
                  )}
                  {selectedSubmission.metadata.userAgent && (
                    <div className="flex">
                      <dt className="w-1/3 text-sm font-medium text-gray-500">
                        User Agent
                      </dt>
                      <dd className="w-2/3 text-sm text-gray-900 truncate">
                        {selectedSubmission.metadata.userAgent}
                      </dd>
                    </div>
                  )}
                </dl>
              </div>
            </div>

            {/* Notes */}
            {selectedSubmission.notes && (
              <div>
                <h4 className="text-sm font-medium text-gray-700 mb-2">Notes</h4>
                <p className="text-sm text-gray-600 bg-gray-50 rounded-lg p-4">
                  {selectedSubmission.notes}
                </p>
              </div>
            )}

            {/* Review info */}
            {selectedSubmission.reviewedAt && (
              <div className="text-sm text-gray-500 border-t pt-4">
                Reviewed by {selectedSubmission.reviewedBy || 'Unknown'} on{' '}
                {formatDate(selectedSubmission.reviewedAt)}
              </div>
            )}

            {/* Actions */}
            <div className="flex justify-between pt-4 border-t">
              <div className="flex space-x-2">
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => {
                    handleStatusChange(selectedSubmission, 'Approved');
                    setShowDetailsModal(false);
                  }}
                >
                  <CheckIcon className="h-4 w-4 mr-1" />
                  Approve
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => {
                    handleStatusChange(selectedSubmission, 'Rejected');
                    setShowDetailsModal(false);
                  }}
                >
                  <XMarkIcon className="h-4 w-4 mr-1" />
                  Reject
                </Button>
              </div>
              <Button
                variant="secondary"
                onClick={() => setShowDetailsModal(false)}
              >
                Close
              </Button>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
}
