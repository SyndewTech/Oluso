import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import {
  BadgeComponent,
  BadgeVariant,
  ButtonComponent,
  CardComponent,
  ConfirmDialogComponent,
  IconComponent,
  LocaleDatePipe,
  ToastService,
  extractErrorMessage,
} from '@syndew/oluso-ui-core';
import { PlatformDomainService } from '../../services/platform-domain.service';
import { PlatformDomain } from '../../models/platform.model';

const STATUSES = ['Pending', 'Verified', 'Active', 'Failed', 'Removed'] as const;

@Component({
  selector: 'admin-platform-domains-page',
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
    LocaleDatePipe,
  ],
  templateUrl: './platform-domains-page.component.html',
})
export class PlatformDomainsPageComponent {
  private readonly domainService = inject(PlatformDomainService);
  private readonly toast = inject(ToastService);

  readonly statuses = STATUSES;
  readonly loading = signal(true);
  readonly domains = signal<PlatformDomain[]>([]);
  readonly search = signal('');
  readonly statusFilter = signal<string>('');

  /** Id of the domain an action is running on, so only its buttons wait. */
  readonly busyId = signal<string | null>(null);
  readonly removing = signal<PlatformDomain | null>(null);

  readonly counts = computed(() => {
    const counts: Partial<Record<string, number>> = {};
    for (const d of this.domains()) counts[d.status] = (counts[d.status] ?? 0) + 1;
    return counts;
  });

  readonly filtered = computed(() => {
    const term = this.search().trim().toLowerCase();
    const status = this.statusFilter();
    return this.domains().filter(
      (d) =>
        (!status || d.status === status) &&
        (!term ||
          d.domain.toLowerCase().includes(term) ||
          (d.tenantName ?? '').toLowerCase().includes(term) ||
          (d.tenantIdentifier ?? '').toLowerCase().includes(term))
    );
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.domainService.getAll().subscribe({
      next: (domains) => {
        this.domains.set(domains);
        this.loading.set(false);
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Could not load domains'));
        this.loading.set(false);
      },
    });
  }

  toggleStatus(status: string): void {
    this.statusFilter.update((current) => (current === status ? '' : status));
  }

  canVerify(d: PlatformDomain): boolean {
    return d.status === 'Pending' || d.status === 'Verified' || d.status === 'Failed';
  }

  verifyLabel(d: PlatformDomain): string {
    if (d.status === 'Failed') return 'Retry verification';
    if (d.status === 'Verified') return 'Retry provisioning';
    return 'Verify now';
  }

  verify(d: PlatformDomain): void {
    this.busyId.set(d.id);
    this.domainService.verify(d).subscribe({
      next: (result) => {
        if (result.status === 'Active') this.toast.success(`${d.domain} is active`);
        else if (result.status === 'Verified') this.toast.success(`${d.domain} is verified`);
        else this.toast.warning(result.failureReason || `${d.domain} isn't verified yet. Check its DNS records.`);
        this.busyId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Verification failed'));
        this.busyId.set(null);
      },
    });
  }

  restore(d: PlatformDomain): void {
    this.busyId.set(d.id);
    this.domainService.restore(d).subscribe({
      next: () => {
        this.toast.success(`${d.domain} is back with ${this.orgName(d)} and waiting for verification`);
        this.busyId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Could not restore the domain'));
        this.busyId.set(null);
      },
    });
  }

  confirmRemove(d: PlatformDomain): void {
    this.removing.set(d);
  }

  remove(): void {
    const d = this.removing();
    if (!d) return;
    this.busyId.set(d.id);
    this.domainService.remove(d).subscribe({
      next: () => {
        this.toast.success(`${d.domain} removed`);
        this.removing.set(null);
        this.busyId.set(null);
        this.load();
      },
      error: (err: unknown) => {
        this.toast.error(extractErrorMessage(err, 'Could not remove the domain'));
        this.busyId.set(null);
      },
    });
  }

  removeMessage(): string {
    const d = this.removing();
    if (!d) return '';
    const serving = d.isServing ? ' It stops serving their sign-in pages straight away.' : '';
    return `Remove ${d.domain} from ${this.orgName(d)}?${serving} The record is kept, so it can be restored or added again later.`;
  }

  orgName(d: PlatformDomain): string {
    return d.tenantName || d.tenantIdentifier || d.tenantId;
  }

  statusVariant(d: PlatformDomain): BadgeVariant {
    switch (d.status) {
      case 'Active':
        return 'success';
      case 'Verified':
        return d.failureReason ? 'danger' : 'info';
      case 'Pending':
        return 'warning';
      case 'Failed':
        return 'danger';
      default:
        return 'default';
    }
  }

  statusLabel(d: PlatformDomain): string {
    if (d.status === 'Verified' && d.failureReason) return 'Edge failed';
    return d.status;
  }

  /** Edge hostname state; only meaningful once ownership is proven. */
  edgeLabel(d: PlatformDomain): string | null {
    if (d.status !== 'Verified' && d.status !== 'Active') return null;
    return d.externalProvisioningId ? 'Provisioned' : 'Not provisioned';
  }
}
