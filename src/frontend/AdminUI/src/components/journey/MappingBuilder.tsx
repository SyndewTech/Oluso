import { useState } from 'react';
import { PlusIcon, TrashIcon, ArrowRightIcon, ChevronDownIcon } from '@heroicons/react/24/outline';

// Types for claim/data mappings
export interface ClaimMapping {
  sourceType: 'journeyData' | 'claim' | 'literal' | 'input' | 'context';
  sourcePath: string;
  targetClaimType: string;
  defaultValue?: string;
  transform?: string;
}

// Transform step mappings
export interface TransformMapping {
  source: string;
  sourceType: 'journeyData' | 'claim' | 'context' | 'literal';
  target: string;
  transform?: 'lowercase' | 'uppercase' | 'trim' | 'split' | 'join' | 'replace' | 'custom';
  transformConfig?: Record<string, string>;
}

// Use a generic type to make the component type-safe
type MappingBuilderProps = {
  mode: 'transform';
  mappings: TransformMapping[];
  onChange: (mappings: TransformMapping[]) => void;
  title?: string;
  description?: string;
} | {
  mode: 'output' | 'input';
  mappings: ClaimMapping[];
  onChange: (mappings: ClaimMapping[]) => void;
  title?: string;
  description?: string;
};

// Common sources
const sourceTypes = [
  { value: 'journeyData', label: 'Journey Data', description: 'Data stored during the journey' },
  { value: 'claim', label: 'Claim', description: 'User claims from authentication' },
  { value: 'context', label: 'Context', description: 'Journey context (userId, tenantId, clientId)' },
  { value: 'literal', label: 'Literal', description: 'Static value' },
  { value: 'input', label: 'Step Input', description: 'Data from previous step or user input' },
];

// Transform options
const transformOptions = [
  { value: '', label: 'None' },
  { value: 'lowercase', label: 'Lowercase' },
  { value: 'uppercase', label: 'Uppercase' },
  { value: 'trim', label: 'Trim Whitespace' },
  { value: 'split', label: 'Split' },
  { value: 'join', label: 'Join' },
  { value: 'replace', label: 'Replace' },
  { value: 'custom', label: 'Custom Expression' },
];

// Common journey data fields for autocomplete
const journeyDataSuggestions = [
  'email', 'username', 'loginHint', 'acrValues', 'scopes',
  'mfaEnabled', 'mfaMethod', 'externalProvider', 'lastError',
  'redirectUri', 'state', 'nonce'
];

// Common claim types for autocomplete
const claimSuggestions = [
  'sub', 'email', 'email_verified', 'name', 'given_name', 'family_name',
  'preferred_username', 'phone_number', 'phone_number_verified',
  'picture', 'locale', 'zoneinfo', 'updated_at', 'role', 'tenant_id'
];

// Context field suggestions
const contextSuggestions = ['userId', 'tenantId', 'clientId'];

