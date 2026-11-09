import { Component, input, output, computed, signal, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '../icon/icon.component';
import { ModalComponent } from '../modal/modal.component';
import type {
  ModuleRolesConfig,
  ModuleRoleDto,
  ModuleRoleUserDto,
  CreateModuleRoleRequest,
  ModulePermissionsResponse,
} from './module-roles.types';

/**
 * Reusable component for managing module-specific roles.
 *
 * This is a presentational component that uses input/output pattern for
 * communication with parent components. The parent is responsible for
 * all API calls and state management.
 *
 * @example
 * ```html
 * <ui-module-roles
 *   [config]="moduleConfig"
 *   [roles]="roles()"
 *   [permissions]="permissions()"
 *   [loading]="loading()"
 *   [builtInRoles]="['HRAdmin', 'HRManager']"
 *   [roleUsers]="roleUsers()"
 *   [loadingUsers]="loadingUsers()"
 *   [userSearchResults]="userSearchResults()"
 *   (createRole)="onCreateRole($event)"
 *   (updateRole)="onUpdateRole($event)"
 *   (deleteRole)="onDeleteRole($event)"
 *   (seedDefaults)="onSeedDefaults()"
 *   (loadRoleUsers)="onLoadRoleUsers($event)"
 *   (assignUser)="onAssignUser($event)"
 *   (removeUser)="onRemoveUser($event)"
 *   (searchUsers)="onSearchUsers($event)"
 * />
 * ```
 */
@Component({
  selector: 'ui-module-roles',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent, ModalComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-semibold text-gray-900">{{ config().moduleName }} Roles</h1>
          <p class="mt-1 text-sm text-gray-500">
            Manage {{ config().moduleName }}-specific roles and permissions. Only {{ config().moduleName }} permissions can be assigned here.
          </p>
        </div>
        <div class="flex items-center gap-2">
          <button
            (click)="onSeedDefaults()"
            [disabled]="saving()"
            class="btn-secondary"
            title="Add the module's default roles to this tenant"
          >
            Seed Default Roles
          </button>
          <button (click)="openCreateModal()" class="btn-primary">
            <ui-icon name="plus" class="w-5 h-5 mr-2" />
            Create Role
          </button>
        </div>
      </div>

      <!-- Info Banner -->
      @if (config().showInfoBanner !== false) {
        <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div class="flex">
            <ui-icon name="shield-check" class="h-5 w-5 text-blue-400 mr-3 mt-0.5" />
            <div class="text-sm text-blue-700">
              <p class="font-medium">Delegated Administration</p>
              <p class="mt-1">
                {{ config().infoBannerDescription || defaultInfoBannerDescription() }}
              </p>
            </div>
          </div>
        </div>
      }

      <!-- Loading -->
      @if (loading()) {
        <div class="flex items-center justify-center h-64">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
        </div>
      } @else {
        <!-- Roles Table -->
        <div class="card overflow-hidden">
          <table class="table">
            <thead>
              <tr>
                <th>Role</th>
                <th>Permissions</th>
                <th>Managed By</th>
                <th class="text-right">Actions</th>
              </tr>
            </thead>
            <tbody>
              @for (role of roles(); track role.id) {
                <tr>
                  <td>
                    <div>
                      <div class="flex items-center gap-2">
                        <span class="font-medium text-gray-900">{{ role.displayName }}</span>
                        @if (isBuiltInRole(role)) {
                          <span class="badge-gray" title="Default role provided by the module">Preset</span>
                        }
                      </div>
                      <div class="text-sm text-gray-500">{{ role.name }}</div>
                      @if (role.description) {
                        <div class="text-xs text-gray-400 mt-1">{{ role.description }}</div>
                      }
                    </div>
                  </td>
                  <td>
                    <div class="flex flex-wrap gap-1">
                      @if (hasWildcard(role.permissions)) {
                        <span class="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-primary-100 text-primary-800">
                          {{ config().wildcardPermission }} (All)
                        </span>
                      } @else {
                        <span class="badge-gray">{{ role.permissions.length }} permissions</span>
                        @if (getSensitiveCount(role.permissions) > 0) {
                          <span class="badge-warning">
                            <ui-icon name="exclamation-triangle" class="w-3 h-3 mr-1" />
                            {{ getSensitiveCount(role.permissions) }} sensitive
                          </span>
                        }
                      }
                    </div>
                  </td>
                  <td class="text-gray-500">{{ role.managedByRole || '-' }}</td>
                  <td class="text-right">
                    <div class="flex items-center justify-end gap-2">
                      <button
                        (click)="openUsersModal(role)"
                        class="p-2 text-gray-400 hover:text-primary-600 hover:bg-gray-100 rounded"
                        title="Manage Users"
                      >
                        <ui-icon name="users" class="w-5 h-5" />
                      </button>
                      <button
                        (click)="openEditModal(role)"
                        class="p-2 text-gray-400 hover:text-primary-600 hover:bg-gray-100 rounded"
                        title="Edit Role"
                      >
                        <ui-icon name="pencil" class="w-5 h-5" />
                      </button>
                      <button
                        (click)="onDeleteRole(role)"
                        class="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded"
                        title="Delete Role"
                      >
                        <ui-icon name="trash" class="w-5 h-5" />
                      </button>
                    </div>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="4" class="text-center py-12">
                    <ui-icon name="shield-check" class="mx-auto h-12 w-12 text-gray-400" />
                    <p class="mt-2 text-gray-500">No {{ config().moduleName }} roles found</p>
                    <div class="mt-4 flex items-center justify-center gap-3">
                      <button
                        (click)="onSeedDefaults()"
                        [disabled]="saving()"
                        class="btn-primary btn-sm"
                        title="Add the module's default roles to this tenant"
                      >
                        Seed Default Roles
                      </button>
                      <button (click)="openCreateModal()" class="btn-secondary btn-sm">
                        Create Custom Role
                      </button>
                    </div>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }

      <!-- Role Edit/Create Modal -->
      <ui-modal
        [open]="isModalOpen()"
        [title]="isCreateMode() ? 'Create ' + config().moduleName + ' Role' : 'Edit ' + config().moduleName + ' Role'"
        size="2xl"
        [scrollable]="true"
        (closed)="closeModal()"
      >
        <form (ngSubmit)="saveRole()">
          <div class="space-y-4">
            @if (modalError()) {
              <div class="alert-error">{{ modalError() }}</div>
            }

            @if (isCreateMode()) {
              <div>
                <label class="block text-sm font-medium text-gray-700">Role Name</label>
                <input
                  type="text"
                  [(ngModel)]="formData.name"
                  name="name"
                  required
                  pattern="[A-Za-z0-9_]+"
                  title="Only letters, numbers, and underscores"
                  class="mt-1"
                />
              </div>
            }

            <div>
              <label class="block text-sm font-medium text-gray-700">Display Name</label>
              <input
                type="text"
                [(ngModel)]="formData.displayName"
                name="displayName"
                required
                class="mt-1"
              />
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-700">Description</label>
              <textarea
                [(ngModel)]="formData.description"
                name="description"
                rows="2"
                class="mt-1"
              ></textarea>
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-700 mb-3">Permissions</label>

              <!-- Wildcard Toggle -->
              <div class="mb-4 border-2 border-primary-200 rounded-lg p-3 bg-primary-50">
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    [checked]="useWildcard()"
                    (change)="toggleWildcard()"
                    class="rounded border-gray-300 text-primary-600 focus:ring-primary-500"
                  />
                  <span class="ml-2 font-medium text-primary-900">Grant All {{ config().moduleName }} Permissions ({{ config().wildcardPermission }})</span>
                </label>
                <p class="ml-6 mt-1 text-xs text-primary-700">
                  Uses the {{ config().wildcardPermission }} wildcard to grant full access to all current and future {{ config().moduleName }} permissions.
                </p>
              </div>

              <!-- Search and Expand/Collapse Controls -->
              <div class="mb-4 flex items-center gap-3">
                  <div class="flex-1 relative">
                    <ui-icon name="magnifying-glass" class="absolute left-3 top-1/2 transform -translate-y-1/2 h-4 w-4 text-gray-400" />
                    <input
                      type="text"
                      [(ngModel)]="permissionSearchTerm"
                      name="permissionSearch"
                      placeholder="Search permissions..."
                      class="pl-9 w-full text-sm"
                    />
                    @if (permissionSearchTerm) {
                      <button
                        type="button"
                        (click)="permissionSearchTerm = ''"
                        class="absolute right-2 top-1/2 transform -translate-y-1/2 text-gray-400 hover:text-gray-600"
                      >
                        <ui-icon name="x-mark" class="h-4 w-4" />
                      </button>
                    }
                  </div>
                  <button
                    type="button"
                    (click)="expandAllCategories()"
                    class="btn-secondary btn-sm"
                    title="Expand All"
                  >
                    <ui-icon name="chevron-down" class="h-4 w-4" />
                  </button>
                  <button
                    type="button"
                    (click)="collapseAllCategories()"
                    class="btn-secondary btn-sm"
                    title="Collapse All"
                  >
                    <ui-icon name="chevron-up" class="h-4 w-4" />
                  </button>
                </div>

                <div class="space-y-2">
                  @for (category of getFilteredPermissionCategories(); track category.name) {
                    <div class="border rounded-lg overflow-hidden">
                      <div
                        class="flex items-center justify-between p-3 bg-gray-50 cursor-pointer hover:bg-gray-100"
                        (click)="toggleCategoryCollapse(category.name)"
                      >
                        <label class="flex items-center" (click)="$event.stopPropagation()">
                          <input
                            type="checkbox"
                            [checked]="isCategorySelected(category.permissions)"
                            [indeterminate]="isCategoryPartiallySelected(category.permissions)"
                            [disabled]="isCategoryLocked(category.permissions)"
                            (change)="toggleCategory(category.permissions)"
                            class="rounded border-gray-300 text-primary-600 focus:ring-primary-500 disabled:opacity-50 disabled:cursor-not-allowed"
                          />
                          <span class="ml-2 font-medium text-gray-900">{{ category.name }}</span>
                        </label>
                        <div class="flex items-center gap-2">
                          <span class="text-xs text-gray-500">
                            {{ getCategorySelectedCount(category.permissions) }}/{{ category.permissions.length }}
                          </span>
                          <ui-icon
                            [name]="collapsedCategories.has(category.name) ? 'chevron-right' : 'chevron-down'"
                            class="h-4 w-4 text-gray-400"
                          />
                        </div>
                      </div>
                      @if (!collapsedCategories.has(category.name)) {
                        <div class="p-3 pt-2 grid grid-cols-2 gap-2 border-t">
                          @for (permission of getFilteredPermissions(category.permissions); track permission) {
                            <label [class]="'flex items-center text-sm ' + (isSensitive(permission) ? 'text-amber-700' : 'text-gray-600') + (isPermissionLocked(permission) ? ' opacity-60' : '')">
                              <input
                                type="checkbox"
                                [checked]="selectedPermissions.has(permission)"
                                [disabled]="isPermissionLocked(permission)"
                                (change)="togglePermission(permission)"
                                class="rounded border-gray-300 text-primary-600 focus:ring-primary-500 disabled:cursor-not-allowed"
                              />
                              <span class="ml-2">
                                {{ formatPermission(permission) }}
                                @if (isSensitive(permission)) {
                                  <ui-icon name="exclamation-triangle" class="inline h-3 w-3 ml-1 text-amber-500" />
                                }
                              </span>
                            </label>
                          }
                        </div>
                      }
                    </div>
                  }
                  @if (getFilteredPermissionCategories().length === 0 && permissionSearchTerm) {
                    <div class="text-center py-8 text-gray-500">
                      <ui-icon name="magnifying-glass" class="mx-auto h-8 w-8 text-gray-300" />
                      <p class="mt-2 text-sm">No permissions found matching "{{ permissionSearchTerm }}"</p>
                    </div>
                  }
                </div>
            </div>
          </div>

          <div modal-footer class="px-6 py-4 border-t bg-gray-50 flex justify-end gap-3">
            <button type="button" (click)="closeModal()" class="btn-secondary">
              Cancel
            </button>
            <button type="submit" [disabled]="saving()" class="btn-primary">
              {{ saving() ? 'Saving...' : 'Save' }}
            </button>
          </div>
        </form>
      </ui-modal>

      <!-- Users Modal -->
      <ui-modal
        [open]="isUsersModalOpen()"
        [title]="'Manage Users - ' + (selectedRole()?.displayName || '')"
        size="lg"
        [scrollable]="true"
        (closed)="closeUsersModal()"
      >
        <div class="space-y-4">
          <!-- Add User Search -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-1">Add User</label>
            <div class="relative">
              <input
                type="text"
                [(ngModel)]="userSearchTerm"
                (input)="onUserSearchInput()"
                placeholder="Search by name or email..."
                class="w-full"
              />
              @if (userSearchResults().length > 0) {
                <div class="absolute z-10 w-full mt-1 bg-white border border-gray-200 rounded-lg shadow-lg max-h-48 overflow-y-auto">
                  @for (user of userSearchResults(); track user.id) {
                    <button
                      (click)="onAssignUserToRole(user)"
                      class="w-full text-left px-4 py-2 hover:bg-gray-50 flex items-center justify-between"
                      [disabled]="isUserAlreadyAssigned(user.id)"
                    >
                      <div>
                        <div class="text-sm font-medium text-gray-900">{{ user.displayName || user.email }}</div>
                        <div class="text-xs text-gray-500">{{ user.email }}</div>
                      </div>
                      @if (isUserAlreadyAssigned(user.id)) {
                        <span class="text-xs text-gray-400">Already assigned</span>
                      } @else {
                        <ui-icon name="plus" class="w-4 h-4 text-primary-600" />
                      }
                    </button>
                  }
                </div>
              }
            </div>
          </div>

          <!-- Current Users -->
          <div>
            <label class="block text-sm font-medium text-gray-700 mb-2">
              Assigned Users ({{ roleUsers().length }})
            </label>
            @if (loadingUsers()) {
              <div class="animate-pulse space-y-2">
                @for (i of [1, 2, 3]; track i) {
                  <div class="h-10 bg-gray-100 rounded"></div>
                }
              </div>
            } @else if (roleUsers().length === 0) {
              <p class="text-gray-500 text-center py-4 text-sm">No users assigned to this role yet</p>
            } @else {
              <div class="space-y-2 max-h-64 overflow-y-auto">
                @for (user of roleUsers(); track user.id) {
                  <div class="flex items-center justify-between p-3 bg-gray-50 rounded-lg">
                    <div>
                      <div class="font-medium text-gray-900 text-sm">{{ user.displayName || user.email }}</div>
                      <div class="text-xs text-gray-500">{{ user.email }}</div>
                    </div>
                    <button
                      (click)="onRemoveUserFromRole(user)"
                      class="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded"
                      title="Remove from role"
                    >
                      <ui-icon name="x-mark" class="w-4 h-4" />
                    </button>
                  </div>
                }
              </div>
            }
          </div>
        </div>

        <div modal-footer class="px-6 py-4 border-t bg-gray-50 flex justify-end">
          <button (click)="closeUsersModal()" class="btn-secondary">
            Close
          </button>
        </div>
      </ui-modal>
    </div>
  `,
})
export class ModuleRolesComponent {
  // ============ Inputs ============
  readonly config = input.required<ModuleRolesConfig>();
  readonly roles = input<ModuleRoleDto[]>([]);
  readonly permissions = input<ModulePermissionsResponse | null>(null);
  readonly loading = input<boolean>(false);
  readonly builtInRoles = input<string[]>([]);
  readonly roleUsers = input<ModuleRoleUserDto[]>([]);
  readonly loadingUsers = input<boolean>(false);
  readonly userSearchResults = input<ModuleRoleUserDto[]>([]);

  // ============ Outputs ============
  readonly createRole = output<CreateModuleRoleRequest>();
  readonly updateRole = output<{ id: string; data: Partial<ModuleRoleDto> }>();
  readonly deleteRole = output<string>();
  readonly seedDefaults = output<void>();
  readonly loadRoleUsers = output<string>();
  readonly assignUser = output<{ roleId: string; userId: string }>();
  readonly removeUser = output<{ roleId: string; userId: string }>();
  readonly searchUsers = output<string>();

  // ============ Internal State ============
  readonly isModalOpen = signal(false);
  readonly isCreateMode = signal(false);
  readonly editingRole = signal<ModuleRoleDto | null>(null);
  readonly modalError = signal<string | null>(null);
  readonly saving = signal(false);

  readonly isUsersModalOpen = signal(false);
  readonly selectedRole = signal<ModuleRoleDto | null>(null);

  readonly useWildcard = signal(false);

  // Form data (using object for two-way binding)
  formData: CreateModuleRoleRequest = {
    name: '',
    displayName: '',
    description: '',
    permissions: [],
  };

  selectedPermissions = new Set<string>();
  permissionSearchTerm = '';
  collapsedCategories = new Set<string>();

  userSearchTerm = '';
  private searchTimeout: ReturnType<typeof setTimeout> | null = null;

  // ============ Computed ============
  readonly defaultInfoBannerDescription = computed(() => {
    const cfg = this.config();
    return `As a ${cfg.moduleName} Administrator, you can create and manage ${cfg.moduleName}-specific roles. These roles can only be assigned ${cfg.moduleName} permissions (${cfg.wildcardPermission}). Sensitive permissions are highlighted.`;
  });

  private readonly permissionCategories = computed(() => {
    return this.permissions()?.categories ?? {};
  });

  private readonly sensitivePermissions = computed(() => {
    return this.permissions()?.sensitivePermissions ?? [];
  });

  // ============ Role Helpers ============
  isBuiltInRole(role: ModuleRoleDto): boolean {
    return role.isBuiltIn || this.builtInRoles().includes(role.name);
  }

  getSensitiveCount(permissions: string[]): number {
    return permissions.filter((p) => this.sensitivePermissions().includes(p)).length;
  }

  hasWildcard(permissions: string[]): boolean {
    return permissions.some((p) => p === this.config().wildcardPermission);
  }

  isSensitive(permission: string): boolean {
    return this.sensitivePermissions().includes(permission);
  }

  formatPermission(permission: string): string {
    const prefix = this.config().modulePrefix + '.';
    return permission.startsWith(prefix) ? permission.substring(prefix.length) : permission;
  }

  // ============ Permission Category Helpers ============
  getPermissionCategories(): { name: string; permissions: string[] }[] {
    return Object.entries(this.permissionCategories()).map(([name, permissions]) => ({
      name,
      permissions,
    }));
  }

  getFilteredPermissionCategories(): { name: string; permissions: string[] }[] {
    if (!this.permissionSearchTerm) {
      return this.getPermissionCategories();
    }
    const searchLower = this.permissionSearchTerm.toLowerCase();
    return this.getPermissionCategories()
      .map((category) => ({
        name: category.name,
        permissions: category.permissions.filter((p) =>
          this.formatPermission(p).toLowerCase().includes(searchLower)
        ),
      }))
      .filter((category) => category.permissions.length > 0);
  }

  getFilteredPermissions(permissions: string[]): string[] {
    if (!this.permissionSearchTerm) {
      return permissions;
    }
    const searchLower = this.permissionSearchTerm.toLowerCase();
    return permissions.filter((p) =>
      this.formatPermission(p).toLowerCase().includes(searchLower)
    );
  }

  // When wildcard is on, non-sensitive perms are covered automatically; the
  // category checkbox only governs the sensitive (RequiresExplicitGrant) ones,
  // which the wildcard cannot grant.
  private effectivePermissionsInCategory(permissions: string[]): string[] {
    if (!this.useWildcard()) return permissions;
    const sensitive = new Set(this.sensitivePermissions());
    return permissions.filter((p) => sensitive.has(p));
  }

  isPermissionLocked(permission: string): boolean {
    return this.useWildcard() && !this.isSensitive(permission);
  }

  isCategoryLocked(permissions: string[]): boolean {
    return this.useWildcard() && this.effectivePermissionsInCategory(permissions).length === 0;
  }

  isCategorySelected(permissions: string[]): boolean {
    const target = this.effectivePermissionsInCategory(permissions);
    if (target.length === 0) return false;
    return target.every((p) => this.selectedPermissions.has(p));
  }

  isCategoryPartiallySelected(permissions: string[]): boolean {
    const target = this.effectivePermissionsInCategory(permissions);
    if (target.length === 0) return false;
    const selectedCount = target.filter((p) => this.selectedPermissions.has(p)).length;
    return selectedCount > 0 && selectedCount < target.length;
  }

  getCategorySelectedCount(permissions: string[]): number {
    const target = this.effectivePermissionsInCategory(permissions);
    return target.filter((p) => this.selectedPermissions.has(p)).length;
  }

  toggleCategory(permissions: string[]): void {
    const target = this.effectivePermissionsInCategory(permissions);
    if (target.length === 0) return;
    const allSelected = target.every((p) => this.selectedPermissions.has(p));
    target.forEach((p) => {
      if (allSelected) {
        this.selectedPermissions.delete(p);
      } else {
        this.selectedPermissions.add(p);
      }
    });
  }

  togglePermission(permission: string): void {
    if (this.selectedPermissions.has(permission)) {
      this.selectedPermissions.delete(permission);
    } else {
      this.selectedPermissions.add(permission);
    }
  }

  toggleWildcard(): void {
    this.useWildcard.set(!this.useWildcard());
    if (this.useWildcard()) {
      // Wildcard covers non-sensitive perms; keep any selected sensitive
      // (RequiresExplicitGrant) ones — those still need literal grants.
      const sensitive = new Set(this.sensitivePermissions());
      this.selectedPermissions = new Set(
        Array.from(this.selectedPermissions).filter((p) => sensitive.has(p))
      );
    }
  }

  toggleCategoryCollapse(categoryName: string): void {
    if (this.collapsedCategories.has(categoryName)) {
      this.collapsedCategories.delete(categoryName);
    } else {
      this.collapsedCategories.add(categoryName);
    }
  }

  expandAllCategories(): void {
    this.collapsedCategories.clear();
  }

  collapseAllCategories(): void {
    const categories = this.getPermissionCategories();
    categories.forEach((cat) => this.collapsedCategories.add(cat.name));
  }

  // ============ Modal Actions ============
  openCreateModal(): void {
    this.isCreateMode.set(true);
    this.editingRole.set(null);
    this.formData = {
      name: '',
      displayName: '',
      description: '',
      permissions: [],
    };
    this.useWildcard.set(false);
    this.selectedPermissions.clear();
    this.permissionSearchTerm = '';
    this.collapsedCategories.clear();
    this.modalError.set(null);
    this.isModalOpen.set(true);
  }

  openEditModal(role: ModuleRoleDto): void {
    this.isCreateMode.set(false);
    this.editingRole.set(role);
    this.formData = {
      name: role.name,
      displayName: role.displayName,
      description: role.description || '',
      permissions: [],
    };
    this.useWildcard.set(this.hasWildcard(role.permissions));
    this.selectedPermissions = new Set(
      role.permissions.filter((p) => p !== this.config().wildcardPermission)
    );
    this.permissionSearchTerm = '';
    this.collapsedCategories.clear();
    this.modalError.set(null);
    this.isModalOpen.set(true);
  }

  closeModal(): void {
    this.isModalOpen.set(false);
    this.editingRole.set(null);
    this.permissionSearchTerm = '';
    this.collapsedCategories.clear();
  }

  saveRole(): void {
    this.saving.set(true);
    this.modalError.set(null);

    const permissions = this.useWildcard()
      ? [
          this.config().wildcardPermission,
          ...Array.from(this.selectedPermissions).filter((p) => this.isSensitive(p)),
        ]
      : Array.from(this.selectedPermissions);

    if (this.isCreateMode()) {
      this.createRole.emit({
        name: this.formData.name,
        displayName: this.formData.displayName,
        description: this.formData.description,
        permissions,
      });
    } else {
      const editing = this.editingRole();
      if (editing) {
        this.updateRole.emit({
          id: editing.id,
          data: {
            displayName: this.formData.displayName,
            description: this.formData.description,
            permissions,
          },
        });
      }
    }
  }

  /**
   * Called by parent after save completes successfully
   */
  onSaveSuccess(): void {
    this.saving.set(false);
    this.closeModal();
  }

  /**
   * Called by parent when save fails
   */
  onSaveError(message: string): void {
    this.saving.set(false);
    this.modalError.set(message);
  }

  onDeleteRole(role: ModuleRoleDto): void {
    if (!confirm(`Are you sure you want to delete the role "${role.displayName}"?`)) {
      return;
    }
    this.deleteRole.emit(role.id);
  }

  onSeedDefaults(): void {
    this.saving.set(true);
    this.seedDefaults.emit();
  }

  /**
   * Called by parent after seeding completes
   */
  onSeedComplete(): void {
    this.saving.set(false);
  }

  // ============ Users Modal Actions ============
  openUsersModal(role: ModuleRoleDto): void {
    this.selectedRole.set(role);
    this.isUsersModalOpen.set(true);
    this.loadRoleUsers.emit(role.id);
  }

  closeUsersModal(): void {
    this.isUsersModalOpen.set(false);
    this.selectedRole.set(null);
    this.userSearchTerm = '';
  }

  onUserSearchInput(): void {
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }

    if (!this.userSearchTerm || this.userSearchTerm.length < 2) {
      return;
    }

    this.searchTimeout = setTimeout(() => {
      this.searchUsers.emit(this.userSearchTerm);
    }, 300);
  }

  onAssignUserToRole(user: ModuleRoleUserDto): void {
    const role = this.selectedRole();
    if (!role || this.isUserAlreadyAssigned(user.id)) return;

    this.assignUser.emit({ roleId: role.id, userId: user.id });
    this.userSearchTerm = '';
  }

  onRemoveUserFromRole(user: ModuleRoleUserDto): void {
    const role = this.selectedRole();
    if (!role) return;

    this.removeUser.emit({ roleId: role.id, userId: user.id });
  }

  isUserAlreadyAssigned(userId: string): boolean {
    return this.roleUsers().some((u) => u.id === userId);
  }
}
