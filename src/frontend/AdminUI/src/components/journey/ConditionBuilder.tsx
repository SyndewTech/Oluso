import { PlusIcon, TrashIcon } from '@heroicons/react/24/outline';
import type { StepCondition } from '../../services/journeyService';

// Re-export StepCondition for convenience
export type { StepCondition };

// Props for the ConditionBuilder
interface ConditionBuilderProps {
  conditions: StepCondition[];
  onChange: (conditions: StepCondition[]) => void;
  availableFields?: FieldDefinition[];
  title?: string;
  description?: string;
}

// Field definition for dynamic fields
export interface FieldDefinition {
  name: string;
  label: string;
  type: 'journeyData' | 'claim' | 'context' | 'path' | 'input' | 'output';
  dataType?: 'string' | 'number' | 'boolean' | 'date' | 'array';
}

// Operators that don't require a value
const noValueOperators = ['exists', 'not_exists', 'empty', 'not_empty', 'true', 'false'];

// Simple condition builder using native form elements
interface SimpleConditionBuilderProps {
  conditions: StepCondition[];
  onChange: (conditions: StepCondition[]) => void;
}

export function SimpleConditionBuilder({ conditions, onChange }: SimpleConditionBuilderProps) {
  const addCondition = () => {
    onChange([
      ...conditions,
      {
        type: 'journeyData',
        field: '',
        operator: 'eq',
        value: '',
        logicalOperator: 'and',
        negate: false,
      },
    ]);
  };

  const updateCondition = (index: number, updates: Partial<StepCondition>) => {
    const newConditions = [...conditions];
    newConditions[index] = { ...newConditions[index], ...updates };
    onChange(newConditions);
  };

  const removeCondition = (index: number) => {
    onChange(conditions.filter((_, i) => i !== index));
  };

  const typeOptions = [
    { value: 'journeyData', label: 'Journey Data' },
    { value: 'claim', label: 'Claim' },
    { value: 'context', label: 'Context' },
    { value: 'path', label: 'Custom Path' },
  ];

  const operatorOptions = [
    { value: 'eq', label: '=' },
    { value: 'ne', label: '!=' },
    { value: 'gt', label: '>' },
    { value: 'lt', label: '<' },
    { value: 'gte', label: '>=' },
    { value: 'lte', label: '<=' },
    { value: 'contains', label: 'contains' },
    { value: 'starts_with', label: 'starts with' },
    { value: 'ends_with', label: 'ends with' },
    { value: 'exists', label: 'exists' },
    { value: 'not_exists', label: 'not exists' },
    { value: 'empty', label: 'is empty' },
    { value: 'not_empty', label: 'not empty' },
    { value: 'in', label: 'in list' },
    { value: 'not_in', label: 'not in list' },
    { value: 'regex', label: 'matches regex' },
    { value: 'true', label: 'is true' },
    { value: 'false', label: 'is false' },
  ];

  // Field suggestions based on type
  const getFieldSuggestions = (type: string): string[] => {
    switch (type) {
      case 'context':
        return ['userId', 'tenantId', 'clientId', 'isAuthenticated'];
      case 'journeyData':
        return ['email', 'username', 'loginHint', 'acrValues', 'scopes', 'mfaEnabled', 'mfaMethod', 'externalProvider', 'lastError'];
      case 'claim':
        return ['sub', 'email', 'email_verified', 'name', 'given_name', 'family_name', 'preferred_username', 'phone_number', 'role'];
      default:
        return [];
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h4 className="text-sm font-medium text-gray-700">Step Conditions</h4>
        <button
          type="button"
          onClick={addCondition}
          className="text-sm text-blue-600 hover:text-blue-800 flex items-center"
        >
          <PlusIcon className="h-4 w-4 mr-1" /> Add Condition
        </button>
      </div>

      {conditions.length === 0 && (
        <p className="text-xs text-gray-500 py-2">No conditions. Step will always execute.</p>
      )}

      <div className="space-y-2">
        {conditions.map((condition, index) => (
          <div key={index} className="flex flex-wrap items-center gap-2 p-2 bg-gray-50 rounded-lg">
            {index > 0 && (
              <select
                value={condition.logicalOperator}
                onChange={(e) => updateCondition(index, { logicalOperator: e.target.value })}
                className="w-16 rounded-md border-gray-300 text-xs"
              >
                <option value="and">AND</option>
                <option value="or">OR</option>
              </select>
            )}

            <label className="flex items-center text-xs">
              <input
                type="checkbox"
                checked={condition.negate}
                onChange={(e) => updateCondition(index, { negate: e.target.checked })}
                className="rounded border-gray-300 text-blue-600 mr-1"
              />
              NOT
            </label>

            <select
              value={condition.type}
              onChange={(e) => updateCondition(index, { type: e.target.value })}
              className="rounded-md border-gray-300 text-xs"
            >
              {typeOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>

            <div className="relative flex-1 min-w-[120px]">
              <input
                type="text"
                value={condition.field}
                onChange={(e) => updateCondition(index, { field: e.target.value })}
                placeholder="Field name"
                list={`field-suggestions-${index}`}
                className="w-full rounded-md border-gray-300 text-xs"
              />
              <datalist id={`field-suggestions-${index}`}>
                {getFieldSuggestions(condition.type).map((s) => (
                  <option key={s} value={s} />
                ))}
              </datalist>
            </div>

            <select
              value={condition.operator}
              onChange={(e) => updateCondition(index, { operator: e.target.value })}
              className="rounded-md border-gray-300 text-xs"
            >
              {operatorOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>
                  {opt.label}
                </option>
              ))}
            </select>

            {!noValueOperators.includes(condition.operator) && (
              <input
                type="text"
                value={condition.value || ''}
                onChange={(e) => updateCondition(index, { value: e.target.value })}
                placeholder={condition.operator === 'in' || condition.operator === 'not_in' ? 'val1, val2, val3' : 'Value'}
                className="flex-1 min-w-[100px] rounded-md border-gray-300 text-xs"
              />
            )}

            <button
              type="button"
              onClick={() => removeCondition(index)}
              className="text-red-400 hover:text-red-600"
            >
              <TrashIcon className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>

      {/* Preview */}
      {conditions.length > 0 && (
        <div className="mt-2 p-2 bg-gray-100 rounded text-xs font-mono">
          <div className="text-gray-500 mb-1">Preview:</div>
          <code className="text-gray-700">
            {conditions.map((c, i) => (
              <span key={i}>
                {i > 0 && <span className="text-blue-600"> {c.logicalOperator.toUpperCase()} </span>}
                {c.negate && <span className="text-red-600">NOT </span>}
                <span className="text-purple-600">{c.type}.</span>
                <span className="text-green-700">{c.field}</span>
                <span className="text-gray-500"> {c.operator} </span>
                {c.value && <span className="text-orange-600">"{c.value}"</span>}
              </span>
            ))}
          </code>
        </div>
      )}
    </div>
  );
}

// Full-featured condition builder (exported as alias for SimpleConditionBuilder for now)
// TODO: Implement with react-querybuilder when package is installed
export function ConditionBuilder({
  conditions,
  onChange,
  title = 'Conditions',
  description,
}: ConditionBuilderProps) {
  return (
    <div className="space-y-2">
      {title && <h4 className="text-sm font-medium text-gray-700">{title}</h4>}
      {description && <p className="text-xs text-gray-500">{description}</p>}
      <SimpleConditionBuilder conditions={conditions} onChange={onChange} />
    </div>
  );
}

export default ConditionBuilder;
