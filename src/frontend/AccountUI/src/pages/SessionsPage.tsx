import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { formatDistanceToNow } from 'date-fns';
import { ComputerDesktopIcon, DevicePhoneMobileIcon, GlobeAltIcon } from '@heroicons/react/24/outline';
import { apiClient } from '../services/api';

interface Session {
  id: string;
  sessionId?: string;
  clientId?: string;
  clientName?: string;
  createdAt: string;
  expiresAt?: string;
  lastActivityAt?: string;
  ipAddress?: string;
  userAgent?: string;
  location?: string;
  isCurrent: boolean;
}

interface SessionList {
  sessions: Session[];
  totalCount: number;
}

function getDeviceIcon(userAgent?: string) {
  if (!userAgent) return GlobeAltIcon;
  if (/mobile|android|iphone|ipad/i.test(userAgent)) return DevicePhoneMobileIcon;
  return ComputerDesktopIcon;
}

function parseUserAgent(userAgent?: string): { browser: string; os: string } {
  if (!userAgent) return { browser: 'Unknown browser', os: 'Unknown device' };

  let browser = 'Unknown browser';
  let os = 'Unknown device';

  // Parse browser
  if (/chrome/i.test(userAgent)) browser = 'Chrome';
  else if (/firefox/i.test(userAgent)) browser = 'Firefox';
  else if (/safari/i.test(userAgent)) browser = 'Safari';
  else if (/edge/i.test(userAgent)) browser = 'Edge';

  // Parse OS
  if (/windows/i.test(userAgent)) os = 'Windows';
  else if (/macintosh|mac os/i.test(userAgent)) os = 'macOS';
  else if (/linux/i.test(userAgent)) os = 'Linux';
  else if (/android/i.test(userAgent)) os = 'Android';
  else if (/iphone|ipad/i.test(userAgent)) os = 'iOS';

  return { browser, os };
}

export function SessionsPage() {
  const queryClient = useQueryClient();

  const { data, isLoading } = useQuery({
    queryKey: ['sessions'],
    queryFn: async () => {
      const response = await apiClient.get<SessionList>('/api/account/sessions');
      return response.data;
    },
  });

  const revokeSessionMutation = useMutation({
    mutationFn: async (sessionId: string) => {
      await apiClient.delete(`/api/account/sessions/${sessionId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
      toast.success('Session revoked');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to revoke session');
    },
  });

  const revokeAllMutation = useMutation({
    mutationFn: async () => {
      const response = await apiClient.post('/api/account/sessions/revoke-all');
      return response.data;
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['sessions'] });
      toast.success(`Revoked ${data.count} sessions`);
    },
    onError: () => {
      toast.error('Failed to revoke sessions');
    },
  });

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  const sessions = data?.sessions || [];
  const otherSessions = sessions.filter(s => !s.isCurrent);

  return (
    <div className="max-w-3xl">
      <div className="mb-8 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Active Sessions</h1>
          <p className="mt-1 text-sm text-gray-500">
            Manage devices and sessions where you're signed in.
          </p>
        </div>
        {otherSessions.length > 0 && (
          <button
            onClick={() => revokeAllMutation.mutate()}
            disabled={revokeAllMutation.isPending}
            className="rounded-md bg-red-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-red-500 disabled:opacity-50"
          >
            {revokeAllMutation.isPending ? 'Revoking...' : 'Sign out all other sessions'}
          </button>
        )}
      </div>

      <div className="space-y-4">
        {sessions.map((session) => {
          const DeviceIcon = getDeviceIcon(session.userAgent);
          const { browser, os } = parseUserAgent(session.userAgent);

          return (
            <div
              key={session.id}
              className={`bg-white shadow rounded-lg p-6 ${
                session.isCurrent ? 'ring-2 ring-indigo-500' : ''
              }`}
            >
              <div className="flex items-start gap-x-4">
                <div className="flex-shrink-0">
                  <DeviceIcon className="h-8 w-8 text-gray-400" />
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-x-2">
                    <p className="text-sm font-semibold text-gray-900">
                      {browser} on {os}
                    </p>
                    {session.isCurrent && (
                      <span className="inline-flex items-center rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
                        Current session
                      </span>
                    )}
                  </div>
                  {session.clientName && (
                    <p className="text-sm text-gray-500">{session.clientName}</p>
                  )}
                  <div className="mt-1 flex items-center gap-x-4 text-xs text-gray-500">
                    {session.ipAddress && <span>{session.ipAddress}</span>}
                    {session.location && <span>{session.location}</span>}
                    <span>
                      Active{' '}
                      {formatDistanceToNow(new Date(session.lastActivityAt || session.createdAt), {
                        addSuffix: true,
                      })}
                    </span>
                  </div>
                </div>
                {!session.isCurrent && (
                  <button
                    onClick={() => revokeSessionMutation.mutate(session.sessionId || session.id)}
                    disabled={revokeSessionMutation.isPending}
                    className="rounded-md bg-white px-3 py-1.5 text-sm font-semibold text-red-600 shadow-sm ring-1 ring-inset ring-red-300 hover:bg-red-50 disabled:opacity-50"
                  >
                    Revoke
                  </button>
                )}
              </div>
            </div>
          );
        })}

        {sessions.length === 0 && (
          <div className="text-center py-12 bg-white rounded-lg shadow">
            <p className="text-gray-500">No active sessions found.</p>
          </div>
        )}
      </div>
    </div>
  );
}
