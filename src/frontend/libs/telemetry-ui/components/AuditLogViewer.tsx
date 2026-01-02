import { useState } from 'react';
import {
  ChevronDownIcon,
  ChevronRightIcon,
  CheckCircleIcon,
  XCircleIcon,
  UserIcon,
  ComputerDesktopIcon,
  GlobeAltIcon,
} from '@heroicons/react/24/outline';
import type { AuditLogEntry } from '../api/telemetryApi';

interface AuditLogViewerProps {
  logs: AuditLogEntry[];
  onUserClick?: (userId: string) => void;
}

const categoryColors: Record<string, string> = {
  Authentication: 'bg-blue-100 text-blue-800',
  User: 'bg-green-100 text-green-800',
  Security: 'bg-yellow-100 text-yellow-800',
  Token: 'bg-purple-100 text-purple-800',
  default: 'bg-gray-100 text-gray-800',
};

export function AuditLogViewer({ logs, onUserClick }: AuditLogViewerProps) {
  const [expandedLogs, setExpandedLogs] = useState<Set<string>>(new Set());

  const toggleExpand = (id: string) => {
    const newExpanded = new Set(expandedLogs);
    if (newExpanded.has(id)) {
      newExpanded.delete(id);
    } else {
      newExpanded.add(id);
    }
    setExpandedLogs(newExpanded);
  };

  return (
    <div className="bg-white rounded-lg shadow overflow-hidden">
      <table className="min-w-full">
        <thead className="bg-gray-50">
          <tr>
            <th className="w-8"></th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Time
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Category
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Action
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              User
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Status
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200">
          {logs.length === 0 ? (
            <tr>
              <td colSpan={6} className="px-4 py-8 text-center text-gray-500">
                No audit logs found
              </td>
            </tr>
          ) : (
            logs.map((log) => {
              const isExpanded = expandedLogs.has(log.id);
              const hasDetails = log.details || log.ipAddress || log.userAgent || log.errorMessage;

              return (
                <>
                  <tr
                    key={log.id}
                    className="hover:bg-gray-50 cursor-pointer"
                    onClick={() => hasDetails && toggleExpand(log.id)}
                  >
                    <td className="pl-4">
                      {hasDetails && (
                        isExpanded ? (
                          <ChevronDownIcon className="h-4 w-4 text-gray-400" />
                        ) : (
                          <ChevronRightIcon className="h-4 w-4 text-gray-400" />
                        )
                      )}
                    </td>
                    <td className="px-4 py-3 text-sm text-gray-500">
                      {new Date(log.timestamp).toLocaleString()}
                    </td>
                    <td className="px-4 py-3">
                      <span
                        className={`px-2 py-1 text-xs rounded-full ${
                          categoryColors[log.category] || categoryColors.default
                        }`}
                      >
                        {log.category}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      <span className="text-sm font-medium text-gray-900">
                        {log.action}
                      </span>
                      <span className="text-xs text-gray-500 ml-2">
                        {log.eventType}
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      {log.subjectId ? (
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            onUserClick?.(log.subjectId!);
                          }}
                          className="flex items-center gap-1 text-blue-600 hover:underline text-sm"
                        >
                          <UserIcon className="h-4 w-4" />
                          {log.subjectName || log.subjectEmail || log.subjectId}
                        </button>
                      ) : (
                        <span className="text-gray-400 text-sm">System</span>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      {log.success ? (
                        <CheckCircleIcon className="h-5 w-5 text-green-500" />
                      ) : (
                        <XCircleIcon className="h-5 w-5 text-red-500" />
                      )}
                    </td>
                  </tr>
                  {isExpanded && hasDetails && (
                    <tr key={`${log.id}-details`}>
                      <td colSpan={6} className="px-8 py-4 bg-gray-50">
                        <div className="grid grid-cols-2 gap-4 text-sm">
                          {log.clientId && (
                            <div>
                              <span className="text-gray-500">Client:</span>{' '}
                              <span className="font-mono">{log.clientId}</span>
                            </div>
                          )}
                          {log.resourceType && (
                            <div>
                              <span className="text-gray-500">Resource:</span>{' '}
                              {log.resourceType}
                              {log.resourceId && ` (${log.resourceId})`}
                            </div>
                          )}
                          {log.ipAddress && (
                            <div className="flex items-center gap-1">
                              <GlobeAltIcon className="h-4 w-4 text-gray-400" />
                              <span className="text-gray-500">IP:</span>{' '}
                              {log.ipAddress}
                            </div>
                          )}
                          {log.userAgent && (
                            <div className="flex items-center gap-1 col-span-2">
                              <ComputerDesktopIcon className="h-4 w-4 text-gray-400" />
                              <span className="text-gray-500">User Agent:</span>{' '}
                              <span className="truncate">{log.userAgent}</span>
                            </div>
                          )}
                          {log.activityId && (
                            <div>
                              <span className="text-gray-500">Activity ID:</span>{' '}
                              <code className="bg-gray-100 px-1 rounded text-xs">
                                {log.activityId}
                              </code>
                            </div>
                          )}
                          {log.errorMessage && (
                            <div className="col-span-2">
                              <span className="text-red-600">Error:</span>{' '}
                              <span className="text-red-700">{log.errorMessage}</span>
                            </div>
                          )}
                          {log.details && Object.keys(log.details).length > 0 && (
                            <div className="col-span-2">
                              <span className="text-gray-500">Details:</span>
                              <pre className="mt-1 text-xs bg-gray-100 p-2 rounded overflow-x-auto">
                                {JSON.stringify(log.details, null, 2)}
                              </pre>
                            </div>
                          )}
                        </div>
                      </td>
                    </tr>
                  )}
                </>
              );
            })
          )}
        </tbody>
      </table>
    </div>
  );
}
