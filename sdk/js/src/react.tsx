import React, {
  createContext,
  useContext,
  useState,
  useCallback,
  useEffect,
  useMemo,
  type ReactNode,
  type FormEvent,
} from 'react';
import { OlusoClient, OlusoError } from './client';
import type {
  OlusoConfig,
  JourneyInfo,
  JourneyField,
  SubmissionResult,
} from './types';

// ============================================================================
// Context & Provider
// ============================================================================

interface OlusoContextValue {
  client: OlusoClient;
}

const OlusoContext = createContext<OlusoContextValue | null>(null);

export interface OlusoProviderProps {
  config: OlusoConfig;
  children: ReactNode;
}

/**
 * Provider component for Oluso SDK
 *
 * @example
 * ```tsx
 * <OlusoProvider config={{ serverUrl: 'https://auth.yoursite.com', tenant: 'acme' }}>
 *   <App />
 * </OlusoProvider>
 * ```
 */
export function OlusoProvider({ config, children }: OlusoProviderProps) {
  const client = useMemo(() => new OlusoClient(config), [config]);

  return (
    <OlusoContext.Provider value={{ client }}>
      {children}
    </OlusoContext.Provider>
  );
}

/**
 * Hook to access the Oluso client
 */
export function useOluso(): OlusoClient {
  const context = useContext(OlusoContext);
  if (!context) {
    throw new Error('useOluso must be used within an OlusoProvider');
  }
  return context.client;
}

// ============================================================================
// useJourney Hook
// ============================================================================

export interface UseJourneyOptions {
  /** Journey/policy ID */
  journeyId: string;
  /** Callback on successful submission */
  onSuccess?: (result: SubmissionResult) => void;
  /** Callback on error */
  onError?: (error: OlusoError) => void;
  /** Initial form values */
  defaultValues?: Record<string, unknown>;
}

export interface UseJourneyResult {
  /** Journey metadata and field definitions */
  journey: JourneyInfo | null;
  /** Loading state for initial fetch */
  isLoading: boolean;
  /** Submitting state */
  isSubmitting: boolean;
  /** Error from fetch or submission */
  error: OlusoError | null;
  /** Current form values */
  values: Record<string, unknown>;
  /** Field-level errors */
  fieldErrors: Record<string, string>;
  /** Update a field value */
  setValue: (field: string, value: unknown) => void;
  /** Submit the form */
  submit: () => Promise<SubmissionResult | null>;
  /** Reset the form */
  reset: () => void;
}

/**
 * Hook for working with a journey (headless)
 *
 * @example
 * ```tsx
 * function WaitlistForm() {
 *   const { journey, values, setValue, submit, isSubmitting, fieldErrors } = useJourney({
 *     journeyId: 'waitlist',
 *     onSuccess: () => alert('Thanks for signing up!'),
 *   });
 *
 *   if (!journey) return <div>Loading...</div>;
 *
 *   return (
 *     <form onSubmit={(e) => { e.preventDefault(); submit(); }}>
 *       {journey.fields.map((field) => (
 *         <div key={field.name}>
 *           <label>{field.label}</label>
 *           <input
 *             type={field.type}
 *             value={values[field.name] as string || ''}
 *             onChange={(e) => setValue(field.name, e.target.value)}
 *           />
 *           {fieldErrors[field.name] && <span>{fieldErrors[field.name]}</span>}
 *         </div>
 *       ))}
 *       <button type="submit" disabled={isSubmitting}>
 *         {isSubmitting ? 'Submitting...' : 'Submit'}
 *       </button>
 *     </form>
 *   );
 * }
 * ```
 */
