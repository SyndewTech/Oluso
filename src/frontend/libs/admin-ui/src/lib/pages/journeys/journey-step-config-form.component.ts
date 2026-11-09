import { Component, computed, effect, forwardRef, inject, input, output, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../services/api.service';

/**
 * A section grouping for the schema renderer. Built from the schema's
 * top-level `x-sections` annotation; absent that, a single headerless
 * section holding every field is used.
 */
interface ResolvedSection {
  /** Stable ID used as the toggle key. */
  id: string;
  /** Display label. Undefined/empty = render fields without a header. */
  label?: string;
  /** Whether this section starts expanded when the schema first loads. */
  defaultExpanded: boolean;
  /** Fields contained in this section (already filtered by showWhen). */
  fields: FieldEntry[];
}

/**
 * Field metadata derived from a JSON-schema entry. The renderer flattens
 * the schema into a list of these so the template can switch on `kind`.
 */
interface FieldEntry {
  name: string;
  label: string;
  description?: string;
  required: boolean;
  kind:
    | 'text'
    | 'number'
    | 'boolean'
    | 'enum'
    | 'secret'
    | 'textarea'
    | 'string-array'
    | 'number-array'
    | 'enum-array'
    | 'object-map'
    | 'object-map-of-object'
    | 'array-of-object'
    | 'nested-object'
    | 'json-fallback'
    | 'json'
    | 'enum-remote'
    | 'unsupported';
  defaultValue?: unknown;
  enumValues?: string[];
  enumSource?: { endpoint: string; valueField: string; labelField: string; descriptionField?: string; filters?: Record<string, string> };
  localizable?: boolean;
  localizableLabels?: boolean;
  // For number ranges (optional)
  min?: number;
  max?: number;
  // Sub-schema for object-map / array-of-object / nested-object — the
  // shape used to recursively render child fields.
  subSchema?: Record<string, unknown>;
  // For object-map (string→primitive): the value type ("string" | "number" | "boolean")
  mapValueType?: 'string' | 'number' | 'boolean';
  // showWhen — sibling-field gate for nested-object child fields. Parsed
  // from the same JSON shape the backend emits: { field, value }.
  showWhen?: { field: string; value: unknown };
  // Optional value→label map for enum dropdowns. Used when the schema's
  // x-enumSource resolved to step IDs but the parent has nicer display
  // names available — set by the journey-builder before passing the
  // schema in.
  enumLabels?: Record<string, string>;
  // Empty-state action — when the dropdown has no options, render a
  // clickable hint that emits (addStep) with this step type so the
  // parent (journey-builder) can append a step of the right type to
  // its policy. Shape: { type: "addStep", stepType: "passwordless_email" }.
  emptyAction?: { type: 'addStep'; stepType: string };
}

/**
 * Schema-driven config form for journey step types. Reads the
 * <c>configurationSchema</c> emitted by the backend (server-side
 * <c>RegisterTypeWithRawSchema</c>) and renders a form that the journey
 * builder can drop in for any step type whose schema is fully described.
 *
 * Supported schema shapes:
 *   - <c>type</c>: "string" | "number" | "boolean" | "array" | "object"
 *   - <c>default</c>: initial value
 *   - <c>description</c>: helper text rendered under the field
 *   - <c>enum</c>: dropdown options (string only)
 *   - <c>items</c>: "string" → string-array; { type: "object", properties }
 *     → array-of-object (Phase 2)
 *   - <c>additionalProperties</c>: { type: "string" | "number" | "boolean" }
 *     → key/value map (Phase 2); { type: "object", properties } →
 *     keyed map of structured rows (Phase 2)
 *   - <c>properties</c> on a "type": "object" → nested object (Phase 2)
 *   - <c>x-control</c>: "secret-input" → password field; "textarea" →
 *     multi-line
 *   - <c>x-enumSource</c>: { endpoint, valueField, labelField } loaded via
 *     ApiService — populates enum-array checkboxes
 *   - <c>showWhen</c>: { field, value } — hides the field when the
 *     sibling field's value doesn't match (used inside nested-object only)
 *   - <c>required</c>: surfaces a red asterisk; not enforced client-side
 */
@Component({
  selector: 'admin-journey-step-config-form',
  standalone: true,
  imports: [CommonModule, FormsModule, forwardRef(() => JourneyStepConfigFormComponent)],
  template: `
    @if (visibleFields().length === 0) {
      <p class="text-xs text-gray-400 italic">This step type has no configurable options.</p>
    } @else {
      @if (isRoot() && hasLocalizableFields() && effectiveLocales().length > 1) {
        <div class="flex items-center gap-1.5 mb-3 pb-2 border-b border-gray-200">
          <span class="text-[11px] text-gray-500 uppercase tracking-wide mr-1">Locale</span>
          @for (loc of effectiveLocales(); track loc) {
            <button
              type="button"
              (click)="selectedLocale.set(loc); localeChange.emit(loc)"
              [class]="selectedLocale() === loc
                ? 'px-2.5 py-0.5 text-xs font-medium rounded-full bg-primary-600 text-white'
                : 'px-2.5 py-0.5 text-xs font-medium rounded-full bg-gray-100 text-gray-600 hover:bg-gray-200'"
            >
              {{ loc }}
            </button>
          }
        </div>
      }
      <div class="space-y-3">
        @for (section of sections(); track section.id) {
          @if (section.label) {
            <div class="border border-gray-200 rounded-md">
              <button
                type="button"
                (click)="toggleSection(section.id)"
                class="w-full flex items-center justify-between p-3 text-left hover:bg-gray-50"
              >
                <span class="font-medium text-sm">{{ section.label }}</span>
                <span class="text-gray-400 text-xs">{{ isSectionExpanded(section.id) ? '▾' : '▸' }}</span>
              </button>
              @if (isSectionExpanded(section.id)) {
                <div class="p-3 border-t bg-gray-50 space-y-3">
                  @for (entry of section.fields; track entry.name) {
                    <ng-container *ngTemplateOutlet="fieldTpl; context: { $implicit: entry }"></ng-container>
                  }
                </div>
              }
            </div>
          } @else {
            @for (entry of section.fields; track entry.name) {
              <ng-container *ngTemplateOutlet="fieldTpl; context: { $implicit: entry }"></ng-container>
            }
          }
        }
      </div>
    }

    <ng-template #fieldTpl let-entry>
      <div>
        @if (entry.kind !== 'boolean') {
          <label class="block text-xs font-medium text-gray-700 mb-1">
            {{ entry.label }}
            @if (entry.required) {
              <span class="text-red-500" aria-label="required">*</span>
            }
          </label>
        }

        @switch (entry.kind) {
              @case ('text') {
                @if (entry.localizable) {
                  <div class="relative">
                    <input
                      type="text"
                      [ngModel]="getLocalizedStringValue(entry.name, entry.defaultValue)"
                      (ngModelChange)="updateLocalizedField(entry.name, $event)"
                      class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md pr-12"
                      [placeholder]="getLocalizedPlaceholder(entry.name, entry.defaultValue)"
                    />
                    <span class="absolute right-2 top-1/2 -translate-y-1/2 text-[10px] font-medium text-gray-400 uppercase">{{ selectedLocale() }}</span>
                  </div>
                } @else {
                  <input
                    type="text"
                    [ngModel]="getStringValue(entry.name, entry.defaultValue)"
                    (ngModelChange)="updateField(entry.name, $event)"
                    class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md"
                  />
                }
              }
              @case ('number') {
                <input
                  type="number"
                  [attr.min]="entry.min ?? null"
                  [attr.max]="entry.max ?? null"
                  [ngModel]="getNumberValue(entry.name, entry.defaultValue)"
                  (ngModelChange)="updateField(entry.name, $event === '' || $event === null ? null : +$event)"
                  class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md"
                />
              }
              @case ('boolean') {
                <div class="flex items-center">
                  <input
                    type="checkbox"
                    [id]="'cfg-' + entry.name"
                    [ngModel]="getBooleanValue(entry.name, entry.defaultValue)"
                    (ngModelChange)="updateField(entry.name, $event)"
                    class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                  />
                  <label [for]="'cfg-' + entry.name" class="ml-2 text-xs text-gray-700">
                    {{ entry.description || entry.label }}
                  </label>
                </div>
              }
              @case ('enum') {
                <select
                  [ngModel]="getStringValue(entry.name, entry.defaultValue)"
                  (ngModelChange)="updateField(entry.name, $event)"
                  class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md bg-white"
                  [disabled]="(entry.enumValues?.length ?? 0) === 0"
                >
                  @if ((entry.enumValues?.length ?? 0) === 0) {
                    <option [value]="''" disabled>No options available</option>
                  } @else {
                    <option [value]="''" disabled>Select…</option>
                    @for (opt of entry.enumValues; track opt) {
                      <option [value]="opt">{{ entry.enumLabels?.[opt] ?? opt }}</option>
                    }
                  }
                </select>
                @if ((entry.enumValues?.length ?? 0) === 0 && entry.emptyAction?.type === 'addStep') {
                  <button
                    type="button"
                    (click)="addStep.emit(entry.emptyAction!.stepType)"
                    class="mt-1 text-xs text-primary-600 hover:text-primary-800 underline"
                  >
                    + Add a {{ humanizeStepType(entry.emptyAction!.stepType) }} step to this journey
                  </button>
                }
                @if (entry.localizableLabels && (entry.enumValues?.length ?? 0) > 0) {
                  <div class="mt-2 border border-gray-200 rounded-md p-2 bg-gray-50 space-y-1.5">
                    <p class="text-[11px] text-gray-500 uppercase tracking-wide">
                      Display labels ({{ selectedLocale() }})
                    </p>
                    @for (opt of entry.enumValues; track opt) {
                      <div class="flex gap-1.5 items-center">
                        <span class="text-xs text-gray-500 font-mono w-24 truncate" [title]="opt">{{ opt }}</span>
                        <input
                          type="text"
                          [ngModel]="getLocalizedEnumLabel(entry.name, opt)"
                          (ngModelChange)="updateLocalizedEnumLabel(entry.name, opt, $event)"
                          [placeholder]="entry.enumLabels?.[opt] ?? opt"
                          class="flex-1 px-2 py-1 text-xs border border-gray-300 rounded"
                        />
                      </div>
                    }
                  </div>
                }
              }
              @case ('secret') {
                <div class="relative">
                  <input
                    [type]="isRevealed(entry.name) ? 'text' : 'password'"
                    autocomplete="off"
                    [ngModel]="getStringValue(entry.name, entry.defaultValue)"
                    (ngModelChange)="updateField(entry.name, $event)"
                    class="w-full pr-16 px-3 py-1.5 text-sm border border-gray-300 rounded-md font-mono"
                  />
                  <button
                    type="button"
                    (click)="toggleReveal(entry.name)"
                    class="absolute right-2 top-1/2 -translate-y-1/2 text-xs text-primary-600 hover:text-primary-800"
                  >
                    {{ isRevealed(entry.name) ? 'Hide' : 'Show' }}
                  </button>
                </div>
              }
              @case ('textarea') {
                @if (entry.localizable) {
                  <div class="relative">
                    <textarea
                      rows="4"
                      [ngModel]="getLocalizedStringValue(entry.name, entry.defaultValue)"
                      (ngModelChange)="updateLocalizedField(entry.name, $event)"
                      class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md font-mono"
                      [placeholder]="'No translation for ' + selectedLocale()"
                    ></textarea>
                    <span class="absolute right-2 top-2 text-[10px] font-medium text-gray-400 uppercase">{{ selectedLocale() }}</span>
                  </div>
                } @else {
                  <textarea
                    rows="4"
                    [ngModel]="getStringValue(entry.name, entry.defaultValue)"
                    (ngModelChange)="updateField(entry.name, $event)"
                    class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md font-mono"
                  ></textarea>
                }
              }
              @case ('string-array') {
                <input
                  type="text"
                  [ngModel]="getStringArrayValue(entry.name)"
                  (ngModelChange)="updateField(entry.name, splitCsv($event))"
                  placeholder="Comma-separated values"
                  class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md font-mono"
                />
              }
              @case ('number-array') {
                <input
                  type="text"
                  [ngModel]="getStringArrayValue(entry.name)"
                  (ngModelChange)="updateField(entry.name, splitCsvNumeric($event))"
                  placeholder="Comma-separated numbers (e.g. 404, 409)"
                  inputmode="numeric"
                  class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md font-mono"
                />
              }
              @case ('json') {
                <textarea
                  rows="8"
                  [ngModel]="getJsonValue(entry.name)"
                  (ngModelChange)="updateJsonObjectField(entry.name, $event)"
                  placeholder='{ "userId": "{claim:sub}" }'
                  class="w-full px-3 py-1.5 text-sm border rounded-md font-mono"
                  [class.border-red-400]="jsonErrors()[entry.name]"
                  [class.border-gray-300]="!jsonErrors()[entry.name]"
                ></textarea>
                @if (jsonErrors()[entry.name]; as jsonError) {
                  <p class="text-xs text-red-600 mt-1">{{ jsonError }} — not saved until it's fixed.</p>
                }
              }
              @case ('json-fallback') {
                <textarea
                  rows="4"
                  [ngModel]="getJsonValue(entry.name)"
                  (ngModelChange)="updateFieldFromJson(entry.name, $event)"
                  placeholder='[ { ... }, { ... } ]'
                  class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md font-mono"
                ></textarea>
                <p class="text-xs text-gray-500 mt-1">JSON editor — this field has no declared shape, so values are entered raw.</p>
              }
              @case ('enum-remote') {
                @let opts = enumSourceOptions(entry.enumSource!);
                @if (opts.loading) {
                  <p class="text-xs text-gray-400 italic">Loading options…</p>
                } @else if (opts.error) {
                  <p class="text-xs text-red-600">Couldn't load options: {{ opts.error }}</p>
                } @else {
                  <select
                    [ngModel]="getStringValue(entry.name, entry.defaultValue)"
                    (ngModelChange)="updateField(entry.name, $event || undefined)"
                    class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md bg-white"
                  >
                    <option [value]="''">{{ entry.required ? 'Select…' : 'None' }}</option>
                    @for (opt of opts.values; track opt.value) {
                      <option [value]="opt.value">{{ opt.label }}{{ opt.description ? ' — ' + opt.description : '' }}</option>
                    }
                    @let current = getStringValue(entry.name, entry.defaultValue);
                    @if (current && !hasOption(opts.values, current)) {
                      <option [value]="current">{{ current }} (not found)</option>
                    }
                  </select>
                }
              }
              @case ('enum-array') {
                @let opts = enumSourceOptions(entry.enumSource!);
                @if (opts.loading) {
                  <p class="text-xs text-gray-400 italic">Loading options…</p>
                } @else if (opts.error) {
                  <p class="text-xs text-red-600">Couldn't load options: {{ opts.error }}</p>
                } @else if (opts.values.length === 0) {
                  <p class="text-xs text-gray-400 italic">No options available.</p>
                } @else {
                  <div class="border border-gray-200 rounded-md p-2 max-h-40 overflow-y-auto space-y-1">
                    @for (opt of opts.values; track opt.value) {
                      <label class="flex items-center text-xs text-gray-700">
                        <input
                          type="checkbox"
                          [checked]="isInArray(entry.name, opt.value)"
                          (change)="toggleArrayValue(entry.name, opt.value, $any($event.target).checked)"
                          class="rounded border-gray-300 text-primary-600 focus:ring-primary-500 mr-2"
                        />
                        <span>
                          <span class="font-medium">{{ opt.label }}</span>
                          @if (opt.description) {
                            <span class="text-gray-400"> — {{ opt.description }}</span>
                          }
                        </span>
                      </label>
                    }
                  </div>
                  <p class="mt-1 text-[11px] text-gray-400">
                    Leave all unchecked to allow every option.
                  </p>
                }
              }
              @case ('object-map') {
                @let entries = getMapEntries(entry.name);
                <div class="space-y-1.5 border border-gray-200 rounded-md p-2 bg-gray-50">
                  @for (row of entries; track row.id) {
                    <div class="flex gap-1.5 items-center">
                      <input
                        type="text"
                        placeholder="key"
                        [ngModel]="row.key"
                        (ngModelChange)="updateMapKey(entry.name, row.id, $event)"
                        class="flex-1 px-2 py-1 text-xs border border-gray-300 rounded font-mono"
                      />
                      <span class="text-gray-400 text-xs">=</span>
                      @if (entry.mapValueType === 'boolean') {
                        <input
                          type="checkbox"
                          [ngModel]="!!row.value"
                          (ngModelChange)="updateMapValue(entry.name, row.id, $event)"
                          class="rounded border-gray-300"
                        />
                      } @else if (entry.mapValueType === 'number') {
                        <input
                          type="number"
                          [ngModel]="row.value"
                          (ngModelChange)="updateMapValue(entry.name, row.id, $event === '' || $event === null ? null : +$event)"
                          class="flex-1 px-2 py-1 text-xs border border-gray-300 rounded font-mono"
                        />
                      } @else {
                        <input
                          type="text"
                          placeholder="value"
                          [ngModel]="row.value"
                          (ngModelChange)="updateMapValue(entry.name, row.id, $event)"
                          class="flex-1 px-2 py-1 text-xs border border-gray-300 rounded font-mono"
                        />
                      }
                      <button
                        type="button"
                        (click)="removeMapEntry(entry.name, row.id)"
                        class="text-red-500 hover:text-red-700 text-xs px-1"
                        aria-label="Remove entry"
                      >
                        ×
                      </button>
                    </div>
                  }
                  <button
                    type="button"
                    (click)="addMapEntry(entry.name, entry.mapValueType)"
                    class="text-xs text-primary-600 hover:text-primary-800"
                  >
                    + Add entry
                  </button>
                </div>
              }
              @case ('object-map-of-object') {
                @let mapEntries = getStructuredMapEntries(entry.name);
                <div class="space-y-2 border border-gray-200 rounded-md p-2 bg-gray-50">
                  @for (row of mapEntries; track row.id) {
                    <div class="border border-gray-300 rounded bg-white p-2 space-y-1.5">
                      <div class="flex gap-1.5 items-center">
                        <span class="text-[11px] text-gray-500 uppercase tracking-wide">key</span>
                        <input
                          type="text"
                          placeholder="key"
                          [ngModel]="row.key"
                          (ngModelChange)="updateStructuredMapKey(entry.name, row.id, $event)"
                          class="flex-1 px-2 py-1 text-xs border border-gray-300 rounded font-mono"
                        />
                        <button
                          type="button"
                          (click)="removeStructuredMapEntry(entry.name, row.id)"
                          class="text-red-500 hover:text-red-700 text-xs px-1"
                          aria-label="Remove entry"
                        >
                          ×
                        </button>
                      </div>
                      <admin-journey-step-config-form
                        [schema]="entry.subSchema"
                        [value]="row.value"
                        [locales]="locales()"
                        [isRoot]="false"
                        [parentLocale]="selectedLocale()"
                        (valueChange)="updateStructuredMapValue(entry.name, row.id, $event)"
                      />
                    </div>
                  }
                  <button
                    type="button"
                    (click)="addStructuredMapEntry(entry.name)"
                    class="text-xs text-primary-600 hover:text-primary-800"
                  >
                    + Add entry
                  </button>
                </div>
              }
              @case ('array-of-object') {
                @let arr = getArrayEntries(entry.name);
                <div class="space-y-2 border border-gray-200 rounded-md p-2 bg-gray-50">
                  @for (row of arr; track row.id; let i = $index) {
                    <div class="border border-gray-300 rounded bg-white p-2 space-y-1.5">
                      <div class="flex justify-between items-center">
                        <span class="text-[11px] text-gray-500 uppercase tracking-wide">item {{ i + 1 }}</span>
                        <button
                          type="button"
                          (click)="removeArrayEntry(entry.name, row.id)"
                          class="text-red-500 hover:text-red-700 text-xs px-1"
                          aria-label="Remove item"
                        >
                          ×
                        </button>
                      </div>
                      <admin-journey-step-config-form
                        [schema]="entry.subSchema"
                        [value]="row.value"
                        [locales]="locales()"
                        [isRoot]="false"
                        [parentLocale]="selectedLocale()"
                        (valueChange)="updateArrayEntry(entry.name, row.id, $event)"
                      />
                    </div>
                  }
                  <button
                    type="button"
                    (click)="addArrayEntry(entry.name)"
                    class="text-xs text-primary-600 hover:text-primary-800"
                  >
                    + Add item
                  </button>
                </div>
              }
              @case ('nested-object') {
                <div class="border-l-2 border-gray-200 pl-3">
                  <admin-journey-step-config-form
                    [schema]="entry.subSchema"
                    [value]="getObjectValue(entry.name)"
                    [locales]="locales()"
                    [isRoot]="false"
                        [parentLocale]="selectedLocale()"
                    (valueChange)="updateField(entry.name, $event)"
                  />
                </div>
              }
              @case ('unsupported') {
                <p class="text-xs text-amber-600 italic">
                  Field type not supported by the schema renderer. Edit the policy JSON to set <code class="font-mono">{{ entry.name }}</code>.
                </p>
              }
            }

        @if (entry.kind !== 'boolean' && entry.description) {
          <p class="mt-1 text-xs text-gray-500">{{ entry.description }}</p>
        }
      </div>
    </ng-template>
  `,
})
export class JourneyStepConfigFormComponent {
  private readonly api = inject(ApiService);

  readonly schema = input<Record<string, unknown> | null | undefined>(null);
  readonly value = input<Record<string, unknown> | null | undefined>({});
  readonly valueChange = output<Record<string, unknown>>();
  /**
   * Emitted when the user clicks an empty-state action ("Add a step")
   * from a dropdown whose schema declared x-emptyAction. Carries the
   * step type the parent should add to its policy.
   */
  readonly addStep = output<string>();

  /**
   * Available locale codes for localizable fields. When provided AND the
   * schema contains at least one field with <c>x-localizable: true</c>,
   * a locale pill bar appears at the top of the form. Switching locales
   * changes which translation the admin is editing — the underlying value
   * is stored as <c>{ "en": "Hello", "fr": "Bonjour" }</c>.
   */
  readonly locales = input<string[]>([]);
  readonly isRoot = input<boolean>(true);
  readonly parentLocale = input<string | undefined>(undefined);

  readonly selectedLocale = signal<string>('en');
  readonly localeChange = output<string>();

  private readonly _syncLocale = effect(() => {
    const parent = this.parentLocale();
    if (parent) this.selectedLocale.set(parent);
  });

  readonly hasLocalizableFields = computed<boolean>(() =>
    this.fields().some((f) => f.localizable),
  );

  readonly effectiveLocales = computed<string[]>(() => {
    const input = this.locales();
    if (input.length > 0) return input;
    return ['en'];
  });

  readonly fields = computed<FieldEntry[]>(() => {
    const s = this.schema();
    if (!s || typeof s !== 'object') return [];
    // Two schema shapes flow in here:
    //   1. Flat properties dict — the shape DefaultStepHandlerRegistry's
    //      RegisterTypeWithRawSchema produces. Fields live at the top level.
    //   2. JSON-Schema envelope `{ type: "object", properties: {...} }` —
    //      the shape SchemaBuilder.Build() produces, used by every plugin
    //      ICustomStepHandlerMetadata (e.g. a module's custom step handler
    //      and its custom step types). Fields live under .properties.
    // Detect (2) by `type === "object"` + a dict-shaped `properties`, then
    // unwrap. Without this every plugin step rendered as a list of
    // unsupported chips because `type`/`title`/`required` were treated as
    // fields, not envelope metadata.
    const fieldsDict = (
      (s as Record<string, unknown>)['type'] === 'object' &&
      isObject((s as Record<string, unknown>)['properties'])
    )
      ? (s as Record<string, unknown>)['properties'] as Record<string, unknown>
      : (s as Record<string, unknown>);

    return Object.entries(fieldsDict)
      .filter(([name]) => !name.startsWith('x-'))
      .map(([name, raw]) => parseField(name, raw));
  });

  /**
   * Fields filtered by their `showWhen` guard. A field with
   * `showWhen: { field: "type", value: "bearer" }` is hidden until the
   * sibling `type` field equals `"bearer"`.
   */
  readonly visibleFields = computed<FieldEntry[]>(() => {
    const v = this.value() ?? {};
    return this.fields().filter((f) => {
      if (!f.showWhen) return true;
      return v[f.showWhen.field] === f.showWhen.value;
    });
  });

  /**
   * Sections derived from the schema's optional `x-sections` annotation.
   * When absent, returns one headerless section containing every visible
   * field — the renderer falls back to the flat layout. When present,
   * fields are grouped per the declared order; any field not listed in
   * a section lands in an "Other" section appended at the end so the
   * admin never loses a field to a typo in x-sections.
   */
  readonly sections = computed<ResolvedSection[]>(() => {
    const visible = this.visibleFields();
    const s = this.schema();
    const xSections = isObject(s) ? (s as Record<string, unknown>)['x-sections'] : null;

    if (!Array.isArray(xSections) || xSections.length === 0) {
      return [{ id: 'default', defaultExpanded: true, fields: visible }];
    }

    const fieldsByName = new Map<string, FieldEntry>();
    for (const f of visible) fieldsByName.set(f.name, f);

    const result: ResolvedSection[] = [];
    const consumed = new Set<string>();

    for (const sec of xSections) {
      if (!isObject(sec)) continue;
      const id = typeof sec['id'] === 'string' && sec['id'] ? (sec['id'] as string) : `s${result.length}`;
      const label = typeof sec['label'] === 'string' ? (sec['label'] as string) : undefined;
      const defaultExpanded = sec['expanded'] === true;
      const fieldNames = Array.isArray(sec['fields']) ? (sec['fields'] as unknown[]) : [];
      const fields: FieldEntry[] = [];
      for (const fname of fieldNames) {
        if (typeof fname !== 'string') continue;
        const f = fieldsByName.get(fname);
        if (f) {
          fields.push(f);
          consumed.add(fname);
        }
      }
      if (fields.length > 0) {
        result.push({ id, label, defaultExpanded, fields });
      }
    }

    const orphans = visible.filter((f) => !consumed.has(f.name));
    if (orphans.length > 0) {
      result.push({ id: '__other', label: 'Other', defaultExpanded: false, fields: orphans });
    }

    return result;
  });

  // Section expand state. Tracks user toggles separately from the
  // schema-declared default so the admin's choice survives schema
  // re-evaluations (which happen on every keystroke that mutates the
  // policy steps via showWhen / x-enumSource resolution).
  private readonly sectionOverrides = signal<Map<string, boolean>>(new Map());

  isSectionExpanded(id: string): boolean {
    const override = this.sectionOverrides().get(id);
    if (override !== undefined) return override;
    const section = this.sections().find((s) => s.id === id);
    return section?.defaultExpanded ?? false;
  }

  toggleSection(id: string): void {
    const next = !this.isSectionExpanded(id);
    this.sectionOverrides.update((map) => {
      const copy = new Map(map);
      copy.set(id, next);
      return copy;
    });
  }

  // Local UI state for password reveal toggles, keyed by field name.
  private readonly revealed = signal<Set<string>>(new Set());
  isRevealed(name: string): boolean {
    return this.revealed().has(name);
  }
  toggleReveal(name: string): void {
    this.revealed.update((set) => {
      const next = new Set(set);
      if (next.has(name)) next.delete(name);
      else next.add(name);
      return next;
    });
  }

  // Stable row IDs for object-map / array-of-object editors. Without
  // these, every keystroke that mutates the parent value would reorder
  // / re-key the rows and steal focus from the input the user is typing
  // in. Map-key collisions (two rows with the same key) are handled by
  // ID-keyed rows; collisions resolve when the second row's key changes.
  private nextRowId = 1;
  private readonly mapRowIds = new Map<string, string[]>();
  private readonly arrayRowIds = new Map<string, string[]>();
  private readonly mapKeyById = new Map<string, string>();

  // ────────── value accessors ──────────

  /**
   * Reads a localizable string field. When the stored value is a locale
   * map (<c>{ "en": "Hello", "fr": "Bonjour" }</c>), returns the entry
   * for the currently selected locale. When it's a plain string (legacy),
   * returns that string regardless of locale.
   */
  getLocalizedPlaceholder(name: string, fallback: unknown): string {
    const locale = this.selectedLocale();
    if (locale === 'en') return '';
    // Show the English value as placeholder so admin knows what to translate
    const v = this.value()?.[name];
    if (typeof v === 'string') return v;
    if (isObject(v)) {
      const en = (v as Record<string, unknown>)['en'];
      if (typeof en === 'string') return en;
    }
    return typeof fallback === 'string' ? fallback : '';
  }

  getLocalizedStringValue(name: string, fallback: unknown): string {
    const v = this.value()?.[name];
    const locale = this.selectedLocale();
    if (v === undefined || v === null) {
      // Show the schema default only for en; other locales start empty
      return locale === 'en' && typeof fallback === 'string' ? fallback : '';
    }
    // Plain string (legacy / not yet localised) — treat as the en value
    if (typeof v === 'string') return locale === 'en' ? v : '';
    if (isObject(v)) {
      const localeMap = v as Record<string, unknown>;
      const val = localeMap[locale];
      return typeof val === 'string' ? val : '';
    }
    return '';
  }

  /**
   * Writes a localizable string field. Stores as <c>{ locale: value }</c>,
   * preserving other locales. If the stored value was a plain string
   * (pre-localization migration), promotes it to a locale map with the
   * plain value filed under "en".
   */
  updateLocalizedField(name: string, newValue: string): void {
    const current = this.value() ?? {};
    const existing = current[name];
    const locale = this.selectedLocale();

    let localeMap: Record<string, string>;
    if (isObject(existing)) {
      localeMap = { ...(existing as Record<string, string>) };
    } else if (typeof existing === 'string' && existing) {
      localeMap = { en: existing };
    } else {
      localeMap = {};
    }

    if (newValue) {
      localeMap[locale] = newValue;
    } else {
      delete localeMap[locale];
    }

    this.valueChange.emit({ ...current, [name]: localeMap });
  }

  /**
   * For a localizable enum field with x-localizableLabels, reads the
   * per-locale label for a given option value. The config stores:
   * <c>{ "fieldName_labels": { "optionValue": { "en": "Label", "fr": "..." } } }</c>
   */
  getLocalizedEnumLabel(name: string, optionValue: string): string {
    const labelsKey = `${name}_labels`;
    const v = this.value()?.[labelsKey];
    if (!isObject(v)) return '';
    const optionLabels = v[optionValue];
    if (!isObject(optionLabels)) return typeof optionLabels === 'string' ? optionLabels : '';
    const locale = this.selectedLocale();
    const val = optionLabels[locale];
    return typeof val === 'string' ? val : '';
  }

  updateLocalizedEnumLabel(name: string, optionValue: string, label: string): void {
    const labelsKey = `${name}_labels`;
    const current = this.value() ?? {};
    const existing = isObject(current[labelsKey]) ? { ...(current[labelsKey] as Record<string, unknown>) } : {};
    const locale = this.selectedLocale();

    let optionLocaleMap: Record<string, string>;
    if (isObject(existing[optionValue])) {
      optionLocaleMap = { ...(existing[optionValue] as Record<string, string>) };
    } else {
      optionLocaleMap = {};
    }

    if (label) {
      optionLocaleMap[locale] = label;
    } else {
      delete optionLocaleMap[locale];
    }

    existing[optionValue] = optionLocaleMap;
    this.valueChange.emit({ ...current, [labelsKey]: existing });
  }

  getStringValue(name: string, fallback: unknown): string {
    const v = this.value()?.[name];
    if (v === undefined || v === null) return typeof fallback === 'string' ? fallback : '';
    if (typeof v === 'string') return v;
    if (isObject(v)) {
      const en = (v as Record<string, unknown>)['en'];
      if (typeof en === 'string') return en;
      const first = Object.values(v as Record<string, unknown>).find(val => typeof val === 'string');
      return typeof first === 'string' ? first : '';
    }
    return String(v);
  }

  getNumberValue(name: string, fallback: unknown): number | null {
    const v = this.value()?.[name];
    if (v === undefined || v === null || v === '') {
      return typeof fallback === 'number' ? fallback : null;
    }
    const n = typeof v === 'number' ? v : Number(v);
    return Number.isFinite(n) ? n : null;
  }

  getBooleanValue(name: string, fallback: unknown): boolean {
    const v = this.value()?.[name];
    if (v === undefined || v === null) return typeof fallback === 'boolean' ? fallback : false;
    return Boolean(v);
  }

  getStringArrayValue(name: string): string {
    const v = this.value()?.[name];
    if (Array.isArray(v)) return v.join(', ');
    if (typeof v === 'string') return v;
    return '';
  }

  isInArray(name: string, candidate: string): boolean {
    const v = this.value()?.[name];
    return Array.isArray(v) && v.includes(candidate);
  }

  getObjectValue(name: string): Record<string, unknown> {
    const v = this.value()?.[name];
    return isObject(v) ? (v as Record<string, unknown>) : {};
  }

  // ────────── mutations ──────────

  updateField(name: string, newValue: unknown): void {
    const current = this.value() ?? {};
    const next = { ...current, [name]: newValue };
    this.valueChange.emit(next);
  }

  toggleArrayValue(name: string, candidate: string, checked: boolean): void {
    const current = this.value() ?? {};
    const arr = Array.isArray(current[name]) ? [...(current[name] as string[])] : [];
    const idx = arr.indexOf(candidate);
    if (checked && idx === -1) arr.push(candidate);
    if (!checked && idx >= 0) arr.splice(idx, 1);
    this.updateField(name, arr);
  }

  /**
   * Pretty-prints a step type identifier for the empty-state hint
   * ("passwordless_email" → "passwordless email"). Kept human-friendly
   * for the inline call-to-action; the journey-builder will pass the
   * raw type back to addStep().
   */
  humanizeStepType(stepType: string): string {
    return stepType.replace(/[_-]+/g, ' ');
  }

  splitCsv(input: string): string[] {
    if (!input || typeof input !== 'string') return [];
    return input
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
  }

  /**
   * Numeric variant of splitCsv. Drops anything that isn't a finite number
   * after trimming so admins can paste a list with stray commas / spaces
   * without the field silently storing strings the backend would reject.
   */
  splitCsvNumeric(input: string): number[] {
    return this.splitCsv(input)
      .map((s) => Number(s))
      .filter((n) => Number.isFinite(n));
  }

  /**
   * Pretty-prints the current value as JSON for the json-fallback editor.
   * Returns "" rather than "undefined"/"null" so the textarea starts empty.
   */
  getJsonValue(name: string): string {
    const v = this.value()?.[name];
    if (v === undefined || v === null) return '';
    // Settings saved by older forms hold the JSON as text.
    if (typeof v === 'string') return v;
    try {
      return JSON.stringify(v, null, 2);
    } catch {
      return '';
    }
  }

  /**
   * Persists a JSON-encoded value from the json-fallback editor. Empty
   * input clears the field; invalid JSON keeps the previous value (no
   * silent partial writes).
   */
  updateFieldFromJson(name: string, raw: string): void {
    if (!raw || raw.trim() === '') {
      this.updateField(name, undefined);
      return;
    }
    try {
      const parsed = JSON.parse(raw);
      this.updateField(name, parsed);
    } catch {
      // Invalid JSON — leave the previous value alone. The textarea still
      // shows the typed text because it's bound to `getJsonValue` only on
      // initial render; ngModel keeps the local edit visible until parse
      // succeeds.
    }
  }

  /** Fields whose JSON text currently doesn't parse, with why. */
  readonly jsonErrors = signal<Record<string, string>>({});

  /**
   * Saves a JSON object typed into a json field. Text that isn't a JSON object is shown
   * as an error and leaves the saved value alone; empty text clears the field.
   */
  updateJsonObjectField(name: string, raw: string): void {
    const setError = (message: string | null) => {
      const next = { ...this.jsonErrors() };
      if (message) next[name] = message;
      else delete next[name];
      this.jsonErrors.set(next);
    };

    if (!raw || raw.trim() === '') {
      setError(null);
      this.updateField(name, undefined);
      return;
    }
    try {
      const parsed = JSON.parse(raw);
      if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
        setError('Must be a JSON object');
        return;
      }
      setError(null);
      this.updateField(name, parsed);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Not valid JSON');
    }
  }

  // ────────── object-map (string→primitive) ──────────

  /**
   * Returns rows keyed by stable IDs so the template can render input
   * bindings that survive across keystrokes. The row IDs persist for the
   * lifetime of the component instance; the underlying value is rebuilt
   * from rows on every mutation.
   */
  getMapEntries(name: string): { id: string; key: string; value: unknown }[] {
    const v = this.value()?.[name];
    const obj = isObject(v) ? (v as Record<string, unknown>) : {};
    let ids = this.mapRowIds.get(name);
    if (!ids) {
      ids = [];
      for (const k of Object.keys(obj)) {
        const id = `r${this.nextRowId++}`;
        ids.push(id);
        this.mapKeyById.set(`${name}:${id}`, k);
      }
      this.mapRowIds.set(name, ids);
    }
    return ids.map((id) => {
      const k = this.mapKeyById.get(`${name}:${id}`) ?? '';
      return { id, key: k, value: k in obj ? obj[k] : '' };
    });
  }

  addMapEntry(name: string, valueType?: string): void {
    const id = `r${this.nextRowId++}`;
    const ids = [...(this.mapRowIds.get(name) ?? []), id];
    this.mapRowIds.set(name, ids);
    this.mapKeyById.set(`${name}:${id}`, '');
    // Adding an empty row doesn't change the value yet (no key set).
    void valueType;
  }

  removeMapEntry(name: string, rowId: string): void {
    const ids = (this.mapRowIds.get(name) ?? []).filter((id) => id !== rowId);
    this.mapRowIds.set(name, ids);
    const removedKey = this.mapKeyById.get(`${name}:${rowId}`);
    this.mapKeyById.delete(`${name}:${rowId}`);
    if (removedKey) {
      this.rebuildMap(name);
    }
  }

  updateMapKey(name: string, rowId: string, newKey: string): void {
    const oldKey = this.mapKeyById.get(`${name}:${rowId}`);
    this.mapKeyById.set(`${name}:${rowId}`, newKey);
    // Carry the value over from old key to new key when the key changes.
    const current = this.currentMap(name);
    if (oldKey && oldKey !== newKey && oldKey in current) {
      current[newKey] = current[oldKey];
      delete current[oldKey];
    }
    this.updateField(name, current);
  }

  updateMapValue(name: string, rowId: string, newValue: unknown): void {
    const key = this.mapKeyById.get(`${name}:${rowId}`);
    if (!key) {
      // No key yet — stash the value via a placeholder so it lands when
      // the user fills the key in.
      this.mapKeyById.set(`${name}:${rowId}`, '');
      return;
    }
    const current = this.currentMap(name);
    current[key] = newValue;
    this.updateField(name, current);
  }

  private currentMap(name: string): Record<string, unknown> {
    const raw = this.value()?.[name];
    return isObject(raw) ? { ...(raw as Record<string, unknown>) } : {};
  }

  private currentArray(name: string): unknown[] {
    const raw = this.value()?.[name];
    return Array.isArray(raw) ? [...raw] : [];
  }

  private rebuildMap(name: string): void {
    const ids = this.mapRowIds.get(name) ?? [];
    const current = this.currentMap(name);
    const next: Record<string, unknown> = {};
    for (const id of ids) {
      const k = this.mapKeyById.get(`${name}:${id}`);
      if (k && k in current) next[k] = current[k];
    }
    this.updateField(name, next);
  }

  // ────────── object-map-of-object (string→sub-form) ──────────

  getStructuredMapEntries(name: string): { id: string; key: string; value: Record<string, unknown> }[] {
    const v = this.value()?.[name];
    const obj = isObject(v) ? (v as Record<string, unknown>) : {};
    let ids = this.mapRowIds.get(name);
    if (!ids) {
      ids = [];
      for (const k of Object.keys(obj)) {
        const id = `r${this.nextRowId++}`;
        ids.push(id);
        this.mapKeyById.set(`${name}:${id}`, k);
      }
      this.mapRowIds.set(name, ids);
    }
    return ids.map((id) => {
      const k = this.mapKeyById.get(`${name}:${id}`) ?? '';
      const inner = k in obj && isObject(obj[k]) ? (obj[k] as Record<string, unknown>) : {};
      return { id, key: k, value: inner };
    });
  }

  addStructuredMapEntry(name: string): void {
    const id = `r${this.nextRowId++}`;
    const ids = [...(this.mapRowIds.get(name) ?? []), id];
    this.mapRowIds.set(name, ids);
    this.mapKeyById.set(`${name}:${id}`, '');
  }

  removeStructuredMapEntry(name: string, rowId: string): void {
    const ids = (this.mapRowIds.get(name) ?? []).filter((id) => id !== rowId);
    this.mapRowIds.set(name, ids);
    const removedKey = this.mapKeyById.get(`${name}:${rowId}`);
    this.mapKeyById.delete(`${name}:${rowId}`);
    if (removedKey) {
      const current = this.currentMap(name);
      delete current[removedKey];
      this.updateField(name, current);
    }
  }

  updateStructuredMapKey(name: string, rowId: string, newKey: string): void {
    this.updateMapKey(name, rowId, newKey);
  }

  updateStructuredMapValue(name: string, rowId: string, newValue: Record<string, unknown>): void {
    const key = this.mapKeyById.get(`${name}:${rowId}`);
    const current = this.currentMap(name);
    if (key) {
      current[key] = newValue;
    }
    this.updateField(name, current);
  }

  // ────────── array-of-object ──────────

  getArrayEntries(name: string): { id: string; value: Record<string, unknown> }[] {
    const v = this.value()?.[name];
    const arr = Array.isArray(v) ? (v as unknown[]) : [];
    let ids = this.arrayRowIds.get(name);
    if (!ids) {
      ids = arr.map(() => `r${this.nextRowId++}`);
      this.arrayRowIds.set(name, ids);
    }
    return ids.map((id, i) => ({ id, value: isObject(arr[i]) ? (arr[i] as Record<string, unknown>) : {} }));
  }

  addArrayEntry(name: string): void {
    const id = `r${this.nextRowId++}`;
    const ids = [...(this.arrayRowIds.get(name) ?? []), id];
    this.arrayRowIds.set(name, ids);
    const current = this.currentArray(name);
    current.push({});
    this.updateField(name, current);
  }

  removeArrayEntry(name: string, rowId: string): void {
    const ids = this.arrayRowIds.get(name) ?? [];
    const idx = ids.indexOf(rowId);
    if (idx === -1) return;
    this.arrayRowIds.set(name, ids.filter((id) => id !== rowId));
    const current = this.currentArray(name);
    current.splice(idx, 1);
    this.updateField(name, current);
  }

  updateArrayEntry(name: string, rowId: string, newValue: Record<string, unknown>): void {
    const ids = this.arrayRowIds.get(name) ?? [];
    const idx = ids.indexOf(rowId);
    if (idx === -1) return;
    const current = this.currentArray(name);
    while (current.length <= idx) current.push({});
    current[idx] = newValue;
    this.updateField(name, current);
  }

  // ────────── x-enumSource async loading ──────────

  // Simple per-endpoint cache shared across instances of this component.
  // Keyed by endpoint path; each entry tracks load state + loaded values.
  private static enumCache = new Map<
    string,
    { loading: boolean; error: string | null; values: { value: string; label: string; description?: string }[] }
  >();
  private readonly _cacheVersion = signal(0);

  enumSourceOptions(source: { endpoint: string; valueField: string; labelField: string; descriptionField?: string; filters?: Record<string, string> }) {
    // Track signal so refreshes re-render
    void this._cacheVersion();
    const query = new URLSearchParams(source.filters ?? {}).toString();
    const url = query ? `${source.endpoint}${source.endpoint.includes('?') ? '&' : '?'}${query}` : source.endpoint;
    const cached = JourneyStepConfigFormComponent.enumCache.get(url);
    if (cached) return cached;

    const entry = { loading: true, error: null as string | null, values: [] as { value: string; label: string; description?: string }[] };
    JourneyStepConfigFormComponent.enumCache.set(url, entry);

    this.api
      .get<unknown>(url)
      .subscribe({
        next: (resp) => {
          const list = extractArray(resp);
          entry.values = list.map((row) => ({
            value: stringFrom(row, source.valueField),
            label: stringFrom(row, source.labelField) || stringFrom(row, source.valueField),
            description: source.descriptionField ? stringFrom(row, source.descriptionField) || undefined : undefined,
          }));
          entry.loading = false;
          entry.error = null;
          this._cacheVersion.update((v) => v + 1);
        },
        error: (err) => {
          entry.loading = false;
          entry.error = err?.message ?? 'Network error';
          this._cacheVersion.update((v) => v + 1);
        },
      });

    return entry;
  }

  hasOption(values: { value: string }[], value: string): boolean {
    return values.some((v) => v.value === value);
  }
}

