import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardComponent, ButtonComponent, IconComponent } from '@oluso/ui-core';
import { GrantService } from '../../services/grant.service';
import { PersistedGrant, ServerSideSession, GrantFilter } from '../../models/grant.model';

type TabType = 'grants' | 'sessions';

@Component({
  selector: 'admin-grants-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, IconComponent],
  templateUrl: './grants-page.component.html',
})
export class GrantsPageComponent {
  private readonly grantService = inject(GrantService);

  readonly activeTab = signal<TabType>('grants');
  readonly loading = signal(false);

  readonly grants = signal<PersistedGrant[]>([]);
  readonly sessions = signal<ServerSideSession[]>([]);

  readonly grantsPage = signal(1);
  readonly grantsTotalCount = signal(0);
  readonly sessionsPage = signal(1);
  readonly sessionsTotalCount = signal(0);

  readonly pageSize = 10;

  readonly revokingKey = signal<string | null>(null);
  readonly terminatingKey = signal<string | null>(null);
  readonly showExpired = signal(false);

  constructor() {
    this.loadGrants();
  }

  setActiveTab(tab: TabType): void {
    this.activeTab.set(tab);
    if (tab === 'grants' && this.grants().length === 0) {
      this.loadGrants();
    } else if (tab === 'sessions' && this.sessions().length === 0) {
      this.loadSessions();
    }
  }

  toggleShowExpired(): void {
    this.showExpired.update((v) => !v);
    this.grantsPage.set(1);
    this.loadGrants();
  }

  loadGrants(): void {
    this.loading.set(true);
    const filter: GrantFilter = { includeExpired: this.showExpired() };
    this.grantService.getPersistedGrants(filter, this.grantsPage(), this.pageSize).subscribe({
      next: (response) => {
        this.grants.set(response.items);
        this.grantsTotalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load grants:', error);
        this.loading.set(false);
      },
    });
  }

  loadSessions(): void {
    this.loading.set(true);
    this.grantService.getSessions(undefined, this.sessionsPage(), this.pageSize).subscribe({
      next: (response) => {
        this.sessions.set(response.items);
        this.sessionsTotalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load sessions:', error);
        this.loading.set(false);
      },
    });
  }

  revokeGrant(grant: PersistedGrant): void {
    this.revokingKey.set(grant.key);
    this.grantService.deletePersistedGrant(grant.key).subscribe({
      next: () => {
        this.revokingKey.set(null);
        this.loadGrants();
      },
      error: (error) => {
        console.error('Failed to revoke grant:', error);
        this.revokingKey.set(null);
      },
    });
  }

  terminateSession(session: ServerSideSession): void {
    this.terminatingKey.set(session.key);
    this.grantService.deleteSession(session.key).subscribe({
      next: () => {
        this.terminatingKey.set(null);
        this.loadSessions();
      },
      error: (error) => {
        console.error('Failed to terminate session:', error);
        this.terminatingKey.set(null);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }

  formatExpiration(dateString?: string): string {
    return dateString ? new Date(dateString).toLocaleString() : 'Never';
  }

  // Pagination for grants
  grantsTotalPages(): number {
    return Math.ceil(this.grantsTotalCount() / this.pageSize);
  }

  onGrantsPreviousPage(): void {
    if (this.grantsPage() > 1) {
      this.grantsPage.set(this.grantsPage() - 1);
      this.loadGrants();
    }
  }

  onGrantsNextPage(): void {
    if (this.grantsPage() < this.grantsTotalPages()) {
      this.grantsPage.set(this.grantsPage() + 1);
      this.loadGrants();
    }
  }

  // Pagination for sessions
  sessionsTotalPages(): number {
    return Math.ceil(this.sessionsTotalCount() / this.pageSize);
  }

  onSessionsPreviousPage(): void {
    if (this.sessionsPage() > 1) {
      this.sessionsPage.set(this.sessionsPage() - 1);
      this.loadSessions();
    }
  }

  onSessionsNextPage(): void {
    if (this.sessionsPage() < this.sessionsTotalPages()) {
      this.sessionsPage.set(this.sessionsPage() + 1);
      this.loadSessions();
    }
  }
}
