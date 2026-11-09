import { Component, inject, signal, computed, OnInit, Type, Injector } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  InputComponent,
  IconComponent,
  PluginService,
  TabDefinition as PluginTabDefinition,
  TabContext,
  TAB_CONTEXT,
  PageSlotHostComponent,
  DetailSectionHostComponent,
  ToastService,
  extractErrorMessage,
  HasPermissionDirective,
  ConfirmDialogComponent,
} from '@syndew/oluso-ui-core';
import { UserService } from '../../services/user.service';
import { RoleService } from '../../services/role.service';
import { UserDto, UserClaimDto, UserSessionDto, ExternalLoginDto, RoleDto } from '../../models/index';

/** Core tab configuration */
interface CoreTab {
  id: string;
  label: string;
  icon: string;
  order: number;
  isCore: true;
}

/** Plugin tab configuration */
interface PluginTab extends PluginTabDefinition {
  isCore: false;
}

type Tab = CoreTab | PluginTab;

/** Core tabs that are always available */
const CORE_TABS: CoreTab[] = [
  { id: 'profile', label: 'Profile', icon: 'user', order: 0, isCore: true },
  { id: 'security', label: 'Security', icon: 'shield', order: 10, isCore: true },
  { id: 'roles', label: 'Roles & Permissions', icon: 'key', order: 20, isCore: true },
  { id: 'sessions', label: 'Sessions', icon: 'clock', order: 30, isCore: true },
  { id: 'mfa', label: 'MFA', icon: 'fingerprint', order: 40, isCore: true },
  { id: 'claims', label: 'Claims', icon: 'document', order: 50, isCore: true },
  { id: 'custom-properties', label: 'Custom Properties', icon: 'collection', order: 55, isCore: true },
  { id: 'external-logins', label: 'External Logins', icon: 'link', order: 60, isCore: true },
];

/**
 * Custom-property keys written by built-in step handlers that admins
 * shouldn't edit by hand — these record consent / verification state
 * that the system relies on. Surfaced read-only in the "System
 * Properties" sub-section so they're visible but not clobberable.
 */
const SYSTEM_CUSTOM_PROPERTY_KEYS = new Set<string>([
  'accepted_terms_version',
  'accepted_terms_at',
  'accepted_privacy_version',
  'accepted_privacy_at',
]);