// ────────── helpers (module-private) ──────────

function parseField(name: string, raw: unknown): FieldEntry {
  if (raw === null || typeof raw !== 'object') {
    return { name, label: humanize(name), required: false, kind: 'unsupported' };
  }

  const f = raw as Record<string, unknown>;
  const description = typeof f['description'] === 'string' ? (f['description'] as string) : undefined;
  const required = f['required'] === true;
  const defaultValue = f['default'];
  const xControl = typeof f['x-control'] === 'string' ? (f['x-control'] as string) : undefined;
  const enumSource = isObject(f['x-enumSource']) ? (f['x-enumSource'] as Record<string, unknown>) : undefined;
  const type = typeof f['type'] === 'string' ? (f['type'] as string) : undefined;
  const enumValues = Array.isArray(f['enum']) ? ((f['enum'] as unknown[]).filter((v) => typeof v === 'string') as string[]) : undefined;
  const items = f['items'];
  const min = typeof f['minimum'] === 'number' ? (f['minimum'] as number) : undefined;
  const max = typeof f['maximum'] === 'number' ? (f['maximum'] as number) : undefined;
  const showWhen = parseShowWhen(f['showWhen']);
  const enumLabels = parseEnumLabels(f['x-enumLabels']);
  const emptyAction = parseEmptyAction(f['x-emptyAction']);
  const localizable = f['x-localizable'] === true;
  const localizableLabels = f['x-localizableLabels'] === true;

  const base: FieldEntry = {
    name,
    label: humanize(name),
    description,
    required,
    kind: 'unsupported',
    defaultValue,
    enumValues,
    min,
    max,
    showWhen,
    enumLabels,
    emptyAction,
    localizable,
    localizableLabels,
  };

  if (xControl === 'secret-input') return { ...base, kind: 'secret' };
  if (xControl === 'textarea') return { ...base, kind: 'textarea' };
  if (xControl === 'json') return { ...base, kind: 'json' };

  switch (type) {
    case 'string': {
      // Render as a dropdown whenever the field declares an `enum` —
      // even an empty one. Empty enum is a meaningful signal ("this is a
      // picker but there are no choices yet"), e.g. when the schema's
      // x-enumSource resolved to no matching options. Falling through
      // to a text input here would let the admin type a value the
      // backend has no chance of recognising.
      if (enumValues !== undefined) return { ...base, kind: 'enum' };
      if (enumSource && typeof enumSource['endpoint'] === 'string') {
        return { ...base, kind: 'enum-remote', enumSource: parseEnumSource(enumSource) };
      }
      return { ...base, kind: 'text' };
    }
    case 'number':
    case 'integer': {
      // JSON Schema treats integer as a number constraint; the renderer
      // uses the same numeric input either way. SchemaBuilder.IntegerProperty
      // emits "integer", so without this case plugin schemas using it
      // (e.g. NumberProperty / IntegerProperty in fluent builders) would
      // fall through to 'unsupported'.
      return { ...base, kind: 'number' };
    }
    case 'boolean': {
      return { ...base, kind: 'boolean' };
    }
    case 'array': {
      if (enumSource && typeof enumSource['endpoint'] === 'string') {
        return {
          ...base,
          kind: 'enum-array',
          enumSource: parseEnumSource(enumSource),
        };
      }
      // String items → CSV-style array
      if (items === 'string') return { ...base, kind: 'string-array' };
      if (isObject(items) && items['type'] === 'string') return { ...base, kind: 'string-array' };
      // Numeric items → CSV-style array, parsed to number[]. e.g. api_call.continueOnStatus.
      if (items === 'number') return { ...base, kind: 'number-array' };
      if (isObject(items) && items['type'] === 'number') return { ...base, kind: 'number-array' };
      // Array of objects → array-of-object editor
      if (isObject(items) && items['type'] === 'object' && isObject(items['properties'])) {
        return { ...base, kind: 'array-of-object', subSchema: items['properties'] as Record<string, unknown> };
      }
      // Bare array (no items spec) — e.g. update_user.fields where the
      // structured editor is still pending. Render as a JSON textarea so
      // admins can at least edit the value, instead of forcing them to
      // hand-edit the whole policy doc.
      return { ...base, kind: 'json-fallback' };
    }
    case 'object': {
      // Nested object with declared properties → recursive sub-form
      if (isObject(f['properties'])) {
        return { ...base, kind: 'nested-object', subSchema: f['properties'] as Record<string, unknown> };
      }
      // Map (additionalProperties)
      const ap = f['additionalProperties'];
      if (isObject(ap)) {
        const apType = typeof ap['type'] === 'string' ? (ap['type'] as string) : undefined;
        if (apType === 'object' && isObject(ap['properties'])) {
          return { ...base, kind: 'object-map-of-object', subSchema: ap['properties'] as Record<string, unknown> };
        }
        if (apType === 'string' || apType === 'number' || apType === 'boolean') {
          return { ...base, kind: 'object-map', mapValueType: apType };
        }
      }
      // Free-form object with no declared shape — treat as a string→string map.
      // This covers things like outputMapping (path → claim) where the
      // backend just declared `type: "object"` without spelling out the
      // value type explicitly.
      return { ...base, kind: 'object-map', mapValueType: 'string' };
    }
    default:
      return base;
  }
}

