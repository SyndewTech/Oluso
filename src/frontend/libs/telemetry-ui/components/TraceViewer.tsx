import { useState } from 'react';
import {
  ChevronDownIcon,
  ChevronRightIcon,
  CheckCircleIcon,
  XCircleIcon,
  MinusCircleIcon,
} from '@heroicons/react/24/outline';
import type { Trace, TraceSpan } from '../api/telemetryApi';

interface TraceViewerProps {
  trace: Trace;
}

const statusConfig = {
  ok: { icon: CheckCircleIcon, color: 'text-green-500' },
  error: { icon: XCircleIcon, color: 'text-red-500' },
  unset: { icon: MinusCircleIcon, color: 'text-gray-400' },
};

export function TraceViewer({ trace }: TraceViewerProps) {
  const [expandedSpans, setExpandedSpans] = useState<Set<string>>(
    new Set([trace.rootSpan.spanId])
  );

  const toggleSpan = (spanId: string) => {
    const newExpanded = new Set(expandedSpans);
    if (newExpanded.has(spanId)) {
      newExpanded.delete(spanId);
    } else {
      newExpanded.add(spanId);
    }
    setExpandedSpans(newExpanded);
  };

  // Build span tree
  const spansByParent = new Map<string | undefined, TraceSpan[]>();
  trace.spans.forEach((span) => {
    const children = spansByParent.get(span.parentSpanId) || [];
    children.push(span);
    spansByParent.set(span.parentSpanId, children);
  });

  const renderSpan = (span: TraceSpan, depth: number = 0) => {
    const children = spansByParent.get(span.spanId) || [];
    const hasChildren = children.length > 0;
    const isExpanded = expandedSpans.has(span.spanId);
    const StatusIcon = statusConfig[span.status].icon;

    // Calculate timing bar
    const traceStart = new Date(trace.startTime).getTime();
    const spanStart = new Date(span.startTime).getTime();
    const offsetPercent = ((spanStart - traceStart) / trace.duration) * 100;
    const widthPercent = (span.duration / trace.duration) * 100;

    return (
      <div key={span.spanId}>
        <div
          className={`flex items-center py-2 px-2 hover:bg-gray-50 cursor-pointer border-b`}
          style={{ paddingLeft: `${depth * 24 + 8}px` }}
          onClick={() => hasChildren && toggleSpan(span.spanId)}
        >
          <div className="w-5 flex-shrink-0">
            {hasChildren && (
              isExpanded ? (
                <ChevronDownIcon className="h-4 w-4 text-gray-400" />
              ) : (
                <ChevronRightIcon className="h-4 w-4 text-gray-400" />
              )
            )}
          </div>
          <StatusIcon className={`h-4 w-4 ${statusConfig[span.status].color} mr-2`} />
          <div className="flex-1 min-w-0">
            <div className="flex items-center gap-2">
              <span className="font-medium text-gray-900 truncate">
                {span.operationName}
              </span>
              <span className="text-xs text-gray-500">{span.serviceName}</span>
            </div>
          </div>
          <div className="w-64 flex-shrink-0 mx-4">
            <div className="h-2 bg-gray-100 rounded relative">
              <div
                className={`absolute h-full rounded ${
                  span.status === 'error' ? 'bg-red-400' : 'bg-blue-400'
                }`}
                style={{
                  left: `${offsetPercent}%`,
                  width: `${Math.max(widthPercent, 1)}%`,
                }}
              />
            </div>
          </div>
          <span className="text-sm text-gray-600 w-20 text-right">
            {formatDuration(span.duration)}
          </span>
        </div>
        {isExpanded && hasChildren && (
          <div>
            {children.map((child) => renderSpan(child, depth + 1))}
          </div>
        )}
      </div>
    );
  };

  return (
    <div className="bg-white rounded-lg shadow overflow-hidden">
      <div className="p-4 border-b bg-gray-50">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="font-medium text-gray-900">{trace.rootSpan.operationName}</h3>
            <p className="text-sm text-gray-500">
              Trace ID: <code className="bg-gray-100 px-1 rounded">{trace.traceId}</code>
            </p>
          </div>
          <div className="text-right">
            <p className="text-lg font-semibold">{formatDuration(trace.duration)}</p>
            <p className="text-sm text-gray-500">
              {new Date(trace.startTime).toLocaleString()}
            </p>
          </div>
        </div>
      </div>
      <div className="divide-y">
        {renderSpan(trace.rootSpan)}
      </div>
    </div>
  );
}

function formatDuration(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}us`;
  if (ms < 1000) return `${ms.toFixed(0)}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
}

// Trace list component
interface TraceListProps {
  traces: Trace[];
  onTraceSelect: (traceId: string) => void;
}

export function TraceList({ traces, onTraceSelect }: TraceListProps) {
  return (
    <div className="bg-white rounded-lg shadow overflow-hidden">
      <table className="min-w-full divide-y divide-gray-200">
        <thead className="bg-gray-50">
          <tr>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Operation
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Service
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Status
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Duration
            </th>
            <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">
              Time
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-200">
          {traces.length === 0 ? (
            <tr>
              <td colSpan={5} className="px-4 py-8 text-center text-gray-500">
                No traces found
              </td>
            </tr>
          ) : (
            traces.map((trace) => {
              const StatusIcon = statusConfig[trace.status].icon;
              return (
                <tr
                  key={trace.traceId}
                  className="hover:bg-gray-50 cursor-pointer"
                  onClick={() => onTraceSelect(trace.traceId)}
                >
                  <td className="px-4 py-3">
                    <span className="font-medium text-blue-600 hover:underline">
                      {trace.rootSpan.operationName}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-600">
                    {trace.serviceName}
                  </td>
                  <td className="px-4 py-3">
                    <StatusIcon
                      className={`h-5 w-5 ${statusConfig[trace.status].color}`}
                    />
                  </td>
                  <td className="px-4 py-3 text-sm">
                    {formatDuration(trace.duration)}
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-500">
                    {new Date(trace.startTime).toLocaleString()}
                  </td>
                </tr>
              );
            })
          )}
        </tbody>
      </table>
    </div>
  );
}
