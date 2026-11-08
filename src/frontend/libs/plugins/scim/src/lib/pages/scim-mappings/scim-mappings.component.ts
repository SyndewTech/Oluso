import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { IconComponent } from '@oluso/ui-core';
import {
  ScimService,
  internalPropertyOptions,
  transformationOptions,
} from '../../services/scim.service';
import {
  ScimClient,
  ScimAttributeMapping,
  SaveScimAttributeMappingRequest,
  ScimAttributeSuggestion,
  ScimMappingOptions,
  ScimMappingPreview,
} from '../../models/scim.model';

interface MappingFormData {
  scimAttribute: string;
  internalProperty: string;
  source: 'attribute' | 'expression';
  expression: string;
  condition: string;
  isRequired: boolean;
  defaultValue: string;
  transformation: string;
  priority: number;
  isEnabled: boolean;
}

const initialFormData: MappingFormData = {
  scimAttribute: '',
  internalProperty: '',
  source: 'attribute',
  expression: '',
  condition: '',
  isRequired: false,
  defaultValue: '',
  transformation: 'none',
  priority: 0,
  isEnabled: true,
};

const SAMPLE_RESOURCE = {
  schemas: [
    'urn:ietf:params:scim:schemas:core:2.0:User',
    'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User',
  ],
  externalId: '3f2c1a0e-0000-4000-8000-000000000001',
  userName: 'jane.doe@example.org',
  name: { givenName: 'Jane', familyName: 'Doe' },
  displayName: 'Jane Doe',
  title: 'Finance Manager',
  userType: 'Employee',
  active: true,
  emails: [{ value: 'jane.doe@example.org', type: 'work', primary: true }],
  phoneNumbers: [{ value: '+44 20 1234 5678', type: 'work' }],
  locale: 'en-GB',
  timezone: 'Europe/London',
  'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User': {
    department: 'Finance',
    employeeNumber: 'E-1042',
    manager: { value: '3f2c1a0e-0000-4000-8000-000000000099' },
  },
};