function parseShowWhen(raw: unknown): { field: string; value: unknown } | undefined {
  if (!isObject(raw)) return undefined;
  const field = raw['field'];
  if (typeof field !== 'string' || !field) return undefined;
  return { field, value: raw['value'] };
}

function parseEnumLabels(raw: unknown): Record<string, string> | undefined {
  if (!isObject(raw)) return undefined;
  const out: Record<string, string> = {};
  for (const [key, value] of Object.entries(raw)) {
    if (typeof value === 'string') {
      out[key] = value;
    } else if (value !== null && value !== undefined) {
      out[key] = String(value);
    }
  }
  return Object.keys(out).length > 0 ? out : undefined;
}

function parseEmptyAction(raw: unknown): { type: 'addStep'; stepType: string } | undefined {
  if (!isObject(raw)) return undefined;
  if (raw['type'] !== 'addStep') return undefined;
  const stepType = raw['stepType'];
  if (typeof stepType !== 'string' || !stepType) return undefined;
  return { type: 'addStep', stepType };
}

function isObject(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v);
}

function humanize(name: string): string {
  // camelCase / snake_case → Title Case Words
  return name
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/[_-]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .replace(/^./, (c) => c.toUpperCase());
}

function parseEnumSource(src: Record<string, unknown>): FieldEntry['enumSource'] & object {
  const filters = isObject(src['filters'])
    ? Object.fromEntries(
        Object.entries(src['filters'] as Record<string, unknown>)
          .filter(([, v]) => v !== null && v !== undefined)
          .map(([k, v]) => [k, String(v)]),
      )
    : undefined;
  return {
    endpoint: src['endpoint'] as string,
    valueField: typeof src['valueField'] === 'string' ? (src['valueField'] as string) : 'value',
    labelField: typeof src['labelField'] === 'string' ? (src['labelField'] as string) : 'label',
    descriptionField: typeof src['descriptionField'] === 'string' ? (src['descriptionField'] as string) : undefined,
    filters,
  };
}

function extractArray(resp: unknown): unknown[] {
  if (Array.isArray(resp)) return resp;
  if (isObject(resp)) {
    if (Array.isArray((resp as { items?: unknown[] }).items)) return (resp as { items: unknown[] }).items;
    if (Array.isArray((resp as { data?: unknown[] }).data)) return (resp as { data: unknown[] }).data;
  }
  return [];
}

function stringFrom(row: unknown, field: string): string {
  if (!isObject(row)) return '';
  const v = row[field];
  return v === null || v === undefined ? '' : String(v);
}
