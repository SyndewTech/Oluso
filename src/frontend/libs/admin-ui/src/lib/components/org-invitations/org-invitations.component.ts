import { Component, inject, input, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  DataTableComponent,
  IconComponent,
  ToastService,
  extractErrorMessage,
} from '@syndew/oluso-ui-core';
import { OrganizationService } from '../../services/organization.service';
import {
  OrganizationInvitation,
  OrganizationTenant,
  OrganizationRole,
  InvitationStatus,
} from '../../models/organization.model';

const ROLES: OrganizationRole[] = ['Member', 'Admin', 'Owner'];

@Component({
  selector: 'admin-org-invitations',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent],
  templateUrl: './org-invitations.component.html',
})
export class OrgInvitationsComponent implements OnInit {
  private readonly organizationService = inject(OrganizationService);
  private readonly toast = inject(ToastService);

  /** Organization ID — required input */
  readonly orgId = input.required<string>();

  readonly invitations = signal<OrganizationInvitation[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);

  /** Tenants of this org, so tenant access is picked rather than typed. */
  readonly tenants = signal<OrganizationTenant[]>([]);

  readonly statusFilter = signal<InvitationStatus | ''>('');
  readonly showInviteModal = signal(false);
  readonly showRevokeModal = signal(false);
  readonly selectedInvitation = signal<OrganizationInvitation | null>(null);

  readonly roles = ROLES;

  inviteFormData: {
    email: string;
    role: OrganizationRole;
    expirationDays: number;
    message: string;
    allTenants: boolean;
    allowedTenantIds: string[];
  } = this.emptyForm();

  ngOnInit(): void {
    this.loadInvitations();
    this.loadTenants();
  }

  private emptyForm() {
    return {
      email: '',
      role: 'Member' as OrganizationRole,
      expirationDays: 7,
      message: '',
      allTenants: true,
      allowedTenantIds: [] as string[],
    };
  }

  loadInvitations(): void {
    const id = this.orgId();
    if (!id) return;

    this.loading.set(true);
    const status = this.statusFilter();
    this.organizationService.getInvitations(id, status || undefined).subscribe({
      next: (invitations) => {
        this.invitations.set(invitations);
        this.loading.set(false);
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to load invitations'));
        this.loading.set(false);
      },
    });
  }

  private loadTenants(): void {
    const id = this.orgId();
    if (!id) return;

    this.organizationService.getTenants(id, { page: 1, pageSize: 100 }).subscribe({
      next: (result) => this.tenants.set(result.items),
      error: (error) => this.toast.error(extractErrorMessage(error, 'Failed to load tenants')),
    });
  }

  onStatusFilterChange(status: InvitationStatus | ''): void {
    this.statusFilter.set(status);
    this.loadInvitations();
  }

  openInviteModal(): void {
    this.inviteFormData = this.emptyForm();
    this.showInviteModal.set(true);
  }

  closeInviteModal(): void {
    this.showInviteModal.set(false);
  }

  toggleTenant(tenantId: string, checked: boolean): void {
    const current = this.inviteFormData.allowedTenantIds;
    this.inviteFormData.allowedTenantIds = checked
      ? [...current, tenantId]
      : current.filter((id) => id !== tenantId);
  }

  isTenantSelected(tenantId: string): boolean {
    return this.inviteFormData.allowedTenantIds.includes(tenantId);
  }

  sendInvitation(): void {
    const id = this.orgId();
    if (!id || !this.inviteFormData.email) return;

    // An empty list would grant nothing, which is never what "specific tenants"
    // means — make the operator pick at least one.
    if (!this.inviteFormData.allTenants && this.inviteFormData.allowedTenantIds.length === 0) {
      this.toast.error('Select at least one tenant, or grant access to all tenants.');
      return;
    }

    this.saving.set(true);
    this.organizationService.createInvitation(id, {
      email: this.inviteFormData.email.trim(),
      role: this.inviteFormData.role,
      expirationDays: this.inviteFormData.expirationDays,
      message: this.inviteFormData.message?.trim() || undefined,
      allowedTenantIds: this.inviteFormData.allTenants ? undefined : this.inviteFormData.allowedTenantIds,
    }).subscribe({
      next: () => {
        this.toast.success(`Invitation sent to ${this.inviteFormData.email}`);
        this.saving.set(false);
        this.closeInviteModal();
        this.loadInvitations();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to send invitation'));
        this.saving.set(false);
      },
    });
  }

  resend(invitation: OrganizationInvitation): void {
    const id = this.orgId();
    if (!id) return;

    this.organizationService.resendInvitation(id, invitation.id).subscribe({
      next: (result) => {
        // The endpoint reports whether the email actually went out — a resend
        // that silently sent nothing is worse than an error.
        if (result?.emailSent === false) {
          this.toast.warning(
            `Invitation for ${invitation.email} was extended, but the email could not be sent.`
          );
        } else {
          this.toast.success(`Invitation resent to ${invitation.email}`);
        }
        this.loadInvitations();
      },
      error: (error) => this.toast.error(extractErrorMessage(error, 'Failed to resend invitation')),
    });
  }

  openRevokeModal(invitation: OrganizationInvitation): void {
    this.selectedInvitation.set(invitation);
    this.showRevokeModal.set(true);
  }

  closeRevokeModal(): void {
    this.showRevokeModal.set(false);
    this.selectedInvitation.set(null);
  }

  confirmRevoke(): void {
    const id = this.orgId();
    const invitation = this.selectedInvitation();
    if (!id || !invitation) return;

    this.saving.set(true);
    this.organizationService.revokeInvitation(id, invitation.id).subscribe({
      next: () => {
        this.toast.success(`Invitation for ${invitation.email} revoked`);
        this.saving.set(false);
        this.closeRevokeModal();
        this.loadInvitations();
      },
      error: (error) => {
        this.toast.error(extractErrorMessage(error, 'Failed to revoke invitation'));
        this.saving.set(false);
      },
    });
  }

  statusVariant(status: InvitationStatus): 'success' | 'warning' | 'danger' | 'default' {
    switch (status) {
      case 'Accepted':
        return 'success';
      case 'Pending':
        return 'warning';
      case 'Expired':
      case 'Revoked':
      case 'Declined':
        return 'danger';
      default:
        return 'default';
    }
  }

  tenantNames(invitation: OrganizationInvitation): string {
    if (!invitation.allowedTenantIds || invitation.allowedTenantIds.length === 0) {
      return 'All tenants';
    }

    const known = this.tenants();
    return invitation.allowedTenantIds
      .map((id) => known.find((t) => t.id === id)?.name ?? id)
      .join(', ');
  }

  isPending(invitation: OrganizationInvitation): boolean {
    return invitation.status === 'Pending';
  }

  formatDate(dateString?: string): string {
    return dateString ? new Date(dateString).toLocaleDateString() : '—';
  }
}