export function MappingBuilder({
  mappings,
  onChange,
  mode,
  title,
  description,
}: MappingBuilderProps) {
  const [expandedIndex, setExpandedIndex] = useState<number | null>(null);

  const effectiveTitle = title || (mode === 'output' ? 'Output Claim Mappings' : mode === 'input' ? 'Input Mappings' : 'Transform Mappings');
  const effectiveDescription = description || (
    mode === 'output'
      ? 'Map journey data or claims to output token claims'
      : mode === 'input'
      ? 'Map data to step input parameters'
      : 'Transform and map data between steps'
  );

  const addMapping = () => {
    if (mode === 'transform') {
      const newMapping: TransformMapping = {
        source: '',
        sourceType: 'journeyData',
        target: '',
      };
      (onChange as (m: TransformMapping[]) => void)([...(mappings as TransformMapping[]), newMapping]);
    } else {
      const newMapping: ClaimMapping = {
        sourceType: 'journeyData',
        sourcePath: '',
        targetClaimType: '',
      };
      (onChange as (m: ClaimMapping[]) => void)([...(mappings as ClaimMapping[]), newMapping]);
    }
    setExpandedIndex(mappings.length);
  };

  const updateMapping = (index: number, updates: Partial<ClaimMapping> | Partial<TransformMapping>) => {
    if (mode === 'transform') {
      const newMappings = [...(mappings as TransformMapping[])];
      newMappings[index] = { ...newMappings[index], ...updates } as TransformMapping;
      (onChange as (m: TransformMapping[]) => void)(newMappings);
    } else {
      const newMappings = [...(mappings as ClaimMapping[])];
      newMappings[index] = { ...newMappings[index], ...updates } as ClaimMapping;
      (onChange as (m: ClaimMapping[]) => void)(newMappings);
    }
  };

  const removeMapping = (index: number) => {
    if (mode === 'transform') {
      (onChange as (m: TransformMapping[]) => void)((mappings as TransformMapping[]).filter((_, i) => i !== index));
    } else {
      (onChange as (m: ClaimMapping[]) => void)((mappings as ClaimMapping[]).filter((_, i) => i !== index));
    }
    if (expandedIndex === index) setExpandedIndex(null);
  };

  const getSuggestionsForType = (sourceType: string): string[] => {
    switch (sourceType) {
      case 'journeyData':
        return journeyDataSuggestions;
      case 'claim':
        return claimSuggestions;
      case 'context':
        return contextSuggestions;
      default:
        return [];
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <div>
          <h4 className="text-sm font-medium text-gray-700">{effectiveTitle}</h4>
          {effectiveDescription && (
            <p className="text-xs text-gray-500">{effectiveDescription}</p>
          )}
        </div>
        <button
          type="button"
          onClick={addMapping}
          className="text-sm text-blue-600 hover:text-blue-800 flex items-center"
        >
          <PlusIcon className="h-4 w-4 mr-1" /> Add Mapping
        </button>
      </div>

      {mappings.length === 0 && (
        <div className="text-center py-4 text-gray-500 text-xs bg-gray-50 rounded-lg">
          No mappings defined. Click "Add Mapping" to create one.
        </div>
      )}

      <div className="space-y-2">
        {(mappings as (ClaimMapping | TransformMapping)[]).map((mapping, index) => (
          <div key={index} className="border rounded-lg bg-white">
            {/* Header - collapsed view */}
            <div
              className="flex items-center justify-between p-3 cursor-pointer hover:bg-gray-50"
              onClick={() => setExpandedIndex(expandedIndex === index ? null : index)}
            >
              <div className="flex items-center gap-2 text-sm">
                <span className="text-gray-400 text-xs">{index + 1}.</span>
                <span className="font-medium text-purple-600">
                  {'sourceType' in mapping ? mapping.sourceType : 'journeyData'}
                </span>
                <span className="text-gray-600">
                  {'sourcePath' in mapping ? mapping.sourcePath : (mapping as TransformMapping).source}
                </span>
                <ArrowRightIcon className="h-3 w-3 text-gray-400" />
                <span className="font-medium text-green-600">
                  {'targetClaimType' in mapping ? mapping.targetClaimType : (mapping as TransformMapping).target}
                </span>
                {'transform' in mapping && mapping.transform && (
                  <span className="text-xs text-orange-600 bg-orange-50 px-1 rounded">
                    {mapping.transform}
                  </span>
                )}
              </div>
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  onClick={(e) => {
                    e.stopPropagation();
                    removeMapping(index);
                  }}
                  className="text-red-400 hover:text-red-600 p-1"
                >
                  <TrashIcon className="h-4 w-4" />
                </button>
                <ChevronDownIcon
                  className={`h-4 w-4 text-gray-400 transition-transform ${
                    expandedIndex === index ? 'rotate-180' : ''
                  }`}
                />
              </div>
            </div>

            {/* Expanded view */}
            {expandedIndex === index && (
              <div className="p-3 border-t bg-gray-50 space-y-3">
                {mode === 'transform' ? (
                  <TransformMappingEditor
                    mapping={mapping as TransformMapping}
                    onChange={(updates) => updateMapping(index, updates)}
                    getSuggestions={getSuggestionsForType}
                  />
                ) : (
                  <ClaimMappingEditor
                    mapping={mapping as ClaimMapping}
                    onChange={(updates) => updateMapping(index, updates)}
                    mode={mode}
                    getSuggestions={getSuggestionsForType}
                  />
                )}
              </div>
            )}
          </div>
        ))}
      </div>

      {/* Preview */}
      {mappings.length > 0 && (
        <div className="mt-2 p-2 bg-gray-100 rounded text-xs">
          <div className="text-gray-500 mb-1">Preview:</div>
          <div className="space-y-1">
            {(mappings as (ClaimMapping | TransformMapping)[]).map((m, i) => (
              <div key={i} className="font-mono text-gray-700">
                {'sourcePath' in m ? (
                  <>
                    <span className="text-purple-600">{m.sourceType}</span>
                    <span className="text-gray-500">.</span>
                    <span className="text-blue-600">{m.sourcePath}</span>
                    <span className="text-gray-400"> → </span>
                    <span className="text-green-600">{m.targetClaimType}</span>
                    {m.defaultValue && (
                      <span className="text-orange-500"> (default: "{m.defaultValue}")</span>
                    )}
                  </>
                ) : (
                  <>
                    <span className="text-purple-600">{(m as TransformMapping).sourceType}</span>
                    <span className="text-gray-500">.</span>
                    <span className="text-blue-600">{(m as TransformMapping).source}</span>
                    {(m as TransformMapping).transform && (
                      <span className="text-orange-500"> | {(m as TransformMapping).transform}</span>
                    )}
                    <span className="text-gray-400"> → </span>
                    <span className="text-green-600">{(m as TransformMapping).target}</span>
                  </>
                )}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

// Claim mapping editor sub-component
function ClaimMappingEditor({
  mapping,
  onChange,
  mode,
  getSuggestions,
}: {
  mapping: ClaimMapping;
  onChange: (updates: Partial<ClaimMapping>) => void;
  mode: 'output' | 'input';
  getSuggestions: (type: string) => string[];
}) {
  const suggestions = getSuggestions(mapping.sourceType);

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Source Type</label>
          <select
            value={mapping.sourceType}
            onChange={(e) => onChange({ sourceType: e.target.value as ClaimMapping['sourceType'] })}
            className="w-full rounded-md border-gray-300 text-sm"
          >
            {sourceTypes
              .filter((t) => mode === 'input' || t.value !== 'input')
              .map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
          </select>
          <p className="text-xs text-gray-500 mt-1">
            {sourceTypes.find((t) => t.value === mapping.sourceType)?.description}
          </p>
        </div>

        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">
            {mapping.sourceType === 'literal' ? 'Value' : 'Source Path'}
          </label>
          <input
            type="text"
            value={mapping.sourcePath}
            onChange={(e) => onChange({ sourcePath: e.target.value })}
            list={`source-suggestions-${mapping.sourceType}`}
            placeholder={mapping.sourceType === 'literal' ? 'Static value' : 'e.g., email or user.profile.name'}
            className="w-full rounded-md border-gray-300 text-sm"
          />
          {suggestions.length > 0 && (
            <datalist id={`source-suggestions-${mapping.sourceType}`}>
              {suggestions.map((s) => (
                <option key={s} value={s} />
              ))}
            </datalist>
          )}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">
            {mode === 'output' ? 'Target Claim Type' : 'Target Parameter'}
          </label>
          <input
            type="text"
            value={mapping.targetClaimType}
            onChange={(e) => onChange({ targetClaimType: e.target.value })}
            list="target-claim-suggestions"
            placeholder={mode === 'output' ? 'e.g., email or custom_claim' : 'e.g., userId'}
            className="w-full rounded-md border-gray-300 text-sm"
          />
          <datalist id="target-claim-suggestions">
            {claimSuggestions.map((s) => (
              <option key={s} value={s} />
            ))}
          </datalist>
        </div>

        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Default Value</label>
          <input
            type="text"
            value={mapping.defaultValue || ''}
            onChange={(e) => onChange({ defaultValue: e.target.value || undefined })}
            placeholder="Used if source is empty"
            className="w-full rounded-md border-gray-300 text-sm"
          />
        </div>
      </div>
    </div>
  );
}

// Transform mapping editor sub-component
function TransformMappingEditor({
  mapping,
  onChange,
  getSuggestions,
}: {
  mapping: TransformMapping;
  onChange: (updates: Partial<TransformMapping>) => void;
  getSuggestions: (type: string) => string[];
}) {
  const suggestions = getSuggestions(mapping.sourceType);

  return (
    <div className="space-y-3">
      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Source Type</label>
          <select
            value={mapping.sourceType}
            onChange={(e) => onChange({ sourceType: e.target.value as TransformMapping['sourceType'] })}
            className="w-full rounded-md border-gray-300 text-sm"
          >
            {sourceTypes
              .filter((t) => t.value !== 'input')
              .map((t) => (
                <option key={t.value} value={t.value}>
                  {t.label}
                </option>
              ))}
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Source Path</label>
          <input
            type="text"
            value={mapping.source}
            onChange={(e) => onChange({ source: e.target.value })}
            list={`transform-source-suggestions-${mapping.sourceType}`}
            placeholder="e.g., email or user.profile.name"
            className="w-full rounded-md border-gray-300 text-sm"
          />
          {suggestions.length > 0 && (
            <datalist id={`transform-source-suggestions-${mapping.sourceType}`}>
              {suggestions.map((s) => (
                <option key={s} value={s} />
              ))}
            </datalist>
          )}
        </div>
      </div>

      <div className="grid grid-cols-2 gap-3">
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Transform</label>
          <select
            value={mapping.transform || ''}
            onChange={(e) => onChange({ transform: e.target.value as TransformMapping['transform'] || undefined })}
            className="w-full rounded-md border-gray-300 text-sm"
          >
            {transformOptions.map((t) => (
              <option key={t.value} value={t.value}>
                {t.label}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Target Path</label>
          <input
            type="text"
            value={mapping.target}
            onChange={(e) => onChange({ target: e.target.value })}
            placeholder="e.g., normalizedEmail"
            className="w-full rounded-md border-gray-300 text-sm"
          />
        </div>
      </div>

      {/* Transform-specific config */}
      {mapping.transform === 'split' && (
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Delimiter</label>
          <input
            type="text"
            value={mapping.transformConfig?.delimiter || ','}
            onChange={(e) =>
              onChange({
                transformConfig: { ...mapping.transformConfig, delimiter: e.target.value },
              })
            }
            placeholder=","
            className="w-32 rounded-md border-gray-300 text-sm"
          />
        </div>
      )}

      {mapping.transform === 'join' && (
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Separator</label>
          <input
            type="text"
            value={mapping.transformConfig?.separator || ', '}
            onChange={(e) =>
              onChange({
                transformConfig: { ...mapping.transformConfig, separator: e.target.value },
              })
            }
            placeholder=", "
            className="w-32 rounded-md border-gray-300 text-sm"
          />
        </div>
      )}

      {mapping.transform === 'replace' && (
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label className="block text-xs font-medium text-gray-600 mb-1">Find</label>
            <input
              type="text"
              value={mapping.transformConfig?.find || ''}
              onChange={(e) =>
                onChange({
                  transformConfig: { ...mapping.transformConfig, find: e.target.value },
                })
              }
              placeholder="Text to find"
              className="w-full rounded-md border-gray-300 text-sm"
            />
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-600 mb-1">Replace With</label>
            <input
              type="text"
              value={mapping.transformConfig?.replaceWith || ''}
              onChange={(e) =>
                onChange({
                  transformConfig: { ...mapping.transformConfig, replaceWith: e.target.value },
                })
              }
              placeholder="Replacement text"
              className="w-full rounded-md border-gray-300 text-sm"
            />
          </div>
        </div>
      )}

      {mapping.transform === 'custom' && (
        <div>
          <label className="block text-xs font-medium text-gray-600 mb-1">Expression</label>
          <input
            type="text"
            value={mapping.transformConfig?.expression || ''}
            onChange={(e) =>
              onChange({
                transformConfig: { ...mapping.transformConfig, expression: e.target.value },
              })
            }
            placeholder="e.g., value.substring(0, 10)"
            className="w-full rounded-md border-gray-300 text-sm font-mono"
          />
          <p className="text-xs text-gray-500 mt-1">
            Use 'value' to reference the source value
          </p>
        </div>
      )}
    </div>
  );
}

export default MappingBuilder;
