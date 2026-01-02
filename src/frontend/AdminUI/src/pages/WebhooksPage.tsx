import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardContent } from '../components/common/Card';
import { Table } from '../components/common/Table';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import Input from '../components/common/Input';
import { Badge } from '../components/common/Badge';
import { webhookService } from '../services/webhookService';
import type {
  WebhookEndpoint,
  CreateWebhookEndpointRequest,
  WebhookEventInfo,
  WebhookDelivery,
} from '../types/webhook';
import {
  PlusIcon,
  PencilIcon,
  TrashIcon,
  ArrowPathIcon,
  BeakerIcon,
  ClipboardDocumentIcon,
  KeyIcon,
  CheckCircleIcon,
  EyeIcon,
} from '@heroicons/react/24/outline';

interface EndpointFormData {
  name: string;
  description: string;
  url: string;
  enabled: boolean;
  eventTypes: string[];
}

const defaultFormData: EndpointFormData = {
  name: '',
  description: '',
  url: '',
  enabled: true,
  eventTypes: [],
};

export default function WebhooksPage() {
  const queryClient = useQueryClient();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [isSecretModalOpen, setIsSecretModalOpen] = useState(false);
  const [isDeliveriesModalOpen, setIsDeliveriesModalOpen] = useState(false);
  const [selectedEndpoint, setSelectedEndpoint] = useState<WebhookEndpoint | null>(null);
  const [newSecret, setNewSecret] = useState<string>('');
  const [formData, setFormData] = useState<EndpointFormData>(defaultFormData);
  const [secretCopied, setSecretCopied] = useState(false);

  const { data: endpoints, isLoading } = useQuery({
    queryKey: ['webhook-endpoints'],
    queryFn: webhookService.getEndpoints,
  });

  const { data: eventsGrouped } = useQuery({
    queryKey: ['webhook-events-grouped'],
    queryFn: webhookService.getEventsGrouped,
  });

  const { data: deliveries, isLoading: deliveriesLoading } = useQuery({
    queryKey: ['webhook-deliveries', selectedEndpoint?.id],
    queryFn: () => selectedEndpoint ? webhookService.getDeliveries(selectedEndpoint.id) : Promise.resolve([]),
    enabled: isDeliveriesModalOpen && !!selectedEndpoint,
  });

  const createMutation = useMutation({
    mutationFn: (data: CreateWebhookEndpointRequest) => webhookService.createEndpoint(data),
    onSuccess: (endpoint) => {
      queryClient.invalidateQueries({ queryKey: ['webhook-endpoints'] });
      if (endpoint.secret) {
        setNewSecret(endpoint.secret);
        setIsSecretModalOpen(true);
      }
      closeModal();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ endpointId, data }: { endpointId: string; data: CreateWebhookEndpointRequest }) =>
      webhookService.updateEndpoint(endpointId, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhook-endpoints'] });
      closeModal();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (endpointId: string) => webhookService.deleteEndpoint(endpointId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhook-endpoints'] });
      setIsDeleteModalOpen(false);
      setSelectedEndpoint(null);
    },
  });

  const rotateSecretMutation = useMutation({
    mutationFn: (endpointId: string) => webhookService.rotateSecret(endpointId),
    onSuccess: (response) => {
      setNewSecret(response.secret);
      setIsSecretModalOpen(true);
    },
  });

  const testMutation = useMutation({
    mutationFn: (endpointId: string) => webhookService.testEndpoint(endpointId),
    onSuccess: (response) => {
      if (response.success) {
        queryClient.invalidateQueries({ queryKey: ['webhook-endpoints'] });
      }
    },
  });

  const retryMutation = useMutation({
    mutationFn: (deliveryId: string) => webhookService.retryDelivery(deliveryId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['webhook-deliveries'] });
      queryClient.invalidateQueries({ queryKey: ['webhook-endpoints'] });
    },
  });

  const closeModal = () => {
    setIsModalOpen(false);
    setSelectedEndpoint(null);
    setFormData(defaultFormData);
  };

  const openCreateModal = () => {
    setSelectedEndpoint(null);
    setFormData(defaultFormData);
    setIsModalOpen(true);
  };

  const openEditModal = (endpoint: WebhookEndpoint) => {
    setSelectedEndpoint(endpoint);
    setFormData({
      name: endpoint.name,
      description: endpoint.description || '',
      url: endpoint.url,
      enabled: endpoint.enabled,
      eventTypes: endpoint.eventTypes,
    });
    setIsModalOpen(true);
  };

  const openDeleteModal = (endpoint: WebhookEndpoint) => {
    setSelectedEndpoint(endpoint);
    setIsDeleteModalOpen(true);
  };

  const openDeliveriesModal = (endpoint: WebhookEndpoint) => {
    setSelectedEndpoint(endpoint);
    setIsDeliveriesModalOpen(true);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    const data: CreateWebhookEndpointRequest = {
      name: formData.name,
      description: formData.description || undefined,
      url: formData.url,
      enabled: formData.enabled,
      eventTypes: formData.eventTypes,
    };

    if (selectedEndpoint) {
      updateMutation.mutate({ endpointId: selectedEndpoint.id, data });
    } else {
      createMutation.mutate(data);
    }
  };

  const toggleEventType = (eventType: string) => {
    setFormData(prev => ({
      ...prev,
      eventTypes: prev.eventTypes.includes(eventType)
        ? prev.eventTypes.filter(e => e !== eventType)
        : [...prev.eventTypes, eventType],
    }));
  };

  const copySecret = async () => {
    await navigator.clipboard.writeText(newSecret);
    setSecretCopied(true);
    setTimeout(() => setSecretCopied(false), 2000);
  };

  const getStatusBadge = (status: number) => {
    const variants: Record<number, 'success' | 'warning' | 'error' | 'default'> = {
      0: 'warning',
      1: 'success',
      2: 'error',
      3: 'default',
      4: 'default',
    };
    const labels: Record<number, string> = {
      0: 'Pending',
      1: 'Success',
      2: 'Failed',
      3: 'Exhausted',
      4: 'Cancelled',
    };
    return <Badge variant={variants[status] || 'default'}>{labels[status] || 'Unknown'}</Badge>;
  };

  const columns = [
    { key: 'name', header: 'Name' },
    {
      key: 'url',
      header: 'URL',
      render: (endpoint: WebhookEndpoint) => (
        <span className="font-mono text-xs truncate max-w-[300px] block">{endpoint.url}</span>
      ),
    },
    {
      key: 'enabled',
      header: 'Status',
      render: (endpoint: WebhookEndpoint) => (
        <Badge variant={endpoint.enabled ? 'success' : 'default'}>
          {endpoint.enabled ? 'Active' : 'Disabled'}
        </Badge>
      ),
    },
    {
      key: 'eventTypes',
      header: 'Events',
      render: (endpoint: WebhookEndpoint) => (
        <span className="text-sm text-gray-500">{endpoint.eventTypes.length} events</span>
      ),
    },
    {
      key: 'stats',
      header: 'Deliveries',
      render: (endpoint: WebhookEndpoint) => (
        <div className="flex items-center gap-2 text-xs">
          <span className="text-green-600">{endpoint.stats?.successfulDeliveries || 0}</span>
          <span className="text-gray-400">/</span>
          <span className="text-red-600">{endpoint.stats?.failedDeliveries || 0}</span>
        </div>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (endpoint: WebhookEndpoint) => (
        <div className="flex gap-1">
          <Button
            variant="ghost"
            size="sm"
            onClick={() => openDeliveriesModal(endpoint)}
            title="View Deliveries"
          >
            <EyeIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => testMutation.mutate(endpoint.id)}
            disabled={testMutation.isPending}
            title="Test Webhook"
          >
            <BeakerIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => rotateSecretMutation.mutate(endpoint.id)}
            disabled={rotateSecretMutation.isPending}
            title="Rotate Secret"
          >
            <KeyIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => openEditModal(endpoint)}
            title="Edit"
          >
            <PencilIcon className="h-4 w-4" />
          </Button>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => openDeleteModal(endpoint)}
            title="Delete"
          >
            <TrashIcon className="h-4 w-4 text-red-500" />
          </Button>
        </div>
      ),
    },
  ];

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Webhooks</h1>
          <p className="text-sm text-gray-500 mt-1">
            Configure webhook endpoints to receive real-time events from your identity server
          </p>
        </div>
        <Button onClick={openCreateModal}>
          <PlusIcon className="h-5 w-5 mr-2" />
          Add Endpoint
        </Button>
      </div>

      <Card>
        <CardContent>
          {isLoading ? (
            <div className="flex justify-center py-8">
              <ArrowPathIcon className="h-8 w-8 animate-spin text-gray-400" />
            </div>
          ) : endpoints && endpoints.length > 0 ? (
            <Table data={endpoints} columns={columns} keyExtractor={(e) => e.id} />
          ) : (
            <div className="text-center py-12">
              <p className="text-gray-500">No webhook endpoints configured</p>
              <p className="text-sm text-gray-400 mt-1">
                Create an endpoint to start receiving events
              </p>
            </div>
          )}
        </CardContent>
      </Card>

      {/* Create/Edit Modal */}
      <Modal
        isOpen={isModalOpen}
        onClose={closeModal}
        title={selectedEndpoint ? 'Edit Webhook Endpoint' : 'Create Webhook Endpoint'}
      >
        <form onSubmit={handleSubmit} className="space-y-4">
          <Input
            label="Name"
            value={formData.name}
            onChange={e => setFormData({ ...formData, name: e.target.value })}
            placeholder="My Webhook"
            required
          />
          <Input
            label="Description"
            value={formData.description}
            onChange={e => setFormData({ ...formData, description: e.target.value })}
            placeholder="Optional description"
          />
          <Input
            label="Endpoint URL"
            type="url"
            value={formData.url}
            onChange={e => setFormData({ ...formData, url: e.target.value })}
            placeholder="https://api.example.com/webhooks"
            required
          />

          <div className="flex items-center gap-2">
            <input
              type="checkbox"
              id="enabled"
              checked={formData.enabled}
              onChange={e => setFormData({ ...formData, enabled: e.target.checked })}
              className="rounded"
            />
            <label htmlFor="enabled" className="text-sm text-gray-700">
              Endpoint is active
            </label>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 mb-2">
              Events to Subscribe
            </label>
            <div className="max-h-64 overflow-y-auto border rounded-md p-2 space-y-4">
              {eventsGrouped && Object.entries(eventsGrouped).map(([category, events]) => (
                <div key={category}>
                  <h4 className="text-xs font-semibold text-gray-500 uppercase mb-2">{category}</h4>
                  <div className="space-y-1">
                    {events.map((event: WebhookEventInfo) => (
                      <label key={event.eventType} className="flex items-center gap-2 cursor-pointer hover:bg-gray-50 p-1 rounded">
                        <input
                          type="checkbox"
                          checked={formData.eventTypes.includes(event.eventType)}
                          onChange={() => toggleEventType(event.eventType)}
                          className="rounded"
                        />
                        <span className="text-sm">{event.displayName}</span>
                        {event.description && (
                          <span className="text-xs text-gray-400">- {event.description}</span>
                        )}
                      </label>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="flex justify-end gap-2 pt-4">
            <Button type="button" variant="secondary" onClick={closeModal}>
              Cancel
            </Button>
            <Button type="submit" disabled={createMutation.isPending || updateMutation.isPending}>
              {selectedEndpoint ? 'Update' : 'Create'}
            </Button>
          </div>
        </form>
      </Modal>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={isDeleteModalOpen}
        onClose={() => setIsDeleteModalOpen(false)}
        title="Delete Webhook Endpoint"
      >
        <p className="text-gray-600 mb-4">
          Are you sure you want to delete the endpoint "{selectedEndpoint?.name}"?
          This action cannot be undone.
        </p>
        <div className="flex justify-end gap-2">
          <Button variant="secondary" onClick={() => setIsDeleteModalOpen(false)}>
            Cancel
          </Button>
          <Button
            variant="danger"
            onClick={() => selectedEndpoint && deleteMutation.mutate(selectedEndpoint.id)}
            disabled={deleteMutation.isPending}
          >
            Delete
          </Button>
        </div>
      </Modal>

      {/* Secret Display Modal */}
      <Modal
        isOpen={isSecretModalOpen}
        onClose={() => {
          setIsSecretModalOpen(false);
          setNewSecret('');
        }}
        title="Webhook Secret"
      >
        <div className="space-y-4">
          <p className="text-amber-600 text-sm">
            This is the only time you'll see this secret. Please copy it now.
          </p>
          <div className="flex items-center gap-2">
            <code className="flex-1 bg-gray-100 px-3 py-2 rounded font-mono text-sm break-all">
              {newSecret}
            </code>
            <Button variant="secondary" onClick={copySecret}>
              {secretCopied ? (
                <CheckCircleIcon className="h-5 w-5 text-green-500" />
              ) : (
                <ClipboardDocumentIcon className="h-5 w-5" />
              )}
            </Button>
          </div>
          <p className="text-xs text-gray-500">
            Use this secret to verify webhook signatures. The signature is sent in the
            X-Webhook-Signature header as: sha256=&lt;hex-signature&gt;
          </p>
          <div className="flex justify-end">
            <Button onClick={() => {
              setIsSecretModalOpen(false);
              setNewSecret('');
            }}>
              Done
            </Button>
          </div>
        </div>
      </Modal>

      {/* Deliveries Modal */}
      <Modal
        isOpen={isDeliveriesModalOpen}
        onClose={() => {
          setIsDeliveriesModalOpen(false);
          setSelectedEndpoint(null);
        }}
        title={`Delivery History - ${selectedEndpoint?.name}`}
        size="xl"
      >
        <div className="space-y-4">
          {deliveriesLoading ? (
            <div className="flex justify-center py-8">
              <ArrowPathIcon className="h-8 w-8 animate-spin text-gray-400" />
            </div>
          ) : deliveries && deliveries.length > 0 ? (
            <div className="max-h-96 overflow-y-auto">
              <table className="min-w-full divide-y divide-gray-200">
                <thead className="bg-gray-50">
                  <tr>
                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Event</th>
                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">HTTP</th>
                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Time</th>
                    <th className="px-4 py-2 text-left text-xs font-medium text-gray-500 uppercase">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-200">
                  {deliveries.map((delivery: WebhookDelivery) => (
                    <tr key={delivery.id}>
                      <td className="px-4 py-2 text-sm font-mono">{delivery.eventType}</td>
                      <td className="px-4 py-2">{getStatusBadge(delivery.status as unknown as number)}</td>
                      <td className="px-4 py-2 text-sm">{delivery.httpStatus || '-'}</td>
                      <td className="px-4 py-2 text-sm text-gray-500">
                        {new Date(delivery.createdAt).toLocaleString()}
                      </td>
                      <td className="px-4 py-2">
                        {(delivery.status === 'Failed'|| delivery.status === 'Exhausted') && (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => retryMutation.mutate(delivery.id)}
                            disabled={retryMutation.isPending}
                            title="Retry"
                          >
                            <ArrowPathIcon className="h-4 w-4" />
                          </Button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : (
            <p className="text-center text-gray-500 py-8">No deliveries yet</p>
          )}
          <div className="flex justify-end">
            <Button onClick={() => {
              setIsDeliveriesModalOpen(false);
              setSelectedEndpoint(null);
            }}>
              Close
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
