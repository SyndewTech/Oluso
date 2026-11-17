import { Component, ElementRef, ViewChild, computed, inject, signal } from '@angular/core';
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
} from '@syndew/oluso-ui-core';
import { RoleService } from '../../services/role.service';
import { PermissionService } from '../../services/permission.service';
import { EntitlementService } from '../../services/entitlement.service';
import { RoleDto, CreateRoleRequest, RoleClaimDto, UserInRoleDto, RoleImportResult, RoleImportItem } from '../../models/role.model';
import { readFileText, saveBlob } from '../journeys/journey-file';
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

  /** Roles picked for export: this organization's own roles only. */
  readonly exportSelection = signal<Set<string>>(new Set());
  readonly exportableVisible = computed(() => this.visibleRoles().filter((r) => this.isExportable(r)));
  readonly allVisibleSelected = computed(() => {
    const selectable = this.exportableVisible();
    const selected = this.exportSelection();
    return selectable.length > 0 && selectable.every((r) => selected.has(r.id));
  });
  readonly exporting = signal(false);

  @ViewChild('importInput') importInput?: ElementRef<HTMLInputElement>;
  readonly showImportModal = signal(false);
  readonly importFileName = signal('');
  readonly importReplace = signal(false);
  readonly importPreview = signal<RoleImportResult | null>(null);
  readonly importBusy = signal(false);
  readonly importError = signal<string | null>(null);
  private importContent = '';

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

  isExportable(role: RoleDto): boolean {
    return !role.isGlobal && !role.isSystemRole;
  }

  toggleExport(role: RoleDto): void {
    this.exportSelection.update((current) => {
      const next = new Set(current);
      if (next.has(role.id)) next.delete(role.id);
      else next.add(role.id);
      return next;
    });
  }

  toggleAllExport(): void {
    const selectable = this.exportableVisible();
    this.exportSelection.update((current) => {
      const next = new Set(current);
      const select = !selectable.every((r) => next.has(r.id));
      for (const role of selectable) {
        if (select) next.add(role.id);
        else next.delete(role.id);
      }
      return next;
    });
  }

  exportSelected(): void {
    const ids = [...this.exportSelection()];
    if (ids.length === 0) return;
    this.exporting.set(true);
    this.roleService.exportRoles(ids).subscribe({
      next: (document) => {
        const single = ids.length === 1 ? this.roles().find((r) => r.id === ids[0]) : undefined;
        const fileName = single
          ? `role-${this.fileSafe(single.name)}.json`
          : `roles-${new Date().toISOString().slice(0, 10)}.json`;
        saveBlob(new Blob([JSON.stringify(document, null, 2)], { type: 'application/json' }), fileName);
        this.toast.success(ids.length === 1 ? 'Role exported' : `${ids.length} roles exported`);
        this.exporting.set(false);
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Could not export the roles'));
        this.exporting.set(false);
      },
    });
  }

  private fileSafe(name: string): string {
    return name.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '') || 'role';
  }

  openImport(): void {
    this.importContent = '';
    this.importFileName.set('');
    this.importReplace.set(false);
    this.importPreview.set(null);
    this.importError.set(null);
    this.showImportModal.set(true);
  }

  closeImport(): void {
    this.showImportModal.set(false);
  }

  chooseImportFile(): void {
    this.importInput?.nativeElement.click();
  }

  async onImportFileSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file) return;

    this.importFileName.set(file.name);
    this.importPreview.set(null);
    this.importError.set(null);
    try {
      this.importContent = await readFileText(file);
    } catch {
      this.importError.set('Could not read the file.');
      return;
    }
    this.refreshPreview();
  }

  setImportReplace(replace: boolean): void {
    this.importReplace.set(replace);
    if (this.importContent) this.refreshPreview();
  }

  private refreshPreview(): void {
    this.importBusy.set(true);
    this.roleService.previewImport(this.importContent, this.importReplace()).subscribe({
      next: (preview) => {
        this.importPreview.set(preview);
        this.importBusy.set(false);
      },
      error: (err: unknown) => {
        this.importError.set(extractErrorMessage(err, 'Could not read the file'));
        this.importBusy.set(false);
      },
    });
  }

  applyImport(): void {
    if (!this.importPreview()?.canImport) return;
    this.importBusy.set(true);
    this.importError.set(null);
    this.roleService.importRoles(this.importContent, this.importReplace()).subscribe({
      next: (result) => {
        this.importPreview.set(result);
        this.importBusy.set(false);
        const failed = result.roles.filter((r) => r.error).length;
        const done = result.roles.filter((r) => (r.action === 'create' || r.action === 'update') && !r.error).length;
        if (failed > 0) this.toast.warning(`${done} imported, ${failed} failed`);
        else this.toast.success(done === 1 ? '1 role imported' : `${done} roles imported`);
        this.loadRoles();
        this.loadEntitlements();
      },
      error: (err: unknown) => {
        this.importError.set(extractErrorMessage(err, 'Could not import the roles'));
        this.importBusy.set(false);
      },
    });
  }

  importActionLabel(item: RoleImportItem, applied: boolean): string {
    if (item.error) return 'Failed';
    switch (item.action) {
      case 'create':
        return applied ? 'Created' : 'Create';
      case 'update':
        return applied ? 'Updated' : 'Update';
      case 'skip':
        return 'Skip';
      default:
        return 'Can\'t import';
    }
  }

  importActionVariant(item: RoleImportItem): 'success' | 'info' | 'default' | 'danger' {
    if (item.error || item.action === 'invalid') return 'danger';
    if (item.action === 'create') return 'success';
    if (item.action === 'update') return 'info';
    return 'default';
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
