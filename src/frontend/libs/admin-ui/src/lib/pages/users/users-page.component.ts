import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CardComponent, ButtonComponent, BadgeComponent, ModalComponent, InputComponent, IconComponent, HasPermissionDirective, ToastService, extractErrorMessage } from '@oluso/ui-core';
import { AuthStore } from '@oluso/auth';
import { UserService } from '../../services/user.service';
import { RoleService } from '../../services/role.service';
import { OrganizationService } from '../../services/organization.service';
import { UserDto, CreateUserRequest, RoleDto } from '../../models/index';
import { PaginationParams } from '../../models/pagination-params.model';

interface UserFormData {
  email: string;
  userName: string;
  password: string;
  /** Create only: email an invite so the user sets their own password, instead of setting one here. */
  sendInvite: boolean;
  firstName: string;
  lastName: string;
  displayName: string;
  phoneNumber: string;
  isActive: boolean;
  emailConfirmed: boolean;
}

@Component({
  selector: 'admin-users-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, InputComponent, IconComponent, HasPermissionDirective],
  templateUrl: './users-page.component.html',
})
export class UsersPageComponent {
  private readonly userService = inject(UserService);
  private readonly toast = inject(ToastService);
  private readonly roleService = inject(RoleService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly authStore = inject(AuthStore);
  private readonly organizationService = inject(OrganizationService);

  readonly loading = signal(true);
  readonly users = signal<UserDto[]>([]);
  readonly currentPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly pageSize = 10;

  // Search and filters
  searchInput = '';
  roleFilter = '';
  statusFilter = '';

  // Modals
  readonly showCreateEditModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly showRolesModal = signal(false);
  readonly showPasswordModal = signal(false);
  readonly showPromoteModal = signal(false);

  // Form state
  readonly selectedUser = signal<UserDto | null>(null);
  readonly saving = signal(false);

  formData: UserFormData = {
    email: '',
    userName: '',
    password: '',
    sendInvite: true,
    firstName: '',
    lastName: '',
    displayName: '',
    phoneNumber: '',
    isActive: true,
    emailConfirmed: false,
  };

  newPassword = '';
  promoteRole = '';

  // Available roles loaded from API
  readonly availableRoles = signal<RoleDto[]>([]);

  selectedRoleIds: string[] = [];

  // Organization context for promote-to-org-member feature
  readonly orgContext = computed(() => this.authStore.currentOrganization());
  readonly isOrgAdmin = computed(() => {
    const org = this.orgContext();
    if (!org) return false;
    const role = org.role?.toLowerCase();
    return role === 'owner' || role === 'admin';
  });
  // Least privileged first: the modal preselects the first entry, and promoting
  // to Admin now also grants the OrgAdmin role, so Admin must be a deliberate
  // choice rather than what you get by clicking through.
  readonly promotableRoles = computed(() => {
    const org = this.orgContext();
    if (!org) return [];
    const role = org.role?.toLowerCase();
    if (role === 'owner') return ['Member', 'Admin'];
    if (role === 'admin') return ['Member'];
    return [];
  });

  constructor() {
    this.searchInput = this.route.snapshot.queryParamMap.get('search') ?? '';
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading.set(true);
    const pagination: PaginationParams = {
      page: this.currentPage(),
      pageSize: this.pageSize,
    };

    const term = this.searchInput.trim();
    this.userService.getUsers(pagination, term ? { search: term } : undefined).subscribe({
      next: (response) => {
        this.users.set(response.items);
        this.totalPages.set(response.totalPages);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load users:', error);
        this.loading.set(false);
      },
    });
  }

  onSearch(): void {
    this.currentPage.set(1);
    this.loadUsers();
  }

  onRowClick(user: UserDto): void {
    this.router.navigate(['/users', user.id]);
  }

  onAddUser(): void {
    this.selectedUser.set(null);
    this.formData = {
      email: '',
      userName: '',
      password: '',
      sendInvite: true,
      firstName: '',
      lastName: '',
      displayName: '',
      phoneNumber: '',
      isActive: true,
      emailConfirmed: false,
    };
    this.showCreateEditModal.set(true);
  }

  /** Profile editing belongs to the tenant that owns the record. */
  readonly editingExternalUser = computed(() => this.selectedUser()?.isExternalToTenant === true);

  onEditUser(user: UserDto, event: Event): void {
    event.stopPropagation();
    this.selectedUser.set(user);
    this.formData = {
      email: user.email,
      userName: user.userName || '',
      password: '',
      sendInvite: false,
      firstName: user.firstName || '',
      lastName: user.lastName || '',
      displayName: user.name || '',
      phoneNumber: user.phoneNumber || '',
      isActive: user.isActive,
      emailConfirmed: user.emailVerified,
    };
    this.showCreateEditModal.set(true);
  }

  onDeleteUser(user: UserDto, event: Event): void {
    event.stopPropagation();
    this.selectedUser.set(user);
    this.showDeleteModal.set(true);
  }

  onManageRoles(user: UserDto, event: Event): void {
    event.stopPropagation();
    this.selectedUser.set(user);
    this.selectedRoleIds = [];
    this.userService.getUserRoles(user.id).subscribe({
      next: (roles) => (this.selectedRoleIds = roles.map((r) => r.id)),
      error: (error) => console.error('Failed to load user roles:', error),
    });
    this.showRolesModal.set(true);
    // Load available roles if not already loaded
    if (this.availableRoles().length === 0) {
      this.roleService.getAllRoles().subscribe({
        next: (roles) => this.availableRoles.set(roles),
        error: (error) => console.error('Failed to load roles:', error),
      });
    }
  }

  onResetPassword(user: UserDto, event: Event): void {
    event.stopPropagation();
    this.selectedUser.set(user);
    this.newPassword = '';
    this.showPasswordModal.set(true);
  }

  onUnlockUser(user: UserDto, event: Event): void {
    event.stopPropagation();
    this.userService.unlockUser(user.id).subscribe({
      next: () => {
        this.loadUsers();
      },
      error: (error) => {
        console.error('Failed to unlock user:', error);
      },
    });
  }

  closeCreateEditModal(): void {
    this.showCreateEditModal.set(false);
    this.selectedUser.set(null);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.selectedUser.set(null);
  }

  closeRolesModal(): void {
    this.showRolesModal.set(false);
    this.selectedUser.set(null);
  }

  closePasswordModal(): void {
    this.showPasswordModal.set(false);
    this.selectedUser.set(null);
    this.newPassword = '';
  }

  onPromoteUser(user: UserDto, event: Event): void {
    event.stopPropagation();
    this.selectedUser.set(user);
    this.promoteRole = this.promotableRoles()[0] || '';
    this.showPromoteModal.set(true);
  }

  closePromoteModal(): void {
    this.showPromoteModal.set(false);
    this.selectedUser.set(null);
    this.promoteRole = '';
  }

  confirmPromote(): void {
    const user = this.selectedUser();
    const org = this.orgContext();
    if (!user || !org || !this.promoteRole) return;

    this.saving.set(true);
    this.organizationService
      .addMember(org.id, { userId: user.id, role: this.promoteRole })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.closePromoteModal();
          this.loadUsers();
        },
        error: (error) => {
          console.error('Failed to promote user to org member:', error);
          this.saving.set(false);
        },
      });
  }

  onSaveUser(): void {
    const user = this.selectedUser();
    this.saving.set(true);

    if (user) {
      // Update existing user
      const updated: UserDto = {
        ...user,
        firstName: this.formData.firstName || undefined,
        lastName: this.formData.lastName || undefined,
        phoneNumber: this.formData.phoneNumber || undefined,
        isActive: this.formData.isActive ?? user.isActive,
        emailVerified: this.formData.emailConfirmed ?? user.emailVerified,
      };

      this.userService.updateUser(user.id, updated).subscribe({
        next: () => {
          this.saving.set(false);
          this.closeCreateEditModal();
          this.loadUsers();
        },
        error: (error) => {
          console.error('Failed to update user:', error);
          this.saving.set(false);
        },
      });
    } else {
      // Create new user
      const createData: CreateUserRequest = {
        email: this.formData.email,
        username: this.formData.userName || undefined,
        password: this.formData.sendInvite ? undefined : this.formData.password,
        sendInvite: this.formData.sendInvite,
        firstName: this.formData.firstName || undefined,
        lastName: this.formData.lastName || undefined,
        phoneNumber: this.formData.phoneNumber || undefined,
        enabled: this.formData.isActive,
        emailVerified: this.formData.emailConfirmed,
      };

      this.userService.createUser(createData).subscribe({
        next: (created) => {
          this.saving.set(false);
          this.closeCreateEditModal();
          this.loadUsers();
          if (created.inviteError) {
            this.toast.error(`${created.email} was created, but the invite wasn't sent: ${created.inviteError} You can resend it from the users list.`);
          } else if (createData.sendInvite) {
            this.toast.success(`Invite sent to ${created.email}`);
          } else {
            this.toast.success(`${created.email} created`);
          }
        },
        error: (error) => {
          this.saving.set(false);
          this.toast.error(extractErrorMessage(error, 'The user could not be created.'));
        },
      });
    }
  }

  confirmDelete(): void {
    const user = this.selectedUser();
    if (!user) return;

    this.userService.deleteUser(user.id).subscribe({
      next: () => {
        this.closeDeleteModal();
        this.loadUsers();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to delete user'));
      },
    });
  }

  saveRoles(): void {
    const user = this.selectedUser();
    if (!user) return;

    this.saving.set(true);
    this.userService.assignRoles(user.id, this.selectedRoleIds).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeRolesModal();
        this.loadUsers();
      },
      error: (error) => {
        console.error('Failed to save roles:', error);
        this.saving.set(false);
      },
    });
  }

  onResendInvite(user: UserDto, event: Event): void {
    event.stopPropagation();
    this.userService.sendInvite(user.id).subscribe({
      next: () => this.toast.success(`Invite sent again to ${user.email}`),
      error: (error) => this.toast.error(extractErrorMessage(error, 'The invite could not be sent.')),
    });
  }

  resetPassword(): void {
    const user = this.selectedUser();
    if (!user || !this.newPassword) return;

    this.saving.set(true);
    this.userService.resetPassword(user.id, this.newPassword).subscribe({
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

  toggleRole(roleId: string): void {
    if (this.selectedRoleIds.includes(roleId)) {
      this.selectedRoleIds = this.selectedRoleIds.filter((id) => id !== roleId);
    } else {
      this.selectedRoleIds = [...this.selectedRoleIds, roleId];
    }
  }

  onPreviousPage(): void {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
      this.loadUsers();
    }
  }

  onNextPage(): void {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update((page) => page + 1);
      this.loadUsers();
    }
  }

  formatDate(dateString: string | undefined): string {
    if (!dateString) return 'Never';
    return new Date(dateString).toLocaleDateString();
  }

  getDisplayName(user: UserDto): string {
    return user.name || `${user.firstName || ''} ${user.lastName || ''}`.trim() || '-';
  }

  isUserLocked(user: UserDto): boolean {
    return !!user.lockoutEnd && new Date(user.lockoutEnd) > new Date();
  }
}
