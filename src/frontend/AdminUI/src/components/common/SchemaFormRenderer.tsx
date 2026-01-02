import React, { useMemo } from 'react';
import { TrashIcon, PlusIcon } from '@heroicons/react/24/outline';
import { useEnumSource, EnumSourceConfig, EnumOption } from '../../hooks/useEnumSource';

// JSON Schema types with custom extensions
export interface SchemaProperty {
  type: 'string' | 'number' | 'integer' | 'boolean' | 'array' | 'object';
  title?: string;
  description?: string;
  default?: unknown;
  enum?: string[];
  'x-enumLabels'?: string[];  // Display labels for static enums
  'x-enumSource'?: EnumSourceConfig;  // Dynamic options from API
  'x-control'?: 'select' | 'multi-select' | 'currency-input' | 'secret-input' | 'textarea';
  'x-currency'?: 'cents' | 'dollars';
  items?: SchemaProperty | { type: string };
  properties?: Record<string, SchemaProperty>;
  required?: string[];
  minimum?: number;
  maximum?: number;
  minLength?: number;
  maxLength?: number;
}

export interface SchemaObject {
  type: 'object';
  properties?: Record<string, SchemaProperty>;
  required?: string[];
  'x-optionsEndpoint'?: string;  // Root-level options endpoint
}

interface SchemaFormRendererProps {
  schema: SchemaObject | Record<string, unknown>;
  value: Record<string, unknown>;
  onChange: (value: Record<string, unknown>) => void;
  className?: string;
  disabled?: boolean;
}

// Wrapper component to handle nested rendering
export function SchemaFormRenderer({
  schema,
  value,
  onChange,
  className = '',
  disabled = false,
}: SchemaFormRendererProps) {
  const schemaObj = schema as SchemaObject;
  const properties = schemaObj.properties || {};

  const handleFieldChange = (key: string, fieldValue: unknown) => {
    onChange({ ...value, [key]: fieldValue });
  };

  return (
    <div className={`space-y-4 ${className}`}>
      {Object.entries(properties).map(([key, propSchema]) => (
        <SchemaField
          key={key}
          name={key}
          schema={propSchema}
          value={value[key]}
          onChange={(v) => handleFieldChange(key, v)}
          required={schemaObj.required?.includes(key)}
          disabled={disabled}
        />
      ))}
    </div>
  );
}

interface SchemaFieldProps {
  name: string;
  schema: SchemaProperty;
  value: unknown;
  onChange: (value: unknown) => void;
  required?: boolean;
  disabled?: boolean;
}

function SchemaField({
  name,
  schema,
  value,
  onChange,
  required,
  disabled,
}: SchemaFieldProps) {
  const title = schema.title || formatTitle(name);

  // Handle nested objects
  if (schema.type === 'object' && schema.properties) {
    // Cast to SchemaObject for recursive rendering
    const nestedSchema: SchemaObject = {
      type: 'object',
      properties: schema.properties,
      required: schema.required,
    };
    return (
      <div className="border rounded-lg p-4 bg-gray-50">
        <h4 className="text-sm font-medium text-gray-700 mb-3">{title}</h4>
        {schema.description && (
          <p className="text-xs text-gray-500 mb-3">{schema.description}</p>
        )}
        <SchemaFormRenderer
          schema={nestedSchema}
          value={(value as Record<string, unknown>) || {}}
          onChange={(v) => onChange(v)}
          disabled={disabled}
        />
      </div>
    );
  }

  return (
    <div>
      <label className="block text-sm font-medium text-gray-700">
        {title}
        {required && <span className="text-red-500 ml-1">*</span>}
      </label>
      {schema.description && (
        <p className="text-xs text-gray-500 mb-1">{schema.description}</p>
      )}
      <SchemaFieldInput
        name={name}
        schema={schema}
        value={value}
        onChange={onChange}
        disabled={disabled}
      />
    </div>
  );
}

interface SchemaFieldInputProps {
  name: string;
  schema: SchemaProperty;
  value: unknown;
  onChange: (value: unknown) => void;
  disabled?: boolean;
}

