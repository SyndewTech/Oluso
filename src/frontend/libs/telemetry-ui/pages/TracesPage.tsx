import { useState, useEffect } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  ArrowLeftIcon,
} from '@heroicons/react/24/outline';
import { telemetryApi, type Trace, type TelemetryQuery } from '../api/telemetryApi';
import { TraceViewer, TraceList } from '../components/TraceViewer';

export default function TracesPage() {
  const { traceId } = useParams<{ traceId?: string }>();
  const navigate = useNavigate();

  const [traces, setTraces] = useState<Trace[]>([]);
  const [selectedTrace, setSelectedTrace] = useState<Trace | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [totalCount, setTotalCount] = useState(0);
  const [searchTerm, setSearchTerm] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 20;

  const loadTraces = async () => {
    try {
      setLoading(true);
      const query: TelemetryQuery = {
        searchTerm: searchTerm || undefined,
        page,
        pageSize,
      };
      const result = await telemetryApi.getTraces(query);
      setTraces(result.items);
      setTotalCount(result.totalCount);
      setError(null);
    } catch (err) {
      setError('Failed to load traces');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const loadTrace = async (id: string) => {
    try {
      setLoading(true);
      const trace = await telemetryApi.getTrace(id);
      setSelectedTrace(trace);
      setError(null);
    } catch (err) {
      setError('Failed to load trace details');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    if (traceId) {
      loadTrace(traceId);
    } else {
      loadTraces();
    }
  }, [traceId, searchTerm, page]);

  const handleTraceSelect = (id: string) => {
    navigate(`/telemetry/traces/${id}`);
  };

  const handleBack = () => {
    setSelectedTrace(null);
    navigate('/telemetry/traces');
  };

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    loadTraces();
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  // Detail view
  if (traceId || selectedTrace) {
    return (
      <div className="p-6">
        <div className="mb-6">
          <button
            onClick={handleBack}
            className="inline-flex items-center text-gray-600 hover:text-gray-900 mb-4"
          >
            <ArrowLeftIcon className="h-4 w-4 mr-1" />
            Back to Traces
          </button>
          <h1 className="text-2xl font-semibold text-gray-900">Trace Details</h1>
        </div>

        {error && (
          <div className="bg-red-50 text-red-700 p-4 rounded-md mb-6">{error}</div>
        )}

        {loading ? (
          <div className="flex items-center justify-center p-12">
            <ArrowPathIcon className="h-8 w-8 animate-spin text-gray-400" />
          </div>
        ) : selectedTrace ? (
          <TraceViewer trace={selectedTrace} />
        ) : (
          <div className="text-center text-gray-500 p-12">Trace not found</div>
        )}
      </div>
    );
  }

  // List view
  return (
    <div className="p-6">
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">Distributed Traces</h1>
          <p className="text-gray-500 mt-1">
            Track requests across services and identify performance bottlenecks
          </p>
        </div>
        <button
          onClick={loadTraces}
          disabled={loading}
          className="inline-flex items-center px-3 py-2 bg-gray-100 rounded-md hover:bg-gray-200 text-sm"
        >
          <ArrowPathIcon className={`h-4 w-4 mr-1 ${loading ? 'animate-spin' : ''}`} />
          Refresh
        </button>
      </div>

      {/* Search */}
      <form onSubmit={handleSearch} className="mb-6">
        <div className="flex gap-4">
          <div className="flex-1 relative">
            <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-gray-400" />
            <input
              type="text"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              placeholder="Search by operation name, trace ID, or service..."
              className="w-full pl-10 pr-4 py-2 border rounded-md"
            />
          </div>
          <button
            type="submit"
            className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
          >
            Search
          </button>
        </div>
      </form>

      {error && (
        <div className="bg-red-50 text-red-700 p-4 rounded-md mb-6">{error}</div>
      )}

      {/* Results info */}
      <div className="mb-4">
        <p className="text-sm text-gray-500">
          {totalCount > 0 ? (
            <>
              Showing {(page - 1) * pageSize + 1} - {Math.min(page * pageSize, totalCount)} of{' '}
              {totalCount.toLocaleString()} traces
            </>
          ) : (
            'No traces found'
          )}
        </p>
      </div>

      {/* Trace list */}
      {loading && traces.length === 0 ? (
        <div className="flex items-center justify-center p-12">
          <ArrowPathIcon className="h-8 w-8 animate-spin text-gray-400" />
        </div>
      ) : (
        <TraceList traces={traces} onTraceSelect={handleTraceSelect} />
      )}

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex justify-center items-center gap-2 mt-6">
          <button
            onClick={() => setPage(Math.max(1, page - 1))}
            disabled={page === 1}
            className="px-3 py-1 border rounded-md disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Previous
          </button>
          <span className="text-sm text-gray-600">
            Page {page} of {totalPages}
          </span>
          <button
            onClick={() => setPage(Math.min(totalPages, page + 1))}
            disabled={page === totalPages}
            className="px-3 py-1 border rounded-md disabled:opacity-50 disabled:cursor-not-allowed"
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
}
