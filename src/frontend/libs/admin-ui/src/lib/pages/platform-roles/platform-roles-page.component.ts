import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import {
  BadgeComponent,
  ButtonComponent,
  CardComponent,
  ConfirmDialogComponent,
  IconComponent,
  LocaleDatePipe,
  ModalComponent,
  SelectComponent,
  ToastService,
  extractErrorMessage,
  remoteOptions,
} from '@syndew/oluso-ui-core';
import { PlatformRoleService } from '../../services/platform-role.service';
import {
  PlatformRole,
  PlatformRoleCandidate,
  PlatformRoleHolder,
  PlatformRoleTenantOption,
} from '../../models/platform.model';

const PICKER_PAGE_SIZE = 20;

@Component({
  selector: 'admin-platform-roles-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterModule,
    CardComponent,
    ButtonComponent,
    BadgeComponent,
    IconComponent,
    ConfirmDialogComponent,
    ModalComponent,
    SelectComponent,
    LocaleDatePipe,
  ],
  templateUrl: './platform-roles-page.component.html',
})
export class PlatformRolesPageComponent {
  private readonly roleService = inject(PlatformRoleService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly roles = signal<PlatformRole[]>([]);
  readonly selectedId = signal<string | null>(null);
  readonly selected = computed(() => this.roles().find((r) => r.id === this.selectedId()) ?? null);

  readonly holdersLoading = signal(false);
  readonly holders = signal<PlatformRoleHolder[]>([]);
  readonly holderSearch = signal('');
  readonly filteredHolders = computed(() => {
    const term = this.holderSearch().trim().toLowerCase();
    if (!term) return this.holders();
    return this.holders().filter((h) =>
      [h.displayName, h.email, h.userName, h.tenantName, h.homeTenantName].some((v) => (v ?? '').toLowerCase().includes(term))
    );
  });

  readonly removing = signal<PlatformRoleHolder | null>(null);
  readonly removeBusy = signal(false);

  readonly showAssign = signal(false);
  readonly assignTenantId = signal('');
  readonly assignUserId = signal('');
  readonly assignBusy = signal(false);
  readonly assignError = signal<string | null>(null);

  readonly tenantLookup = remoteOptions<PlatformRoleTenantOption>({
    search: (term) => this.roleService.searchTenants(term),
    toOption: (t) => ({ value: t.id, label: t.name, subLabel: t.identifier ?? undefined }),
    pageSize: PICKER_PAGE_SIZE,
  });

  readonly candidateLookup = remoteOptions<PlatformRoleCandidate>({
    search: (term) => this.roleService.searchCandidates(this.assignTenantId(), term),
    toOption: (u) => ({ value: u.id, label: u.displayName || u.email || u.userName, subLabel: u.email }),
    pageSize: PICKER_PAGE_SIZE,
  });

  constructor() {
    this.loadRoles();
  }

  loadRoles(): void {
    this.loading.set(true);
    this.roleService.getAll().subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.loading.set(false);
        const current = this.selectedId();
        const next = roles.find((r) => r.id === current) ?? roles[0];
        if (next) this.select(next, current === next.id);
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Could not load platform roles'));
        this.loading.set(false);
      },
    });
  }

  select(role: PlatformRole, keepSearch = false): void {
    this.selectedId.set(role.id);
    if (!keepSearch) this.holderSearch.set('');
    this.loadHolders(role.id);
  }

  private loadHolders(roleId: string): void {
    this.holdersLoading.set(true);
    this.roleService.getHolders(roleId).subscribe({
      next: (holders) => {
        if (this.selectedId() !== roleId) return;
        this.holders.set(holders);
        this.holdersLoading.set(false);
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Could not load who holds this role'));
        this.holdersLoading.set(false);
      },
    });
  }

  openAssign(): void {
    this.assignTenantId.set('');
    this.assignUserId.set('');
    this.assignError.set(null);
    this.tenantLookup.clearPinned();
    this.candidateLookup.clearPinned();
    this.tenantLookup.load();
    this.showAssign.set(true);
  }

  closeAssign(): void {
    this.showAssign.set(false);
  }

  onTenantChange(tenantId: string): void {
    this.assignTenantId.set(tenantId);
    this.tenantLookup.ensure(tenantId);
    this.assignUserId.set('');
    this.candidateLookup.clearPinned();
    if (tenantId) this.candidateLookup.load();
  }

  onUserChange(userId: string): void {
    this.assignUserId.set(userId);
    this.candidateLookup.ensure(userId);
  }

  assign(): void {
    const role = this.selected();
    if (!role) return;
    if (!this.assignTenantId() || !this.assignUserId()) {
      this.assignError.set('Choose an organization and a person.');
      return;
    }

    this.assignBusy.set(true);
    this.assignError.set(null);
    this.roleService.assign(role.id, this.assignUserId(), this.assignTenantId()).subscribe({
      next: () => {
        this.toast.success(`${role.displayName} assigned`);
        this.assignBusy.set(false);
        this.showAssign.set(false);
        this.loadRoles();
      },
      error: (err: unknown) => {
        this.assignError.set(extractErrorMessage(err, 'Could not assign the role'));
        this.assignBusy.set(false);
      },
    });
  }

  confirmRemove(holder: PlatformRoleHolder): void {
    this.removing.set(holder);
  }

  remove(): void {
    const role = this.selected();
    const holder = this.removing();
    if (!role || !holder) return;

    this.removeBusy.set(true);
    this.roleService.remove(role.id, holder).subscribe({
      next: () => {
        this.toast.success(`${role.displayName} removed from ${this.personName(holder)}`);
        this.removeBusy.set(false);
        this.removing.set(null);
        this.loadRoles();
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Could not remove the role'));
        this.removeBusy.set(false);
      },
    });
  }

  removeMessage(): string {
    const role = this.selected();
    const holder = this.removing();
    if (!role || !holder) return '';
    return role.appliesEverywhere
      ? `Remove ${role.displayName} from ${this.personName(holder)}? It applies in every organization, so they lose it everywhere.`
      : `Remove ${role.displayName} from ${this.personName(holder)} in ${holder.tenantName || holder.tenantId}? Their assignments in other organizations stay.`;
  }

  personName(h: PlatformRoleHolder): string {
    return h.displayName || h.email || h.userName;
  }

  assignedByLabel(h: PlatformRoleHolder): string | null {
    if (!h.assignedBy) return null;
    if (h.assignedBy === 'system') return 'the system';
    return h.assignedByName || h.assignedBy;
  }
}