function SchemaFieldInput({
  name,
  schema,
  value,
  onChange,
  disabled,
}: SchemaFieldInputProps) {
  const control = schema['x-control'];
  const enumSource = schema['x-enumSource'];

  // Dynamic enum source
  const { options: dynamicOptions, loading: dynamicLoading } = useEnumSource(enumSource);

  // Determine if we should use select/multi-select
  const hasStaticEnum = !!schema.enum;
  const hasDynamicEnum = !!enumSource;
  const isMultiSelect = control === 'multi-select' || (schema.type === 'array' && (hasStaticEnum || hasDynamicEnum));

  // Build options from static enum or dynamic source
  const options: EnumOption[] = useMemo(() => {
    if (hasDynamicEnum) {
      return dynamicOptions;
    }
    if (hasStaticEnum) {
      return schema.enum!.map((v, i) => ({
        value: v,
        label: schema['x-enumLabels']?.[i] || v,
      }));
    }
    return [];
  }, [hasStaticEnum, hasDynamicEnum, schema.enum, schema['x-enumLabels'], dynamicOptions]);

  // Multi-select
  if (isMultiSelect) {
    return (
      <MultiSelectInput
        name={name}
        options={options}
        value={(value as string[]) || []}
        onChange={(v) => onChange(v)}
        loading={dynamicLoading}
        disabled={disabled}
      />
    );
  }

  // Single select (static or dynamic)
  if (hasStaticEnum || hasDynamicEnum || control === 'select') {
    return (
      <SelectInput
        name={name}
        options={options}
        value={value as string}
        onChange={onChange}
        loading={dynamicLoading}
        disabled={disabled}
      />
    );
  }

  // Boolean
  if (schema.type === 'boolean') {
    return (
      <input
        data-field={name}
        type="checkbox"
        checked={(value as boolean) ?? schema.default ?? false}
        onChange={(e) => onChange(e.target.checked)}
        disabled={disabled}
        className="mt-1 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
      />
    );
  }

  // Number or Integer
  if (schema.type === 'number' || schema.type === 'integer') {
    if (control === 'currency-input') {
      return (
        <CurrencyInput
          name={name}
          value={value as number}
          onChange={onChange}
          currency={schema['x-currency']}
          min={schema.minimum}
          max={schema.maximum}
          disabled={disabled}
        />
      );
    }

    return (
      <input
        data-field={name}
        type="number"
        value={(value as number) ?? schema.default ?? ''}
        onChange={(e) => onChange(e.target.value ? parseFloat(e.target.value) : undefined)}
        min={schema.minimum}
        max={schema.maximum}
        disabled={disabled}
        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100"
      />
    );
  }

  // Array of strings (without enum)
  if (schema.type === 'array') {
    return (
      <ArrayInput
        name={name}
        value={(value as string[]) || []}
        onChange={onChange}
        disabled={disabled}
      />
    );
  }

  // Secret input
  if (control === 'secret-input') {
    return (
      <input
        data-field={name}
        type="password"
        value={(value as string) ?? ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={schema.description}
        disabled={disabled}
        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm font-mono disabled:bg-gray-100"
      />
    );
  }

  // Textarea
  if (control === 'textarea') {
    return (
      <textarea
        data-field={name}
        value={(value as string) ?? schema.default ?? ''}
        onChange={(e) => onChange(e.target.value)}
        placeholder={schema.description}
        disabled={disabled}
        rows={4}
        className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100"
      />
    );
  }

  // Default: text input
  return (
    <input
      data-field={name}
      type="text"
      value={(value as string) ?? schema.default ?? ''}
      onChange={(e) => onChange(e.target.value)}
      placeholder={schema.description}
      disabled={disabled}
      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100"
    />
  );
}

// Helper components

interface SelectInputProps {
  name: string;
  options: EnumOption[];
  value: string | undefined;
  onChange: (value: unknown) => void;
  loading?: boolean;
  disabled?: boolean;
}

function SelectInput({ name, options, value, onChange, loading, disabled }: SelectInputProps) {
  return (
    <select
      data-field={name}
      value={value ?? ''}
      onChange={(e) => onChange(e.target.value || undefined)}
      disabled={disabled || loading}
      className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100"
    >
      <option value="">{loading ? 'Loading...' : 'Select...'}</option>
      {options.map((opt) => (
        <option key={opt.value} value={opt.value} title={opt.description}>
          {opt.label}
          {opt.description ? ` - ${opt.description}` : ''}
        </option>
      ))}
    </select>
  );
}

interface MultiSelectInputProps {
  name: string;
  options: EnumOption[];
  value: string[];
  onChange: (value: string[]) => void;
  loading?: boolean;
  disabled?: boolean;
}

function MultiSelectInput({ name, options, value, onChange, loading, disabled }: MultiSelectInputProps) {
  const toggleOption = (optValue: string) => {
    if (value.includes(optValue)) {
      onChange(value.filter((v) => v !== optValue));
    } else {
      onChange([...value, optValue]);
    }
  };

  if (loading) {
    return (
      <div className="mt-1 p-2 border rounded-md bg-gray-50 text-gray-500 text-sm">
        Loading options...
      </div>
    );
  }

  if (options.length === 0) {
    return (
      <div className="mt-1 p-2 border rounded-md bg-gray-50 text-gray-500 text-sm">
        No options available
      </div>
    );
  }

  return (
    <div data-field={name} className="mt-1 border rounded-md max-h-48 overflow-y-auto">
      {options.map((opt) => (
        <label
          key={opt.value}
          className={`flex items-start p-2 cursor-pointer hover:bg-gray-50 ${
            disabled ? 'opacity-50 cursor-not-allowed' : ''
          }`}
        >
          <input
            type="checkbox"
            checked={value.includes(opt.value)}
            onChange={() => !disabled && toggleOption(opt.value)}
            disabled={disabled}
            className="mt-0.5 rounded border-gray-300 text-blue-600 focus:ring-blue-500"
          />
          <div className="ml-2">
            <div className="text-sm text-gray-900">{opt.label}</div>
            {opt.description && (
              <div className="text-xs text-gray-500">{opt.description}</div>
            )}
          </div>
        </label>
      ))}
    </div>
  );
}

interface ArrayInputProps {
  name: string;
  value: string[];
  onChange: (value: string[]) => void;
  disabled?: boolean;
}

function ArrayInput({ name, value, onChange, disabled }: ArrayInputProps) {
  const addItem = () => onChange([...value, '']);
  const removeItem = (idx: number) => onChange(value.filter((_, i) => i !== idx));
  const updateItem = (idx: number, newValue: string) => {
    const arr = [...value];
    arr[idx] = newValue;
    onChange(arr);
  };

  return (
    <div data-field={name} className="mt-1 space-y-2">
      {value.map((item, idx) => (
        <div key={idx} className="flex items-center space-x-2">
          <input
            type="text"
            value={item}
            onChange={(e) => updateItem(idx, e.target.value)}
            disabled={disabled}
            className="flex-1 rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100"
          />
          <button
            type="button"
            onClick={() => removeItem(idx)}
            disabled={disabled}
            className="text-red-500 hover:text-red-700 disabled:opacity-50"
          >
            <TrashIcon className="h-4 w-4" />
          </button>
        </div>
      ))}
      <button
        type="button"
        onClick={addItem}
        disabled={disabled}
        className="flex items-center text-sm text-blue-600 hover:text-blue-800 disabled:opacity-50"
      >
        <PlusIcon className="h-4 w-4 mr-1" />
        Add item
      </button>
    </div>
  );
}

interface CurrencyInputProps {
  name: string;
  value: number | undefined;
  onChange: (value: unknown) => void;
  currency?: 'cents' | 'dollars';
  min?: number;
  max?: number;
  disabled?: boolean;
}

function CurrencyInput({ name, value, onChange, currency = 'cents', min, max, disabled }: CurrencyInputProps) {
  const displayValue = currency === 'cents' && value ? value / 100 : value;

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const inputValue = e.target.value;
    if (!inputValue) {
      onChange(undefined);
      return;
    }

    const numValue = parseFloat(inputValue);
    if (currency === 'cents') {
      onChange(Math.round(numValue * 100));
    } else {
      onChange(numValue);
    }
  };

  return (
    <div className="mt-1 relative">
      <span className="absolute left-3 top-1/2 -translate-y-1/2 text-gray-500">$</span>
      <input
        data-field={name}
        type="number"
        value={displayValue ?? ''}
        onChange={handleChange}
        min={min !== undefined ? (currency === 'cents' ? min / 100 : min) : undefined}
        max={max !== undefined ? (currency === 'cents' ? max / 100 : max) : undefined}
        step="0.01"
        disabled={disabled}
        className="block w-full pl-7 rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm disabled:bg-gray-100"
      />
    </div>
  );
}

// Utility to format a camelCase/snake_case key into a title
function formatTitle(key: string): string {
  return key
    .replace(/([A-Z])/g, ' $1')
    .replace(/_/g, ' ')
    .replace(/^\w/, (c) => c.toUpperCase())
    .trim();
}

export default SchemaFormRenderer;
