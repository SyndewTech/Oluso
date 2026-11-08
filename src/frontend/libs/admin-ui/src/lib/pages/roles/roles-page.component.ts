import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  IconComponent,
  extractErrorMessage,
  InputComponent,
  ToastService,
} from '@oluso/ui-core';
import { RoleService } from '../../services/role.service';
import { PermissionService } from '../../services/permission.service';
import { EntitlementService } from '../../services/entitlement.service';
import { RoleDto, CreateRoleRequest, RoleClaimDto, UserInRoleDto } from '../../models/role.model';
import { PermissionCategory, PermissionsResponse } from '../../models/permission.model';
import { EntitlementDto } from '../../models/entitlement.model';

interface RoleFormData {
  name: string;
  displayName: string;
  description: string;
  permissions: string[];
  entitlements: string[];
  claims: RoleClaimDto[];
}

@Component({
  selector: 'admin-roles-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent, InputComponent],
  templateUrl: './roles-page.component.html',
})
export class RolesPageComponent {
  private readonly roleService = inject(RoleService);
  private readonly route = inject(ActivatedRoute);
  private readonly permissionService = inject(PermissionService);
  private readonly entitlementService = inject(EntitlementService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly roles = signal<RoleDto[]>([]);
  readonly searchTerm = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  readonly visibleRoles = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.roles();
    return this.roles().filter(x => (x.name ?? '').toLowerCase().includes(term) || (x.displayName ?? '').toLowerCase().includes(term) || (x.description ?? '').toLowerCase().includes(term));
  });
  readonly permissionsData = signal<PermissionsResponse | null>(null);
  readonly loadingPermissions = signal(false);
  readonly entitlements = signal<EntitlementDto[]>([]);
  readonly usersInRole = signal<UserInRoleDto[]>([]);
  readonly loadingUsers = signal(false);

  readonly showCreateEditModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly deleteError = signal<string | null>(null);
  readonly showUsersModal = signal(false);

  readonly selectedRole = signal<RoleDto | null>(null);
  readonly expandedCategories = signal<Set<string>>(new Set());

  formData: RoleFormData = {
    name: '',
    displayName: '',
    description: '',
    permissions: [],
    entitlements: [],
    claims: [],
  };

  newClaimType = '';
  newClaimValue = '';
  permissionSearchTerm = '';

  constructor() {
    this.loadRoles();
    this.loadPermissions();
    this.loadEntitlements();
  }

  loadEntitlements(): void {
    this.entitlementService.getAll().subscribe({
      next: (entitlements) => this.entitlements.set(entitlements),
      error: (error) => console.error('Failed to load entitlements:', error),
    });
  }

  loadRoles(): void {
    this.loading.set(true);
    this.roleService.getAllRoles().subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load roles:', error);
        this.loading.set(false);
      },
    });
  }

  loadPermissions(): void {
    this.loadingPermissions.set(true);
    this.permissionService.getAll().subscribe({
      next: (data) => {
        this.permissionsData.set(data);
        this.loadingPermissions.set(false);
      },
      error: (error) => {
        console.error('Failed to load permissions:', error);
        this.loadingPermissions.set(false);
      },
    });
  }

  openCreateModal(): void {
    this.selectedRole.set(null);
    this.formData = {
      name: '',
      displayName: '',
      description: '',
      permissions: [],
      entitlements: [],
      claims: [],
    };
    this.permissionSearchTerm = '';
    // Expand all categories by default when creating
    const categories = this.permissionsData()?.categories;
    if (categories) {
      this.expandedCategories.set(new Set(categories.map((c) => c.name)));
    }
    this.showCreateEditModal.set(true);
  }

  openEditModal(role: RoleDto): void {
    this.selectedRole.set(role);
    this.formData = {
      name: role.name,
      displayName: role.displayName || '',
      description: role.description || '',
      permissions: [...(role.permissions || [])],
      entitlements: [...(role.entitlements || [])],
      claims: [...(role.claims || [])],
    };
    this.permissionSearchTerm = '';
    // Expand categories that have selected permissions
    const categories = this.permissionsData()?.categories;
    if (categories) {
      const categoriesWithSelectedPerms = new Set(
        categories.filter((cat) => cat.permissions.some((p) => role.permissions?.includes(p.name))).map((c) => c.name)
      );
      this.expandedCategories.set(
        categoriesWithSelectedPerms.size > 0 ? categoriesWithSelectedPerms : new Set(categories.map((c) => c.name))
      );
    }
    this.showCreateEditModal.set(true);
  }

  openDeleteModal(role: RoleDto): void {
    this.selectedRole.set(role);
    this.deleteError.set(null);
    this.showDeleteModal.set(true);
  }

  openUsersModal(role: RoleDto): void {
    this.selectedRole.set(role);
    this.loadUsersInRole(role.id);
    this.showUsersModal.set(true);
  }

  loadUsersInRole(roleId: string): void {
    this.loadingUsers.set(true);
    this.roleService.getUsersInRole(roleId).subscribe({
      next: (users) => {
        this.usersInRole.set(users);
        this.loadingUsers.set(false);
      },
      error: (error) => {
        console.error('Failed to load users in role:', error);
        this.loadingUsers.set(false);
      },
    });
  }

  closeCreateEditModal(): void {
    this.showCreateEditModal.set(false);
    this.selectedRole.set(null);
    this.newClaimType = '';
    this.newClaimValue = '';
    this.permissionSearchTerm = '';
    this.expandedCategories.set(new Set());
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.deleteError.set(null);
    this.selectedRole.set(null);
  }

  closeUsersModal(): void {
    this.showUsersModal.set(false);
    this.selectedRole.set(null);
    this.usersInRole.set([]);
  }

  onSave(): void {
    const role = this.selectedRole();
    this.saving.set(true);

    if (role) {
      const updated: RoleDto = {
        ...role,
        name: this.formData.name,
        displayName: this.formData.displayName || undefined,
        description: this.formData.description || undefined,
        permissions: this.formData.permissions,
        entitlements: this.formData.entitlements,
        claims: this.formData.claims,
      };
      this.roleService.updateRole(role.id, updated).subscribe({
        next: () => {
          this.saving.set(false);
          this.closeCreateEditModal();
          this.loadRoles();
        },
        error: (error) => {
          console.error('Failed to update role:', error);
          this.saving.set(false);
        },
      });
    } else {
      const request: CreateRoleRequest = {
        name: this.formData.name,
        displayName: this.formData.displayName || undefined,
        description: this.formData.description || undefined,
        permissions: this.formData.permissions,
        entitlements: this.formData.entitlements,
        claims: this.formData.claims,
      };
      this.roleService.createRole(request).subscribe({
        next: () => {
          this.saving.set(false);
          this.closeCreateEditModal();
          this.loadRoles();
        },
        error: (error) => {
          console.error('Failed to create role:', error);
          this.saving.set(false);
        },
      });
    }
  }

  confirmDelete(): void {
    const role = this.selectedRole();
    if (!role) return;

    this.deleteError.set(null);
    this.roleService.deleteRole(role.id).subscribe({
      next: () => {
        this.closeDeleteModal();
        this.loadRoles();
      },
      error: (error) => {
        this.deleteError.set(extractErrorMessage(error, 'Failed to delete role'));
        console.error('Failed to delete role:', error);
      },
    });
  }

  togglePermission(permission: string): void {
    if (this.formData.permissions.includes(permission)) {
      this.formData.permissions = this.formData.permissions.filter((p) => p !== permission);
    } else {
      this.formData.permissions = [...this.formData.permissions, permission];
    }
  }

  isEntitlementSelected(name: string): boolean {
    return this.formData.entitlements.includes(name);
  }

  toggleEntitlement(name: string): void {
    if (this.formData.entitlements.includes(name)) {
      this.formData.entitlements = this.formData.entitlements.filter((e) => e !== name);
    } else {
      this.formData.entitlements = [...this.formData.entitlements, name];
    }
  }

  /** Catalog grouped for display; entries with no group fall under "General". */
  entitlementGroups(): { name: string; entitlements: EntitlementDto[] }[] {
    const groups: { name: string; entitlements: EntitlementDto[] }[] = [];
    for (const entitlement of this.entitlements()) {
      const groupName = entitlement.group || 'General';
      const group = groups.find((g) => g.name === groupName);
      if (group) {
        group.entitlements.push(entitlement);
      } else {
        groups.push({ name: groupName, entitlements: [entitlement] });
      }
    }
    return groups;
  }

  toggleCategory(categoryName: string): void {
    const current = this.expandedCategories();
    const next = new Set(current);
    if (next.has(categoryName)) {
      next.delete(categoryName);
    } else {
      next.add(categoryName);
    }
    this.expandedCategories.set(next);
  }

  toggleAllInCategory(category: PermissionCategory): void {
    const categoryPermissions = category.permissions.map((p) => p.name);
    const allSelected = categoryPermissions.every((p) => this.formData.permissions.includes(p));

    if (allSelected) {
      this.formData.permissions = this.formData.permissions.filter((p) => !categoryPermissions.includes(p));
    } else {
      this.formData.permissions = [...new Set([...this.formData.permissions, ...categoryPermissions])];
    }
  }

  isCategoryExpanded(categoryName: string): boolean {
    return this.expandedCategories().has(categoryName);
  }

  getSelectedCountInCategory(category: PermissionCategory): number {
    return category.permissions.filter((p) => this.formData.permissions.includes(p.name)).length;
  }

  isAllInCategorySelected(category: PermissionCategory): boolean {
    return category.permissions.every((p) => this.formData.permissions.includes(p.name));
  }

  isSomeInCategorySelected(category: PermissionCategory): boolean {
    const count = this.getSelectedCountInCategory(category);
    return count > 0 && count < category.permissions.length;
  }

  expandAllCategories(): void {
    const categories = this.permissionsData()?.categories;
    if (categories) {
      this.expandedCategories.set(new Set(categories.map((c) => c.name)));
    }
  }

  collapseAllCategories(): void {
    this.expandedCategories.set(new Set());
  }

  getFilteredCategories(): PermissionCategory[] {
    const categories = this.permissionsData()?.categories || [];
    if (!this.permissionSearchTerm) {
      return categories;
    }
    const searchLower = this.permissionSearchTerm.toLowerCase();
    return categories
      .map((category) => ({
        ...category,
        permissions: category.permissions.filter(
          (p) =>
            p.name.toLowerCase().includes(searchLower) ||
            p.displayName.toLowerCase().includes(searchLower) ||
            (p.description && p.description.toLowerCase().includes(searchLower))
        ),
      }))
      .filter((category) => category.permissions.length > 0);
  }

  addClaim(): void {
    if (this.newClaimType && this.newClaimValue) {
      this.formData.claims = [...this.formData.claims, { type: this.newClaimType, value: this.newClaimValue }];
      this.newClaimType = '';
      this.newClaimValue = '';
    }
  }

  removeClaim(index: number): void {
    this.formData.claims = this.formData.claims.filter((_, i) => i !== index);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }

  getRoleTypeLabel(role: RoleDto): string {
    if (role.isSystemRole) return 'System';
    if (role.isGlobal) return 'Global';
    return 'Tenant';
  }

  getRoleTypeVariant(role: RoleDto): 'default' | 'success' | 'warning' | 'danger' | 'info' {
    if (role.isSystemRole) return 'info';
    if (role.isGlobal) return 'warning';
    return 'default';
  }

  /** Other systems (journey steps, service clients) refer to a role by this id, never its name. */
  copyRoleId(id: string): void {
    navigator.clipboard.writeText(id).then(
      () => this.toast.success('Role ID copied'),
      () => this.toast.error('Couldn\'t copy the role ID'),
    );
  }
}
