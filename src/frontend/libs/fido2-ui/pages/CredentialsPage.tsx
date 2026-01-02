import { useEffect, useState, useCallback } from 'react';
import {
  TrashIcon,
  MagnifyingGlassIcon,
  FingerPrintIcon,
  DevicePhoneMobileIcon,
  KeyIcon,
} from '@heroicons/react/24/outline';
import { fido2Api, type Fido2UserSummary, type Fido2Credential } from '../api/fido2Api';

interface CredentialListProps {
  credentials: Fido2Credential[];
  userId: string;
  onDelete: (credentialId: string) => void;
  deleting: string | null;
}

function CredentialList({ credentials, onDelete, deleting }: CredentialListProps) {
  if (credentials.length === 0) {
    return (
      <p className="text-sm text-gray-500 italic">No credentials registered</p>
    );
  }

  return (
    <div className="space-y-2">
      {credentials.map((cred) => (
        <div
          key={cred.id}
          className="flex items-center justify-between p-3 bg-gray-50 rounded-lg"
        >
          <div className="flex items-center space-x-3">
            {cred.authenticatorType === 'platform' ? (
              <DevicePhoneMobileIcon className="h-5 w-5 text-gray-400" />
            ) : (
              <KeyIcon className="h-5 w-5 text-gray-400" />
            )}
            <div>
              <p className="text-sm font-medium text-gray-900">
                {cred.displayName || 'Unnamed credential'}
              </p>
              <p className="text-xs text-gray-500">
                Created: {new Date(cred.createdAt).toLocaleDateString()}
                {cred.lastUsedAt && (
                  <> | Last used: {new Date(cred.lastUsedAt).toLocaleDateString()}</>
                )}
              </p>
            </div>
          </div>
          <button
            onClick={() => onDelete(cred.id)}
            disabled={deleting === cred.id}
            className="p-2 text-red-600 hover:text-red-800 hover:bg-red-50 rounded-md disabled:opacity-50"
            title="Delete credential"
          >
            {deleting === cred.id ? (
              <span className="animate-spin">...</span>
            ) : (
              <TrashIcon className="h-4 w-4" />
            )}
          </button>
        </div>
      ))}
    </div>
  );
}

