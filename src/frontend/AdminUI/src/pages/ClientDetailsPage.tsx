import { useState } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Card, CardHeader, CardContent } from '../components/common/Card';
import Button from '../components/common/Button';
import Modal from '../components/common/Modal';
import { Tabs, TabList, Tab, TabPanels, TabPanel } from '../components/common/Tabs';
import { clientService } from '../services/clientService';
import { apiScopeService } from '../services/resourceService';
import { roleService, userService } from '../services/userService';
import type {
  CreateClientRequest,
  UpdateClientRequest,
  AllowedUser,
  ClientClaim,
  ClientProperty,
  TokenUsage,
  TokenExpiration,
  AccessTokenType,
} from '../types/client';
import {
  ArrowLeftIcon,
  PlusIcon,
  XMarkIcon,
  PencilIcon,
  Cog6ToothIcon,
  ShieldCheckIcon,
  KeyIcon,
  LinkIcon,
  UserGroupIcon,
  ArrowRightOnRectangleIcon,
  ClipboardDocumentListIcon,
  AdjustmentsHorizontalIcon,
} from '@heroicons/react/24/outline';

const COMMON_GRANT_TYPES = [
  { value: 'authorization_code', label: 'Authorization Code' },
  { value: 'client_credentials', label: 'Client Credentials' },
  { value: 'refresh_token', label: 'Refresh Token' },
  { value: 'implicit', label: 'Implicit (legacy)' },
  { value: 'password', label: 'Resource Owner Password (legacy)' },
  { value: 'urn:ietf:params:oauth:grant-type:device_code', label: 'Device Code' },
  { value: 'urn:openid:params:grant-type:ciba', label: 'CIBA (Backchannel Authentication)' },
];

const CIBA_TOKEN_DELIVERY_MODES = [
  { value: 'poll', label: 'Poll' },
  { value: 'ping', label: 'Ping' },
  { value: 'push', label: 'Push' },
];

const IDENTITY_SCOPES = ['openid', 'profile', 'email', 'address', 'phone', 'offline_access'];

