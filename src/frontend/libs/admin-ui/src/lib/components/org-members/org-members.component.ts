import { Component, inject, input, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin, Observable } from 'rxjs';
import { ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent, ToastService, extractErrorMessage } from '@syndew/oluso-ui-core';
import { OrganizationService } from '../../services/organization.service';
import { UserService } from '../../services/user.service';
import { OrganizationMember, MemberTenantAccess, OrganizationUser } from '../../models/organization.model';
import { PaginationParams } from '../../models/pagination-params.model';

@Component({
  selector: 'admin-org-members',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent],
  templateUrl: './org-members.component.html',
})
export class OrgMembersComponent implements OnInit {
  private readonly organizationService = inject(OrganizationService);
  private readonly userService = inject(UserService);
  private readonly toast = inject(ToastService);

  /** Organization ID — required input */
  readonly orgId = input.required<string>();

  // Members state
  readonly members = signal<OrganizationMember[]>([]);
  readonly membersLoading = signal(false);
  readonly membersPage = signal(1);
  readonly membersTotalPages = signal(1);
  readonly membersTotalCount = signal(0);
  readonly membersPageSize = 20;
  membersSearchTerm = '';

  readonly saving = signal(false);

  // Modals
  readonly showAddMemberModal = signal(false);
  readonly showEditMemberModal = signal(false);
  readonly showRemoveMemberModal = signal(false);
  readonly showMemberTenantsModal = signal(false);
  readonly selectedMember = signal<OrganizationMember | null>(null);

  // Member tenant access state
  readonly memberTenantAccess = signal<MemberTenantAccess[]>([]);
  readonly memberTenantsLoading = signal(false);
  readonly memberTenantsSaving = signal(false);
  memberTenantToggles: Record<string, boolean> = {};

  // User search state (Add Member modal)
  readonly userSearchResults = signal<OrganizationUser[]>([]);
  readonly selectedUserForAdd = signal<OrganizationUser | null>(null);
  readonly userSearchMode = signal<'search' | 'manual'>('search');
  userSearchTerm = '';
  private searchTimeout: ReturnType<typeof setTimeout> | undefined;

  // Edit member state
  editMemberFormData = { role: 'Member' };

  memberFormData = { userId: '', role: 'Member' };

  ngOnInit(): void {
    this.loadMembers();
  }

  loadMembers(): void {
    const id = this.orgId();
    if (!id) return;
    this.membersLoading.set(true);
    const pagination: PaginationParams = {
      page: this.membersPage(),
      pageSize: this.membersPageSize,
    };
    const filters: Record<string, string> = {};
    if (this.membersSearchTerm) {
      filters['search'] = this.membersSearchTerm;
    }
    this.organizationService.getMembers(id, pagination, Object.keys(filters).length ? filters : undefined).subscribe({
      next: (response) => {
        this.members.set(response.items);
        this.membersTotalPages.set(response.totalPages);
        this.membersTotalCount.set(response.totalCount);
        this.membersLoading.set(false);
      },
      error: (error) => {
        console.error('Failed to load members:', error);
        this.membersLoading.set(false);
      },
    });
  }

  onMembersSearch(term: string): void {
    this.membersSearchTerm = term;
    this.membersPage.set(1);
    this.loadMembers();
  }

  onMembersPageChange(page: number): void {
    this.membersPage.set(page);
    this.loadMembers();
  }

  // User search for Add Member
  onUserSearch(term: string): void {
    this.userSearchTerm = term;
    clearTimeout(this.searchTimeout);
    if (!term || term.length < 2) {
      this.userSearchResults.set([]);
      return;
    }
    this.searchTimeout = setTimeout(() => {
      // Org-scoped, not tenant-scoped: the member being added may live in any
      // tenant of this organization.
      this.organizationService
        .searchOrganizationUsers(this.orgId(), { page: 1, pageSize: 8 }, term)
        .subscribe({
          next: (response) => this.userSearchResults.set(response.items),
          error: () => this.userSearchResults.set([]),
        });
    }, 300);
  }

  selectUserForAdd(user: OrganizationUser): void {
    this.selectedUserForAdd.set(user);
    this.memberFormData.userId = user.id;
    this.userSearchResults.set([]);
    this.userSearchTerm = '';
  }

  clearSelectedUser(): void {
    this.selectedUserForAdd.set(null);
    this.memberFormData.userId = '';
    this.userSearchTerm = '';
  }