@Component({
  selector: 'lib-scim-mappings',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule, IconComponent],
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div class="flex items-center space-x-4">
          <a
            [routerLink]="['/scim/clients', clientId]"
            class="text-gray-500 hover:text-gray-700 transition-colors"
          >
            <ui-icon name="arrow-left" class="h-5 w-5" />
          </a>
          <div>
            <h1 class="text-2xl font-semibold text-gray-900">Attribute Mappings</h1>
            @if (client()) {
              <p class="text-sm text-gray-500">
                How attributes sent by
                <span class="font-medium text-gray-700">{{ client()!.name }}</span>
                are written onto users. userName, email and active always apply; extension
                attributes such as department can be stored as custom properties.
              </p>
            }
          </div>
        </div>
        <div class="flex items-center space-x-3">
          <button
            (click)="showDefaultsModal.set(true)"
            class="inline-flex items-center px-3 py-2 border border-gray-300 rounded-lg text-sm font-medium text-gray-700 bg-white hover:bg-gray-50 transition-colors"
          >
            <ui-icon name="sparkles" class="h-4 w-4 mr-2" />
            Apply Preset
          </button>
          <button
            (click)="openCreateForm()"
            class="inline-flex items-center px-4 py-2 bg-primary-600 hover:bg-primary-700 text-white text-sm font-medium rounded-lg transition-colors"
          >
            <ui-icon name="plus" class="h-4 w-4 mr-2" />
            Add Mapping
          </button>
        </div>
      </div>

      @if (error()) {
        <div class="bg-red-50 border border-red-200 rounded-lg p-4 text-red-700">
          {{ error() }}
        </div>
      }

      <!-- Mappings Table -->
      <div class="bg-white rounded-lg border border-gray-200 overflow-x-auto shadow-sm">
        @if (loading()) {
          <div class="flex items-center justify-center h-64">
            <svg
              class="h-8 w-8 animate-spin text-gray-400"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
            >
              <path
                stroke-linecap="round"
                stroke-linejoin="round"
                stroke-width="2"
                d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15"
              />
            </svg>
          </div>
        } @else if (mappings().length === 0) {
          <div class="p-6">
            <div class="text-center mb-6">
              <ui-icon name="arrows-right-left" class="h-12 w-12 text-gray-400 mx-auto mb-4" />
              <h3 class="text-lg font-medium text-gray-900 mb-2">Using the standard mappings</h3>
              <p class="text-gray-500">
                No mappings are configured, so the rows below apply. Add a mapping or apply a preset
                to take over.
              </p>
            </div>
            @if (options(); as o) {
              <table class="min-w-full divide-y divide-gray-200 text-sm">
                <tbody class="divide-y divide-gray-100">
                  @for (d of o.defaults; track d.scimAttribute + d.internalProperty) {
                    <tr class="text-gray-600">
                      <td class="px-4 py-2 font-mono text-xs">{{ d.scimAttribute }}</td>
                      <td class="px-4 py-2 font-mono text-xs">→ {{ d.internalProperty }}</td>
                      <td class="px-4 py-2 font-mono text-xs text-purple-700">
                        {{ d.expression || '' }}
                      </td>
                      <td class="px-4 py-2 text-xs">{{ d.isRequired ? 'required' : '' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </div>
        } @else {
          <table class="min-w-full divide-y divide-gray-200">
            <thead class="bg-gray-50">
              <tr>
                <th
                  class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Status
                </th>
                <th
                  class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  SCIM Attribute
                </th>
                <th
                  class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Internal Property
                </th>
                <th
                  class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Expression / Condition
                </th>
                <th
                  class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Transform
                </th>
                <th
                  class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Required
                </th>
                <th
                  class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Priority
                </th>
                <th
                  class="px-4 py-3 text-right text-xs font-medium text-gray-500 uppercase tracking-wider"
                >
                  Actions
                </th>
              </tr>
            </thead>
            <tbody class="bg-white divide-y divide-gray-200">
              @for (mapping of mappings(); track mapping.id) {
                <tr [class.opacity-50]="!mapping.isEnabled" [class.bg-gray-50]="!mapping.isEnabled">
                  <td class="px-4 py-4 whitespace-nowrap">
                    <button
                      (click)="handleToggleEnabled(mapping)"
                      class="focus:outline-none"
                      [title]="mapping.isEnabled ? 'Disable mapping' : 'Enable mapping'"
                    >
                      @if (mapping.isEnabled) {
                        <ui-icon name="check-circle" class="h-5 w-5 text-green-500" />
                      } @else {
                        <ui-icon name="x-circle" class="h-5 w-5 text-gray-400" />
                      }
                    </button>
                  </td>
                  <td class="px-4 py-4">
                    <code class="text-sm text-blue-700 bg-blue-50 px-2 py-1 rounded break-all">
                      {{ mapping.scimAttribute }}
                    </code>
                  </td>
                  <td class="px-4 py-4">
                    <code class="text-sm text-green-700 bg-green-50 px-2 py-1 rounded">
                      {{ mapping.internalProperty }}
                    </code>
                  </td>
                  <td class="px-4 py-4 text-xs font-mono">
                    @if (mapping.expression) {
                      <div class="text-purple-700 break-all">{{ mapping.expression }}</div>
                    }
                    @if (mapping.condition) {
                      <div class="text-gray-500 break-all">if {{ mapping.condition }}</div>
                    }
                    @if (!mapping.expression && !mapping.condition) {
                      <span class="text-gray-400">-</span>
                    }
                  </td>
                  <td class="px-4 py-4 whitespace-nowrap text-sm text-gray-500">
                    {{ mapping.transformation || '-' }}
                    @if (mapping.defaultValue) {
                      <div class="text-xs text-gray-400">default: {{ mapping.defaultValue }}</div>
                    }
                  </td>
                  <td class="px-4 py-4 whitespace-nowrap">
                    @if (mapping.isRequired) {
                      <span
                        class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-orange-100 text-orange-700"
                      >
                        Required
                      </span>
                    } @else {
                      <span class="text-gray-400 text-sm">Optional</span>
                    }
                  </td>
                  <td class="px-4 py-4 whitespace-nowrap text-sm text-gray-500">
                    {{ mapping.priority }}
                  </td>
                  <td class="px-4 py-4 whitespace-nowrap text-right text-sm font-medium">
                    <button
                      (click)="handleEdit(mapping)"
                      class="text-blue-600 hover:text-blue-800 mr-3"
                    >
                      <ui-icon name="pencil" class="h-4 w-4" />
                    </button>
                    <button
                      (click)="handleDelete(mapping.id)"
                      class="text-red-600 hover:text-red-800"
                    >
                      <ui-icon name="trash" class="h-4 w-4" />
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </div>

      <!-- Preview -->
      <div class="bg-white rounded-lg border border-gray-200 shadow-sm p-6">
        <div class="flex items-center justify-between mb-2">
          <h2 class="text-lg font-medium text-gray-900">Try it with a sample request</h2>
          <div class="flex items-center gap-2">
            <select
              [(ngModel)]="previewMode"
              class="border border-gray-300 rounded-md px-2 py-1 text-sm"
            >
              <option value="create">Create (POST)</option>
              <option value="replace">Replace (PUT)</option>
              <option value="patch">Patch result</option>
            </select>
            <button
              (click)="runPreview()"
              [disabled]="previewing()"
              class="inline-flex items-center px-3 py-1.5 bg-gray-100 hover:bg-gray-200 rounded-md text-sm disabled:opacity-50"
            >
              <ui-icon name="play-circle" class="h-4 w-4 mr-1" />
              {{ previewing() ? 'Running...' : 'Preview' }}
            </button>
          </div>
        </div>
        <p class="text-xs text-gray-500 mb-3">
          Paste a SCIM User as the identity provider sends it. Nothing is stored. Expressions can
          read
          <code>scim.*</code>, <code>enterprise.*</code>, <code>attr "path"</code> and
          <code>client.name</code>.
        </p>
        <div class="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <textarea
            [(ngModel)]="previewJson"
            rows="14"
            spellcheck="false"
            class="w-full border border-gray-300 rounded-md px-3 py-2 font-mono text-xs"
          ></textarea>
          <div>
            @if (previewError()) {
              <div class="bg-red-50 text-red-700 p-3 rounded-md text-xs">{{ previewError() }}</div>
            } @else if (preview(); as pv) {
              @if (!pv.success) {
                <div class="bg-red-50 text-red-700 p-3 rounded-md text-xs">{{ pv.error }}</div>
              } @else {
                @if (pv.usingDefaults) {
                  <p class="text-xs text-gray-500 mb-2">
                    Using the standard mappings (none configured).
                  </p>
                }
                <table class="min-w-full text-sm">
                  <tbody class="divide-y divide-gray-100">
                    @for (f of previewFields(); track f.key) {
                      <tr>
                        <td class="py-1.5 pr-4 font-mono text-xs text-green-700">{{ f.key }}</td>
                        <td
                          class="py-1.5 font-mono text-xs"
                          [class.text-gray-400]="f.value === null"
                        >
                          {{ f.value === null ? '(cleared)' : f.value }}
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              }
            } @else {
              <p class="text-sm text-gray-400">Run a preview to see what would be written.</p>
            }
          </div>
        </div>
      </div>

      <!-- Mapping Form Modal -->
      @if (showForm()) {
        <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div
            class="bg-white rounded-lg shadow-xl p-6 max-w-2xl w-full mx-4 max-h-[90vh] overflow-y-auto"
          >
            <h2 class="text-xl font-semibold text-gray-900 mb-6">
              {{ editingMapping() ? 'Edit Mapping' : 'Add Mapping' }}
            </h2>

            <form (ngSubmit)="handleSubmit()" class="space-y-6">
              <div class="grid grid-cols-2 gap-6">
                <!-- SCIM Attribute -->
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-2">SCIM Attribute</label>
                  <input
                    type="text"
                    list="scim-suggestions"
                    [(ngModel)]="formData.scimAttribute"
                    name="scimAttribute"
                    class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 font-mono text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    placeholder='e.g., userName, emails[type eq "work"].value'
                    required
                  />
                  <datalist id="scim-suggestions">
                    @for (s of scimSuggestions; track s.attribute) {
                      <option [value]="s.attribute">{{ s.description }}</option>
                    }
                  </datalist>
                  <p class="mt-1 text-xs text-gray-500">
                    The attribute path in the incoming request. Also names the mapping when an
                    expression is used.
                  </p>
                </div>

                <!-- Internal Property -->
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-2"
                    >Internal Property</label
                  >
                  <input
                    type="text"
                    list="internal-property-suggestions"
                    [(ngModel)]="formData.internalProperty"
                    name="internalProperty"
                    class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 font-mono text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    placeholder="Email, FirstName, custom:department"
                    required
                  />
                  <datalist id="internal-property-suggestions">
                    @for (p of internalProperties(); track p.name) {
                      <option [value]="p.name">{{ p.displayName }}</option>
                    }
                  </datalist>
                  <p class="mt-1 text-xs text-gray-500">
                    The user property to write. <code>custom:&lt;key&gt;</code> stores into custom
                    properties.
                  </p>
                </div>

                <!-- Value source -->
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-2"
                    >Value comes from</label
                  >
                  <select
                    [(ngModel)]="formData.source"
                    name="source"
                    class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  >
                    <option value="attribute">The SCIM attribute</option>
                    <option value="expression">An expression</option>
                  </select>
                </div>

                <!-- Transformation -->
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-2">Transformation</label>
                  <select
                    [(ngModel)]="formData.transformation"
                    name="transformation"
                    class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                  >
                    @for (t of transformations(); track t.name) {
                      <option [value]="t.name">{{ t.description }}</option>
                    }
                  </select>
                </div>

                @if (formData.source === 'expression') {
                  <div class="col-span-2">
                    <label class="block text-sm font-medium text-gray-700 mb-2"
                      >Expression (Scriban)</label
                    >
                    <input
                      type="text"
                      [(ngModel)]="formData.expression"
                      name="expression"
                      class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 font-mono text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                      placeholder="{{ '{{' }} scim.name.givenName {{ '}}' }} {{
                        '{{'
                      }} scim.name.familyName {{ '}}' }}"
                    />
                    <p class="mt-1 text-xs text-gray-500">
                      Model: <code>scim</code> (the resource), <code>enterprise</code> (the
                      enterprise extension), <code>attr "path"</code>, <code>client.name</code>.
                    </p>
                  </div>
                }

                <!-- Condition -->
                <div class="col-span-2">
                  <label class="block text-sm font-medium text-gray-700 mb-2"
                    >Condition (optional)</label
                  >
                  <input
                    type="text"
                    [(ngModel)]="formData.condition"
                    name="condition"
                    class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 font-mono text-sm focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    placeholder='scim.userType == "Employee"'
                  />
                  <p class="mt-1 text-xs text-gray-500">
                    Scriban boolean expression. The mapping is skipped when it is false.
                  </p>
                </div>

                <!-- Default Value -->
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-2">Default Value</label>
                  <input
                    type="text"
                    [(ngModel)]="formData.defaultValue"
                    name="defaultValue"
                    class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    placeholder="Used when nothing resolves"
                  />
                </div>

                <!-- Priority -->
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-2">Priority</label>
                  <input
                    type="number"
                    [(ngModel)]="formData.priority"
                    name="priority"
                    class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
                    min="-100"
                    max="100"
                  />
                  <p class="mt-1 text-xs text-gray-500">
                    When several mappings write the same property, the highest priority with a value
                    wins.
                  </p>
                </div>
              </div>

              <!-- Checkboxes -->
              <div class="flex items-center space-x-6">
                <label class="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.isRequired"
                    name="isRequired"
                    class="h-4 w-4 border-gray-300 rounded text-primary-600 focus:ring-primary-500"
                  />
                  <span class="text-sm text-gray-700">Required for user creation</span>
                </label>
                <label class="flex items-center space-x-2">
                  <input
                    type="checkbox"
                    [(ngModel)]="formData.isEnabled"
                    name="isEnabled"
                    class="h-4 w-4 border-gray-300 rounded text-primary-600 focus:ring-primary-500"
                  />
                  <span class="text-sm text-gray-700">Enabled</span>
                </label>
              </div>

              @if (formError()) {
                <div class="bg-red-50 text-red-700 p-3 rounded-md text-sm">{{ formError() }}</div>
              }

              <!-- Form Actions -->
              <div class="flex justify-end space-x-3 pt-4 border-t border-gray-200">
                <button
                  type="button"
                  (click)="closeForm()"
                  class="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-lg transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  [disabled]="saving()"
                  class="px-4 py-2 text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 rounded-lg disabled:opacity-50 transition-colors"
                >
                  {{
                    saving() ? 'Saving...' : editingMapping() ? 'Update Mapping' : 'Create Mapping'
                  }}
                </button>
              </div>
            </form>
          </div>
        </div>
      }

      <!-- Apply Preset Modal -->
      @if (showDefaultsModal()) {
        <div class="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div class="bg-white rounded-lg shadow-xl p-6 max-w-md w-full mx-4">
            <h2 class="text-xl font-semibold text-gray-900 mb-4">Apply Preset Mappings</h2>
            <p class="text-gray-600 mb-6">
              Adds the preset's rows without removing existing ones. Attributes that already have a
              mapping are skipped.
            </p>

            <div class="mb-6">
              <label class="block text-sm font-medium text-gray-700 mb-2">Identity provider</label>
              <select
                [(ngModel)]="selectedProvider"
                class="w-full px-3 py-2 bg-white border border-gray-300 rounded-lg text-gray-900 focus:ring-2 focus:ring-primary-500 focus:border-transparent"
              >
                <option value="standard">Standard SCIM 2.0</option>
                <option value="azure">Microsoft Entra ID</option>
                <option value="okta">Okta</option>
                <option value="google">Google Workspace</option>
              </select>
            </div>

            <div class="flex justify-end space-x-3">
              <button
                (click)="showDefaultsModal.set(false)"
                class="px-4 py-2 text-sm font-medium text-gray-700 bg-gray-100 hover:bg-gray-200 rounded-lg transition-colors"
              >
                Cancel
              </button>
              <button
                (click)="handleApplyDefaults()"
                [disabled]="saving()"
                class="px-4 py-2 text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 rounded-lg disabled:opacity-50 transition-colors"
              >
                {{ saving() ? 'Applying...' : 'Apply' }}
              </button>
            </div>
          </div>
        </div>
      }
    </div>
  `,
})
export class ScimMappingsComponent implements OnInit {
  private readonly scimService = inject(ScimService);
  private readonly route = inject(ActivatedRoute);

  client = signal<ScimClient | null>(null);
  mappings = signal<ScimAttributeMapping[]>([]);
  options = signal<ScimMappingOptions | null>(null);
  loading = signal(true);
  error = signal<string | null>(null);
  formError = signal<string | null>(null);
  showForm = signal(false);
  editingMapping = signal<ScimAttributeMapping | null>(null);
  saving = signal(false);
  showDefaultsModal = signal(false);
  selectedProvider = 'standard';
  clientId = '';

  previewJson = JSON.stringify(SAMPLE_RESOURCE, null, 2);
  previewMode: 'create' | 'replace' | 'patch' = 'create';
  previewing = signal(false);
  preview = signal<ScimMappingPreview | null>(null);
  previewError = signal<string | null>(null);
  previewFields = signal<{ key: string; value: string | null }[]>([]);

  formData: MappingFormData = { ...initialFormData };

  scimSuggestions: ScimAttributeSuggestion[] = [];
  /** The server's mapping-options decide which properties and transformations are offered. */
  internalProperties = computed(() =>
    internalPropertyOptions(this.options()?.internalProperties ?? []),
  );
  transformations = computed(() => transformationOptions(this.options()?.transformations ?? []));

  ngOnInit(): void {
    this.clientId = this.route.snapshot.paramMap.get('clientId') || '';
    this.scimSuggestions = this.scimService.getScimAttributeSuggestions();

    this.scimService.getMappingOptions().subscribe({
      next: (o) => this.options.set(o),
      error: (err) => console.error('Failed to load mapping options:', err),
    });

    if (this.clientId) {
      this.loadData();
    }
  }

  loadData(): void {
    this.loading.set(true);
    this.error.set(null);

    forkJoin({
      client: this.scimService.getClient(this.clientId),
      mappings: this.scimService.getMappings(this.clientId),
    }).subscribe({
      next: ({ client, mappings }) => {
        this.client.set(client);
        this.mappings.set(mappings);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(this.messageOf(err, 'Failed to load data'));
        this.loading.set(false);
      },
    });
  }

  openCreateForm(): void {
    this.editingMapping.set(null);
    this.formData = { ...initialFormData };
    this.formError.set(null);
    this.showForm.set(true);
  }

  closeForm(): void {
    this.showForm.set(false);
    this.editingMapping.set(null);
    this.formData = { ...initialFormData };
  }

  handleEdit(mapping: ScimAttributeMapping): void {
    this.editingMapping.set(mapping);
    this.formError.set(null);
    this.formData = {
      scimAttribute: mapping.scimAttribute,
      internalProperty: mapping.internalProperty,
      source: mapping.expression ? 'expression' : 'attribute',
      expression: mapping.expression || '',
      condition: mapping.condition || '',
      isRequired: mapping.isRequired,
      defaultValue: mapping.defaultValue || '',
      transformation: mapping.transformation || 'none',
      priority: mapping.priority,
      isEnabled: mapping.isEnabled,
    };
    this.showForm.set(true);
  }

  handleSubmit(): void {
    this.saving.set(true);
    this.formError.set(null);

    const request = this.toRequest(this.formData);
    const editing = this.editingMapping();
    const operation = editing
      ? this.scimService.updateMapping(this.clientId, editing.id, request)
      : this.scimService.createMapping(this.clientId, request);

    operation.subscribe({
      next: () => {
        this.saving.set(false);
        this.closeForm();
        this.loadData();
      },
      error: (err) => {
        this.formError.set(this.messageOf(err, 'Failed to save mapping'));
        this.saving.set(false);
      },
    });
  }

  handleDelete(mappingId: string): void {
    if (!confirm('Are you sure you want to delete this mapping?')) return;

    this.error.set(null);
    this.scimService.deleteMapping(this.clientId, mappingId).subscribe({
      next: () => this.loadData(),
      error: (err) => {
        this.error.set(this.messageOf(err, 'Failed to delete mapping'));
      },
    });
  }

  handleToggleEnabled(mapping: ScimAttributeMapping): void {
    const request: SaveScimAttributeMappingRequest = {
      scimAttribute: mapping.scimAttribute,
      internalProperty: mapping.internalProperty,
      expression: mapping.expression,
      condition: mapping.condition,
      isRequired: mapping.isRequired,
      defaultValue: mapping.defaultValue,
      transformation: mapping.transformation,
      priority: mapping.priority,
      isEnabled: !mapping.isEnabled,
    };
    this.scimService.updateMapping(this.clientId, mapping.id, request).subscribe({
      next: () => this.loadData(),
      error: (err) => {
        this.error.set(this.messageOf(err, 'Failed to update mapping'));
      },
    });
  }

  handleApplyDefaults(): void {
    this.saving.set(true);
    this.error.set(null);

    this.scimService.applyDefaultMappings(this.clientId, this.selectedProvider).subscribe({
      next: () => {
        this.showDefaultsModal.set(false);
        this.loadData();
        this.saving.set(false);
      },
      error: (err) => {
        this.error.set(this.messageOf(err, 'Failed to apply preset mappings'));
        this.saving.set(false);
      },
    });
  }

  runPreview(): void {
    this.previewError.set(null);
    this.preview.set(null);
    let resource: Record<string, unknown>;
    try {
      resource = JSON.parse(this.previewJson);
    } catch (e) {
      this.previewError.set(`Sample is not valid JSON: ${(e as Error).message}`);
      return;
    }

    this.previewing.set(true);
    this.scimService.previewMappings(this.clientId, resource, this.previewMode).subscribe({
      next: (pv) => {
        this.preview.set(pv);
        this.previewFields.set(
          Object.entries(pv.fields ?? {})
            .map(([key, value]) => ({ key, value }))
            .sort((a, b) => a.key.localeCompare(b.key)),
        );
        this.previewing.set(false);
      },
      error: (err) => {
        this.previewError.set(this.messageOf(err, 'Preview failed'));
        this.previewing.set(false);
      },
    });
  }

  private toRequest(f: MappingFormData): SaveScimAttributeMappingRequest {
    return {
      scimAttribute: f.scimAttribute.trim(),
      internalProperty: f.internalProperty.trim(),
      expression: f.source === 'expression' ? f.expression || undefined : undefined,
      condition: f.condition || undefined,
      isRequired: f.isRequired,
      defaultValue: f.defaultValue || undefined,
      transformation: f.transformation === 'none' ? undefined : f.transformation,
      priority: Number(f.priority) || 0,
      isEnabled: f.isEnabled,
    };
  }

  private messageOf(err: unknown, fallback: string): string {
    const e = err as { error?: unknown; message?: string };
    if (typeof e?.error === 'string') return e.error;
    if (e instanceof Error && e.message) return e.message;
    return fallback;
  }
}