export default function CredentialsPage() {
  const [users, setUsers] = useState<Fido2UserSummary[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(1);
  const [expandedUser, setExpandedUser] = useState<string | null>(null);
  const [userCredentials, setUserCredentials] = useState<Record<string, Fido2Credential[]>>({});
  const [loadingCredentials, setLoadingCredentials] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<string | null>(null);

  const pageSize = 20;

  const loadUsers = useCallback(async () => {
    try {
      setLoading(true);
      const data = await fido2Api.getUsersWithCredentials({
        search: search || undefined,
        page,
        pageSize,
      });
      setUsers(data.users);
      setTotalCount(data.totalCount);
      setError(null);
    } catch (err) {
      setError('Failed to load users with FIDO2 credentials');
      console.error('Error loading users:', err);
    } finally {
      setLoading(false);
    }
  }, [search, page]);

  useEffect(() => {
    loadUsers();
  }, [loadUsers]);

  const handleToggleUser = async (userId: string) => {
    if (expandedUser === userId) {
      setExpandedUser(null);
      return;
    }

    setExpandedUser(userId);

    if (!userCredentials[userId]) {
      try {
        setLoadingCredentials(userId);
        const credentials = await fido2Api.getUserCredentials(userId);
        setUserCredentials((prev) => ({ ...prev, [userId]: credentials }));
      } catch (err) {
        console.error('Error loading credentials:', err);
      } finally {
        setLoadingCredentials(null);
      }
    }
  };

  const handleDeleteCredential = async (userId: string, credentialId: string) => {
    if (!confirm('Are you sure you want to delete this credential?')) {
      return;
    }

    try {
      setDeleting(credentialId);
      await fido2Api.deleteUserCredential(userId, credentialId);
      setUserCredentials((prev) => ({
        ...prev,
        [userId]: prev[userId]?.filter((c) => c.id !== credentialId) ?? [],
      }));
      // Reload users to update credential count
      loadUsers();
    } catch (err) {
      console.error('Error deleting credential:', err);
      alert('Failed to delete credential');
    } finally {
      setDeleting(null);
    }
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">FIDO2 Credentials</h1>
        <span className="text-sm text-gray-500">
          {totalCount} user{totalCount !== 1 ? 's' : ''} with credentials
        </span>
      </div>

      <div className="flex items-center space-x-4">
        <div className="flex-1 max-w-md">
          <div className="relative">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 transform -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="text"
              placeholder="Search users..."
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
              className="w-full pl-10 pr-4 py-2 border border-gray-300 rounded-md focus:ring-primary-500 focus:border-primary-500"
            />
          </div>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 border border-red-200 rounded-lg p-4">
          <p className="text-red-700">{error}</p>
        </div>
      )}

      {loading ? (
        <div className="space-y-4">
          {[1, 2, 3].map((i) => (
            <div key={i} className="bg-gray-200 h-16 rounded-lg animate-pulse" />
          ))}
        </div>
      ) : users.length === 0 ? (
        <div className="text-center py-12 bg-white rounded-lg shadow">
          <FingerPrintIcon className="mx-auto h-12 w-12 text-gray-400" />
          <h3 className="mt-2 text-sm font-medium text-gray-900">No credentials found</h3>
          <p className="mt-1 text-sm text-gray-500">
            {search ? 'No users match your search.' : 'No users have registered FIDO2 credentials yet.'}
          </p>
        </div>
      ) : (
        <div className="bg-white shadow rounded-lg overflow-hidden">
          <ul className="divide-y divide-gray-200">
            {users.map((user) => (
              <li key={user.userId}>
                <button
                  onClick={() => handleToggleUser(user.userId)}
                  className="w-full px-6 py-4 hover:bg-gray-50 text-left"
                >
                  <div className="flex items-center justify-between">
                    <div className="flex items-center space-x-4">
                      <FingerPrintIcon className="h-6 w-6 text-gray-400" />
                      <div>
                        <p className="font-medium text-gray-900">
                          {user.displayName || user.userName || user.email || user.userId}
                        </p>
                        {user.email && user.displayName && (
                          <p className="text-sm text-gray-500">{user.email}</p>
                        )}
                      </div>
                    </div>
                    <div className="flex items-center space-x-4">
                      <span className="inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium bg-primary-100 text-primary-800">
                        {user.credentialCount} credential{user.credentialCount !== 1 ? 's' : ''}
                      </span>
                      <svg
                        className={`h-5 w-5 text-gray-400 transform transition-transform ${
                          expandedUser === user.userId ? 'rotate-180' : ''
                        }`}
                        fill="none"
                        viewBox="0 0 24 24"
                        stroke="currentColor"
                      >
                        <path
                          strokeLinecap="round"
                          strokeLinejoin="round"
                          strokeWidth={2}
                          d="M19 9l-7 7-7-7"
                        />
                      </svg>
                    </div>
                  </div>
                </button>

                {expandedUser === user.userId && (
                  <div className="px-6 pb-4 border-t border-gray-100 bg-gray-50">
                    <div className="pt-4">
                      {loadingCredentials === user.userId ? (
                        <div className="animate-pulse space-y-2">
                          {[1, 2].map((i) => (
                            <div key={i} className="bg-gray-200 h-12 rounded-lg" />
                          ))}
                        </div>
                      ) : (
                        <CredentialList
                          credentials={userCredentials[user.userId] ?? []}
                          userId={user.userId}
                          onDelete={(credId) => handleDeleteCredential(user.userId, credId)}
                          deleting={deleting}
                        />
                      )}
                    </div>
                  </div>
                )}
              </li>
            ))}
          </ul>
        </div>
      )}

      {totalPages > 1 && (
        <div className="flex items-center justify-between">
          <p className="text-sm text-gray-700">
            Showing page {page} of {totalPages}
          </p>
          <div className="flex space-x-2">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Previous
            </button>
            <button
              onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
              disabled={page === totalPages}
              className="px-4 py-2 border border-gray-300 rounded-md text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50 disabled:cursor-not-allowed"
            >
              Next
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
