import { useState } from 'react';
import {
  ChevronDownIcon,
  ChevronRightIcon,
  ExclamationTriangleIcon,
  ExclamationCircleIcon,
  InformationCircleIcon,
  BugAntIcon,
} from '@heroicons/react/24/outline';
import type { LogEntry } from '../api/telemetryApi';

interface LogViewerProps {
  logs: LogEntry[];
  onTraceClick?: (traceId: string) => void;
}

const levelConfig = {
  trace: { color: 'text-gray-400', bg: 'bg-gray-100', icon: BugAntIcon },
  debug: { color: 'text-gray-500', bg: 'bg-gray-100', icon: BugAntIcon },
  info: { color: 'text-blue-600', bg: 'bg-blue-100', icon: InformationCircleIcon },
  warn: { color: 'text-yellow-600', bg: 'bg-yellow-100', icon: ExclamationTriangleIcon },
  error: { color: 'text-red-600', bg: 'bg-red-100', icon: ExclamationCircleIcon },
  fatal: { color: 'text-red-800', bg: 'bg-red-200', icon: ExclamationCircleIcon },
};

export function LogViewer({ logs, onTraceClick }: LogViewerProps) {
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
    <div className="bg-gray-900 rounded-lg overflow-hidden font-mono text-sm">
      <div className="max-h-[600px] overflow-y-auto">
        {logs.length === 0 ? (
          <div className="p-8 text-center text-gray-500">No logs found</div>
        ) : (
          logs.map((log) => {
            const config = levelConfig[log.level];
            const isExpanded = expandedLogs.has(log.id);
            const hasDetails = log.properties || log.exception || log.traceId;

            return (
              <div key={log.id} className="border-b border-gray-800 hover:bg-gray-800/50">
                <div
                  className="flex items-start p-2 cursor-pointer"
                  onClick={() => hasDetails && toggleExpand(log.id)}
                >
                  <div className="w-5 flex-shrink-0 mt-0.5">
                    {hasDetails && (
                      isExpanded ? (
                        <ChevronDownIcon className="h-4 w-4 text-gray-500" />
                      ) : (
                        <ChevronRightIcon className="h-4 w-4 text-gray-500" />
                      )
                    )}
                  </div>
                  <span className="text-gray-500 w-44 flex-shrink-0">
                    {new Date(log.timestamp).toLocaleString()}
                  </span>
                  <span
                    className={`px-2 py-0.5 rounded text-xs font-medium w-16 text-center flex-shrink-0 ${config.bg} ${config.color}`}
                  >
                    {log.level.toUpperCase()}
                  </span>
                  <span className="text-purple-400 w-32 flex-shrink-0 ml-2 truncate">
                    {log.category}
                  </span>
                  <span className="text-gray-300 ml-2 flex-1 break-all">
                    {log.message}
                  </span>
                </div>
                {isExpanded && hasDetails && (
                  <div className="px-8 pb-4 space-y-2">
                    {log.traceId && (
                      <div className="flex items-center gap-2">
                        <span className="text-gray-500">Trace:</span>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            onTraceClick?.(log.traceId!);
                          }}
                          className="text-blue-400 hover:underline"
                        >
                          {log.traceId}
                        </button>
                        {log.spanId && (
                          <span className="text-gray-600">/ {log.spanId}</span>
                        )}
                      </div>
                    )}
                    {log.properties && Object.keys(log.properties).length > 0 && (
                      <div>
                        <span className="text-gray-500">Properties:</span>
                        <pre className="mt-1 text-xs text-gray-400 bg-gray-800 p-2 rounded overflow-x-auto">
                          {JSON.stringify(log.properties, null, 2)}
                        </pre>
                      </div>
                    )}
                    {log.exception && (
                      <div>
                        <span className="text-red-400">Exception:</span>
                        <pre className="mt-1 text-xs text-red-300 bg-red-900/20 p-2 rounded overflow-x-auto whitespace-pre-wrap">
                          {log.exception}
                        </pre>
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
