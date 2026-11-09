import { Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { map } from 'rxjs';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  BadgeVariant,
  IconComponent,
  LocaleDatePipe,
  SelectComponent,
  SelectOption,
  ToastService,
  extractErrorMessage,
  remoteOptions,
} from '@syndew/oluso-ui-core';
import { AuditLogService } from '../../services/audit-log.service';
import { ClientService } from '../../services/client.service';
import { UserService } from '../../services/user.service';
import {
  AuditLog,
  AuditLogFilter,
  AuditOriginKind,
  AUDIT_LOG_CATEGORIES,
  AUDIT_ORIGIN_KINDS,
} from '../../models/audit-log.model';
import { Client } from '../../models/client.model';
import { UserDto } from '../../models/user.model';

const USER_PAGE_SIZE = 50;

const ORIGIN_BADGES: Record<AuditOriginKind, BadgeVariant> = {
  admin: 'warning',
  service_client: 'info',
  scim: 'info',
  integration: 'info',
  journey: 'default',
  user: 'success',
  anonymous: 'default',
  system: 'default',
};

@Component({
  selector: 'admin-audit-logs-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent, LocaleDatePipe, SelectComponent],
  templateUrl: './audit-logs-page.component.html',
})
export class AuditLogsPageComponent {
  private readonly auditLogService = inject(AuditLogService);
  private readonly clientService = inject(ClientService);
  private readonly userService = inject(UserService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly exporting = signal(false);
  readonly logs = signal<AuditLog[]>([]);
  readonly totalCount = signal(0);
  readonly totalPages = signal(0);

  readonly categories = AUDIT_LOG_CATEGORIES;
  readonly originKinds = AUDIT_ORIGIN_KINDS;
  readonly expandedId = signal<number | null>(null);

  readonly clients = signal<Client[]>([]);
  readonly clientOptions = computed<SelectOption[]>(() =>
    this.clients().map((c) => ({
      value: c.clientId,
      label: c.clientName || c.clientId,
      subLabel: c.clientName ? c.clientId : undefined,
    }))
  );

  readonly subjectLookup = this.userLookup();
  readonly adminLookup = this.userLookup();

  filter: AuditLogFilter = {
    pageNumber: 1,
    pageSize: 50,
  };

  searchTerm = '';
  actionFilter = '';

  constructor() {
    this.loadLogs();
    this.loadClients();
    this.subjectLookup.load();
    this.adminLookup.load();
  }

  private userLookup() {
    return remoteOptions<UserDto>({
      search: (term) =>
        this.userService
          .getUsers({ page: 1, pageSize: USER_PAGE_SIZE }, term ? { search: term } : undefined)
          .pipe(map((page) => page.items)),
      resolve: (id) => this.userService.getUser(id),
      toOption: (u) => ({ value: u.id, label: this.userName(u), subLabel: u.email }),
      pageSize: USER_PAGE_SIZE,
    });
  }

  private userName(user: UserDto): string {
    const full = [user.firstName, user.lastName].filter(Boolean).join(' ');
    return user.displayName || full || user.email || user.userName || user.id;
  }

  private loadClients(): void {
    this.clientService.getClients().subscribe({
      next: (clients) => this.clients.set(clients),
      error: (error: unknown) => this.toast.error(extractErrorMessage(error, 'Failed to load clients')),
    });
  }

  loadLogs(): void {
    this.loading.set(true);
    this.auditLogService.getLogs(this.filter).subscribe({
      next: (response) => {
        this.logs.set(response.items);
        this.totalCount.set(response.totalCount);
        this.totalPages.set(response.totalPages);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.toast.error(extractErrorMessage(error, 'Failed to load activity logs'));
        this.loading.set(false);
      },
    });
  }

  private applyFilter(changes: Partial<AuditLogFilter>): void {
    this.filter = { ...this.filter, ...changes, pageNumber: 1 };
    this.loadLogs();
  }

  onSearch(): void {
    this.applyFilter({
      search: this.searchTerm || undefined,
      action: this.actionFilter || undefined,
    });
  }

  onCategoryChange(event: Event): void {
    this.applyFilter({ category: (event.target as HTMLSelectElement).value || undefined });
  }

  onOriginKindChange(event: Event): void {
    const value = (event.target as HTMLSelectElement).value as AuditOriginKind | '';
    this.applyFilter({ originKind: value || undefined, originId: undefined });
  }

  onOriginIdChange(originId: string): void {
    this.applyFilter({ originId: originId || undefined });
  }

  onSubjectChange(subjectId: string): void {
    this.applyFilter({ subjectId: subjectId || undefined });
  }

  onClientChange(clientId: string): void {
    this.applyFilter({ clientId: clientId || undefined });
  }

  onFromDateChange(event: Event): void {
    this.applyFilter({ from: (event.target as HTMLInputElement).value || undefined });
  }

  onToDateChange(event: Event): void {
    this.applyFilter({ to: (event.target as HTMLInputElement).value || undefined });
  }

  /** Narrows the list to everything the log's actor did. */
  filterByOrigin(log: AuditLog): void {
    if (!log.originKind || !log.originId) return;
    if (log.originKind === 'admin') this.adminLookup.ensure(log.originId);
    this.applyFilter({ originKind: log.originKind, originId: log.originId });
  }

  /** Whether the actor of this kind can be picked in the origin filter. */
  canFilterByActor(kind: AuditOriginKind | undefined): boolean {
    return kind === 'admin' || kind === 'service_client';
  }

  clearFilters(): void {
    this.filter = {
      pageNumber: 1,
      pageSize: 50,
    };
    this.searchTerm = '';
    this.actionFilter = '';
    this.loadLogs();
  }

  onRefresh(): void {
    this.loadLogs();
  }

  onExport(format: 'csv' | 'json'): void {
    this.exporting.set(true);
    this.auditLogService.export(this.filter, format).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `activity-logs-${new Date().toISOString().split('T')[0]}.${format}`;
        a.click();
        URL.revokeObjectURL(url);
        this.exporting.set(false);
      },
      error: (error: unknown) => {
        this.toast.error(extractErrorMessage(error, 'Export failed'));
        this.exporting.set(false);
      },
    });
  }

  onPreviousPage(): void {
    if (this.filter.pageNumber > 1) {
      this.filter = { ...this.filter, pageNumber: this.filter.pageNumber - 1 };
      this.loadLogs();
    }
  }

  onNextPage(): void {
    if (this.filter.pageNumber < this.totalPages()) {
      this.filter = { ...this.filter, pageNumber: this.filter.pageNumber + 1 };
      this.loadLogs();
    }
  }

  toggleDetails(log: AuditLog): void {
    this.expandedId.update((current) => (current === log.id ? null : log.id));
  }

  /** Details as indented JSON when they parse, otherwise as recorded. */
  prettyDetails(log: AuditLog): string | null {
    if (!log.details) return null;
    try {
      const parsed: unknown = JSON.parse(log.details);
      if (parsed && typeof parsed === 'object' && Object.keys(parsed as object).length === 0) return null;
      return JSON.stringify(parsed, null, 2);
    } catch {
      return log.details;
    }
  }

  originLabel(kind: AuditOriginKind | undefined): string {
    return AUDIT_ORIGIN_KINDS.find((k) => k.value === kind)?.label ?? kind ?? '';
  }

  originBadge(kind: AuditOriginKind | undefined): BadgeVariant {
    return kind ? ORIGIN_BADGES[kind] ?? 'default' : 'default';
  }

  /** The actor's display name, or for a service client its registered name when the log has none. */
  originActor(log: AuditLog): string | null {
    if (log.originName) return log.originName;
    if (log.originKind === 'service_client' && log.originId) {
      return this.clients().find((c) => c.clientId === log.originId)?.clientName || log.originId;
    }
    return log.originId ?? null;
  }

  getStartIndex(): number {
    return (this.filter.pageNumber - 1) * this.filter.pageSize + 1;
  }

  getEndIndex(): number {
    return Math.min(this.filter.pageNumber * this.filter.pageSize, this.totalCount());
  }
}
