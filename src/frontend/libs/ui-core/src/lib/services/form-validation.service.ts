/**
 * Lightweight form validation utility for modal forms across all Oluso admin/workspace plugins.
 * No external dependencies — pure functions that take form data and declarative rules.
 *
 * Usage:
 *   import { validateForm, extractErrorMessage, type ValidationRule } from '@oluso/ui-core';
 *
 *   const RULES: ValidationRule[] = [
 *     { field: 'name', label: 'Name', required: true, minLength: 2 },
 *     { field: 'amount', label: 'Amount', required: true, min: 0 },
 *   ];
 *
 *   saveItem(): void {
 *     const v = validateForm(this.formData, RULES);
 *     if (!v.valid) { this.saveError.set(v.errorSummary); return; }
 *     this.saving.set(true);
 *     // ... API call
 *   }
 */

export interface ValidationRule {
  field: string;
  label: string;
  required?: boolean;
  minLength?: number;
  maxLength?: number;
  min?: number;
  max?: number;
  pattern?: RegExp;
  patternMessage?: string;
  custom?: (value: any, formData: any) => string | null;
}

export interface ValidationResult {
  valid: boolean;
  errors: { field: string; label: string; message: string }[];
  errorMessages: string[];
  errorSummary: string;
}

export function validateForm(formData: Record<string, any>, rules: ValidationRule[]): ValidationResult {
  const errors: { field: string; label: string; message: string }[] = [];

  for (const rule of rules) {
    const value = formData[rule.field];

    if (rule.required) {
      if (value === undefined || value === null || value === '' || (typeof value === 'number' && isNaN(value))) {
        errors.push({ field: rule.field, label: rule.label, message: `${rule.label} is required` });
        continue;
      }
    }

    if (value === undefined || value === null || value === '') continue;

    if (rule.minLength && typeof value === 'string' && value.length < rule.minLength) {
      errors.push({ field: rule.field, label: rule.label, message: `${rule.label} must be at least ${rule.minLength} characters` });
    }

    if (rule.maxLength && typeof value === 'string' && value.length > rule.maxLength) {
      errors.push({ field: rule.field, label: rule.label, message: `${rule.label} must be at most ${rule.maxLength} characters` });
    }

    if (rule.min !== undefined && typeof value === 'number' && value < rule.min) {
      errors.push({ field: rule.field, label: rule.label, message: `${rule.label} must be at least ${rule.min}` });
    }

    if (rule.max !== undefined && typeof value === 'number' && value > rule.max) {
      errors.push({ field: rule.field, label: rule.label, message: `${rule.label} must be at most ${rule.max}` });
    }

    if (rule.pattern && typeof value === 'string' && !rule.pattern.test(value)) {
      errors.push({ field: rule.field, label: rule.label, message: rule.patternMessage || `${rule.label} format is invalid` });
    }

    if (rule.custom) {
      const customError = rule.custom(value, formData);
      if (customError) {
        errors.push({ field: rule.field, label: rule.label, message: customError });
      }
    }
  }

  return {
    valid: errors.length === 0,
    errors,
    errorMessages: errors.map(e => e.message),
    errorSummary: errors.map(e => e.message).join('\n'),
  };
}

/**
 * Extracts error messages from ASP.NET Core API error responses.
 * Handles multiple response formats:
 *   { message: string }
 *   { Message: string }
 *   { errors: { [field]: string[] } }  (model binding validation)
 *   { title: string }  (ProblemDetails)
 */
export function extractErrorMessage(err: unknown, defaultMessage: string): string {
  const error = err as {
    error?: {
      error?: string;
      error_description?: string;
      message?: string;
      Message?: string;
      errors?: Record<string, string[]>;
      title?: string;
    };
  };

  // The admin API's validation failures are BadRequest(new { error = "..." }),
  // so the useful text is at error.error.error. Without this the caller falls
  // through to the generic default and the reason is lost.
  // OAuth-style bodies carry a code in `error` and the readable reason in `error_description`.
  if (typeof error.error?.error_description === 'string' && error.error.error_description) {
    return error.error.error_description;
  }
  if (typeof error.error?.error === 'string') return error.error.error;

  if (error.error?.message) return error.error.message;
  if (error.error?.Message) return error.error.Message;

  if (error.error?.errors) {
    const messages: string[] = [];
    for (const [field, fieldMessages] of Object.entries(error.error.errors)) {
      if (Array.isArray(fieldMessages)) {
        messages.push(`${field}: ${fieldMessages.join(', ')}`);
      }
    }
    if (messages.length > 0) return messages.join('; ');
  }

  if (error.error?.title) return error.error.title;
  return defaultMessage;
}