@Component({
  selector: 'admin-user-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    NgComponentOutlet,
    HasPermissionDirective,
    ConfirmDialogComponent,
    CardComponent,
    ButtonComponent,
    BadgeComponent,
    ModalComponent,
    InputComponent,
    IconComponent,
    PageSlotHostComponent,
    DetailSectionHostComponent,
  ],
  templateUrl: './user-detail-page.component.html',
})
export class UserDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly userService = inject(UserService);
  private readonly roleService = inject(RoleService);
  private readonly pluginService = inject(PluginService);
  readonly injector = inject(Injector);

  readonly activeTab = signal<string>('profile');

  /** Combined tabs: core + plugin tabs, sorted by order */
  readonly tabs = computed<Tab[]>(() => {
    const pluginTabs = this.pluginService.userDetailTabs().map(
      (tab): PluginTab => ({ ...tab, isCore: false })
    );
    return [...CORE_TABS, ...pluginTabs].sort((a, b) => (a.order ?? 100) - (b.order ?? 100));
  });

  /** Get the currently active tab */
  readonly activeTabConfig = computed(() => this.tabs().find((t) => t.id === this.activeTab()));

  /** Get the component for the active plugin tab */
  readonly activePluginComponent = computed<Type<unknown> | null>(() => {
    const tab = this.activeTabConfig();
    if (tab && !tab.isCore) {
      return (tab as PluginTab).component;
    }
    return null;
  });

  /** Context to pass to plugin tab components */
  readonly tabContext = computed<TabContext>(() => ({
    entityId: this.route.snapshot.paramMap.get('id') || '',
    entityType: 'user',
    data: {
      user: this.user(),
      roles: this.roles(),
      sessions: this.sessions(),
      externalLogins: this.externalLogins(),
    },
  }));

  /** Injector with TAB_CONTEXT for plugin components */
  readonly pluginInjector = computed(() =>
    Injector.create({
      providers: [{ provide: TAB_CONTEXT, useValue: this.tabContext() }],
      parent: this.injector,
    })
  );
  readonly loading = signal(true);
  readonly user = signal<UserDto | null>(null);

  // Roles data
  readonly roles = signal<RoleDto[]>([]);

  // Sessions data
  readonly sessions = signal<UserSessionDto[]>([]);
  readonly loadingSessions = signal(false);

  // External logins data
  readonly externalLogins = signal<ExternalLoginDto[]>([]);
  readonly loadingExternalLogins = signal(false);

  // Modals
  readonly showEditModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly showPasswordModal = signal(false);
  readonly showRolesModal = signal(false);
  readonly showClaimModal = signal(false);

  // Form state
  readonly saving = signal(false);

  editFormData: Partial<UserDto> = {};
  newPassword = '';
  readonly selectedRoleIds = signal<string[]>([]);
  readonly loadingRoleSelection = signal(false);
  newClaim = { type: '', value: '' };

  // Computed
  readonly isLocked = computed(() => {
    const u = this.user();
    return !!u?.lockoutEnd && new Date(u.lockoutEnd) > new Date();
  });

  readonly displayName = computed(() => {
    const u = this.user();
    if (!u) return '';
    return u.displayName || u.name || `${u.firstName || ''} ${u.lastName || ''}`.trim() || u.userName || u.email;
  });

  readonly initials = computed(() => {
    const u = this.user();
    if (!u) return '?';
    const name = this.displayName();
    return name.charAt(0).toUpperCase();
  });

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.loadUser(id);
      this.loadRoles();
    } else {
      this.router.navigate(['/users']);
    }
  }

  loadUser(id: string): void {
    this.loading.set(true);
    this.userService.getUser(id).subscribe({
      next: (user) => {
        this.user.set(user);
        this.resetEditForm();
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load user:', error);
        this.loading.set(false);
      },
    });
  }

  loadRoles(): void {
    this.roleService.getAllRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: (error) => console.error('Failed to load roles:', error),
    });
  }

  loadSessions(): void {
    const u = this.user();
    if (!u) return;

    this.loadingSessions.set(true);
    this.userService.getSessions(u.id).subscribe({
      next: (sessions) => {
        this.sessions.set(sessions);
        this.loadingSessions.set(false);
      },
      error: (error) => {
        console.error('Failed to load sessions:', error);
        this.loadingSessions.set(false);
      },
    });
  }

  loadExternalLogins(): void {
    const u = this.user();
    if (!u) return;

    this.loadingExternalLogins.set(true);
    this.userService.getExternalLogins(u.id).subscribe({
      next: (logins) => {
        this.externalLogins.set(logins);
        this.loadingExternalLogins.set(false);
      },
      error: (error) => {
        console.error('Failed to load external logins:', error);
        this.loadingExternalLogins.set(false);
      },
    });
  }

  setActiveTab(tabId: string): void {
    this.activeTab.set(tabId);

    // Load tab-specific data for core tabs
    if (tabId === 'sessions' && this.sessions().length === 0) {
      this.loadSessions();
    } else if (tabId === 'external-logins' && this.externalLogins().length === 0) {
      this.loadExternalLogins();
    }
  }

  onBack(): void {
    this.router.navigate(['/users']);
  }

  // Edit user
  openEditModal(): void {
    this.resetEditForm();
    this.showEditModal.set(true);
  }

  closeEditModal(): void {
    this.showEditModal.set(false);
  }

  resetEditForm(): void {
    const u = this.user();
    if (u) {
      this.editFormData = { ...u };
    }
  }

  saveUser(): void {
    const u = this.user();
    if (!u) return;

    this.saving.set(true);
    this.userService.updateUser(u.id, { ...u, ...this.editFormData } as UserDto).subscribe({
      next: (updated) => {
        this.user.set(updated);
        this.saving.set(false);
        this.closeEditModal();
      },
      error: (error) => {
        console.error('Failed to update user:', error);
        this.saving.set(false);
      },
    });
  }

  // Delete user
  openDeleteModal(): void {
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
  }

  confirmDelete(): void {
    const u = this.user();
    if (!u) return;

    this.userService.deleteUser(u.id).subscribe({
      next: () => {
        this.router.navigate(['/users']);
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to delete user'));
      },
    });
  }

  // Password reset
  openPasswordModal(): void {
    this.newPassword = '';
    this.showPasswordModal.set(true);
  }

  closePasswordModal(): void {
    this.showPasswordModal.set(false);
    this.newPassword = '';
  }

  resetPassword(): void {
    const u = this.user();
    if (!u || !this.newPassword) return;

    this.saving.set(true);
    this.userService.resetPassword(u.id, this.newPassword).subscribe({
      next: () => {
        this.saving.set(false);
        this.closePasswordModal();
      },
      error: (error) => {
        console.error('Failed to reset password:', error);
        this.saving.set(false);
      },
    });
  }

  // Unlock user
  readonly showLockConfirm = signal(false);
  readonly locking = signal(false);

  /** Locks the account until an admin unlocks it; the server also ends its sessions. */
  lockUser(): void {
    const u = this.user();
    if (!u) return;

    this.locking.set(true);
    this.userService.lockUser(u.id).subscribe({
      next: (updated) => {
        this.user.set(updated);
        this.locking.set(false);
        this.showLockConfirm.set(false);
        this.toast.success('Account locked and signed out');
      },
      error: (error) => {
        this.locking.set(false);
        this.showLockConfirm.set(false);
        this.toast.error(extractErrorMessage(error, 'Failed to lock the account'));
      },
    });
  }

  unlockUser(): void {
    const u = this.user();
    if (!u) return;

    this.userService.unlockUser(u.id).subscribe({
      next: (updated) => {
        this.user.set(updated);
        this.toast.success('Account unlocked');
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to unlock the account'));
      },
    });
  }

  // Roles management
  openRolesModal(): void {
    const u = this.user();
    if (!u) return;
    this.selectedRoleIds.set([]);
    this.loadingRoleSelection.set(true);
    this.showRolesModal.set(true);
    this.userService.getUserRoles(u.id).subscribe({
      next: (roles) => {
        this.selectedRoleIds.set(roles.map((r) => r.id));
        this.loadingRoleSelection.set(false);
      },
      error: (error) => {
        console.error('Failed to load user roles:', error);
        this.loadingRoleSelection.set(false);
        this.showRolesModal.set(false);
        this.toast.error(extractErrorMessage(error, 'Failed to load the roles this user holds'));
      },
    });
  }

  closeRolesModal(): void {
    this.showRolesModal.set(false);
  }

  toggleRole(roleId: string): void {
    this.selectedRoleIds.update((ids) =>
      ids.includes(roleId) ? ids.filter((id) => id !== roleId) : [...ids, roleId]
    );
  }

  saveRoles(): void {
    const u = this.user();
    if (!u) return;

    this.saving.set(true);
    this.userService.assignRoles(u.id, this.selectedRoleIds()).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeRolesModal();
        this.loadUser(u.id);
      },
      error: (error) => {
        console.error('Failed to save roles:', error);
        this.saving.set(false);
      },
    });
  }

  // Claims management
  openClaimModal(): void {
    this.newClaim = { type: '', value: '' };
    this.showClaimModal.set(true);
  }

  closeClaimModal(): void {
    this.showClaimModal.set(false);
  }

  addClaim(): void {
    const u = this.user();
    if (!u || !this.newClaim.type || !this.newClaim.value) return;

    this.saving.set(true);
    this.userService.addClaim(u.id, this.newClaim).subscribe({
      next: () => {
        this.loadUser(u.id);
        this.saving.set(false);
        this.closeClaimModal();
      },
      error: (error) => {
        console.error('Failed to add claim:', error);
        this.saving.set(false);
      },
    });
  }

  deleteClaim(claim: UserClaimDto): void {
    const u = this.user();
    if (!u) return;

    this.userService.deleteClaim(u.id, claim.type, claim.value).subscribe({
      next: () => {
        this.loadUser(u.id);
      },
      error: (error) => {
        console.error('Failed to delete claim:', error);
      },
    });
  }

  // Sessions management
  revokeSession(session: UserSessionDto): void {
    const u = this.user();
    if (!u) return;

    this.userService.revokeSession(u.id, session.sessionId).subscribe({
      next: () => {
        this.loadSessions();
      },
      error: (error) => {
        console.error('Failed to revoke session:', error);
      },
    });
  }

  revokeAllSessions(): void {
    const u = this.user();
    if (!u) return;

    this.userService.revokeAllSessions(u.id).subscribe({
      next: () => {
        this.loadSessions();
      },
      error: (error) => {
        console.error('Failed to revoke all sessions:', error);
      },
    });
  }

  // External logins management
  removeExternalLogin(login: ExternalLoginDto): void {
    const u = this.user();
    if (!u) return;

    this.userService.removeExternalLogin(u.id, login.loginProvider, login.providerKey).subscribe({
      next: () => {
        this.loadExternalLogins();
      },
      error: (error) => {
        console.error('Failed to remove external login:', error);
      },
    });
  }

  // MFA management
  resetMfa(): void {
    const u = this.user();
    if (!u) return;

    this.userService.resetMfa(u.id).subscribe({
      next: () => {
        this.loadUser(u.id);
      },
      error: (error) => {
        console.error('Failed to reset MFA:', error);
      },
    });
  }

  // Utility methods
  formatDate(dateString: string | undefined): string {
    if (!dateString) return 'Never';
    return new Date(dateString).toLocaleString();
  }

  formatUserAgent(userAgent?: string): string {
    if (!userAgent) return 'Unknown';
    if (userAgent.includes('Chrome')) return 'Chrome';
    if (userAgent.includes('Firefox')) return 'Firefox';
    if (userAgent.includes('Safari')) return 'Safari';
    if (userAgent.includes('Edge')) return 'Edge';
    return userAgent.length > 30 ? userAgent.substring(0, 30) + '...' : userAgent;
  }

  truncateKey(key: string): string {
    return key.length > 20 ? key.substring(0, 20) + '...' : key;
  }

  // ────────── Custom Properties ──────────

  /** Editable custom-property rows the admin can mutate. */
  readonly userCustomProperties = computed<CustomPropertyRow[]>(() => {
    const props = this.user()?.customProperties ?? {};
    return Object.entries(props)
      .filter(([key]) => !SYSTEM_CUSTOM_PROPERTY_KEYS.has(key))
      .map(([key, value]) => ({ key, value }))
      .sort((a, b) => a.key.localeCompare(b.key));
  });

  /** System-managed keys, surfaced read-only. */
  readonly systemCustomProperties = computed<CustomPropertyRow[]>(() => {
    const props = this.user()?.customProperties ?? {};
    return Object.entries(props)
      .filter(([key]) => SYSTEM_CUSTOM_PROPERTY_KEYS.has(key))
      .map(([key, value]) => ({ key, value }))
      .sort((a, b) => a.key.localeCompare(b.key));
  });

  readonly newPropertyKey = signal<string>('');
  readonly newPropertyValue = signal<string>('');
  readonly customPropertyError = signal<string | null>(null);
  readonly customPropertySaving = signal<boolean>(false);

  addCustomProperty(): void {
    const key = this.newPropertyKey().trim();
    const value = this.newPropertyValue();
    if (!key) {
      this.customPropertyError.set('Key is required');
      return;
    }
    if (SYSTEM_CUSTOM_PROPERTY_KEYS.has(key)) {
      this.customPropertyError.set(`'${key}' is a system-managed key and cannot be set manually`);
      return;
    }
    const u = this.user();
    if (!u) return;
    const next = { ...(u.customProperties ?? {}), [key]: value };
    this.persistCustomProperties(u, next, () => {
      this.newPropertyKey.set('');
      this.newPropertyValue.set('');
    });
  }

  updateCustomProperty(key: string, value: string): void {
    const u = this.user();
    if (!u || SYSTEM_CUSTOM_PROPERTY_KEYS.has(key)) return;
    const next = { ...(u.customProperties ?? {}), [key]: value };
    this.persistCustomProperties(u, next);
  }

  removeCustomProperty(key: string): void {
    const u = this.user();
    if (!u || SYSTEM_CUSTOM_PROPERTY_KEYS.has(key)) return;
    const next = { ...(u.customProperties ?? {}) };
    delete next[key];
    this.persistCustomProperties(u, next);
  }

  private persistCustomProperties(
    user: UserDto,
    next: Record<string, string>,
    onSuccess?: () => void,
  ): void {
    this.customPropertyError.set(null);
    this.customPropertySaving.set(true);
    const payload: UserDto = { ...user, customProperties: next };
    this.userService.updateUser(user.id, payload).subscribe({
      next: (updated) => {
        this.user.set(updated);
        this.customPropertySaving.set(false);
        onSuccess?.();
      },
      error: (err) => {
        this.customPropertySaving.set(false);
        this.customPropertyError.set(err?.error?.message ?? err?.message ?? 'Failed to save');
      },
    });
  }
}

interface CustomPropertyRow {
  key: string;
  value: string;
}
