import React from 'react';
import { useTenantFeatures, useFeature, useLimit } from '../../contexts/TenantFeaturesContext';
import type { FeatureKey, LimitKey } from '../../types/features';

interface FeatureGateProps {
  /** Feature key to check */
  feature: FeatureKey | string;
  /** Content to render when feature is available */
  children: React.ReactNode;
  /** Optional fallback to render when feature is not available */
  fallback?: React.ReactNode;
  /** If true, show upgrade prompt instead of hiding */
  showUpgrade?: boolean;
}

/**
 * Conditionally renders children based on feature availability.
 *
 * @example
 * ```tsx
 * <FeatureGate feature="webhooks">
 *   <WebhooksPage />
 * </FeatureGate>
 *
 * <FeatureGate feature="custom_branding" showUpgrade>
 *   <BrandingSettings />
 * </FeatureGate>
 * ```
 */
export function FeatureGate({ feature, children, fallback, showUpgrade = false }: FeatureGateProps) {
  const { enabled, isLoading } = useFeature(feature);
  const { plan, billingEnabled } = useTenantFeatures();

  // While loading, optionally show loading state or children
  if (isLoading) {
    return null;
  }

  if (enabled) {
    return <>{children}</>;
  }

  if (showUpgrade && billingEnabled) {
    return (
      <UpgradePrompt
        featureName={feature}
        planName={plan?.displayName ?? plan?.name}
      />
    );
  }

  if (fallback) {
    return <>{fallback}</>;
  }

  return null;
}

interface LimitGateProps {
  /** Limit key to check */
  limit: LimitKey | string;
  /** Amount to check (default 1) */
  amount?: number;
  /** Content to render when within limit */
  children: React.ReactNode;
  /** Optional fallback to render when limit exceeded */
  fallback?: React.ReactNode;
  /** If true, show limit exceeded message */
  showLimitInfo?: boolean;
}

/**
 * Conditionally renders children based on usage limits.
 *
 * @example
 * ```tsx
 * <LimitGate limit="clients">
 *   <CreateClientButton />
 * </LimitGate>
 *
 * <LimitGate limit="clients" showLimitInfo>
 *   <CreateClientButton />
 * </LimitGate>
 * ```
 */
export function LimitGate({ limit: limitKey, amount = 1, children, fallback, showLimitInfo = false }: LimitGateProps) {
  const { limit, isWithinLimit, isLoading } = useLimit(limitKey);
  const { billingEnabled } = useTenantFeatures();

  if (isLoading) {
    return null;
  }

  const withinLimit = isWithinLimit(amount);

  if (withinLimit) {
    return <>{children}</>;
  }

  if (showLimitInfo && billingEnabled && limit) {
    return (
      <LimitExceededInfo
        limitName={limit.displayName}
        current={limit.current}
        max={limit.limit}
      />
    );
  }

  if (fallback) {
    return <>{fallback}</>;
  }

  return null;
}

interface UpgradePromptProps {
  featureName: string;
  planName?: string;
}

function UpgradePrompt({ featureName, planName }: UpgradePromptProps) {
  return (
    <div className="rounded-lg border border-yellow-200 bg-yellow-50 p-4">
      <div className="flex">
        <div className="flex-shrink-0">
          <svg className="h-5 w-5 text-yellow-400" viewBox="0 0 20 20" fill="currentColor">
            <path fillRule="evenodd" d="M8.257 3.099c.765-1.36 2.722-1.36 3.486 0l5.58 9.92c.75 1.334-.213 2.98-1.742 2.98H4.42c-1.53 0-2.493-1.646-1.743-2.98l5.58-9.92zM11 13a1 1 0 11-2 0 1 1 0 012 0zm-1-8a1 1 0 00-1 1v3a1 1 0 002 0V6a1 1 0 00-1-1z" clipRule="evenodd" />
          </svg>
        </div>
        <div className="ml-3">
          <h3 className="text-sm font-medium text-yellow-800">
            Feature not available
          </h3>
          <div className="mt-2 text-sm text-yellow-700">
            <p>
              The <strong>{featureName}</strong> feature is not included in your
              {planName && <> <strong>{planName}</strong></>} plan.
            </p>
          </div>
          <div className="mt-4">
            <a
              href="/billing/upgrade"
              className="text-sm font-medium text-yellow-800 hover:text-yellow-600"
            >
              Upgrade your plan &rarr;
            </a>
          </div>
        </div>
      </div>
    </div>
  );
}

interface LimitExceededInfoProps {
  limitName: string;
  current: number;
  max: number;
}

function LimitExceededInfo({ limitName, current, max }: LimitExceededInfoProps) {
  return (
    <div className="rounded-lg border border-red-200 bg-red-50 p-4">
      <div className="flex">
        <div className="flex-shrink-0">
          <svg className="h-5 w-5 text-red-400" viewBox="0 0 20 20" fill="currentColor">
            <path fillRule="evenodd" d="M10 18a8 8 0 100-16 8 8 0 000 16zM8.707 7.293a1 1 0 00-1.414 1.414L8.586 10l-1.293 1.293a1 1 0 101.414 1.414L10 11.414l1.293 1.293a1 1 0 001.414-1.414L11.414 10l1.293-1.293a1 1 0 00-1.414-1.414L10 8.586 8.707 7.293z" clipRule="evenodd" />
          </svg>
        </div>
        <div className="ml-3">
          <h3 className="text-sm font-medium text-red-800">
            Limit reached
          </h3>
          <div className="mt-2 text-sm text-red-700">
            <p>
              You've reached your limit of <strong>{max}</strong> {limitName.toLowerCase()}.
              Currently using <strong>{current}</strong>.
            </p>
          </div>
          <div className="mt-4">
            <a
              href="/billing/upgrade"
              className="text-sm font-medium text-red-800 hover:text-red-600"
            >
              Upgrade for more &rarr;
            </a>
          </div>
        </div>
      </div>
    </div>
  );
}

/**
 * Usage indicator bar for limits
 */
interface UsageBarProps {
  limitKey: LimitKey | string;
  showLabel?: boolean;
  className?: string;
}

export function UsageBar({ limitKey, showLabel = true, className = '' }: UsageBarProps) {
  const { limit, isLoading } = useLimit(limitKey);
  const { billingEnabled } = useTenantFeatures();

  if (isLoading || !billingEnabled || !limit) {
    return null;
  }

  if (limit.isUnlimited) {
    return showLabel ? (
      <div className={`text-sm text-gray-500 ${className}`}>
        {limit.displayName}: Unlimited
      </div>
    ) : null;
  }

  const percentage = Math.min((limit.current / limit.limit) * 100, 100);
  const isNearLimit = percentage >= 80;
  const isAtLimit = percentage >= 100;

  const barColor = isAtLimit
    ? 'bg-red-500'
    : isNearLimit
    ? 'bg-yellow-500'
    : 'bg-blue-500';

  return (
    <div className={className}>
      {showLabel && (
        <div className="flex justify-between text-sm text-gray-600 mb-1">
          <span>{limit.displayName}</span>
          <span>
            {limit.current.toLocaleString()} / {limit.limit.toLocaleString()}
          </span>
        </div>
      )}
      <div className="h-2 bg-gray-200 rounded-full overflow-hidden">
        <div
          className={`h-full ${barColor} transition-all duration-300`}
          style={{ width: `${percentage}%` }}
        />
      </div>
    </div>
  );
}

export default FeatureGate;