// Helper to format seconds to human-readable
const formatDuration = (seconds: number): string => {
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`;
  return `${Math.floor(seconds / 86400)}d`;
};

// Full edit state interface with all properties
interface EditState {
  // Basic
  clientName: string;
  description: string;
  clientUri: string;
  logoUri: string;
  enabled: boolean;

  // Authentication
  requireClientSecret: boolean;
  requirePkce: boolean;
  allowPlainTextPkce: boolean;
  requireRequestObject: boolean;
  requireDPoP: boolean;
  requirePushedAuthorization: boolean;
  pushedAuthorizationLifetime: number;

  // Consent
  requireConsent: boolean;
  allowRememberConsent: boolean;
  consentLifetime: number | null;

  // Tokens
  allowOfflineAccess: boolean;
  allowAccessTokensViaBrowser: boolean;
  alwaysIncludeUserClaimsInIdToken: boolean;
  accessTokenLifetime: number;
  identityTokenLifetime: number;
  authorizationCodeLifetime: number;
  absoluteRefreshTokenLifetime: number;
  slidingRefreshTokenLifetime: number;
  refreshTokenUsage: TokenUsage;
  refreshTokenExpiration: TokenExpiration;
  updateAccessTokenClaimsOnRefresh: boolean;
  accessTokenType: AccessTokenType;
  allowedIdentityTokenSigningAlgorithms: string;
  includeJwtId: boolean;

  // Client claims
  alwaysSendClientClaims: boolean;
  clientClaimsPrefix: string;
  pairWiseSubjectSalt: string;

  // Logout
  frontChannelLogoutUri: string;
  frontChannelLogoutSessionRequired: boolean;
  backChannelLogoutUri: string;
  backChannelLogoutSessionRequired: boolean;

  // SSO & Device
  enableLocalLogin: boolean;
  userSsoLifetime: number | null;
  userCodeType: string;
  deviceCodeLifetime: number;

  // CIBA
  cibaEnabled: boolean;
  cibaTokenDeliveryMode: string;
  cibaClientNotificationEndpoint: string;
  cibaRequestLifetime: number;
  cibaPollingInterval: number;
  cibaRequireUserCode: boolean;

  // UI Flow
  useJourneyFlow: boolean | null;

  // Collections
  allowedGrantTypes: string[];
  redirectUris: string[];
  postLogoutRedirectUris: string[];
  allowedScopes: string[];
  allowedCorsOrigins: string[];
  claims: ClientClaim[];
  properties: ClientProperty[];
  identityProviderRestrictions: string[];
  allowedRoles: string[];
  allowedUsers: AllowedUser[];
}

export default function ClientDetailsPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [showDeleteModal, setShowDeleteModal] = useState(false);
  const [showSecretModal, setShowSecretModal] = useState(false);
  const [newSecret, setNewSecret] = useState('');

  const isNew = id === 'new';

  // Create form state for new clients
  const [formData, setFormData] = useState<CreateClientRequest>({
    clientId: '',
    clientName: '',
    description: '',
    allowedGrantTypes: ['authorization_code'],
    allowedScopes: ['openid', 'profile'],
    redirectUris: [],
    postLogoutRedirectUris: [],
    requireClientSecret: true,
    requirePkce: true,
    allowedRoles: [],
    allowedUsers: [],
  });

  // Input states for create form collections
  const [newRedirectUri, setNewRedirectUri] = useState('');
  const [newPostLogoutUri, setNewPostLogoutUri] = useState('');
  const [newCorsOrigin, setNewCorsOrigin] = useState('');
  const [newRole, setNewRole] = useState('');
  const [newUserSubjectId, setNewUserSubjectId] = useState('');
  const [newUserDisplayName, setNewUserDisplayName] = useState('');
  const [newIdpRestriction, setNewIdpRestriction] = useState('');

  // Edit mode state for existing clients
  const [isEditing, setIsEditing] = useState(false);
  const [editData, setEditData] = useState<EditState | null>(null);

  // Edit mode input states
  const [editNewRedirectUri, setEditNewRedirectUri] = useState('');
  const [editNewPostLogoutUri, setEditNewPostLogoutUri] = useState('');
  const [editNewCorsOrigin, setEditNewCorsOrigin] = useState('');
  const [editNewRole, setEditNewRole] = useState('');
  const [editNewUserSubjectId, setEditNewUserSubjectId] = useState('');
  const [editNewUserDisplayName, setEditNewUserDisplayName] = useState('');
  const [editNewIdpRestriction, setEditNewIdpRestriction] = useState('');
  const [editNewClaimType, setEditNewClaimType] = useState('');
  const [editNewClaimValue, setEditNewClaimValue] = useState('');
  const [editNewPropertyKey, setEditNewPropertyKey] = useState('');
  const [editNewPropertyValue, setEditNewPropertyValue] = useState('');

  const { data: client, isLoading } = useQuery({
    queryKey: ['client', id],
    queryFn: () => clientService.getByClientId(id!),
    enabled: !!id && !isNew,
  });

  // Fetch API scopes for scope selection
  const { data: apiScopesResult } = useQuery({
    queryKey: ['apiScopes'],
    queryFn: () => apiScopeService.getAll(1, 100),
  });
  const apiScopes = apiScopesResult?.items?.map(s => s.name) || [];

  // Fetch roles for role picker
  const { data: availableRoles = [] } = useQuery({
    queryKey: ['roles'],
    queryFn: () => roleService.getAll(),
  });

  // Fetch users for user picker
  const { data: usersResult } = useQuery({
    queryKey: ['users'],
    queryFn: () => userService.getAll({ pageSize: 100 }),
  });
  const availableUsers = usersResult?.items || [];

  const createMutation = useMutation({
    mutationFn: (data: CreateClientRequest) => clientService.create(data),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ['clients'] });
      navigate(`/clients/${encodeURIComponent(created.clientId)}`);
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (clientId: string) => clientService.delete(clientId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['clients'] });
      navigate('/clients');
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ clientId, data }: { clientId: string; data: UpdateClientRequest }) =>
      clientService.update(clientId, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['client', id] });
      setIsEditing(false);
    },
  });

  const regenerateSecretMutation = useMutation({
    mutationFn: (clientId: string) => clientService.regenerateSecret(clientId),
    onSuccess: (result) => {
      setNewSecret(result.clientSecret);
      setShowSecretModal(true);
      queryClient.invalidateQueries({ queryKey: ['client', id] });
    },
  });

  // Initialize edit state from client data
  const startEditing = () => {
    if (!client) return;
    setEditData({
      clientName: client.clientName || '',
      description: client.description || '',
      clientUri: client.clientUri || '',
      logoUri: client.logoUri || '',
      enabled: client.enabled,

      requireClientSecret: client.requireClientSecret,
      requirePkce: client.requirePkce,
      allowPlainTextPkce: client.allowPlainTextPkce,
      requireRequestObject: client.requireRequestObject,
      requireDPoP: client.requireDPoP,
      requirePushedAuthorization: client.requirePushedAuthorization,
      pushedAuthorizationLifetime: client.pushedAuthorizationLifetime,

      requireConsent: client.requireConsent,
      allowRememberConsent: client.allowRememberConsent,
      consentLifetime: client.consentLifetime ?? null,

      allowOfflineAccess: client.allowOfflineAccess,
      allowAccessTokensViaBrowser: client.allowAccessTokensViaBrowser,
      alwaysIncludeUserClaimsInIdToken: client.alwaysIncludeUserClaimsInIdToken,
      accessTokenLifetime: client.accessTokenLifetime,
      identityTokenLifetime: client.identityTokenLifetime,
      authorizationCodeLifetime: client.authorizationCodeLifetime,
      absoluteRefreshTokenLifetime: client.absoluteRefreshTokenLifetime,
      slidingRefreshTokenLifetime: client.slidingRefreshTokenLifetime,
      refreshTokenUsage: client.refreshTokenUsage,
      refreshTokenExpiration: client.refreshTokenExpiration,
      updateAccessTokenClaimsOnRefresh: client.updateAccessTokenClaimsOnRefresh,
      accessTokenType: client.accessTokenType,
      allowedIdentityTokenSigningAlgorithms: client.allowedIdentityTokenSigningAlgorithms || '',
      includeJwtId: client.includeJwtId,

      alwaysSendClientClaims: client.alwaysSendClientClaims,
      clientClaimsPrefix: client.clientClaimsPrefix,
      pairWiseSubjectSalt: client.pairWiseSubjectSalt || '',

      frontChannelLogoutUri: client.frontChannelLogoutUri || '',
      frontChannelLogoutSessionRequired: client.frontChannelLogoutSessionRequired,
      backChannelLogoutUri: client.backChannelLogoutUri || '',
      backChannelLogoutSessionRequired: client.backChannelLogoutSessionRequired,

      enableLocalLogin: client.enableLocalLogin,
      userSsoLifetime: client.userSsoLifetime ?? null,
      userCodeType: client.userCodeType || '',
      deviceCodeLifetime: client.deviceCodeLifetime,

      cibaEnabled: client.cibaEnabled,
      cibaTokenDeliveryMode: client.cibaTokenDeliveryMode || 'poll',
      cibaClientNotificationEndpoint: client.cibaClientNotificationEndpoint || '',
      cibaRequestLifetime: client.cibaRequestLifetime,
      cibaPollingInterval: client.cibaPollingInterval,
      cibaRequireUserCode: client.cibaRequireUserCode,

      useJourneyFlow: client.useJourneyFlow ?? null,

      allowedGrantTypes: [...client.allowedGrantTypes],
      redirectUris: [...client.redirectUris],
      postLogoutRedirectUris: [...client.postLogoutRedirectUris],
      allowedScopes: [...client.allowedScopes],
      allowedCorsOrigins: [...client.allowedCorsOrigins],
      claims: [...client.claims],
      properties: [...client.properties],
      identityProviderRestrictions: [...client.identityProviderRestrictions],
      allowedRoles: [...client.allowedRoles],
      allowedUsers: [...client.allowedUsers],
    });
    setIsEditing(true);
  };

  const cancelEditing = () => {
    setIsEditing(false);
    setEditData(null);
  };

  const saveChanges = () => {
    if (!client || !editData) return;
    updateMutation.mutate({
      clientId: client.clientId,
      data: {
        clientName: editData.clientName,
        description: editData.description,
        clientUri: editData.clientUri || undefined,
        logoUri: editData.logoUri || undefined,
        enabled: editData.enabled,

        requireClientSecret: editData.requireClientSecret,
        requirePkce: editData.requirePkce,
        allowPlainTextPkce: editData.allowPlainTextPkce,
        requireRequestObject: editData.requireRequestObject,
        requireDPoP: editData.requireDPoP,
        requirePushedAuthorization: editData.requirePushedAuthorization,
        pushedAuthorizationLifetime: editData.pushedAuthorizationLifetime,

        requireConsent: editData.requireConsent,
        allowRememberConsent: editData.allowRememberConsent,
        consentLifetime: editData.consentLifetime ?? undefined,

        allowOfflineAccess: editData.allowOfflineAccess,
        allowAccessTokensViaBrowser: editData.allowAccessTokensViaBrowser,
        alwaysIncludeUserClaimsInIdToken: editData.alwaysIncludeUserClaimsInIdToken,
        accessTokenLifetime: editData.accessTokenLifetime,
        identityTokenLifetime: editData.identityTokenLifetime,
        authorizationCodeLifetime: editData.authorizationCodeLifetime,
        absoluteRefreshTokenLifetime: editData.absoluteRefreshTokenLifetime,
        slidingRefreshTokenLifetime: editData.slidingRefreshTokenLifetime,
        refreshTokenUsage: editData.refreshTokenUsage,
        refreshTokenExpiration: editData.refreshTokenExpiration,
        updateAccessTokenClaimsOnRefresh: editData.updateAccessTokenClaimsOnRefresh,
        accessTokenType: editData.accessTokenType,
        allowedIdentityTokenSigningAlgorithms: editData.allowedIdentityTokenSigningAlgorithms || undefined,
        includeJwtId: editData.includeJwtId,

        alwaysSendClientClaims: editData.alwaysSendClientClaims,
        clientClaimsPrefix: editData.clientClaimsPrefix,
        pairWiseSubjectSalt: editData.pairWiseSubjectSalt || undefined,

        frontChannelLogoutUri: editData.frontChannelLogoutUri || undefined,
        frontChannelLogoutSessionRequired: editData.frontChannelLogoutSessionRequired,
        backChannelLogoutUri: editData.backChannelLogoutUri || undefined,
        backChannelLogoutSessionRequired: editData.backChannelLogoutSessionRequired,

        enableLocalLogin: editData.enableLocalLogin,
        userSsoLifetime: editData.userSsoLifetime ?? undefined,
        userCodeType: editData.userCodeType || undefined,
        deviceCodeLifetime: editData.deviceCodeLifetime,

        cibaEnabled: editData.cibaEnabled,
        cibaTokenDeliveryMode: editData.cibaTokenDeliveryMode,
        cibaClientNotificationEndpoint: editData.cibaClientNotificationEndpoint || undefined,
        cibaRequestLifetime: editData.cibaRequestLifetime,
        cibaPollingInterval: editData.cibaPollingInterval,
        cibaRequireUserCode: editData.cibaRequireUserCode,

        useJourneyFlow: editData.useJourneyFlow,

        allowedGrantTypes: editData.allowedGrantTypes,
        redirectUris: editData.redirectUris,
        postLogoutRedirectUris: editData.postLogoutRedirectUris,
        allowedScopes: editData.allowedScopes,
        allowedCorsOrigins: editData.allowedCorsOrigins,
        claims: editData.claims,
        properties: editData.properties,
        identityProviderRestrictions: editData.identityProviderRestrictions,
        allowedRoles: editData.allowedRoles,
        allowedUsers: editData.allowedUsers,
      },
    });
  };

  const handleCreate = (e: React.FormEvent) => {
    e.preventDefault();
    if (formData.clientId) {
      createMutation.mutate(formData);
    }
  };

  const handleDelete = () => {
    if (client) {
      deleteMutation.mutate(client.clientId);
    }
  };

  // Collection helpers for create form
  const addToCollection = (
    key: keyof CreateClientRequest,
    value: string,
    clearFn: () => void
  ) => {
    if (!value) return;
    const current = (formData[key] as string[]) || [];
    if (!current.includes(value)) {
      setFormData({ ...formData, [key]: [...current, value] });
      clearFn();
    }
  };

  const removeFromCollection = (key: keyof CreateClientRequest, value: string) => {
    const current = (formData[key] as string[]) || [];
    setFormData({ ...formData, [key]: current.filter(v => v !== value) });
  };

  // Collection helpers for edit form
  const addToEditCollection = (
    key: keyof EditState,
    value: string,
    clearFn: () => void
  ) => {
    if (!value || !editData) return;
    const current = (editData[key] as string[]) || [];
    if (!current.includes(value)) {
      setEditData({ ...editData, [key]: [...current, value] });
      clearFn();
    }
  };

  const removeFromEditCollection = (key: keyof EditState, value: string) => {
    if (!editData) return;
    const current = (editData[key] as string[]) || [];
    setEditData({ ...editData, [key]: current.filter(v => v !== value) });
  };

  // Toggle helpers
  const toggleGrantType = (grantType: string) => {
    setFormData(prev => ({
      ...prev,
      allowedGrantTypes: prev.allowedGrantTypes?.includes(grantType)
        ? prev.allowedGrantTypes.filter(g => g !== grantType)
        : [...(prev.allowedGrantTypes || []), grantType],
    }));
  };

  const toggleScope = (scope: string) => {
    setFormData(prev => ({
      ...prev,
      allowedScopes: prev.allowedScopes?.includes(scope)
        ? prev.allowedScopes.filter(s => s !== scope)
        : [...(prev.allowedScopes || []), scope],
    }));
  };

  const toggleEditGrantType = (grantType: string) => {
    if (!editData) return;
    setEditData({
      ...editData,
      allowedGrantTypes: editData.allowedGrantTypes.includes(grantType)
        ? editData.allowedGrantTypes.filter(g => g !== grantType)
        : [...editData.allowedGrantTypes, grantType],
    });
  };

  const toggleEditScope = (scope: string) => {
    if (!editData) return;
    setEditData({
      ...editData,
      allowedScopes: editData.allowedScopes.includes(scope)
        ? editData.allowedScopes.filter(s => s !== scope)
        : [...editData.allowedScopes, scope],
    });
  };

  // User helpers
  const addUser = () => {
    if (!newUserSubjectId) return;
    if (!formData.allowedUsers?.some(u => u.subjectId === newUserSubjectId)) {
      setFormData(prev => ({
        ...prev,
        allowedUsers: [
          ...(prev.allowedUsers || []),
          { subjectId: newUserSubjectId, displayName: newUserDisplayName || undefined },
        ],
      }));
      setNewUserSubjectId('');
      setNewUserDisplayName('');
    }
  };

  const removeUser = (subjectId: string) => {
    setFormData(prev => ({
      ...prev,
      allowedUsers: prev.allowedUsers?.filter(u => u.subjectId !== subjectId) || [],
    }));
  };

  const addEditUser = () => {
    if (!editNewUserSubjectId || !editData) return;
    if (!editData.allowedUsers.some(u => u.subjectId === editNewUserSubjectId)) {
      setEditData({
        ...editData,
        allowedUsers: [
          ...editData.allowedUsers,
          { subjectId: editNewUserSubjectId, displayName: editNewUserDisplayName || undefined },
        ],
      });
      setEditNewUserSubjectId('');
      setEditNewUserDisplayName('');
    }
  };

  const removeEditUser = (subjectId: string) => {
    if (!editData) return;
    setEditData({
      ...editData,
      allowedUsers: editData.allowedUsers.filter(u => u.subjectId !== subjectId),
    });
  };

  // Claim helpers for edit mode
  const addEditClaim = () => {
    if (!editNewClaimType || !editNewClaimValue || !editData) return;
    setEditData({
      ...editData,
      claims: [...editData.claims, { type: editNewClaimType, value: editNewClaimValue }],
    });
    setEditNewClaimType('');
    setEditNewClaimValue('');
  };

  const removeEditClaim = (index: number) => {
    if (!editData) return;
    setEditData({
      ...editData,
      claims: editData.claims.filter((_, i) => i !== index),
    });
  };

  // Property helpers for edit mode
  const addEditProperty = () => {
    if (!editNewPropertyKey || !editNewPropertyValue || !editData) return;
    setEditData({
      ...editData,
      properties: [...editData.properties, { key: editNewPropertyKey, value: editNewPropertyValue }],
    });
    setEditNewPropertyKey('');
    setEditNewPropertyValue('');
  };

  const removeEditProperty = (index: number) => {
    if (!editData) return;
    setEditData({
      ...editData,
      properties: editData.properties.filter((_, i) => i !== index),
    });
  };

  // ===============================================
  // CREATE NEW CLIENT UI
  // ===============================================
  if (isNew) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/clients')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <h1 className="text-2xl font-bold text-gray-900">Create New Client</h1>
        </div>

        <form onSubmit={handleCreate}>
          <Tabs defaultTab="basic">
            <TabList>
              <Tab id="basic" icon={<Cog6ToothIcon className="h-4 w-4" />}>Basic</Tab>
              <Tab id="auth" icon={<ShieldCheckIcon className="h-4 w-4" />}>Authentication</Tab>
              <Tab id="uris" icon={<LinkIcon className="h-4 w-4" />}>URIs & CORS</Tab>
              <Tab id="scopes" icon={<KeyIcon className="h-4 w-4" />}>Scopes</Tab>
              <Tab id="access" icon={<UserGroupIcon className="h-4 w-4" />}>Access Control</Tab>
            </TabList>

            <TabPanels>
              <TabPanel id="basic">
                <Card>
                  <CardHeader title="Basic Information" />
                  <CardContent className="space-y-4">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <label className="form-label">Client ID *</label>
                        <input
                          type="text"
                          value={formData.clientId}
                          onChange={(e) => setFormData({ ...formData, clientId: e.target.value })}
                          required
                          placeholder="e.g., my-web-app"
                        />
                        <p className="form-helper">Unique identifier for this client</p>
                      </div>
                      <div>
                        <label className="form-label">Client Name</label>
                        <input
                          type="text"
                          value={formData.clientName || ''}
                          onChange={(e) => setFormData({ ...formData, clientName: e.target.value })}
                          placeholder="e.g., My Web Application"
                        />
                      </div>
                    </div>
                    <div>
                      <label className="form-label">Description</label>
                      <textarea
                        value={formData.description || ''}
                        onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                        rows={2}
                      />
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <label className="form-label">Client URI</label>
                        <input
                          type="url"
                          value={formData.clientUri || ''}
                          onChange={(e) => setFormData({ ...formData, clientUri: e.target.value })}
                          placeholder="https://myapp.com"
                        />
                        <p className="form-helper">Homepage URL of the client</p>
                      </div>
                      <div>
                        <label className="form-label">Logo URI</label>
                        <input
                          type="url"
                          value={formData.logoUri || ''}
                          onChange={(e) => setFormData({ ...formData, logoUri: e.target.value })}
                          placeholder="https://myapp.com/logo.png"
                        />
                      </div>
                    </div>
                  </CardContent>
                </Card>
              </TabPanel>

              <TabPanel id="auth">
                <Card>
                  <CardHeader title="Grant Types & Security" />
                  <CardContent className="space-y-6">
                    <div>
                      <label className="form-label">Grant Types *</label>
                      <div className="grid grid-cols-2 md:grid-cols-3 gap-2 mt-2">
                        {COMMON_GRANT_TYPES.map((grant) => (
                          <label key={grant.value} className="flex items-center gap-2 p-2 border rounded hover:bg-gray-50">
                            <input
                              type="checkbox"
                              checked={formData.allowedGrantTypes?.includes(grant.value) || false}
                              onChange={() => toggleGrantType(grant.value)}
                            />
                            <span className="text-sm">{grant.label}</span>
                          </label>
                        ))}
                      </div>
                    </div>

                    <div className="grid grid-cols-2 md:grid-cols-4 gap-4 pt-4 border-t">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={formData.requirePkce ?? true}
                          onChange={(e) => setFormData({ ...formData, requirePkce: e.target.checked })}
                        />
                        <span className="text-sm">Require PKCE</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={formData.requireClientSecret ?? true}
                          onChange={(e) => setFormData({ ...formData, requireClientSecret: e.target.checked })}
                        />
                        <span className="text-sm">Require Secret</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={formData.requireDPoP ?? false}
                          onChange={(e) => setFormData({ ...formData, requireDPoP: e.target.checked })}
                        />
                        <span className="text-sm">Require DPoP</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={formData.requirePushedAuthorization ?? false}
                          onChange={(e) => setFormData({ ...formData, requirePushedAuthorization: e.target.checked })}
                        />
                        <span className="text-sm">Require PAR</span>
                      </label>
                    </div>
                  </CardContent>
                </Card>
              </TabPanel>

              <TabPanel id="uris">
                <div className="space-y-6">
                  <Card>
                    <CardHeader title="Redirect URIs" />
                    <CardContent className="space-y-4">
                      <div className="flex flex-wrap gap-2">
                        {formData.redirectUris?.map((uri) => (
                          <span
                            key={uri}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromCollection('redirectUris', uri)}
                          >
                            {uri} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="url"
                          value={newRedirectUri}
                          onChange={(e) => setNewRedirectUri(e.target.value)}
                          placeholder="https://myapp.com/callback"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToCollection('redirectUris', newRedirectUri, () => setNewRedirectUri('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToCollection('redirectUris', newRedirectUri, () => setNewRedirectUri(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader title="Post Logout Redirect URIs" />
                    <CardContent className="space-y-4">
                      <div className="flex flex-wrap gap-2">
                        {formData.postLogoutRedirectUris?.map((uri) => (
                          <span
                            key={uri}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromCollection('postLogoutRedirectUris', uri)}
                          >
                            {uri} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="url"
                          value={newPostLogoutUri}
                          onChange={(e) => setNewPostLogoutUri(e.target.value)}
                          placeholder="https://myapp.com/logout"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToCollection('postLogoutRedirectUris', newPostLogoutUri, () => setNewPostLogoutUri('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToCollection('postLogoutRedirectUris', newPostLogoutUri, () => setNewPostLogoutUri(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader title="CORS Origins" />
                    <CardContent className="space-y-4">
                      <p className="text-sm text-gray-600">Origins allowed to make cross-origin requests</p>
                      <div className="flex flex-wrap gap-2">
                        {formData.allowedCorsOrigins?.map((origin) => (
                          <span
                            key={origin}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromCollection('allowedCorsOrigins', origin)}
                          >
                            {origin} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="url"
                          value={newCorsOrigin}
                          onChange={(e) => setNewCorsOrigin(e.target.value)}
                          placeholder="https://myapp.com"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToCollection('allowedCorsOrigins', newCorsOrigin, () => setNewCorsOrigin('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToCollection('allowedCorsOrigins', newCorsOrigin, () => setNewCorsOrigin(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </CardContent>
                  </Card>
                </div>
              </TabPanel>

              <TabPanel id="scopes">
                <Card>
                  <CardHeader title="Allowed Scopes" />
                  <CardContent className="space-y-4">
                    <div>
                      <label className="text-xs text-gray-500 mb-2 block">Identity Scopes</label>
                      <div className="flex flex-wrap gap-2">
                        {IDENTITY_SCOPES.map((scope) => (
                          <button
                            key={scope}
                            type="button"
                            onClick={() => toggleScope(scope)}
                            className={`badge cursor-pointer ${
                              formData.allowedScopes?.includes(scope) ? 'badge-primary' : 'badge-gray'
                            }`}
                          >
                            {scope}
                          </button>
                        ))}
                      </div>
                    </div>
                    {apiScopes.length > 0 && (
                      <div className="pt-4 border-t">
                        <label className="text-xs text-gray-500 mb-2 block">API Scopes</label>
                        <div className="flex flex-wrap gap-2">
                          {apiScopes.map((scope) => (
                            <button
                              key={scope}
                              type="button"
                              onClick={() => toggleScope(scope)}
                              className={`badge cursor-pointer ${
                                formData.allowedScopes?.includes(scope) ? 'badge-success' : 'badge-gray'
                              }`}
                            >
                              {scope}
                            </button>
                          ))}
                        </div>
                      </div>
                    )}
                  </CardContent>
                </Card>
              </TabPanel>

              <TabPanel id="access">
                <div className="space-y-6">
                  <Card>
                    <CardHeader title="Role-Based Access" />
                    <CardContent className="space-y-4">
                      <p className="text-sm text-gray-600">Users with any of these roles can authenticate</p>
                      <div className="flex flex-wrap gap-2">
                        {formData.allowedRoles?.map((role) => (
                          <span
                            key={role}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromCollection('allowedRoles', role)}
                          >
                            {role} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <select
                          value={newRole}
                          onChange={(e) => setNewRole(e.target.value)}
                          className="flex-1"
                        >
                          <option value="">Select a role...</option>
                          {availableRoles
                            .filter(r => !formData.allowedRoles?.includes(r.name))
                            .map(r => (
                              <option key={r.id} value={r.name}>{r.name}</option>
                            ))}
                        </select>
                        <Button type="button" variant="secondary" onClick={() => addToCollection('allowedRoles', newRole, () => setNewRole(''))} disabled={!newRole}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader title="User-Based Access" />
                    <CardContent className="space-y-4">
                      <p className="text-sm text-gray-600">Specific users who can authenticate</p>
                      <div className="flex flex-wrap gap-2">
                        {formData.allowedUsers?.map((user) => (
                          <span
                            key={user.subjectId}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeUser(user.subjectId)}
                          >
                            {user.displayName || user.subjectId} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <select
                          value={newUserSubjectId}
                          onChange={(e) => {
                            const selectedUser = availableUsers.find(u => u.id === e.target.value);
                            setNewUserSubjectId(e.target.value);
                            setNewUserDisplayName(selectedUser?.email || selectedUser?.userName || '');
                          }}
                          className="flex-1"
                        >
                          <option value="">Select a user...</option>
                          {availableUsers
                            .filter(u => !formData.allowedUsers?.some(au => au.subjectId === u.id))
                            .map(u => (
                              <option key={u.id} value={u.id}>
                                {u.email || u.userName}
                              </option>
                            ))}
                        </select>
                        <Button type="button" variant="secondary" onClick={addUser} disabled={!newUserSubjectId}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </CardContent>
                  </Card>

                  <Card>
                    <CardHeader title="Identity Provider Restrictions" />
                    <CardContent className="space-y-4">
                      <p className="text-sm text-gray-600">
                        Restrict which external identity providers can be used for authentication.
                        Leave empty to allow all configured providers.
                      </p>
                      <div className="flex flex-wrap gap-2">
                        {formData.identityProviderRestrictions?.map((idp) => (
                          <span
                            key={idp}
                            className="badge badge-warning cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromCollection('identityProviderRestrictions', idp)}
                          >
                            {idp} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="text"
                          value={newIdpRestriction}
                          onChange={(e) => setNewIdpRestriction(e.target.value)}
                          placeholder="e.g., Google, AzureAD, Okta"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToCollection('identityProviderRestrictions', newIdpRestriction, () => setNewIdpRestriction('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToCollection('identityProviderRestrictions', newIdpRestriction, () => setNewIdpRestriction(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </CardContent>
                  </Card>
                </div>
              </TabPanel>
            </TabPanels>
          </Tabs>

          <div className="flex justify-end gap-3 mt-6">
            <Button type="button" variant="secondary" onClick={() => navigate('/clients')}>
              Cancel
            </Button>
            <Button
              type="submit"
              disabled={!formData.clientId || (formData.allowedGrantTypes?.length || 0) === 0 || createMutation.isPending}
            >
              {createMutation.isPending ? 'Creating...' : 'Create Client'}
            </Button>
          </div>
        </form>
      </div>
    );
  }

  // ===============================================
  // LOADING / NOT FOUND STATES
  // ===============================================
  if (isLoading) {
    return <div className="flex items-center justify-center h-64">Loading...</div>;
  }

  if (!client) {
    return <div className="text-center py-8 text-gray-500">Client not found</div>;
  }

  // ===============================================
  // VIEW / EDIT EXISTING CLIENT
  // ===============================================
  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <Button variant="ghost" onClick={() => navigate('/clients')}>
            <ArrowLeftIcon className="h-4 w-4 mr-2" />
            Back
          </Button>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">{client.clientName || client.clientId}</h1>
            <p className="text-sm text-gray-500">{client.clientId}</p>
          </div>
          <span className={`badge ${client.enabled ? 'badge-success' : 'badge-danger'}`}>
            {client.enabled ? 'Enabled' : 'Disabled'}
          </span>
        </div>
        <div className="space-x-2">
          {isEditing ? (
            <>
              <Button variant="secondary" onClick={cancelEditing}>
                Cancel
              </Button>
              <Button onClick={saveChanges} disabled={updateMutation.isPending}>
                {updateMutation.isPending ? 'Saving...' : 'Save Changes'}
              </Button>
            </>
          ) : (
            <>
              <Button variant="secondary" onClick={startEditing}>
                <PencilIcon className="h-4 w-4 mr-2" />
                Edit
              </Button>
              <Button variant="danger" onClick={() => setShowDeleteModal(true)}>
                Delete
              </Button>
            </>
          )}
        </div>
      </div>

      {/* Tabbed Content */}
      <Tabs defaultTab="basic">
        <TabList>
          <Tab id="basic" icon={<Cog6ToothIcon className="h-4 w-4" />}>Basic</Tab>
          <Tab id="auth" icon={<ShieldCheckIcon className="h-4 w-4" />}>Authentication</Tab>
          <Tab id="tokens" icon={<KeyIcon className="h-4 w-4" />}>Tokens</Tab>
          <Tab id="uris" icon={<LinkIcon className="h-4 w-4" />}>URIs & CORS</Tab>
          <Tab id="scopes" icon={<KeyIcon className="h-4 w-4" />}>Scopes</Tab>
          <Tab id="logout" icon={<ArrowRightOnRectangleIcon className="h-4 w-4" />}>Logout</Tab>
          <Tab id="access" icon={<UserGroupIcon className="h-4 w-4" />}>Access Control</Tab>
          <Tab id="claims" icon={<ClipboardDocumentListIcon className="h-4 w-4" />}>Claims & Properties</Tab>
          <Tab id="advanced" icon={<AdjustmentsHorizontalIcon className="h-4 w-4" />}>Advanced</Tab>
        </TabList>

        <TabPanels>
          {/* Basic Tab */}
          <TabPanel id="basic">
            <Card>
              <CardHeader title="Basic Information" />
              <CardContent>
                {isEditing && editData ? (
                  <div className="space-y-4">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <label className="form-label">Client ID</label>
                        <input type="text" value={client.clientId} disabled className="bg-gray-100" />
                        <p className="form-helper">Cannot be changed</p>
                      </div>
                      <div>
                        <label className="form-label">Client Name</label>
                        <input
                          type="text"
                          value={editData.clientName}
                          onChange={(e) => setEditData({ ...editData, clientName: e.target.value })}
                        />
                      </div>
                    </div>
                    <div>
                      <label className="form-label">Description</label>
                      <textarea
                        value={editData.description}
                        onChange={(e) => setEditData({ ...editData, description: e.target.value })}
                        rows={2}
                      />
                    </div>
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <label className="form-label">Client URI</label>
                        <input
                          type="url"
                          value={editData.clientUri}
                          onChange={(e) => setEditData({ ...editData, clientUri: e.target.value })}
                          placeholder="https://myapp.com"
                        />
                      </div>
                      <div>
                        <label className="form-label">Logo URI</label>
                        <input
                          type="url"
                          value={editData.logoUri}
                          onChange={(e) => setEditData({ ...editData, logoUri: e.target.value })}
                          placeholder="https://myapp.com/logo.png"
                        />
                      </div>
                    </div>
                    <div>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.enabled}
                          onChange={(e) => setEditData({ ...editData, enabled: e.target.checked })}
                        />
                        <span className="text-sm">Enabled</span>
                      </label>
                    </div>
                  </div>
                ) : (
                  <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Client ID</dt>
                      <dd className="mt-1 text-sm text-gray-900 font-mono">{client.clientId}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Client Name</dt>
                      <dd className="mt-1 text-sm text-gray-900">{client.clientName || '-'}</dd>
                    </div>
                    <div className="md:col-span-2">
                      <dt className="text-sm font-medium text-gray-500">Description</dt>
                      <dd className="mt-1 text-sm text-gray-900">{client.description || '-'}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Client URI</dt>
                      <dd className="mt-1 text-sm text-gray-900">
                        {client.clientUri ? <a href={client.clientUri} className="text-blue-600 hover:underline" target="_blank" rel="noopener noreferrer">{client.clientUri}</a> : '-'}
                      </dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Logo URI</dt>
                      <dd className="mt-1 text-sm text-gray-900">{client.logoUri || '-'}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Created</dt>
                      <dd className="mt-1 text-sm text-gray-900">{new Date(client.created).toLocaleString()}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Last Updated</dt>
                      <dd className="mt-1 text-sm text-gray-900">{client.updated ? new Date(client.updated).toLocaleString() : '-'}</dd>
                    </div>
                    <div>
                      <dt className="text-sm font-medium text-gray-500">Last Accessed</dt>
                      <dd className="mt-1 text-sm text-gray-900">{client.lastAccessed ? new Date(client.lastAccessed).toLocaleString() : 'Never'}</dd>
                    </div>
                  </dl>
                )}
              </CardContent>
            </Card>
          </TabPanel>

          {/* Authentication Tab */}
          <TabPanel id="auth">
            <div className="space-y-6">
              <Card>
                <CardHeader title="Grant Types" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="grid grid-cols-2 md:grid-cols-3 gap-2">
                      {COMMON_GRANT_TYPES.map((grant) => (
                        <label key={grant.value} className="flex items-center gap-2 p-2 border rounded hover:bg-gray-50">
                          <input
                            type="checkbox"
                            checked={editData.allowedGrantTypes.includes(grant.value)}
                            onChange={() => toggleEditGrantType(grant.value)}
                          />
                          <span className="text-sm">{grant.label}</span>
                        </label>
                      ))}
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-2">
                      {client.allowedGrantTypes.map(grant => (
                        <span key={grant} className="badge badge-gray">{grant}</span>
                      ))}
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Security Settings" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.requireClientSecret}
                          onChange={(e) => setEditData({ ...editData, requireClientSecret: e.target.checked })}
                        />
                        <span className="text-sm">Require Client Secret</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.requirePkce}
                          onChange={(e) => setEditData({ ...editData, requirePkce: e.target.checked })}
                        />
                        <span className="text-sm">Require PKCE</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.allowPlainTextPkce}
                          onChange={(e) => setEditData({ ...editData, allowPlainTextPkce: e.target.checked })}
                        />
                        <span className="text-sm">Allow Plain Text PKCE</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.requireRequestObject}
                          onChange={(e) => setEditData({ ...editData, requireRequestObject: e.target.checked })}
                        />
                        <span className="text-sm">Require Request Object (JAR)</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.requireDPoP}
                          onChange={(e) => setEditData({ ...editData, requireDPoP: e.target.checked })}
                        />
                        <span className="text-sm">Require DPoP</span>
                      </label>
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.requirePushedAuthorization}
                          onChange={(e) => setEditData({ ...editData, requirePushedAuthorization: e.target.checked })}
                        />
                        <span className="text-sm">Require PAR</span>
                      </label>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-2 md:grid-cols-3 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Require Client Secret</dt>
                        <dd className="mt-1 text-sm">{client.requireClientSecret ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Require PKCE</dt>
                        <dd className="mt-1 text-sm">{client.requirePkce ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Allow Plain Text PKCE</dt>
                        <dd className="mt-1 text-sm">{client.allowPlainTextPkce ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Require Request Object</dt>
                        <dd className="mt-1 text-sm">{client.requireRequestObject ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Require DPoP</dt>
                        <dd className="mt-1 text-sm">{client.requireDPoP ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Require PAR</dt>
                        <dd className="mt-1 text-sm">{client.requirePushedAuthorization ? 'Yes' : 'No'}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>

              {client.requireClientSecret && !isEditing && (
                <Card>
                  <CardHeader title="Client Secret" />
                  <CardContent>
                    <div className="flex items-center gap-4">
                      <p className="text-sm text-gray-600">
                        The client secret is hashed and cannot be viewed. You can regenerate it if needed.
                      </p>
                      <Button
                        variant="secondary"
                        onClick={() => regenerateSecretMutation.mutate(client.clientId)}
                        disabled={regenerateSecretMutation.isPending}
                      >
                        {regenerateSecretMutation.isPending ? 'Regenerating...' : 'Regenerate Secret'}
                      </Button>
                    </div>
                  </CardContent>
                </Card>
              )}

              <Card>
                <CardHeader title="Consent Settings" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <div className="flex gap-6">
                        <label className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={editData.requireConsent}
                            onChange={(e) => setEditData({ ...editData, requireConsent: e.target.checked })}
                          />
                          <span className="text-sm">Require Consent</span>
                        </label>
                        <label className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={editData.allowRememberConsent}
                            onChange={(e) => setEditData({ ...editData, allowRememberConsent: e.target.checked })}
                          />
                          <span className="text-sm">Allow Remember Consent</span>
                        </label>
                      </div>
                      <div className="max-w-xs">
                        <label className="form-label">Consent Lifetime (seconds)</label>
                        <input
                          type="number"
                          value={editData.consentLifetime ?? ''}
                          onChange={(e) => setEditData({ ...editData, consentLifetime: e.target.value ? parseInt(e.target.value) : null })}
                          placeholder="Leave empty for no expiry"
                        />
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-2 md:grid-cols-3 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Require Consent</dt>
                        <dd className="mt-1 text-sm">{client.requireConsent ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Allow Remember Consent</dt>
                        <dd className="mt-1 text-sm">{client.allowRememberConsent ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Consent Lifetime</dt>
                        <dd className="mt-1 text-sm">{client.consentLifetime ? formatDuration(client.consentLifetime) : 'No expiry'}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>
            </div>
          </TabPanel>

          {/* Tokens Tab */}
          <TabPanel id="tokens">
            <div className="space-y-6">
              <Card>
                <CardHeader title="Token Lifetimes" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                      <div>
                        <label className="form-label">Access Token (seconds)</label>
                        <input
                          type="number"
                          value={editData.accessTokenLifetime}
                          onChange={(e) => setEditData({ ...editData, accessTokenLifetime: parseInt(e.target.value) || 3600 })}
                        />
                        <p className="form-helper">{formatDuration(editData.accessTokenLifetime)}</p>
                      </div>
                      <div>
                        <label className="form-label">Identity Token (seconds)</label>
                        <input
                          type="number"
                          value={editData.identityTokenLifetime}
                          onChange={(e) => setEditData({ ...editData, identityTokenLifetime: parseInt(e.target.value) || 300 })}
                        />
                        <p className="form-helper">{formatDuration(editData.identityTokenLifetime)}</p>
                      </div>
                      <div>
                        <label className="form-label">Authorization Code (seconds)</label>
                        <input
                          type="number"
                          value={editData.authorizationCodeLifetime}
                          onChange={(e) => setEditData({ ...editData, authorizationCodeLifetime: parseInt(e.target.value) || 300 })}
                        />
                        <p className="form-helper">{formatDuration(editData.authorizationCodeLifetime)}</p>
                      </div>
                      <div>
                        <label className="form-label">Absolute Refresh Token (seconds)</label>
                        <input
                          type="number"
                          value={editData.absoluteRefreshTokenLifetime}
                          onChange={(e) => setEditData({ ...editData, absoluteRefreshTokenLifetime: parseInt(e.target.value) || 2592000 })}
                        />
                        <p className="form-helper">{formatDuration(editData.absoluteRefreshTokenLifetime)}</p>
                      </div>
                      <div>
                        <label className="form-label">Sliding Refresh Token (seconds)</label>
                        <input
                          type="number"
                          value={editData.slidingRefreshTokenLifetime}
                          onChange={(e) => setEditData({ ...editData, slidingRefreshTokenLifetime: parseInt(e.target.value) || 1296000 })}
                        />
                        <p className="form-helper">{formatDuration(editData.slidingRefreshTokenLifetime)}</p>
                      </div>
                      <div>
                        <label className="form-label">Device Code (seconds)</label>
                        <input
                          type="number"
                          value={editData.deviceCodeLifetime}
                          onChange={(e) => setEditData({ ...editData, deviceCodeLifetime: parseInt(e.target.value) || 300 })}
                        />
                        <p className="form-helper">{formatDuration(editData.deviceCodeLifetime)}</p>
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-2 md:grid-cols-3 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Access Token</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.accessTokenLifetime)}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Identity Token</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.identityTokenLifetime)}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Authorization Code</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.authorizationCodeLifetime)}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Absolute Refresh Token</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.absoluteRefreshTokenLifetime)}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Sliding Refresh Token</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.slidingRefreshTokenLifetime)}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Device Code</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.deviceCodeLifetime)}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Token Behavior" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                        <div>
                          <label className="form-label">Refresh Token Usage</label>
                          <select
                            value={editData.refreshTokenUsage}
                            onChange={(e) => setEditData({ ...editData, refreshTokenUsage: parseInt(e.target.value) as TokenUsage })}
                          >
                            <option value={0}>ReUse</option>
                            <option value={1}>One Time Only</option>
                          </select>
                        </div>
                        <div>
                          <label className="form-label">Refresh Token Expiration</label>
                          <select
                            value={editData.refreshTokenExpiration}
                            onChange={(e) => setEditData({ ...editData, refreshTokenExpiration: parseInt(e.target.value) as TokenExpiration })}
                          >
                            <option value={0}>Sliding</option>
                            <option value={1}>Absolute</option>
                          </select>
                        </div>
                        <div>
                          <label className="form-label">Access Token Type</label>
                          <select
                            value={editData.accessTokenType}
                            onChange={(e) => setEditData({ ...editData, accessTokenType: parseInt(e.target.value) as AccessTokenType })}
                          >
                            <option value={0}>JWT</option>
                            <option value={1}>Reference</option>
                          </select>
                        </div>
                      </div>
                      <div className="grid grid-cols-2 md:grid-cols-3 gap-4 pt-4 border-t">
                        <label className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={editData.allowOfflineAccess}
                            onChange={(e) => setEditData({ ...editData, allowOfflineAccess: e.target.checked })}
                          />
                          <span className="text-sm">Allow Offline Access</span>
                        </label>
                        <label className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={editData.allowAccessTokensViaBrowser}
                            onChange={(e) => setEditData({ ...editData, allowAccessTokensViaBrowser: e.target.checked })}
                          />
                          <span className="text-sm">Allow Access Tokens Via Browser</span>
                        </label>
                        <label className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={editData.alwaysIncludeUserClaimsInIdToken}
                            onChange={(e) => setEditData({ ...editData, alwaysIncludeUserClaimsInIdToken: e.target.checked })}
                          />
                          <span className="text-sm">Include User Claims in ID Token</span>
                        </label>
                        <label className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={editData.updateAccessTokenClaimsOnRefresh}
                            onChange={(e) => setEditData({ ...editData, updateAccessTokenClaimsOnRefresh: e.target.checked })}
                          />
                          <span className="text-sm">Update Claims on Refresh</span>
                        </label>
                        <label className="flex items-center gap-2">
                          <input
                            type="checkbox"
                            checked={editData.includeJwtId}
                            onChange={(e) => setEditData({ ...editData, includeJwtId: e.target.checked })}
                          />
                          <span className="text-sm">Include JWT ID (jti)</span>
                        </label>
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-2 md:grid-cols-3 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Refresh Token Usage</dt>
                        <dd className="mt-1 text-sm">{client.refreshTokenUsage === 0 ? 'ReUse' : 'One Time Only'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Refresh Token Expiration</dt>
                        <dd className="mt-1 text-sm">{client.refreshTokenExpiration === 0 ? 'Sliding' : 'Absolute'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Access Token Type</dt>
                        <dd className="mt-1 text-sm">{client.accessTokenType === 0 ? 'JWT' : 'Reference'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Allow Offline Access</dt>
                        <dd className="mt-1 text-sm">{client.allowOfflineAccess ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Allow Access Tokens Via Browser</dt>
                        <dd className="mt-1 text-sm">{client.allowAccessTokensViaBrowser ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Include User Claims in ID Token</dt>
                        <dd className="mt-1 text-sm">{client.alwaysIncludeUserClaimsInIdToken ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Update Claims on Refresh</dt>
                        <dd className="mt-1 text-sm">{client.updateAccessTokenClaimsOnRefresh ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Include JWT ID</dt>
                        <dd className="mt-1 text-sm">{client.includeJwtId ? 'Yes' : 'No'}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>
            </div>
          </TabPanel>

          {/* URIs & CORS Tab */}
          <TabPanel id="uris">
            <div className="space-y-6">
              <Card>
                <CardHeader title="Redirect URIs" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <div className="flex flex-wrap gap-2">
                        {editData.redirectUris.map((uri) => (
                          <span
                            key={uri}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromEditCollection('redirectUris', uri)}
                          >
                            {uri} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="url"
                          value={editNewRedirectUri}
                          onChange={(e) => setEditNewRedirectUri(e.target.value)}
                          placeholder="https://myapp.com/callback"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToEditCollection('redirectUris', editNewRedirectUri, () => setEditNewRedirectUri('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToEditCollection('redirectUris', editNewRedirectUri, () => setEditNewRedirectUri(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <ul className="space-y-1">
                      {client.redirectUris.length > 0 ? client.redirectUris.map((uri, i) => (
                        <li key={i} className="text-sm font-mono text-gray-700">{uri}</li>
                      )) : <li className="text-sm text-gray-500">No redirect URIs configured</li>}
                    </ul>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Post Logout Redirect URIs" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <div className="flex flex-wrap gap-2">
                        {editData.postLogoutRedirectUris.map((uri) => (
                          <span
                            key={uri}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromEditCollection('postLogoutRedirectUris', uri)}
                          >
                            {uri} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="url"
                          value={editNewPostLogoutUri}
                          onChange={(e) => setEditNewPostLogoutUri(e.target.value)}
                          placeholder="https://myapp.com/logout"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToEditCollection('postLogoutRedirectUris', editNewPostLogoutUri, () => setEditNewPostLogoutUri('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToEditCollection('postLogoutRedirectUris', editNewPostLogoutUri, () => setEditNewPostLogoutUri(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <ul className="space-y-1">
                      {client.postLogoutRedirectUris.length > 0 ? client.postLogoutRedirectUris.map((uri, i) => (
                        <li key={i} className="text-sm font-mono text-gray-700">{uri}</li>
                      )) : <li className="text-sm text-gray-500">No post logout URIs configured</li>}
                    </ul>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="CORS Origins" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <p className="text-sm text-gray-600">Origins allowed to make cross-origin requests</p>
                      <div className="flex flex-wrap gap-2">
                        {editData.allowedCorsOrigins.map((origin) => (
                          <span
                            key={origin}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromEditCollection('allowedCorsOrigins', origin)}
                          >
                            {origin} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="url"
                          value={editNewCorsOrigin}
                          onChange={(e) => setEditNewCorsOrigin(e.target.value)}
                          placeholder="https://myapp.com"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToEditCollection('allowedCorsOrigins', editNewCorsOrigin, () => setEditNewCorsOrigin('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToEditCollection('allowedCorsOrigins', editNewCorsOrigin, () => setEditNewCorsOrigin(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <ul className="space-y-1">
                      {client.allowedCorsOrigins.length > 0 ? client.allowedCorsOrigins.map((origin, i) => (
                        <li key={i} className="text-sm font-mono text-gray-700">{origin}</li>
                      )) : <li className="text-sm text-gray-500">No CORS origins configured</li>}
                    </ul>
                  )}
                </CardContent>
              </Card>
            </div>
          </TabPanel>

          {/* Scopes Tab */}
          <TabPanel id="scopes">
            <Card>
              <CardHeader title="Allowed Scopes" />
              <CardContent>
                {isEditing && editData ? (
                  <div className="space-y-4">
                    <div>
                      <label className="text-xs text-gray-500 mb-2 block">Identity Scopes</label>
                      <div className="flex flex-wrap gap-2">
                        {IDENTITY_SCOPES.map((scope) => (
                          <button
                            key={scope}
                            type="button"
                            onClick={() => toggleEditScope(scope)}
                            className={`badge cursor-pointer ${
                              editData.allowedScopes.includes(scope) ? 'badge-primary' : 'badge-gray'
                            }`}
                          >
                            {scope}
                          </button>
                        ))}
                      </div>
                    </div>
                    {apiScopes.length > 0 && (
                      <div className="pt-4 border-t">
                        <label className="text-xs text-gray-500 mb-2 block">API Scopes</label>
                        <div className="flex flex-wrap gap-2">
                          {apiScopes.map((scope) => (
                            <button
                              key={scope}
                              type="button"
                              onClick={() => toggleEditScope(scope)}
                              className={`badge cursor-pointer ${
                                editData.allowedScopes.includes(scope) ? 'badge-success' : 'badge-gray'
                              }`}
                            >
                              {scope}
                            </button>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                ) : (
                  <div className="space-y-4">
                    <div>
                      <label className="text-xs text-gray-500 mb-2 block">Identity Scopes</label>
                      <div className="flex flex-wrap gap-2">
                        {client.allowedScopes.filter(s => IDENTITY_SCOPES.includes(s)).map((scope) => (
                          <span key={scope} className="badge badge-primary">{scope}</span>
                        ))}
                        {!client.allowedScopes.some(s => IDENTITY_SCOPES.includes(s)) && (
                          <span className="text-sm text-gray-500">None</span>
                        )}
                      </div>
                    </div>
                    {client.allowedScopes.some(s => !IDENTITY_SCOPES.includes(s)) && (
                      <div className="pt-4 border-t">
                        <label className="text-xs text-gray-500 mb-2 block">API Scopes</label>
                        <div className="flex flex-wrap gap-2">
                          {client.allowedScopes.filter(s => !IDENTITY_SCOPES.includes(s)).map((scope) => (
                            <span key={scope} className="badge badge-success">{scope}</span>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                )}
              </CardContent>
            </Card>
          </TabPanel>

          {/* Logout Tab */}
          <TabPanel id="logout">
            <Card>
              <CardHeader title="Logout Settings" />
              <CardContent>
                {isEditing && editData ? (
                  <div className="space-y-6">
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                      <div>
                        <h4 className="text-sm font-medium text-gray-900 mb-4">Front Channel Logout</h4>
                        <div className="space-y-4">
                          <div>
                            <label className="form-label">Front Channel Logout URI</label>
                            <input
                              type="url"
                              value={editData.frontChannelLogoutUri}
                              onChange={(e) => setEditData({ ...editData, frontChannelLogoutUri: e.target.value })}
                              placeholder="https://myapp.com/signout"
                            />
                          </div>
                          <label className="flex items-center gap-2">
                            <input
                              type="checkbox"
                              checked={editData.frontChannelLogoutSessionRequired}
                              onChange={(e) => setEditData({ ...editData, frontChannelLogoutSessionRequired: e.target.checked })}
                            />
                            <span className="text-sm">Session Required</span>
                          </label>
                        </div>
                      </div>
                      <div>
                        <h4 className="text-sm font-medium text-gray-900 mb-4">Back Channel Logout</h4>
                        <div className="space-y-4">
                          <div>
                            <label className="form-label">Back Channel Logout URI</label>
                            <input
                              type="url"
                              value={editData.backChannelLogoutUri}
                              onChange={(e) => setEditData({ ...editData, backChannelLogoutUri: e.target.value })}
                              placeholder="https://myapp.com/backchannel-logout"
                            />
                          </div>
                          <label className="flex items-center gap-2">
                            <input
                              type="checkbox"
                              checked={editData.backChannelLogoutSessionRequired}
                              onChange={(e) => setEditData({ ...editData, backChannelLogoutSessionRequired: e.target.checked })}
                            />
                            <span className="text-sm">Session Required</span>
                          </label>
                        </div>
                      </div>
                    </div>
                  </div>
                ) : (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                    <div>
                      <h4 className="text-sm font-medium text-gray-900 mb-4">Front Channel Logout</h4>
                      <dl className="space-y-2">
                        <div>
                          <dt className="text-sm text-gray-500">URI</dt>
                          <dd className="text-sm font-mono">{client.frontChannelLogoutUri || '-'}</dd>
                        </div>
                        <div>
                          <dt className="text-sm text-gray-500">Session Required</dt>
                          <dd className="text-sm">{client.frontChannelLogoutSessionRequired ? 'Yes' : 'No'}</dd>
                        </div>
                      </dl>
                    </div>
                    <div>
                      <h4 className="text-sm font-medium text-gray-900 mb-4">Back Channel Logout</h4>
                      <dl className="space-y-2">
                        <div>
                          <dt className="text-sm text-gray-500">URI</dt>
                          <dd className="text-sm font-mono">{client.backChannelLogoutUri || '-'}</dd>
                        </div>
                        <div>
                          <dt className="text-sm text-gray-500">Session Required</dt>
                          <dd className="text-sm">{client.backChannelLogoutSessionRequired ? 'Yes' : 'No'}</dd>
                        </div>
                      </dl>
                    </div>
                  </div>
                )}
              </CardContent>
            </Card>
          </TabPanel>

          {/* Access Control Tab */}
          <TabPanel id="access">
            <div className="space-y-6">
              <Card>
                <CardHeader title="Allowed Roles" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <p className="text-sm text-gray-600">Users with any of these roles can authenticate</p>
                      <div className="flex flex-wrap gap-2">
                        {editData.allowedRoles.map((role) => (
                          <span
                            key={role}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromEditCollection('allowedRoles', role)}
                          >
                            {role} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <select
                          value={editNewRole}
                          onChange={(e) => setEditNewRole(e.target.value)}
                          className="flex-1"
                        >
                          <option value="">Select a role...</option>
                          {availableRoles
                            .filter(r => !editData.allowedRoles.includes(r.name))
                            .map(r => (
                              <option key={r.id} value={r.name}>{r.name}</option>
                            ))}
                        </select>
                        <Button type="button" variant="secondary" onClick={() => addToEditCollection('allowedRoles', editNewRole, () => setEditNewRole(''))} disabled={!editNewRole}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div>
                      {client.allowedRoles.length > 0 ? (
                        <div className="flex flex-wrap gap-2">
                          {client.allowedRoles.map((role) => (
                            <span key={role} className="badge badge-info">{role}</span>
                          ))}
                        </div>
                      ) : (
                        <p className="text-sm text-gray-500">No role restrictions (all roles allowed)</p>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Allowed Users" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <p className="text-sm text-gray-600">Specific users who can authenticate</p>
                      <div className="flex flex-wrap gap-2">
                        {editData.allowedUsers.map((user) => (
                          <span
                            key={user.subjectId}
                            className="badge badge-info cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeEditUser(user.subjectId)}
                          >
                            {user.displayName || user.subjectId} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <select
                          value={editNewUserSubjectId}
                          onChange={(e) => {
                            const selectedUser = availableUsers.find(u => u.id === e.target.value);
                            setEditNewUserSubjectId(e.target.value);
                            setEditNewUserDisplayName(selectedUser?.email || selectedUser?.userName || '');
                          }}
                          className="flex-1"
                        >
                          <option value="">Select a user...</option>
                          {availableUsers
                            .filter(u => !editData.allowedUsers.some(au => au.subjectId === u.id))
                            .map(u => (
                              <option key={u.id} value={u.id}>
                                {u.email || u.userName}
                              </option>
                            ))}
                        </select>
                        <Button type="button" variant="secondary" onClick={addEditUser} disabled={!editNewUserSubjectId}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div>
                      {client.allowedUsers.length > 0 ? (
                        <div className="flex flex-wrap gap-2">
                          {client.allowedUsers.map((user) => (
                            <span key={user.subjectId} className="badge badge-info">
                              {user.displayName || user.subjectId}
                            </span>
                          ))}
                        </div>
                      ) : (
                        <p className="text-sm text-gray-500">No user restrictions</p>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Identity Provider Restrictions" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <p className="text-sm text-gray-600">
                        Restrict which external identity providers can be used. Leave empty to allow all.
                      </p>
                      <div className="flex flex-wrap gap-2">
                        {editData.identityProviderRestrictions.map((idp) => (
                          <span
                            key={idp}
                            className="badge badge-warning cursor-pointer hover:bg-red-100 hover:text-red-700"
                            onClick={() => removeFromEditCollection('identityProviderRestrictions', idp)}
                          >
                            {idp} <XMarkIcon className="h-3 w-3 inline ml-1" />
                          </span>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="text"
                          value={editNewIdpRestriction}
                          onChange={(e) => setEditNewIdpRestriction(e.target.value)}
                          placeholder="e.g., Google, AzureAD, Okta"
                          onKeyDown={(e) => e.key === 'Enter' && (e.preventDefault(), addToEditCollection('identityProviderRestrictions', editNewIdpRestriction, () => setEditNewIdpRestriction('')))}
                        />
                        <Button type="button" variant="secondary" onClick={() => addToEditCollection('identityProviderRestrictions', editNewIdpRestriction, () => setEditNewIdpRestriction(''))}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div>
                      {client.identityProviderRestrictions.length > 0 ? (
                        <div className="flex flex-wrap gap-2">
                          {client.identityProviderRestrictions.map((idp) => (
                            <span key={idp} className="badge badge-warning">{idp}</span>
                          ))}
                        </div>
                      ) : (
                        <p className="text-sm text-gray-500">No provider restrictions (all providers allowed)</p>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>
            </div>
          </TabPanel>

          {/* Claims & Properties Tab */}
          <TabPanel id="claims">
            <div className="space-y-6">
              <Card>
                <CardHeader title="Client Claims" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <p className="text-sm text-gray-600">Claims that will be added to tokens issued for this client</p>
                      <div className="space-y-2">
                        {editData.claims.map((claim, i) => (
                          <div key={i} className="flex items-center gap-2 p-2 bg-gray-50 rounded">
                            <span className="text-sm font-medium">{claim.type}:</span>
                            <span className="text-sm text-gray-700 flex-1">{claim.value}</span>
                            <button
                              type="button"
                              onClick={() => removeEditClaim(i)}
                              className="text-red-500 hover:text-red-700"
                            >
                              <XMarkIcon className="h-4 w-4" />
                            </button>
                          </div>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="text"
                          value={editNewClaimType}
                          onChange={(e) => setEditNewClaimType(e.target.value)}
                          placeholder="Claim type"
                          className="w-1/3"
                        />
                        <input
                          type="text"
                          value={editNewClaimValue}
                          onChange={(e) => setEditNewClaimValue(e.target.value)}
                          placeholder="Claim value"
                          className="flex-1"
                        />
                        <Button type="button" variant="secondary" onClick={addEditClaim} disabled={!editNewClaimType || !editNewClaimValue}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div>
                      {client.claims.length > 0 ? (
                        <div className="space-y-2">
                          {client.claims.map((claim, i) => (
                            <div key={i} className="flex items-center gap-2 p-2 bg-gray-50 rounded">
                              <span className="text-sm font-medium">{claim.type}:</span>
                              <span className="text-sm text-gray-700">{claim.value}</span>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <p className="text-sm text-gray-500">No custom claims configured</p>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Client Properties" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <p className="text-sm text-gray-600">Custom key-value properties for this client</p>
                      <div className="space-y-2">
                        {editData.properties.map((prop, i) => (
                          <div key={i} className="flex items-center gap-2 p-2 bg-gray-50 rounded">
                            <span className="text-sm font-medium">{prop.key}:</span>
                            <span className="text-sm text-gray-700 flex-1">{prop.value}</span>
                            <button
                              type="button"
                              onClick={() => removeEditProperty(i)}
                              className="text-red-500 hover:text-red-700"
                            >
                              <XMarkIcon className="h-4 w-4" />
                            </button>
                          </div>
                        ))}
                      </div>
                      <div className="flex gap-2">
                        <input
                          type="text"
                          value={editNewPropertyKey}
                          onChange={(e) => setEditNewPropertyKey(e.target.value)}
                          placeholder="Property key"
                          className="w-1/3"
                        />
                        <input
                          type="text"
                          value={editNewPropertyValue}
                          onChange={(e) => setEditNewPropertyValue(e.target.value)}
                          placeholder="Property value"
                          className="flex-1"
                        />
                        <Button type="button" variant="secondary" onClick={addEditProperty} disabled={!editNewPropertyKey || !editNewPropertyValue}>
                          <PlusIcon className="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                  ) : (
                    <div>
                      {client.properties.length > 0 ? (
                        <div className="space-y-2">
                          {client.properties.map((prop, i) => (
                            <div key={i} className="flex items-center gap-2 p-2 bg-gray-50 rounded">
                              <span className="text-sm font-medium">{prop.key}:</span>
                              <span className="text-sm text-gray-700">{prop.value}</span>
                            </div>
                          ))}
                        </div>
                      ) : (
                        <p className="text-sm text-gray-500">No custom properties configured</p>
                      )}
                    </div>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Client Claims Settings" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.alwaysSendClientClaims}
                          onChange={(e) => setEditData({ ...editData, alwaysSendClientClaims: e.target.checked })}
                        />
                        <span className="text-sm">Always Send Client Claims</span>
                      </label>
                      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                        <div>
                          <label className="form-label">Client Claims Prefix</label>
                          <input
                            type="text"
                            value={editData.clientClaimsPrefix}
                            onChange={(e) => setEditData({ ...editData, clientClaimsPrefix: e.target.value })}
                            placeholder="client_"
                          />
                        </div>
                        <div>
                          <label className="form-label">Pairwise Subject Salt</label>
                          <input
                            type="text"
                            value={editData.pairWiseSubjectSalt}
                            onChange={(e) => setEditData({ ...editData, pairWiseSubjectSalt: e.target.value })}
                            placeholder="Leave empty for global salt"
                          />
                        </div>
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-1 md:grid-cols-3 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Always Send Client Claims</dt>
                        <dd className="mt-1 text-sm">{client.alwaysSendClientClaims ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Client Claims Prefix</dt>
                        <dd className="mt-1 text-sm font-mono">{client.clientClaimsPrefix || '-'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Pairwise Subject Salt</dt>
                        <dd className="mt-1 text-sm">{client.pairWiseSubjectSalt || 'Global'}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>
            </div>
          </TabPanel>

          {/* Advanced Tab */}
          <TabPanel id="advanced">
            <div className="space-y-6">
              <Card>
                <CardHeader title="SSO Settings" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.enableLocalLogin}
                          onChange={(e) => setEditData({ ...editData, enableLocalLogin: e.target.checked })}
                        />
                        <span className="text-sm">Enable Local Login</span>
                      </label>
                      <div>
                        <label className="form-label">User SSO Lifetime (seconds)</label>
                        <input
                          type="number"
                          value={editData.userSsoLifetime ?? ''}
                          onChange={(e) => setEditData({ ...editData, userSsoLifetime: e.target.value ? parseInt(e.target.value) : null })}
                          placeholder="Leave empty for default"
                        />
                        {editData.userSsoLifetime && (
                          <p className="form-helper">{formatDuration(editData.userSsoLifetime)}</p>
                        )}
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Enable Local Login</dt>
                        <dd className="mt-1 text-sm">{client.enableLocalLogin ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">User SSO Lifetime</dt>
                        <dd className="mt-1 text-sm">{client.userSsoLifetime ? formatDuration(client.userSsoLifetime) : 'Default'}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader
                  title="UI Flow Settings"
                  description="Configure the authentication UI flow for this client"
                />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <div>
                        <label className="form-label">Authentication Flow</label>
                        <select
                          value={editData.useJourneyFlow === null ? 'inherit' : editData.useJourneyFlow ? 'journey' : 'standalone'}
                          onChange={(e) => {
                            const value = e.target.value;
                            setEditData({
                              ...editData,
                              useJourneyFlow: value === 'inherit' ? null : value === 'journey'
                            });
                          }}
                          className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 sm:text-sm"
                        >
                          <option value="inherit">Inherit from tenant</option>
                          <option value="journey">Journey flow</option>
                          <option value="standalone">Standalone pages</option>
                        </select>
                        <p className="form-helper mt-2 text-amber-600">
                          Note: If the tenant has disabled journey flow, this setting cannot enable it.
                          Client can only opt-out of journey flow when tenant allows it.
                        </p>
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-1 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Authentication Flow</dt>
                        <dd className="mt-1 text-sm">
                          {client.useJourneyFlow === null || client.useJourneyFlow === undefined
                            ? 'Inherit from tenant'
                            : client.useJourneyFlow
                            ? 'Journey flow'
                            : 'Standalone pages'}
                        </dd>
                        <p className="mt-1 text-xs text-gray-400">
                          Tenant settings take priority. Client can only customize within tenant-allowed options.
                        </p>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Device Flow Settings" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <label className="form-label">User Code Type</label>
                        <input
                          type="text"
                          value={editData.userCodeType}
                          onChange={(e) => setEditData({ ...editData, userCodeType: e.target.value })}
                          placeholder="e.g., Numeric"
                        />
                      </div>
                      <div>
                        <label className="form-label">Device Code Lifetime (seconds)</label>
                        <input
                          type="number"
                          value={editData.deviceCodeLifetime}
                          onChange={(e) => setEditData({ ...editData, deviceCodeLifetime: parseInt(e.target.value) || 300 })}
                        />
                        <p className="form-helper">{formatDuration(editData.deviceCodeLifetime)}</p>
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">User Code Type</dt>
                        <dd className="mt-1 text-sm">{client.userCodeType || 'Default'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Device Code Lifetime</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.deviceCodeLifetime)}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="PAR Settings" />
                <CardContent>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.requirePushedAuthorization}
                          onChange={(e) => setEditData({ ...editData, requirePushedAuthorization: e.target.checked })}
                        />
                        <span className="text-sm">Require Pushed Authorization Requests (PAR)</span>
                      </label>
                      <div className="max-w-xs">
                        <label className="form-label">PAR Lifetime (seconds)</label>
                        <input
                          type="number"
                          value={editData.pushedAuthorizationLifetime}
                          onChange={(e) => setEditData({ ...editData, pushedAuthorizationLifetime: parseInt(e.target.value) || 60 })}
                        />
                        <p className="form-helper">{formatDuration(editData.pushedAuthorizationLifetime)}</p>
                      </div>
                    </div>
                  ) : (
                    <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">Require PAR</dt>
                        <dd className="mt-1 text-sm">{client.requirePushedAuthorization ? 'Yes' : 'No'}</dd>
                      </div>
                      <div>
                        <dt className="text-sm font-medium text-gray-500">PAR Lifetime</dt>
                        <dd className="mt-1 text-sm">{formatDuration(client.pushedAuthorizationLifetime)}</dd>
                      </div>
                    </dl>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="CIBA Settings" />
                <CardContent>
                  <p className="text-sm text-gray-500 mb-4">
                    Client Initiated Backchannel Authentication (CIBA) allows authentication to be initiated
                    by a consumption device while the user authenticates on a separate authentication device.
                  </p>
                  {isEditing && editData ? (
                    <div className="space-y-4">
                      <label className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={editData.cibaEnabled}
                          onChange={(e) => setEditData({ ...editData, cibaEnabled: e.target.checked })}
                        />
                        <span className="text-sm">Enable CIBA</span>
                      </label>
                      {editData.cibaEnabled && (
                        <>
                          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            <div>
                              <label className="form-label">Token Delivery Mode</label>
                              <select
                                value={editData.cibaTokenDeliveryMode}
                                onChange={(e) => setEditData({ ...editData, cibaTokenDeliveryMode: e.target.value })}
                                className="w-full"
                              >
                                {CIBA_TOKEN_DELIVERY_MODES.map((mode) => (
                                  <option key={mode.value} value={mode.value}>
                                    {mode.label}
                                  </option>
                                ))}
                              </select>
                              <p className="form-helper">
                                Poll: Client polls token endpoint. Ping: Server notifies client. Push: Server pushes tokens.
                              </p>
                            </div>
                            {(editData.cibaTokenDeliveryMode === 'ping' || editData.cibaTokenDeliveryMode === 'push') && (
                              <div>
                                <label className="form-label">Client Notification Endpoint</label>
                                <input
                                  type="url"
                                  value={editData.cibaClientNotificationEndpoint}
                                  onChange={(e) => setEditData({ ...editData, cibaClientNotificationEndpoint: e.target.value })}
                                  placeholder="https://client.example.com/ciba/notify"
                                />
                                <p className="form-helper">Required for ping/push delivery modes</p>
                              </div>
                            )}
                          </div>
                          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                            <div>
                              <label className="form-label">Request Lifetime (seconds)</label>
                              <input
                                type="number"
                                value={editData.cibaRequestLifetime}
                                onChange={(e) => setEditData({ ...editData, cibaRequestLifetime: parseInt(e.target.value) || 120 })}
                              />
                              <p className="form-helper">{formatDuration(editData.cibaRequestLifetime)} - How long the auth request is valid</p>
                            </div>
                            <div>
                              <label className="form-label">Polling Interval (seconds)</label>
                              <input
                                type="number"
                                value={editData.cibaPollingInterval}
                                onChange={(e) => setEditData({ ...editData, cibaPollingInterval: parseInt(e.target.value) || 5 })}
                              />
                              <p className="form-helper">Minimum interval between client poll requests</p>
                            </div>
                          </div>
                          <label className="flex items-center gap-2">
                            <input
                              type="checkbox"
                              checked={editData.cibaRequireUserCode}
                              onChange={(e) => setEditData({ ...editData, cibaRequireUserCode: e.target.checked })}
                            />
                            <span className="text-sm">Require User Code</span>
                          </label>
                          <p className="form-helper">
                            When enabled, the client must provide a user code that the end-user enters on the authentication device.
                          </p>
                        </>
                      )}
                    </div>
                  ) : (
                    <dl className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div>
                        <dt className="text-sm font-medium text-gray-500">CIBA Enabled</dt>
                        <dd className="mt-1 text-sm">{client.cibaEnabled ? 'Yes' : 'No'}</dd>
                      </div>
                      {client.cibaEnabled && (
                        <>
                          <div>
                            <dt className="text-sm font-medium text-gray-500">Token Delivery Mode</dt>
                            <dd className="mt-1 text-sm capitalize">{client.cibaTokenDeliveryMode}</dd>
                          </div>
                          {client.cibaClientNotificationEndpoint && (
                            <div className="md:col-span-2">
                              <dt className="text-sm font-medium text-gray-500">Notification Endpoint</dt>
                              <dd className="mt-1 text-sm font-mono text-xs break-all">{client.cibaClientNotificationEndpoint}</dd>
                            </div>
                          )}
                          <div>
                            <dt className="text-sm font-medium text-gray-500">Request Lifetime</dt>
                            <dd className="mt-1 text-sm">{formatDuration(client.cibaRequestLifetime)}</dd>
                          </div>
                          <div>
                            <dt className="text-sm font-medium text-gray-500">Polling Interval</dt>
                            <dd className="mt-1 text-sm">{client.cibaPollingInterval}s</dd>
                          </div>
                          <div>
                            <dt className="text-sm font-medium text-gray-500">Require User Code</dt>
                            <dd className="mt-1 text-sm">{client.cibaRequireUserCode ? 'Yes' : 'No'}</dd>
                          </div>
                        </>
                      )}
                    </dl>
                  )}
                </CardContent>
              </Card>

              <Card>
                <CardHeader title="Token Signing" />
                <CardContent>
                  {isEditing && editData ? (
                    <div>
                      <label className="form-label">Allowed Identity Token Signing Algorithms</label>
                      <input
                        type="text"
                        value={editData.allowedIdentityTokenSigningAlgorithms}
                        onChange={(e) => setEditData({ ...editData, allowedIdentityTokenSigningAlgorithms: e.target.value })}
                        placeholder="e.g., RS256, ES256 (comma-separated)"
                      />
                      <p className="form-helper">Leave empty to allow all supported algorithms</p>
                    </div>
                  ) : (
                    <dl>
                      <dt className="text-sm font-medium text-gray-500">Allowed Signing Algorithms</dt>
                      <dd className="mt-1 text-sm">{client.allowedIdentityTokenSigningAlgorithms || 'All supported'}</dd>
                    </dl>
                  )}
                </CardContent>
              </Card>
            </div>
          </TabPanel>
        </TabPanels>
      </Tabs>

      {/* Delete Confirmation Modal */}
      <Modal
        isOpen={showDeleteModal}
        onClose={() => setShowDeleteModal(false)}
        title="Delete Client"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Are you sure you want to delete the client <strong>{client.clientName || client.clientId}</strong>?
            This action cannot be undone.
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="secondary" onClick={() => setShowDeleteModal(false)}>
              Cancel
            </Button>
            <Button variant="danger" onClick={handleDelete} disabled={deleteMutation.isPending}>
              {deleteMutation.isPending ? 'Deleting...' : 'Delete'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Secret Modal */}
      <Modal
        isOpen={showSecretModal}
        onClose={() => setShowSecretModal(false)}
        title="New Client Secret"
      >
        <div className="space-y-4">
          <p className="text-sm text-gray-600">
            Your new client secret has been generated. Make sure to copy it now - you won't be able to see it again.
          </p>
          <div className="p-3 bg-gray-100 rounded font-mono text-sm break-all">
            {newSecret}
          </div>
          <div className="flex justify-end">
            <Button onClick={() => {
              navigator.clipboard.writeText(newSecret);
              setShowSecretModal(false);
            }}>
              Copy & Close
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