  openAddMemberModal(): void {
    this.memberFormData = { userId: '', role: 'Member' };
    this.selectedUserForAdd.set(null);
    this.userSearchResults.set([]);
    this.userSearchTerm = '';
    this.userSearchMode.set('search');
    this.showAddMemberModal.set(true);
  }

  closeAddMemberModal(): void {
    this.showAddMemberModal.set(false);
  }

  addMember(): void {
    const id = this.orgId();
    if (!id || !this.memberFormData.userId) return;

    this.saving.set(true);
    this.organizationService.addMember(id, {
      userId: this.memberFormData.userId,
      role: this.memberFormData.role,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeAddMemberModal();
        this.loadMembers();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to add member'));
        this.saving.set(false);
      },
    });
  }

  openEditMemberModal(member: OrganizationMember): void {
    this.selectedMember.set(member);
    this.editMemberFormData = { role: member.role };
    this.showEditMemberModal.set(true);
  }

  closeEditMemberModal(): void {
    this.showEditMemberModal.set(false);
    this.selectedMember.set(null);
  }

  updateMember(): void {
    const id = this.orgId();
    const member = this.selectedMember();
    if (!id || !member) return;

    this.saving.set(true);
    this.organizationService.updateMember(id, member.id, {
      role: this.editMemberFormData.role,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeEditMemberModal();
        this.loadMembers();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to update member'));
        this.saving.set(false);
      },
    });
  }

  openRemoveMemberModal(member: OrganizationMember): void {
    this.selectedMember.set(member);
    this.showRemoveMemberModal.set(true);
  }

  closeRemoveMemberModal(): void {
    this.showRemoveMemberModal.set(false);
    this.selectedMember.set(null);
  }

  confirmRemoveMember(): void {
    const id = this.orgId();
    const member = this.selectedMember();
    if (!id || !member) return;

    this.organizationService.removeMember(id, member.id).subscribe({
      next: () => {
        this.closeRemoveMemberModal();
        this.loadMembers();
      },
      error: (error) => {
        console.error('Failed to remove member:', error);
      },
    });
  }

  // Member tenant access
  openMemberTenantsModal(member: OrganizationMember): void {
    this.selectedMember.set(member);
    this.showMemberTenantsModal.set(true);
    this.loadMemberTenants(member.userId);
  }

  closeMemberTenantsModal(): void {
    this.showMemberTenantsModal.set(false);
    this.selectedMember.set(null);
    this.memberTenantAccess.set([]);
    this.memberTenantToggles = {};
  }

  private loadMemberTenants(userId: string): void {
    const orgId = this.orgId();
    if (!orgId) return;

    this.memberTenantsLoading.set(true);
    this.organizationService.getMemberTenants(orgId, userId).subscribe({
      next: (access) => {
        this.memberTenantAccess.set(access);
        this.memberTenantToggles = {};
        for (const item of access) {
          this.memberTenantToggles[item.tenantId] = item.hasAccess;
        }
        this.memberTenantsLoading.set(false);
      },
      error: (error) => {
        console.error('Failed to load member tenants:', error);
        this.memberTenantsLoading.set(false);
      },
    });
  }

  onMemberTenantToggle(tenantId: string, checked: boolean): void {
    this.memberTenantToggles[tenantId] = checked;
  }

  saveMemberTenants(): void {
    const orgId = this.orgId();
    const member = this.selectedMember();
    if (!orgId || !member) return;

    // Only what changed since the modal opened is sent: one grant or revoke per
    // tenant, never a full list that could drop access by omission.
    const calls: Observable<void>[] = [];
    for (const item of this.memberTenantAccess()) {
      const wanted = !!this.memberTenantToggles[item.tenantId];
      if (wanted === item.hasAccess || item.isHomeTenant) continue;
      calls.push(wanted
        ? this.organizationService.grantMemberTenant(orgId, member.userId, item.tenantId)
        : this.organizationService.revokeMemberTenant(orgId, member.userId, item.tenantId));
    }

    if (calls.length === 0) {
      this.closeMemberTenantsModal();
      return;
    }

    this.memberTenantsSaving.set(true);
    forkJoin(calls).subscribe({
      next: () => {
        this.memberTenantsSaving.set(false);
        this.closeMemberTenantsModal();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to update tenant access'));
        this.memberTenantsSaving.set(false);
        this.loadMemberTenants(member.userId);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
