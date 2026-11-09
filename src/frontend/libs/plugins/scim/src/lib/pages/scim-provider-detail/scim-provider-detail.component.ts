import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '@syndew/oluso-ui-core';
import { RoleService, TeamService, RoleDto, TeamSummary } from '@syndew/oluso-admin-ui';
import { ScimService } from '../../services/scim.service';
import {
  ScimProvider,
  ScimProviderOptions,
  ScimProviderFlavor,
  ScimProviderAttributeMapping,
  ScimProviderTestResult,
  ScimProviderQueueResponse,
  ScimProviderLog,
  ScimProviderPreview,
  ScimProviderAuthType,
  ScimProviderUserNameSource,
  ScimProviderDeprovisionMode,
  ScimProviderGroupSource,
  ScimQueueStatus,
} from '../../models/scim-provider.model';
import { ScimClient } from '../../models/scim.model';

interface MappingForm {
  id: string | null;
  scimAttribute: string;
  source: 'property' | 'expression';
  internalProperty: string;
  expression: string;
  condition: string;
  transformation: string;
  defaultValue: string;
  isEnabled: boolean;
}

@Component({
  selector: 'lib-scim-provider-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  template: `
    <div class="space-y-6">
      <button (click)="goBack()" class="inline-flex items-center text-gray-600 hover:text-gray-900 transition-colors">
        <ui-icon name="arrow-left" class="h-4 w-4 mr-2" />
        Back to SCIM Providers
      </button>

      @if (loading()) {
        <div class="p-6 text-center text-gray-500">Loading...</div>
      } @else if (!provider()) {
        <div class="p-6 text-center text-gray-500">Provider not found</div>
      } @else {
        <div class="flex items-start justify-between">
          <div>
            <h1 class="text-2xl font-semibold text-gray-900">{{ provider()!.name }}</h1>
            <p class="text-sm text-gray-500 mt-1">
              {{ flavorName(provider()!.flavor) }} ·
              <code class="text-xs">{{ provider()!.baseUrl }}</code>
            </p>
          </div>
          <span
            class="inline-flex px-2 py-1 text-xs rounded-full"
            [class.bg-green-100]="provider()!.isEnabled && provider()!.hasSecret"
            [class.text-green-800]="provider()!.isEnabled && provider()!.hasSecret"
            [class.bg-yellow-100]="provider()!.isEnabled && !provider()!.hasSecret"
            [class.text-yellow-800]="provider()!.isEnabled && !provider()!.hasSecret"
            [class.bg-gray-100]="!provider()!.isEnabled"
            [class.text-gray-800]="!provider()!.isEnabled"
          >
            {{ !provider()!.isEnabled ? 'Disabled' : (provider()!.hasSecret ? 'Enabled' : 'No secret set') }}
          </span>
        </div>

        <div class="grid grid-cols-1 lg:grid-cols-3 gap-6">
          <!-- Left column -->
          <div class="lg:col-span-2 space-y-6">
            <!-- Settings -->
            <div class="bg-white shadow rounded-lg p-6">
              <h2 class="text-lg font-medium mb-4">Settings</h2>

              <div class="grid grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Name</label>
                  <input type="text" [(ngModel)]="form.name" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent" />
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Description</label>
                  <input type="text" [(ngModel)]="form.description" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent" />
                </div>

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Type</label>
                  <select [(ngModel)]="form.flavor" (ngModelChange)="onFlavorChange($event)" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent">
                    @for (f of flavors(); track f.key) {
                      <option [value]="f.key">{{ f.displayName }}</option>
                    }
                  </select>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">SCIM base URL</label>
                  <input type="url" [(ngModel)]="form.baseUrl" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent font-mono text-sm" />
                </div>

                @if (selectedFlavor()?.notes) {
                  <div class="col-span-2 bg-blue-50 border border-blue-200 text-blue-800 text-xs rounded-md p-3">
                    {{ selectedFlavor()!.notes }}
                  </div>
                }

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Authentication</label>
                  <select [(ngModel)]="form.authType" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent">
                    <option value="bearer">Bearer token / API key</option>
                    <option value="oauth_client_credentials">OAuth 2.0 client credentials</option>
                  </select>
                </div>
                @if (form.authType === 'oauth_client_credentials') {
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Token endpoint</label>
                    <input type="url" [(ngModel)]="form.oauthTokenEndpoint" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent font-mono text-sm" />
                  </div>
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Client ID</label>
                    <input type="text" [(ngModel)]="form.oauthClientId" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent" />
                  </div>
                  <div>
                    <label class="block text-sm font-medium text-gray-700 mb-1">Scope (optional)</label>
                    <input type="text" [(ngModel)]="form.oauthScope" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent" />
                  </div>
                } @else {
                  <div></div>
                }

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">userName comes from</label>
                  <select
                    [(ngModel)]="form.userNameSource"
                    [disabled]="!!selectedFlavor()?.requiresEmailUserName && form.userNameSource !== 'expression'"
                    class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent disabled:bg-gray-50"
                  >
                    <option value="userName">Username</option>
                    <option value="email">Email address</option>
                    <option value="expression">Expression</option>
                  </select>
                  @if (selectedFlavor()?.requiresEmailUserName) {
                    <p class="text-xs text-gray-500 mt-1">
                      This type needs an email address as userName. Choose Expression to derive one.
                    </p>
                  }
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">When a user is removed</label>
                  <select [(ngModel)]="form.deprovisionMode" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent">
                    <option value="deactivate">Deactivate the remote account</option>
                    <option value="delete">Delete the remote account</option>
                  </select>
                </div>

                @if (form.userNameSource === 'expression') {
                  <div class="col-span-2">
                    <label class="block text-sm font-medium text-gray-700 mb-1">userName expression</label>
                    <input
                      type="text"
                      [(ngModel)]="form.userNameExpression"
                      class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent font-mono text-sm"
                      placeholder="{{ '{{' }} user.first_name | string.downcase {{ '}}' }}.{{ '{{' }} user.last_name | string.downcase {{ '}}' }}@mail.example.org"
                    />
                    <p class="text-xs text-gray-500 mt-1">
                      Scriban template. Available: user.first_name, user.last_name, user.user_name, user.email, user.display_name, roles, teams, team_codes, custom.
                    </p>
                  </div>
                }

                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Full reconcile every (minutes)</label>
                  <input type="number" min="0" [(ngModel)]="form.reconcileIntervalMinutes" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent" />
                  <p class="text-xs text-gray-500 mt-1">0 turns scheduled reconciles off; you can still run one manually.</p>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Same system as inbound client</label>
                  <select [(ngModel)]="form.linkedScimClientId" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent">
                    <option value="">None</option>
                    @for (c of inboundClients(); track c.id) {
                      <option [value]="c.id">{{ c.name }}</option>
                    }
                  </select>
                  <p class="text-xs text-gray-500 mt-1">When this system also provisions into the tenant, the changes it makes are not pushed back to it.</p>
                </div>
              </div>

              <h3 class="text-sm font-medium text-gray-700 mt-6 mb-3">What to push</h3>
              <div class="grid grid-cols-2 gap-3">
                <label class="flex items-center">
                  <input type="checkbox" [(ngModel)]="form.isEnabled" class="rounded border-gray-300 text-primary-600 mr-2" />
                  <span class="text-sm">Enabled</span>
                </label>
                <div></div>
                <label class="flex items-center">
                  <input type="checkbox" [(ngModel)]="form.syncUsers" class="rounded border-gray-300 text-primary-600 mr-2" />
                  <span class="text-sm">Users</span>
                </label>
                <label class="flex items-center" [class.opacity-50]="groupsUnsupported()">
                  <input type="checkbox" [(ngModel)]="form.syncGroups" [disabled]="groupsUnsupported()" class="rounded border-gray-300 text-primary-600 mr-2" />
                  <span class="text-sm">Groups</span>
                </label>
                @if (form.syncGroups) {
                  <div class="col-span-2">
                    <label class="block text-sm font-medium text-gray-700 mb-1">Groups are built from</label>
                    <select [(ngModel)]="form.groupSource" class="w-full border border-gray-300 rounded-md px-3 py-2 focus:ring-2 focus:ring-primary-500 focus:border-transparent">
                      <option value="roles">Roles</option>
                      <option value="teams">Teams (members include sub-teams)</option>
                      <option value="both">Roles and teams</option>
                    </select>
                  </div>
                }
              </div>

              <h3 class="text-sm font-medium text-gray-700 mt-6 mb-1">Scope</h3>
              <p class="text-xs text-gray-500 mb-3">
                Leave both empty to provision everyone in the tenant. Otherwise a user is provisioned when they hold one of
                the selected roles or belong to one of the selected teams, and only those roles and teams are pushed as groups.
              </p>
              <div class="grid grid-cols-2 gap-4">
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Roles</label>
                  <div class="border border-gray-200 rounded-md max-h-48 overflow-y-auto p-2 space-y-1">
                    @if (roles().length === 0) {
                      <p class="text-xs text-gray-400 px-1">No roles</p>
                    }
                    @for (r of roles(); track r.id) {
                      <label class="flex items-center text-sm">
                        <input type="checkbox" [checked]="scopeRoleIds().has(r.id)" (change)="toggleScopeRole(r.id)" class="rounded border-gray-300 text-primary-600 mr-2" />
                        <span>{{ r.displayName || r.name }}</span>
                      </label>
                    }
                  </div>
                </div>
                <div>
                  <label class="block text-sm font-medium text-gray-700 mb-1">Teams</label>
                  <div class="border border-gray-200 rounded-md max-h-48 overflow-y-auto p-2 space-y-1">
                    @if (teams().length === 0) {
                      <p class="text-xs text-gray-400 px-1">No teams</p>
                    }
                    @for (t of teams(); track t.id) {
                      <label class="flex items-center text-sm">
                        <input type="checkbox" [checked]="scopeTeamIds().has(t.id)" (change)="toggleScopeTeam(t.id)" class="rounded border-gray-300 text-primary-600 mr-2" />
                        <span>{{ t.name }} <span class="text-gray-400 text-xs">{{ t.code }}</span></span>
                      </label>
                    }
                  </div>
                </div>
              </div>

              @if (saveError()) {
                <div class="mt-4 bg-red-50 text-red-700 p-3 rounded-md text-sm">{{ saveError() }}</div>
              }

              <div class="mt-6 flex justify-end">
                <button (click)="handleSave()" [disabled]="saving()" class="px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 disabled:opacity-50 transition-colors">
                  {{ saving() ? 'Saving...' : 'Save Changes' }}
                </button>
              </div>
            </div>

            <!-- Attribute mappings -->
            <div class="bg-white shadow rounded-lg p-6">
              <div class="flex items-center justify-between mb-1">
                <h2 class="text-lg font-medium">Attribute Mappings</h2>
                <div class="flex gap-2">
                  <button (click)="applyDefaults()" class="text-sm px-3 py-1.5 bg-gray-100 rounded-md hover:bg-gray-200 transition-colors">Add type defaults</button>
                  <button (click)="startNewMapping()" class="inline-flex items-center text-sm px-3 py-1.5 bg-primary-600 text-white rounded-md hover:bg-primary-700 transition-colors">
                    <ui-icon name="plus" class="h-4 w-4 mr-1" /> Add
                  </button>
                </div>
              </div>
              <p class="text-xs text-gray-500 mb-4">
                userName, externalId and active are always sent. With no rows here the type's defaults are used.
                An expression replaces the property; a condition leaves the attribute out when false.
              </p>

              @if (mappingForm(); as mf) {
                <div class="border border-primary-200 bg-primary-50/40 rounded-md p-4 mb-4">
                  <div class="grid grid-cols-2 gap-3">
                    <div class="col-span-2">
                      <label class="block text-xs font-medium text-gray-700 mb-1">SCIM attribute</label>
                      <input type="text" [(ngModel)]="mf.scimAttribute" list="scim-attr-suggestions" class="w-full border border-gray-300 rounded-md px-3 py-2 text-sm font-mono" placeholder='displayName, name.givenName, emails[type eq "work"].value, urn:...:User:department' />
                      <datalist id="scim-attr-suggestions">
                        @for (s of attributeSuggestions; track s) {
                          <option [value]="s"></option>
                        }
                      </datalist>
                    </div>
                    <div>
                      <label class="block text-xs font-medium text-gray-700 mb-1">Value from</label>
                      <select [(ngModel)]="mf.source" class="w-full border border-gray-300 rounded-md px-3 py-2 text-sm">
                        <option value="property">Internal property</option>
                        <option value="expression">Expression</option>
                      </select>
                    </div>
                    @if (mf.source === 'property') {
                      <div>
                        <label class="block text-xs font-medium text-gray-700 mb-1">Internal property</label>
                        <input type="text" [(ngModel)]="mf.internalProperty" list="scim-prop-suggestions" class="w-full border border-gray-300 rounded-md px-3 py-2 text-sm" placeholder="Email, FirstName, custom:department" />
                        <datalist id="scim-prop-suggestions">
                          @for (p of internalProperties(); track p) {
                            <option [value]="p"></option>
                          }
                        </datalist>
                      </div>
                    } @else {
                      <div>
                        <label class="block text-xs font-medium text-gray-700 mb-1">Expression (Scriban)</label>
                        <input type="text" [(ngModel)]="mf.expression" class="w-full border border-gray-300 rounded-md px-3 py-2 text-sm font-mono" placeholder="{{ '{{' }} user.first_name {{ '}}' }} {{ '{{' }} user.last_name {{ '}}' }}" />
                      </div>
                    }
                    <div class="col-span-2">
                      <label class="block text-xs font-medium text-gray-700 mb-1">Condition (optional, Scriban boolean)</label>
                      <input type="text" [(ngModel)]="mf.condition" class="w-full border border-gray-300 rounded-md px-3 py-2 text-sm font-mono" placeholder='roles | array.contains "Manager"' />
                    </div>
                    <div>
                      <label class="block text-xs font-medium text-gray-700 mb-1">Transformation</label>
                      <select [(ngModel)]="mf.transformation" class="w-full border border-gray-300 rounded-md px-3 py-2 text-sm">
                        @for (t of transformations(); track t) {
                          <option [value]="t">{{ t }}</option>
                        }
                      </select>
                    </div>
                    <div>
                      <label class="block text-xs font-medium text-gray-700 mb-1">Default when empty</label>
                      <input type="text" [(ngModel)]="mf.defaultValue" class="w-full border border-gray-300 rounded-md px-3 py-2 text-sm" />
                    </div>
                    <label class="flex items-center text-sm col-span-2">
                      <input type="checkbox" [(ngModel)]="mf.isEnabled" class="rounded border-gray-300 text-primary-600 mr-2" /> Enabled
                    </label>
                  </div>
                  @if (mappingError()) {
                    <div class="mt-3 bg-red-50 text-red-700 p-2 rounded-md text-xs">{{ mappingError() }}</div>
                  }
                  <div class="flex justify-end gap-2 mt-3">
                    <button (click)="mappingForm.set(null)" class="px-3 py-1.5 text-sm text-gray-700 hover:bg-gray-100 rounded-md">Cancel</button>
                    <button (click)="saveMapping()" [disabled]="savingMapping()" class="px-3 py-1.5 text-sm bg-primary-600 text-white rounded-md hover:bg-primary-700 disabled:opacity-50">
                      {{ savingMapping() ? 'Saving...' : (mf.id ? 'Update' : 'Add') }}
                    </button>
                  </div>
                </div>
              }

              @if (loadingMappings()) {
                <div class="p-4 text-center text-gray-500 text-sm">Loading...</div>
              } @else if (mappings().length === 0) {
                <div class="text-sm text-gray-500 border border-dashed border-gray-200 rounded-md p-4">
                  Using {{ selectedFlavor()?.displayName || 'type' }} defaults:
                  <ul class="mt-2 space-y-1 font-mono text-xs text-gray-600">
                    @for (d of selectedFlavor()?.defaultUserMappings ?? []; track d.scimAttribute) {
                      <li>{{ d.scimAttribute }} ← {{ d.internalProperty }}</li>
                    }
                  </ul>
                </div>
              } @else {
                <table class="min-w-full divide-y divide-gray-200 text-sm">
                  <thead class="bg-gray-50">
                    <tr>
                      <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Attribute</th>
                      <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Value</th>
                      <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Condition</th>
                      <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Transform</th>
                      <th class="px-3 py-2 text-right text-xs font-medium text-gray-500 uppercase"></th>
                    </tr>
                  </thead>
                  <tbody class="divide-y divide-gray-200">
                    @for (m of mappings(); track m.id) {
                      <tr [class.opacity-50]="!m.isEnabled">
                        <td class="px-3 py-2 font-mono text-xs">{{ m.scimAttribute }}</td>
                        <td class="px-3 py-2 font-mono text-xs">
                          @if (m.expression) {
                            <span class="text-purple-700">{{ m.expression }}</span>
                          } @else {
                            {{ m.internalProperty }}
                          }
                          @if (m.defaultValue) {
                            <span class="text-gray-400"> ?? {{ m.defaultValue }}</span>
                          }
                        </td>
                        <td class="px-3 py-2 font-mono text-xs text-gray-600">{{ m.condition || '' }}</td>
                        <td class="px-3 py-2 text-xs text-gray-600">{{ m.transformation || '' }}</td>
                        <td class="px-3 py-2 text-right whitespace-nowrap">
                          <button (click)="editMapping(m)" class="text-gray-600 hover:text-gray-900 p-1" title="Edit"><ui-icon name="pencil" class="h-4 w-4" /></button>
                          <button (click)="deleteMapping(m)" class="text-red-600 hover:text-red-900 p-1" title="Delete"><ui-icon name="trash" class="h-4 w-4" /></button>
                        </td>
                      </tr>
                    }
                  </tbody>
                </table>
              }

              <!-- Preview -->
              <h3 class="text-sm font-medium text-gray-700 mt-6 mb-2">Preview payload for a user</h3>
              <div class="flex gap-2">
                <input type="text" [(ngModel)]="previewUserId" class="flex-1 border border-gray-300 rounded-md px-3 py-2 text-sm font-mono" placeholder="User id" />
                <button (click)="runPreview()" [disabled]="previewing() || !previewUserId" class="px-3 py-2 text-sm bg-gray-100 rounded-md hover:bg-gray-200 disabled:opacity-50">
                  {{ previewing() ? 'Building...' : 'Preview' }}
                </button>
              </div>
              @if (preview(); as pv) {
                <div class="mt-3">
                  @if (pv.error) {
                    <div class="bg-red-50 text-red-700 p-3 rounded-md text-xs">{{ pv.error }}</div>
                  } @else {
                    <p class="text-xs text-gray-500 mb-1">
                      userName <code>{{ pv.userName }}</code> ·
                      {{ pv.inScope ? 'in scope' : 'outside scope (would not be provisioned)' }}
                    </p>
                    <pre class="bg-gray-100 p-3 rounded-md text-xs font-mono overflow-x-auto">{{ pv.payload | json }}</pre>
                  }
                </div>
              }
            </div>

            <!-- Queue -->
            <div class="bg-white shadow rounded-lg p-6">
              <div class="flex items-center justify-between mb-4">
                <h2 class="text-lg font-medium">Queue</h2>
                <div class="flex gap-2">
                  <button (click)="loadQueue()" class="text-gray-600 hover:text-gray-900 p-1" title="Refresh"><ui-icon name="refresh" class="h-5 w-5" /></button>
                  <button (click)="retryFailed()" [disabled]="!queue() || (queue()!.summary.failed + queue()!.summary.dead) === 0" class="text-sm px-3 py-1.5 bg-gray-100 rounded-md hover:bg-gray-200 disabled:opacity-50">Retry failed</button>
                  <button (click)="runReconcile()" [disabled]="reconciling()" class="text-sm px-3 py-1.5 bg-primary-600 text-white rounded-md hover:bg-primary-700 disabled:opacity-50">
                    {{ reconciling() ? 'Queuing...' : 'Run full reconcile' }}
                  </button>
                </div>
              </div>
              @if (queueMessage()) {
                <div class="mb-3 bg-blue-50 text-blue-800 p-2 rounded-md text-xs">{{ queueMessage() }}</div>
              }
              @if (queue(); as q) {
                <div class="grid grid-cols-5 gap-3 mb-4 text-center">
                  <div><div class="text-xl font-bold">{{ q.summary.pending }}</div><div class="text-xs text-gray-500">Pending</div></div>
                  <div><div class="text-xl font-bold">{{ q.summary.processing }}</div><div class="text-xs text-gray-500">Processing</div></div>
                  <div><div class="text-xl font-bold text-yellow-600">{{ q.summary.failed }}</div><div class="text-xs text-gray-500">Retrying</div></div>
                  <div><div class="text-xl font-bold text-red-600">{{ q.summary.dead }}</div><div class="text-xs text-gray-500">Gave up</div></div>
                  <div><div class="text-xl font-bold text-green-600">{{ q.summary.succeededLast24h }}</div><div class="text-xs text-gray-500">Done (24h)</div></div>
                </div>
                <div class="flex items-center gap-2 mb-2">
                  <label class="text-xs text-gray-500">Show</label>
                  <select [(ngModel)]="queueFilter" (ngModelChange)="loadQueue()" class="border border-gray-300 rounded-md px-2 py-1 text-xs">
                    <option value="failed,dead">Problems</option>
                    <option value="pending,processing">Waiting</option>
                    <option value="succeeded">Done</option>
                    <option value="">Everything</option>
                  </select>
                </div>
                @if (q.items.length === 0) {
                  <p class="text-sm text-gray-500">Nothing here.</p>
                } @else {
                  <table class="min-w-full divide-y divide-gray-200 text-sm">
                    <thead class="bg-gray-50">
                      <tr>
                        <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Resource</th>
                        <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Op</th>
                        <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Status</th>
                        <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Tries</th>
                        <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 uppercase">Detail</th>
                      </tr>
                    </thead>
                    <tbody class="divide-y divide-gray-200">
                      @for (i of q.items; track i.id) {
                        <tr>
                          <td class="px-3 py-2 font-mono text-xs">{{ i.resourceType }} {{ i.internalId }}</td>
                          <td class="px-3 py-2 text-xs">{{ i.operation }}</td>
                          <td class="px-3 py-2 text-xs">
                            <span class="px-2 py-0.5 rounded-full" [class]="statusClass(i.status)">{{ i.status }}</span>
                          </td>
                          <td class="px-3 py-2 text-xs">{{ i.attempts }}</td>
                          <td class="px-3 py-2 text-xs text-gray-600">
                            @if (i.lastError) { <span class="text-red-700">{{ i.lastError }}</span> }
                            @else { {{ i.reason }} }
                          </td>
                        </tr>
                      }
                    </tbody>
                  </table>
                }
              }
            </div>

            <!-- Recent activity -->
            <div class="bg-white shadow rounded-lg p-6">
              <div class="flex items-center justify-between mb-4">
                <h2 class="text-lg font-medium">Recent Activity</h2>
                <button (click)="loadLogs()" class="text-gray-600 hover:text-gray-900 p-1" title="Refresh"><ui-icon name="refresh" class="h-5 w-5" /></button>
              </div>
              @if (logs().length === 0) {
                <p class="text-sm text-gray-500">No calls made yet.</p>
              } @else {
                <div class="space-y-2">
                  @for (l of logs(); track l.id) {
                    <div class="flex items-start gap-3 text-sm">
                      <ui-icon [name]="l.success ? 'check-circle' : 'x-circle'" class="h-5 w-5 mt-0.5" [class.text-green-600]="l.success" [class.text-red-600]="!l.success" />
                      <div class="flex-1 min-w-0">
                        <div class="font-mono text-xs truncate">
                          <span class="font-semibold">{{ l.method }}</span> {{ l.path }}
                          <span class="text-gray-400">→ {{ l.statusCode }} · {{ l.durationMs }}ms</span>
                        </div>
                        <div class="text-xs text-gray-500">
                          {{ l.operation }} {{ l.resourceType || '' }} {{ l.internalId || '' }}
                          @if (l.errorMessage) { <span class="text-red-600">· {{ l.errorMessage }}</span> }
                        </div>
                      </div>
                      <div class="text-xs text-gray-400 whitespace-nowrap">{{ formatDate(l.timestamp) }}</div>
                    </div>
                  }
                </div>
              }
            </div>
          </div>

          <!-- Right column -->
          <div class="lg:col-span-1 space-y-6">
            <!-- Credential -->
            <div class="bg-white shadow rounded-lg p-6">
              <h2 class="text-lg font-medium mb-2">Credential</h2>
              <p class="text-xs text-gray-500 mb-3">
                @if (provider()!.hasSecret) {
                  Secret set {{ formatDate(provider()!.secretUpdatedAt!) }}. Enter a new one to rotate.
                } @else {
                  No secret stored. Pushes fail until one is set.
                }
              </p>
              <div class="relative mb-2">
                <input
                  [type]="showSecret() ? 'text' : 'password'"
                  [(ngModel)]="newSecret"
                  autocomplete="new-password"
                  class="w-full border border-gray-300 rounded-md px-3 py-2 pr-10 text-sm font-mono"
                  [placeholder]="form.authType === 'bearer' ? 'Bearer token / API key' : 'Client secret'"
                />
                <button type="button" (click)="showSecret.set(!showSecret())" class="absolute inset-y-0 right-0 px-3 text-gray-500 hover:text-gray-700">
                  <ui-icon [name]="showSecret() ? 'eye-slash' : 'eye'" class="h-4 w-4" />
                </button>
              </div>
              @if (secretError()) {
                <div class="mb-2 bg-red-50 text-red-700 p-2 rounded-md text-xs">{{ secretError() }}</div>
              }
              <button (click)="saveSecret()" [disabled]="!newSecret || savingSecret()" class="w-full px-4 py-2 bg-primary-600 text-white rounded-md hover:bg-primary-700 disabled:opacity-50 text-sm">
                {{ savingSecret() ? 'Saving...' : (provider()!.hasSecret ? 'Rotate secret' : 'Save secret') }}
              </button>

              <div class="border-t border-gray-100 mt-4 pt-4">
                <button (click)="testConnection()" [disabled]="testing()" class="w-full inline-flex items-center justify-center px-4 py-2 bg-gray-100 rounded-md hover:bg-gray-200 disabled:opacity-50 text-sm">
                  <ui-icon name="play-circle" class="h-4 w-4 mr-2" />
                  {{ testing() ? 'Testing...' : 'Test connection' }}
                </button>
                @if (testResult(); as t) {
                  <div class="mt-3 text-xs rounded-md p-3" [class.bg-green-50]="t.success" [class.text-green-800]="t.success" [class.bg-red-50]="!t.success" [class.text-red-800]="!t.success">
                    <p>{{ t.message }}</p>
                    <ul class="mt-2 space-y-0.5 text-gray-700">
                      <li>Discovery: {{ t.discoveryReachable ? 'ok' : 'failed' }} ({{ t.discoveryStatusCode }})</li>
                      <li>Authenticated list: {{ t.authenticationOk ? 'ok' : 'failed' }} ({{ t.authenticationStatusCode }})</li>
                      @if (t.patchSupported !== null && t.patchSupported !== undefined) {
                        <li>PATCH {{ t.patchSupported ? 'yes' : 'no' }} · Bulk {{ t.bulkSupported ? 'yes' : 'no' }} · Filter {{ t.filterSupported ? 'yes' : 'no' }}{{ t.filterMaxResults ? ' (max ' + t.filterMaxResults + ')' : '' }} · ETag {{ t.etagSupported ? 'yes' : 'no' }}</li>
                      }
                      @if (t.authenticationSchemes.length) {
                        <li>Auth schemes: {{ t.authenticationSchemes.join(', ') }}</li>
                      }
                    </ul>
                  </div>
                }
              </div>
            </div>

            <!-- Statistics -->
            <div class="bg-white shadow rounded-lg p-6">
              <h2 class="text-lg font-medium mb-4">Statistics</h2>
              <dl class="space-y-2 text-sm">
                <div class="flex justify-between"><dt class="text-gray-500">Successful operations</dt><dd class="font-medium text-green-600">{{ provider()!.successCount }}</dd></div>
                <div class="flex justify-between"><dt class="text-gray-500">Failed operations</dt><dd class="font-medium text-red-600">{{ provider()!.errorCount }}</dd></div>
                <div class="flex justify-between"><dt class="text-gray-500">Provisioned users</dt><dd class="font-medium">{{ resourceCounts().users }}</dd></div>
                <div class="flex justify-between"><dt class="text-gray-500">Provisioned groups</dt><dd class="font-medium">{{ resourceCounts().groups }}</dd></div>
                <div class="flex justify-between"><dt class="text-gray-500">Last success</dt><dd>{{ provider()!.lastSuccessAt ? formatDate(provider()!.lastSuccessAt!) : 'Never' }}</dd></div>
                <div class="flex justify-between"><dt class="text-gray-500">Last error</dt><dd>{{ provider()!.lastErrorAt ? formatDate(provider()!.lastErrorAt!) : 'Never' }}</dd></div>
                <div class="flex justify-between"><dt class="text-gray-500">Last reconcile</dt><dd>{{ provider()!.lastReconcileAt ? formatDate(provider()!.lastReconcileAt!) : 'Never' }}</dd></div>
              </dl>
              @if (provider()!.lastError) {
                <p class="mt-3 text-xs text-red-700 break-words">{{ provider()!.lastError }}</p>
              }
              @if (provider()!.lastReconcileSummary) {
                <p class="mt-3 text-xs text-gray-500">{{ provider()!.lastReconcileSummary }}</p>
              }
            </div>
          </div>
        </div>
      }
    </div>
  `,
})
export class ScimProviderDetailComponent implements OnInit {
  private readonly scimService = inject(ScimService);
  private readonly roleService = inject(RoleService);
  private readonly teamService = inject(TeamService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  providerId = '';
  provider = signal<ScimProvider | null>(null);
  options = signal<ScimProviderOptions | null>(null);
  flavors = computed<ScimProviderFlavor[]>(() => this.options()?.flavors ?? []);
  internalProperties = computed(() => this.options()?.internalProperties ?? []);
  transformations = computed(() => this.options()?.transformations ?? ['none']);
  roles = signal<RoleDto[]>([]);
  teams = signal<TeamSummary[]>([]);
  inboundClients = signal<ScimClient[]>([]);
  loading = signal(true);
  saving = signal(false);
  saveError = signal<string | null>(null);

  selectedFlavorKey = signal('generic');
  selectedFlavor = computed(() => this.flavors().find((f) => f.key === this.selectedFlavorKey()) ?? null);
  groupsUnsupported = computed(() => {
    const f = this.selectedFlavor();
    return !!f && !f.supportsGroups;
  });
  scopeRoleIds = signal<Set<string>>(new Set());
  scopeTeamIds = signal<Set<string>>(new Set());

  mappings = signal<ScimProviderAttributeMapping[]>([]);
  loadingMappings = signal(true);
  mappingForm = signal<MappingForm | null>(null);
  mappingError = signal<string | null>(null);
  savingMapping = signal(false);

  previewUserId = '';
  previewing = signal(false);
  preview = signal<ScimProviderPreview | null>(null);

  queue = signal<ScimProviderQueueResponse | null>(null);
  queueFilter = 'failed,dead';
  queueMessage = signal<string | null>(null);
  reconciling = signal(false);

  logs = signal<ScimProviderLog[]>([]);
  resourceCounts = signal({ users: 0, groups: 0 });

  newSecret = '';
  showSecret = signal(false);
  savingSecret = signal(false);
  secretError = signal<string | null>(null);
  testing = signal(false);
  testResult = signal<ScimProviderTestResult | null>(null);

  readonly attributeSuggestions = [
    'displayName',
    'name.givenName',
    'name.familyName',
    'name.formatted',
    'nickName',
    'title',
    'preferredLanguage',
    'locale',
    'timezone',
    'emails[type eq "work"].value',
    'phoneNumbers[type eq "work"].value',
    'phoneNumbers[type eq "mobile"].value',
    'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department',
    'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:employeeNumber',
    'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:organization',
    'urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:costCenter',
  ];

  form = {
    name: '',
    description: '',
    isEnabled: true,
    flavor: 'generic',
    baseUrl: '',
    authType: 'bearer' as ScimProviderAuthType,
    oauthTokenEndpoint: '',
    oauthClientId: '',
    oauthScope: '',
    syncUsers: true,
    syncGroups: false,
    groupSource: 'roles' as ScimProviderGroupSource,
    userNameSource: 'userName' as ScimProviderUserNameSource,
    userNameExpression: '',
    deprovisionMode: 'deactivate' as ScimProviderDeprovisionMode,
    reconcileIntervalMinutes: 360,
    linkedScimClientId: '',
  };

  ngOnInit(): void {
    this.providerId = this.route.snapshot.paramMap.get('id') || '';
    if (!this.providerId) return;

    this.scimService.getProviderOptions().subscribe({
      next: (o) => this.options.set(o),
      error: (err) => console.error('Failed to load provider options:', err),
    });
    this.roleService.getAllRoles().subscribe({
      next: (r) => this.roles.set(r.filter((x) => !x.isGlobal)),
      error: (err) => console.error('Failed to load roles:', err),
    });
    this.teamService.picker().subscribe({
      next: (t) => this.teams.set(t),
      error: (err) => console.error('Failed to load teams:', err),
    });
    this.scimService.getClients().subscribe({
      next: (c) => this.inboundClients.set(c),
      error: (err) => console.error('Failed to load inbound SCIM clients:', err),
    });

    this.loadProvider();
    this.loadMappings();
    this.loadQueue();
    this.loadLogs();
    this.loadResourceCounts();
  }

  loadProvider(): void {
    this.scimService.getProvider(this.providerId).subscribe({
      next: (p) => {
        this.applyProvider(p);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load provider:', err);
        this.loading.set(false);
      },
    });
  }

  private applyProvider(p: ScimProvider): void {
    this.provider.set(p);
    this.selectedFlavorKey.set(p.flavor);
    this.scopeRoleIds.set(new Set(p.scopeRoleIds));
    this.scopeTeamIds.set(new Set(p.scopeTeamIds));
    this.form = {
      name: p.name,
      description: p.description || '',
      isEnabled: p.isEnabled,
      flavor: p.flavor,
      baseUrl: p.baseUrl,
      authType: p.authType,
      oauthTokenEndpoint: p.oauthTokenEndpoint || '',
      oauthClientId: p.oauthClientId || '',
      oauthScope: p.oauthScope || '',
      syncUsers: p.syncUsers,
      syncGroups: p.syncGroups,
      groupSource: p.groupSource || 'roles',
      userNameSource: p.userNameSource,
      userNameExpression: p.userNameExpression || '',
      deprovisionMode: p.deprovisionMode,
      reconcileIntervalMinutes: p.reconcileIntervalMinutes,
      linkedScimClientId: p.linkedScimClientId || '',
    };
  }

  onFlavorChange(key: string): void {
    this.selectedFlavorKey.set(key);
    const f = this.selectedFlavor();
    if (f?.requiresEmailUserName && this.form.userNameSource === 'userName') {
      this.form.userNameSource = 'email';
    }
    if (f && !f.supportsGroups) {
      this.form.syncGroups = false;
    }
  }

  toggleScopeRole(id: string): void {
    this.scopeRoleIds.update((s) => {
      const next = new Set(s);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  toggleScopeTeam(id: string): void {
    this.scopeTeamIds.update((s) => {
      const next = new Set(s);
      next.has(id) ? next.delete(id) : next.add(id);
      return next;
    });
  }

  handleSave(): void {
    this.saving.set(true);
    this.saveError.set(null);
    const f = this.form;
    const oauth = f.authType === 'oauth_client_credentials';
    this.scimService
      .updateProvider(this.providerId, {
        name: f.name,
        description: f.description || undefined,
        isEnabled: f.isEnabled,
        flavor: f.flavor,
        baseUrl: f.baseUrl,
        authType: f.authType,
        oauthTokenEndpoint: oauth ? f.oauthTokenEndpoint || undefined : undefined,
        oauthClientId: oauth ? f.oauthClientId || undefined : undefined,
        oauthScope: oauth ? f.oauthScope || undefined : undefined,
        syncUsers: f.syncUsers,
        syncGroups: f.syncGroups,
        groupSource: f.groupSource,
        scopeRoleIds: [...this.scopeRoleIds()],
        scopeTeamIds: [...this.scopeTeamIds()],
        userNameSource: f.userNameSource,
        userNameExpression: f.userNameSource === 'expression' ? f.userNameExpression || undefined : undefined,
        deprovisionMode: f.deprovisionMode,
        reconcileIntervalMinutes: Number(f.reconcileIntervalMinutes) || 0,
        linkedScimClientId: f.linkedScimClientId || undefined,
      })
      .subscribe({
        next: (updated) => {
          this.applyProvider(updated);
          this.saving.set(false);
        },
        error: (err) => {
          this.saveError.set(typeof err?.error === 'string' ? err.error : 'Failed to save changes');
          this.saving.set(false);
        },
      });
  }

  // ── Credential / test ─────────────────────────────────────────────

  saveSecret(): void {
    if (!this.newSecret) return;
    this.savingSecret.set(true);
    this.secretError.set(null);
    this.scimService.setProviderSecret(this.providerId, this.newSecret).subscribe({
      next: (updated) => {
        this.applyProvider(updated);
        this.newSecret = '';
        this.showSecret.set(false);
        this.savingSecret.set(false);
      },
      error: (err) => {
        this.secretError.set(typeof err?.error === 'string' ? err.error : 'Failed to save secret');
        this.savingSecret.set(false);
      },
    });
  }

  testConnection(): void {
    this.testing.set(true);
    this.testResult.set(null);
    this.scimService.testProvider(this.providerId).subscribe({
      next: (r) => {
        this.testResult.set(r);
        this.testing.set(false);
        this.loadLogs();
      },
      error: (err) => {
        this.testResult.set({
          success: false,
          message: typeof err?.error === 'string' ? err.error : 'Test request failed',
          discoveryReachable: false,
          discoveryStatusCode: 0,
          authenticationOk: false,
          authenticationStatusCode: 0,
          authenticationSchemes: [],
        });
        this.testing.set(false);
      },
    });
  }

  // ── Mappings ──────────────────────────────────────────────────────

  loadMappings(): void {
    this.loadingMappings.set(true);
    this.scimService.getProviderMappings(this.providerId).subscribe({
      next: (m) => {
        this.mappings.set(m);
        this.loadingMappings.set(false);
      },
      error: (err) => {
        console.error('Failed to load mappings:', err);
        this.loadingMappings.set(false);
      },
    });
  }

  startNewMapping(): void {
    this.mappingError.set(null);
    this.mappingForm.set({
      id: null,
      scimAttribute: '',
      source: 'property',
      internalProperty: '',
      expression: '',
      condition: '',
      transformation: 'none',
      defaultValue: '',
      isEnabled: true,
    });
  }

  editMapping(m: ScimProviderAttributeMapping): void {
    this.mappingError.set(null);
    this.mappingForm.set({
      id: m.id,
      scimAttribute: m.scimAttribute,
      source: m.expression ? 'expression' : 'property',
      internalProperty: m.internalProperty || '',
      expression: m.expression || '',
      condition: m.condition || '',
      transformation: m.transformation || 'none',
      defaultValue: m.defaultValue || '',
      isEnabled: m.isEnabled,
    });
  }

  saveMapping(): void {
    const mf = this.mappingForm();
    if (!mf) return;
    this.savingMapping.set(true);
    this.mappingError.set(null);
    const request = {
      scimAttribute: mf.scimAttribute,
      internalProperty: mf.source === 'property' ? mf.internalProperty || undefined : undefined,
      expression: mf.source === 'expression' ? mf.expression || undefined : undefined,
      condition: mf.condition || undefined,
      transformation: mf.transformation === 'none' ? undefined : mf.transformation,
      defaultValue: mf.defaultValue || undefined,
      isEnabled: mf.isEnabled,
    };
    const call = mf.id
      ? this.scimService.updateProviderMapping(this.providerId, mf.id, request)
      : this.scimService.createProviderMapping(this.providerId, request);
    call.subscribe({
      next: () => {
        this.savingMapping.set(false);
        this.mappingForm.set(null);
        this.loadMappings();
      },
      error: (err) => {
        this.mappingError.set(typeof err?.error === 'string' ? err.error : 'Failed to save mapping');
        this.savingMapping.set(false);
      },
    });
  }

  deleteMapping(m: ScimProviderAttributeMapping): void {
    if (!confirm(`Remove the mapping for ${m.scimAttribute}?`)) return;
    this.scimService.deleteProviderMapping(this.providerId, m.id).subscribe({
      next: () => this.mappings.update((list) => list.filter((x) => x.id !== m.id)),
      error: (err) => console.error('Failed to delete mapping:', err),
    });
  }

  applyDefaults(): void {
    this.scimService.applyProviderDefaultMappings(this.providerId).subscribe({
      next: () => this.loadMappings(),
      error: (err) => console.error('Failed to apply defaults:', err),
    });
  }

  runPreview(): void {
    if (!this.previewUserId) return;
    this.previewing.set(true);
    this.scimService.previewProvider(this.providerId, this.previewUserId.trim()).subscribe({
      next: (pv) => {
        this.preview.set(pv);
        this.previewing.set(false);
      },
      error: (err) => {
        this.preview.set({
          userId: this.previewUserId,
          inScope: false,
          error: typeof err?.error === 'string' ? err.error : 'Preview failed',
        });
        this.previewing.set(false);
      },
    });
  }

  // ── Queue / logs ──────────────────────────────────────────────────

  loadQueue(): void {
    this.scimService.getProviderQueue(this.providerId, this.queueFilter || undefined, 0, 25).subscribe({
      next: (q) => this.queue.set(q),
      error: (err) => console.error('Failed to load queue:', err),
    });
  }

  retryFailed(): void {
    this.scimService.retryProviderQueue(this.providerId).subscribe({
      next: (r) => {
        this.queueMessage.set(`${r.requeued} item(s) queued again.`);
        this.loadQueue();
      },
      error: (err) => console.error('Failed to retry queue:', err),
    });
  }

  runReconcile(): void {
    this.reconciling.set(true);
    this.queueMessage.set(null);
    this.scimService.reconcileProvider(this.providerId).subscribe({
      next: (r) => {
        this.queueMessage.set(`Reconcile queued: ${r.summary}.`);
        this.reconciling.set(false);
        this.loadQueue();
        this.loadProvider();
      },
      error: (err) => {
        this.queueMessage.set(typeof err?.error === 'string' ? err.error : 'Failed to queue reconcile');
        this.reconciling.set(false);
      },
    });
  }

  loadLogs(): void {
    this.scimService.getProviderLogs(this.providerId, 1, 20).subscribe({
      next: (r) => this.logs.set(r.items),
      error: (err) => console.error('Failed to load logs:', err),
    });
  }

  loadResourceCounts(): void {
    this.scimService.getProviderResources(this.providerId, 'User', 1, 1).subscribe({
      next: (r) => this.resourceCounts.update((c) => ({ ...c, users: r.totalCount })),
      error: (err) => console.error('Failed to load user count:', err),
    });
    this.scimService.getProviderResources(this.providerId, 'Group', 1, 1).subscribe({
      next: (r) => this.resourceCounts.update((c) => ({ ...c, groups: r.totalCount })),
      error: (err) => console.error('Failed to load group count:', err),
    });
  }

  statusClass(status: ScimQueueStatus): string {
    switch (status) {
      case 'succeeded':
        return 'bg-green-100 text-green-800';
      case 'failed':
        return 'bg-yellow-100 text-yellow-800';
      case 'dead':
        return 'bg-red-100 text-red-800';
      case 'processing':
        return 'bg-blue-100 text-blue-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }

  flavorName(key: string): string {
    return this.flavors().find((f) => f.key === key)?.displayName ?? key;
  }

  goBack(): void {
    this.router.navigate(['/scim/providers']);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }
}