export function useJourney(options: UseJourneyOptions): UseJourneyResult {
  const client = useOluso();
  const [journey, setJourney] = useState<JourneyInfo | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState<OlusoError | null>(null);
  const [values, setValues] = useState<Record<string, unknown>>(options.defaultValues || {});
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  // Fetch journey on mount
  useEffect(() => {
    let cancelled = false;

    async function fetchJourney() {
      try {
        setIsLoading(true);
        setError(null);
        const data = await client.getJourney(options.journeyId);
        if (!cancelled) {
          setJourney(data);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof OlusoError ? err : new OlusoError(String(err), 0));
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    }

    fetchJourney();

    return () => {
      cancelled = true;
    };
  }, [client, options.journeyId]);

  const setValue = useCallback((field: string, value: unknown) => {
    setValues((prev) => ({ ...prev, [field]: value }));
    // Clear field error when value changes
    setFieldErrors((prev) => {
      if (prev[field]) {
        const { [field]: _, ...rest } = prev;
        return rest;
      }
      return prev;
    });
  }, []);

  const submit = useCallback(async (): Promise<SubmissionResult | null> => {
    if (!journey) return null;

    try {
      setIsSubmitting(true);
      setError(null);
      setFieldErrors({});

      const result = await client.submit(options.journeyId, values);

      if (result.success) {
        options.onSuccess?.(result);
      }

      return result;
    } catch (err) {
      const olusError = err instanceof OlusoError ? err : new OlusoError(String(err), 0);
      setError(olusError);
      if (olusError.fieldErrors) {
        setFieldErrors(olusError.fieldErrors);
      }
      options.onError?.(olusError);
      return null;
    } finally {
      setIsSubmitting(false);
    }
  }, [client, journey, options, values]);

  const reset = useCallback(() => {
    setValues(options.defaultValues || {});
    setFieldErrors({});
    setError(null);
  }, [options.defaultValues]);

  return {
    journey,
    isLoading,
    isSubmitting,
    error,
    values,
    fieldErrors,
    setValue,
    submit,
    reset,
  };
}

// ============================================================================
// Pre-built Form Components
// ============================================================================

export interface JourneyFormProps {
  /** Journey/policy ID */
  journeyId: string;
  /** Callback on successful submission */
  onSuccess?: (result: SubmissionResult) => void;
  /** Callback on error */
  onError?: (error: OlusoError) => void;
  /** Custom class name */
  className?: string;
  /** Custom submit button text */
  submitText?: string;
  /** Custom loading text */
  loadingText?: string;
  /** Render custom field */
  renderField?: (field: JourneyField, props: FieldRenderProps) => ReactNode;
  /** Render custom submit button */
  renderSubmit?: (props: { isSubmitting: boolean; onClick: () => void }) => ReactNode;
  /** Show success message instead of form after submission */
  showSuccessMessage?: boolean;
  /** Custom success message component */
  renderSuccess?: (result: SubmissionResult) => ReactNode;
}

export interface FieldRenderProps {
  value: unknown;
  onChange: (value: unknown) => void;
  error?: string;
}

/**
 * Pre-built form component that renders a journey form
 *
 * @example
 * ```tsx
 * <JourneyForm
 *   journeyId="waitlist"
 *   onSuccess={() => console.log('Submitted!')}
 *   submitText="Join Waitlist"
 * />
 * ```
 */
export function JourneyForm({
  journeyId,
  onSuccess,
  onError,
  className,
  submitText = 'Submit',
  loadingText = 'Loading...',
  renderField,
  renderSubmit,
  showSuccessMessage = true,
  renderSuccess,
}: JourneyFormProps) {
  const {
    journey,
    isLoading,
    isSubmitting,
    error,
    values,
    fieldErrors,
    setValue,
    submit,
  } = useJourney({ journeyId, onSuccess, onError });

  const [submitted, setSubmitted] = useState<SubmissionResult | null>(null);

  const handleSubmit = useCallback(async (e: FormEvent) => {
    e.preventDefault();
    const result = await submit();
    if (result?.success && showSuccessMessage) {
      setSubmitted(result);
    }
  }, [submit, showSuccessMessage]);

  if (isLoading) {
    return <div className={className}>{loadingText}</div>;
  }

  if (!journey) {
    return <div className={className}>Journey not found</div>;
  }

  if (submitted) {
    if (renderSuccess) {
      return <>{renderSuccess(submitted)}</>;
    }
    return (
      <div className={className}>
        <p>{submitted.message || 'Thank you for your submission!'}</p>
      </div>
    );
  }

  return (
    <form onSubmit={handleSubmit} className={className}>
      {journey.fields.map((field) => {
        const fieldProps: FieldRenderProps = {
          value: values[field.name],
          onChange: (value) => setValue(field.name, value),
          error: fieldErrors[field.name],
        };

        if (renderField) {
          return <React.Fragment key={field.name}>{renderField(field, fieldProps)}</React.Fragment>;
        }

        return (
          <DefaultField key={field.name} field={field} {...fieldProps} />
        );
      })}

      {error && !error.fieldErrors && (
        <div style={{ color: 'red', marginBottom: '1rem' }}>
          {error.message}
        </div>
      )}

      {renderSubmit ? (
        renderSubmit({ isSubmitting, onClick: () => submit() })
      ) : (
        <button type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Submitting...' : submitText}
        </button>
      )}
    </form>
  );
}

// ============================================================================
// Default Field Component
// ============================================================================

interface DefaultFieldProps extends FieldRenderProps {
  field: JourneyField;
}

function DefaultField({ field, value, onChange, error }: DefaultFieldProps) {
  const id = `oluso-field-${field.name}`;

  const inputProps = {
    id,
    name: field.name,
    required: field.required,
    placeholder: field.placeholder,
    'aria-describedby': field.description ? `${id}-desc` : undefined,
    'aria-invalid': !!error,
  };

  let input: ReactNode;

  switch (field.type) {
    case 'textarea':
      input = (
        <textarea
          {...inputProps}
          value={(value as string) || ''}
          onChange={(e) => onChange(e.target.value)}
          rows={4}
        />
      );
      break;

    case 'select':
      input = (
        <select
          {...inputProps}
          value={(value as string) || ''}
          onChange={(e) => onChange(e.target.value)}
        >
          <option value="">Select...</option>
          {field.options?.map((opt) => (
            <option key={opt.value} value={opt.value}>
              {opt.label}
            </option>
          ))}
        </select>
      );
      break;

    case 'checkbox':
      input = (
        <input
          {...inputProps}
          type="checkbox"
          checked={!!value}
          onChange={(e) => onChange(e.target.checked)}
        />
      );
      break;

    case 'radio':
      input = (
        <div role="radiogroup">
          {field.options?.map((opt) => (
            <label key={opt.value}>
              <input
                type="radio"
                name={field.name}
                value={opt.value}
                checked={value === opt.value}
                onChange={(e) => onChange(e.target.value)}
              />
              {opt.label}
            </label>
          ))}
        </div>
      );
      break;

    default:
      input = (
        <input
          {...inputProps}
          type={field.type}
          value={(value as string) || ''}
          onChange={(e) => onChange(e.target.value)}
          minLength={field.validation?.minLength}
          maxLength={field.validation?.maxLength}
          pattern={field.validation?.pattern}
          min={field.validation?.min}
          max={field.validation?.max}
        />
      );
  }

  return (
    <div style={{ marginBottom: '1rem' }}>
      <label htmlFor={id} style={{ display: 'block', marginBottom: '0.25rem' }}>
        {field.label}
        {field.required && <span style={{ color: 'red' }}> *</span>}
      </label>
      {input}
      {field.description && (
        <small id={`${id}-desc`} style={{ display: 'block', color: '#666' }}>
          {field.description}
        </small>
      )}
      {error && (
        <small style={{ display: 'block', color: 'red' }}>
          {error}
        </small>
      )}
    </div>
  );
}

// ============================================================================
// Iframe Embed Component
// ============================================================================

export interface JourneyEmbedProps {
  /** Journey/policy ID */
  journeyId: string;
  /** Width of the iframe */
  width?: string | number;
  /** Height of the iframe */
  height?: string | number;
  /** Theme */
  theme?: 'light' | 'dark';
  /** Hide the header */
  hideHeader?: boolean;
  /** Custom class name */
  className?: string;
  /** Custom style */
  style?: React.CSSProperties;
  /** Callback when journey completes (via postMessage) */
  onComplete?: (result: SubmissionResult) => void;
}

/**
 * Embed a journey via iframe
 *
 * @example
 * ```tsx
 * <JourneyEmbed
 *   journeyId="waitlist"
 *   height={400}
 *   onComplete={(result) => console.log('Submitted!', result)}
 * />
 * ```
 */
export function JourneyEmbed({
  journeyId,
  width = '100%',
  height = 500,
  theme,
  hideHeader,
  className,
  style,
  onComplete,
}: JourneyEmbedProps) {
  const client = useOluso();
  const src = client.getEmbedUrl(journeyId, { theme, hideHeader });

  useEffect(() => {
    if (!onComplete) return;

    const handleMessage = (event: MessageEvent) => {
      // Verify origin matches server URL
      if (!client.getJourneyUrl(journeyId).startsWith(event.origin)) {
        return;
      }

      if (event.data?.type === 'oluso:complete') {
        onComplete(event.data.result);
      }
    };

    window.addEventListener('message', handleMessage);
    return () => window.removeEventListener('message', handleMessage);
  }, [client, journeyId, onComplete]);

  return (
    <iframe
      src={src}
      width={width}
      height={height}
      className={className}
      style={{
        border: 'none',
        ...style,
      }}
      title={`Oluso Journey: ${journeyId}`}
      allow="clipboard-write"
    />
  );
}
