import { useState, useEffect, useCallback, useRef } from 'react';
import api from '../services/api';

export interface EnumSourceConfig {
  endpoint: string;
  path?: string;  // JSON path to the array of options in response
  valueField: string;
  labelField: string;
  descriptionField?: string;
}

export interface EnumOption {
  value: string;
  label: string;
  description?: string;
  // Additional fields from the response
  [key: string]: unknown;
}

interface CacheEntry {
  data: EnumOption[];
  timestamp: number;
}

// Simple in-memory cache for enum sources
const enumSourceCache = new Map<string, CacheEntry>();
const CACHE_TTL = 5 * 60 * 1000; // 5 minutes

function getCacheKey(config: EnumSourceConfig): string {
  return `${config.endpoint}:${config.path || ''}`;
}

function getFromCache(key: string): EnumOption[] | null {
  const entry = enumSourceCache.get(key);
  if (!entry) return null;

  if (Date.now() - entry.timestamp > CACHE_TTL) {
    enumSourceCache.delete(key);
    return null;
  }

  return entry.data;
}

function setCache(key: string, data: EnumOption[]): void {
  enumSourceCache.set(key, {
    data,
    timestamp: Date.now(),
  });
}

// Extract value from nested path like "plans" or "data.items"
function extractFromPath(data: unknown, path: string): unknown[] {
  if (!path) return Array.isArray(data) ? data : [];

  const parts = path.split('.');
  let current: unknown = data;

  for (const part of parts) {
    if (current === null || current === undefined) return [];
    if (typeof current !== 'object') return [];
    current = (current as Record<string, unknown>)[part];
  }

  return Array.isArray(current) ? current : [];
}

export function useEnumSource(config: EnumSourceConfig | null | undefined) {
  const [options, setOptions] = useState<EnumOption[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abortControllerRef = useRef<AbortController | null>(null);

  const fetchOptions = useCallback(async () => {
    if (!config) {
      setOptions([]);
      return;
    }

    const cacheKey = getCacheKey(config);
    const cached = getFromCache(cacheKey);
    if (cached) {
      setOptions(cached);
      return;
    }

    // Cancel any pending request
    if (abortControllerRef.current) {
      abortControllerRef.current.abort();
    }
    abortControllerRef.current = new AbortController();

    setLoading(true);
    setError(null);

    try {
      const response = await api.get(config.endpoint, {
        signal: abortControllerRef.current.signal,
      });

      const items = extractFromPath(response.data, config.path || '');

      const mappedOptions: EnumOption[] = items.map((item: unknown) => {
        const record = item as Record<string, unknown>;
        return {
          value: String(record[config.valueField] ?? ''),
          label: String(record[config.labelField] ?? record[config.valueField] ?? ''),
          description: config.descriptionField
            ? String(record[config.descriptionField] ?? '')
            : undefined,
          ...record, // Include all fields for custom rendering
        };
      });

      setOptions(mappedOptions);
      setCache(cacheKey, mappedOptions);
    } catch (err) {
      if ((err as Error).name !== 'AbortError') {
        setError((err as Error).message || 'Failed to load options');
        setOptions([]);
      }
    } finally {
      setLoading(false);
    }
  }, [config]);

  useEffect(() => {
    fetchOptions();

    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort();
      }
    };
  }, [fetchOptions]);

  const refresh = useCallback(() => {
    if (config) {
      const cacheKey = getCacheKey(config);
      enumSourceCache.delete(cacheKey);
      fetchOptions();
    }
  }, [config, fetchOptions]);

  return { options, loading, error, refresh };
}

// Hook to batch multiple enum sources
export function useEnumSources(configs: Record<string, EnumSourceConfig | null | undefined>) {
  const [optionsMap, setOptionsMap] = useState<Record<string, EnumOption[]>>({});
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    const fetchAll = async () => {
      setLoading(true);
      const results: Record<string, EnumOption[]> = {};

      await Promise.all(
        Object.entries(configs).map(async ([key, config]) => {
          if (!config) {
            results[key] = [];
            return;
          }

          const cacheKey = getCacheKey(config);
          const cached = getFromCache(cacheKey);
          if (cached) {
            results[key] = cached;
            return;
          }

          try {
            const response = await api.get(config.endpoint);
            const items = extractFromPath(response.data, config.path || '');

            const mappedOptions: EnumOption[] = items.map((item: unknown) => {
              const record = item as Record<string, unknown>;
              return {
                value: String(record[config.valueField] ?? ''),
                label: String(record[config.labelField] ?? record[config.valueField] ?? ''),
                description: config.descriptionField
                  ? String(record[config.descriptionField] ?? '')
                  : undefined,
                ...record,
              };
            });

            results[key] = mappedOptions;
            setCache(cacheKey, mappedOptions);
          } catch {
            results[key] = [];
          }
        })
      );

      setOptionsMap(results);
      setLoading(false);
    };

    const hasConfigs = Object.values(configs).some(c => c !== null && c !== undefined);
    if (hasConfigs) {
      fetchAll();
    }
  }, [JSON.stringify(configs)]);

  return { optionsMap, loading };
}

// Utility to clear the cache
export function clearEnumSourceCache(): void {
  enumSourceCache.clear();
}
