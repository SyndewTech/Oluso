import { useState, useEffect } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import {
  MagnifyingGlassIcon,
  ArrowPathIcon,
  FunnelIcon,
  PlayIcon,
  StopIcon,
} from '@heroicons/react/24/outline';
import { telemetryApi, type LogEntry, type TelemetryQuery } from '../api/telemetryApi';
import { LogViewer } from '../components/LogViewer';

export default function LogsPage() {
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();

  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [categories, setCategories] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [totalCount, setTotalCount] = useState(0);
  const [liveTail, setLiveTail] = useState(false);

  // Filters
  const [searchTerm, setSearchTerm] = useState(searchParams.get('search') || '');
  const [level, setLevel] = useState(searchParams.get('level') || '');
  const [category, setCategory] = useState(searchParams.get('category') || '');
  const [page, setPage] = useState(1);
  const pageSize = 50;

  const loadLogs = async () => {
    try {
      setLoading(true);
      const query: TelemetryQuery = {
        searchTerm: searchTerm || undefined,
        level: level || undefined,
        category: category || undefined,
        page,
        pageSize,
      };
      const result = await telemetryApi.getLogs(query);
      setLogs(result.items);
      setTotalCount(result.totalCount);
      setError(null);
    } catch (err) {
      setError('Failed to load logs');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const loadCategories = async () => {
    try {
      const cats = await telemetryApi.getLogCategories();
      setCategories(cats);
    } catch (err) {
      console.error('Failed to load categories:', err);
    }
  };

  useEffect(() => {
    loadCategories();
  }, []);

  useEffect(() => {
    loadLogs();
  }, [searchTerm, level, category, page]);

  useEffect(() => {
    if (liveTail) {
      const interval = setInterval(loadLogs, 2000);
      return () => clearInterval(interval);
    }
  }, [liveTail, searchTerm, level, category]);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setPage(1);
    const params = new URLSearchParams();
    if (searchTerm) params.set('search', searchTerm);
    if (level) params.set('level', level);
    if (category) params.set('category', category);
    setSearchParams(params);
  };

  const handleTraceClick = (traceId: string) => {
    navigate(`/telemetry/traces/${traceId}`);
  };

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div className="p-6">
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-2xl font-semibold text-gray-900">Application Logs</h1>
          <p className="text-gray-500 mt-1">
            Search and filter logs from all services
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setLiveTail(!liveTail)}
            className={`inline-flex items-center px-3 py-2 rounded-md text-sm ${
              liveTail
                ? 'bg-green-600 text-white'
                : 'bg-gray-100 text-gray-700 hover:bg-gray-200'
            }`}
          >
            {liveTail ? (
              <>
                <StopIcon className="h-4 w-4 mr-1" />
                Stop Live Tail
              </>
            ) : (
              <>
                <PlayIcon className="h-4 w-4 mr-1" />
                Live Tail
              </>
            )}
          </button>
          <button
            onClick={loadLogs}
            disabled={loading}
            className="inline-flex items-center px-3 py-2 bg-gray-100 rounded-md hover:bg-gray-200 text-sm"
          >
            <ArrowPathIcon className={`h-4 w-4 mr-1 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>
      </div>

      {/* Filters */}
      <form onSubmit={handleSearch} className="mb-6">
        <div className="flex gap-4 items-end">
          <div className="flex-1">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Search
            </label>
            <div className="relative">
              <MagnifyingGlassIcon className="absolute left-3 top-1/2 -translate-y-1/2 h-5 w-5 text-gray-400" />
              <input
                type="text"
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                placeholder="Search logs..."
                className="w-full pl-10 pr-4 py-2 border rounded-md"
              />
            </div>
          </div>
          <div className="w-40">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Level
            </label>
            <select
              value={level}
              onChange={(e) => setLevel(e.target.value)}
              className="w-full border rounded-md px-3 py-2"
            >
              <option value="">All Levels</option>
              <option value="trace">Trace</option>
              <option value="debug">Debug</option>
              <option value="info">Info</option>
              <option value="warn">Warning</option>
              <option value="error">Error</option>
              <option value="fatal">Fatal</option>
            </select>
          </div>
          <div className="w-48">
            <label className="block text-sm font-medium text-gray-700 mb-1">
              Category
            </label>
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="w-full border rounded-md px-3 py-2"
            >
              <option value="">All Categories</option>
              {categories.map((cat) => (
                <option key={cat} value={cat}>
                  {cat}
                </option>
              ))}
            </select>
          </div>
          <button
            type="submit"
            className="inline-flex items-center px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700"
          >
            <FunnelIcon className="h-4 w-4 mr-1" />
            Filter
          </button>
        </div>
      </form>

      {error && (
        <div className="bg-red-50 text-red-700 p-4 rounded-md mb-6">{error}</div>
      )}

      {/* Results info */}
      <div className="flex justify-between items-center mb-4">
        <p className="text-sm text-gray-500">
          {totalCount > 0 ? (
            <>
              Showing {(page - 1) * pageSize + 1} - {Math.min(page * pageSize, totalCount)} of{' '}
              {totalCount.toLocaleString()} logs
            </>
          ) : (
            'No logs found'
          )}
        </p>
        {liveTail && (
          <span className="inline-flex items-center text-sm text-green-600">
            <span className="h-2 w-2 bg-green-500 rounded-full mr-2 animate-pulse" />
            Live updating...
          </span>
        )}
      </div>

      {/* Log viewer */}
      <LogViewer logs={logs} onTraceClick={handleTraceClick} />

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
