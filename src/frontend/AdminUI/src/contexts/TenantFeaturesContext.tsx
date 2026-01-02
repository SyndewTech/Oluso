import React, { createContext, useContext, useState, useEffect, useCallback } from 'react';
import { featureService } from '../services/featureService';
import type {
  TenantFeaturesResponse,
  FeatureStatus,
  LimitStatus,
  FeatureKey,
  LimitKey,
} from '../types/features';

interface TenantFeaturesContextType {
  // Feature data
  features: Record<string, FeatureStatus>;
  limits: Record<string, LimitStatus>;
  plan: TenantFeaturesResponse['plan'];
  subscriptionStatus?: string;
  isTrialing: boolean;
  billingEnabled: boolean;

  // Loading state
  isLoading: boolean;
  error: string | null;

  // Helper functions
  hasFeature: (featureKey: FeatureKey | string) => boolean;
  getFeature: (featureKey: FeatureKey | string) => FeatureStatus | undefined;
  getLimit: (limitKey: LimitKey | string) => LimitStatus | undefined;
  isWithinLimit: (limitKey: LimitKey | string, amount?: number) => boolean;
  getRemainingLimit: (limitKey: LimitKey | string) => number;

  // Refresh
  refresh: () => Promise<void>;
}

const TenantFeaturesContext = createContext<TenantFeaturesContextType | undefined>(undefined);

export function TenantFeaturesProvider({ children }: { children: React.ReactNode }) {
  const [data, setData] = useState<TenantFeaturesResponse | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const fetchFeatures = useCallback(async () => {
    try {
      setIsLoading(true);
      setError(null);
      const response = await featureService.getTenantFeatures();
      setData(response);
    } catch (err) {
      console.error('Failed to fetch tenant features:', err);
      setError(err instanceof Error ? err.message : 'Failed to load features');
      // On error, default to all features enabled (graceful degradation)
      setData(null);
    } finally {
      setIsLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchFeatures();
  }, [fetchFeatures]);

  const hasFeature = useCallback(
    (featureKey: string): boolean => {
      if (!data?.billingEnabled) return true; // If billing not enabled, all features available
      if (!data?.features) return true;
      return data.features[featureKey]?.enabled ?? false;
    },
    [data]
  );

  const getFeature = useCallback(
    (featureKey: string): FeatureStatus | undefined => {
      return data?.features?.[featureKey];
    },
    [data]
  );

  const getLimit = useCallback(
    (limitKey: string): LimitStatus | undefined => {
      return data?.limits?.[limitKey];
    },
    [data]
  );

  const isWithinLimit = useCallback(
    (limitKey: string, amount = 1): boolean => {
      if (!data?.billingEnabled) return true; // If billing not enabled, unlimited
      const limit = data?.limits?.[limitKey];
      if (!limit) return true;
      if (limit.isUnlimited) return true;
      return limit.current + amount <= limit.limit;
    },
    [data]
  );

  const getRemainingLimit = useCallback(
    (limitKey: string): number => {
      if (!data?.billingEnabled) return Infinity;
      const limit = data?.limits?.[limitKey];
      if (!limit) return Infinity;
      if (limit.isUnlimited) return Infinity;
      return limit.remaining;
    },
    [data]
  );

  const value: TenantFeaturesContextType = {
    features: data?.features ?? {},
    limits: data?.limits ?? {},
    plan: data?.plan,
    subscriptionStatus: data?.subscriptionStatus,
    isTrialing: data?.isTrialing ?? false,
    billingEnabled: data?.billingEnabled ?? false,
    isLoading,
    error,
    hasFeature,
    getFeature,
    getLimit,
    isWithinLimit,
    getRemainingLimit,
    refresh: fetchFeatures,
  };

  return (
    <TenantFeaturesContext.Provider value={value}>
      {children}
    </TenantFeaturesContext.Provider>
  );
}

export function useTenantFeatures(): TenantFeaturesContextType {
  const context = useContext(TenantFeaturesContext);
  if (context === undefined) {
    throw new Error('useTenantFeatures must be used within a TenantFeaturesProvider');
  }
  return context;
}

/**
 * Hook to check if a specific feature is available
 */
export function useFeature(featureKey: FeatureKey | string): {
  enabled: boolean;
  feature?: FeatureStatus;
  isLoading: boolean;
} {
  const { hasFeature, getFeature, isLoading } = useTenantFeatures();
  return {
    enabled: hasFeature(featureKey),
    feature: getFeature(featureKey),
    isLoading,
  };
}

/**
 * Hook to check a usage limit
 */
export function useLimit(limitKey: LimitKey | string): {
  limit?: LimitStatus;
  isWithinLimit: (amount?: number) => boolean;
  remaining: number;
  isLoading: boolean;
} {
  const { getLimit, isWithinLimit, getRemainingLimit, isLoading } = useTenantFeatures();
  return {
    limit: getLimit(limitKey),
    isWithinLimit: (amount = 1) => isWithinLimit(limitKey, amount),
    remaining: getRemainingLimit(limitKey),
    isLoading,
  };
}

export default TenantFeaturesContext;
