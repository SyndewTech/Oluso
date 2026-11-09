export type DynamicFieldType =
  | 'checkbox'
  | 'yesno'
  | 'passfail'
  | 'rating'
  | 'text'
  | 'number'
  | 'photo'
  | 'signature'
  | 'select'
  | 'multiselect';

export interface DynamicFieldConfig {
  id: string;
  name: string;
  type: DynamicFieldType;
  label: string;
  description?: string;
  required?: boolean;
  order?: number;
  // For rating fields
  ratingMin?: number;
  ratingMax?: number;
  // For select fields
  options?: { value: string; label: string }[];
  // For number fields
  min?: number;
  max?: number;
  // For text fields
  maxLength?: number;
  multiline?: boolean;
  // Validation
  passingValue?: string;
  /** Failing this field is serious (shown in red; other failures are amber). */
  critical?: boolean;
  // Conditional visibility
  visibleWhen?: { fieldId: string; equals: string | boolean };
}

export interface DynamicFieldValue {
  fieldId: string;
  value: string | number | boolean | string[] | null;
  notes?: string;
  photoUrls?: string[];
  /** The answer's result as decided by the owner (e.g. the server); when absent it is worked out from the field. */
  outcome?: DynamicFieldOutcome;
}

/** Whether an answer passed, failed, or carries no pass/fail meaning. */
export type DynamicFieldOutcome = 'pass' | 'fail' | 'neutral';

export interface DynamicFormConfig {
  fields: DynamicFieldConfig[];
  allowNotes?: boolean;
  allowPhotos?: boolean;
}
