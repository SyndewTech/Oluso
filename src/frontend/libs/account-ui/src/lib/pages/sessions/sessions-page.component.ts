import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent } from '@syndew/oluso-ui-core';
import { AccountService } from '../../services/account.service';
import { Session } from '../../models';

@Component({
  selector: 'account-sessions-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent],
  template: `
    <div class="space-y-6">
      <div class="flex justify-between items-center">
        <div>
          <h1 class="text-2xl font-semibold text-gray-900">Active Sessions</h1>
          <p class="text-gray-500 mt-1">Manage your active login sessions ({{ sessions().length }})</p>
        </div>
        @if (sessions().length > 1) {
          <ui-button variant="danger" size="sm" (clicked)="showRevokeAllModal.set(true)">
            Revoke All Other Sessions
          </ui-button>
        }
      </div>

      @if (loading()) {
        <div class="flex justify-center py-12">
          <svg
            class="animate-spin h-8 w-8 text-primary-600"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
          >
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4"></circle>
            <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"></path>
          </svg>
        </div>
      } @else if (sessions().length === 0) {
        <ui-card>
          <div class="text-center py-8 text-gray-500">
            <ui-icon name="computer-desktop" class="mx-auto h-12 w-12 text-gray-400" />
            <p class="mt-4">No active sessions found</p>
          </div>
        </ui-card>
      } @else {
        <ui-card>
          <div class="divide-y divide-gray-100">
            @for (session of sessions(); track session.id) {
              <div class="flex items-center justify-between px-4 py-3 hover:bg-gray-50">
                <div class="flex items-center gap-3 min-w-0">
                  <div class="p-1.5 bg-gray-100 rounded-lg shrink-0">
                    @if (isDesktop(session.userAgent)) {
                      <ui-icon name="computer-desktop" class="h-5 w-5 text-gray-500" />
                    } @else {
                      <ui-icon name="device-phone-mobile" class="h-5 w-5 text-gray-500" />
                    }
                  </div>
                  <div class="min-w-0">
                    <div class="flex items-center gap-2">
                      <span class="text-sm font-medium text-gray-900 truncate">
                        {{ session.clientName || 'Signed in' }}
                      </span>
                      @if (session.isCurrent) {
                        <ui-badge variant="success">Current</ui-badge>
                      }
                    </div>
                    <div class="flex items-center gap-3 text-xs text-gray-500 mt-0.5">
                      <span>{{ parseUserAgent(session.userAgent) }}</span>
                      @if (session.ipAddress) {
                        <span>{{ session.ipAddress }}</span>
                      }
                      <span>Signed in {{ formatDate(session.createdAt) }}</span>
                      @if (session.lastActivityAt && session.lastActivityAt !== session.createdAt) {
                        <span>Active {{ formatDate(session.lastActivityAt) }}</span>
                      }
                    </div>
                  </div>
                </div>
                @if (!session.isCurrent) {
                  <ui-button
                    variant="danger"
                    size="sm"
                    [loading]="revokingSessionId() === session.id"
                    (clicked)="revokeSession(session)"
                  >
                    Revoke
                  </ui-button>
                }
              </div>
            }
          </div>
        </ui-card>
      }
    </div>

    <!-- Revoke All Modal -->
    <ui-modal
      [open]="showRevokeAllModal()"
      title="Revoke All Other Sessions"
      (closed)="showRevokeAllModal.set(false)"
    >
      <p class="text-sm text-gray-600">
        Are you sure you want to revoke all other sessions? You will remain logged in on this device only.
      </p>

      <div modal-footer class="px-6 py-4 border-t border-gray-200 flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="showRevokeAllModal.set(false)">Cancel</ui-button>
        <ui-button variant="danger" [loading]="revokingAll()" (clicked)="revokeAllSessions()">
          Revoke All
        </ui-button>
      </div>
    </ui-modal>
  `,
})
export class SessionsPageComponent implements OnInit {
  private readonly accountService = inject(AccountService);

  readonly loading = signal(true);
  readonly sessions = signal<Session[]>([]);
  readonly revokingSessionId = signal<string | null>(null);
  readonly revokingAll = signal(false);
  readonly showRevokeAllModal = signal(false);

  ngOnInit(): void {
    this.loadSessions();
  }

  loadSessions(): void {
    this.loading.set(true);
    this.accountService.getSessions().subscribe({
      next: (sessions) => {
        this.sessions.set(sessions);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load sessions:', err);
        this.loading.set(false);
      },
    });
  }

  revokeSession(session: Session): void {
    this.revokingSessionId.set(session.id);
    this.accountService.revokeSession(session.id).subscribe({
      next: () => {
        this.sessions.update((sessions) => sessions.filter((s) => s.id !== session.id));
        this.revokingSessionId.set(null);
      },
      error: (err) => {
        console.error('Failed to revoke session:', err);
        this.revokingSessionId.set(null);
        alert('Failed to revoke session');
      },
    });
  }

  revokeAllSessions(): void {
    this.revokingAll.set(true);
    this.accountService.revokeAllSessions().subscribe({
      next: () => {
        this.revokingAll.set(false);
        this.showRevokeAllModal.set(false);
        this.loadSessions();
      },
      error: (err) => {
        console.error('Failed to revoke all sessions:', err);
        this.revokingAll.set(false);
        alert('Failed to revoke sessions');
      },
    });
  }

  isDesktop(userAgent?: string): boolean {
    if (!userAgent) return true;
    const mobileKeywords = ['mobile', 'android', 'iphone', 'ipad'];
    return !mobileKeywords.some((keyword) => userAgent.toLowerCase().includes(keyword));
  }

  parseUserAgent(userAgent?: string): string {
    if (!userAgent) return 'Unknown device';

    const ua = userAgent;
    const browser = ua.includes('Edg/') ? 'Edge'
      : ua.includes('OPR/') ? 'Opera'
      : ua.includes('Chrome/') ? 'Chrome'
      : ua.includes('Firefox/') ? 'Firefox'
      : ua.includes('Safari/') ? 'Safari'
      : null;
    const os = /iPhone|iPad/.test(ua) ? 'iOS'
      : ua.includes('Android') ? 'Android'
      : ua.includes('Mac OS X') ? 'macOS'
      : ua.includes('Windows') ? 'Windows'
      : ua.includes('Linux') ? 'Linux'
      : null;

    if (browser && os) return `${browser} on ${os}`;
    return browser ?? os ?? 'Unknown device';
  }

  formatDate(dateString: string): string {
    const date = new Date(dateString);
    const now = new Date();
    const diffMs = now.getTime() - date.getTime();
    const diffMins = Math.floor(diffMs / 60000);
    const diffHours = Math.floor(diffMins / 60);
    const diffDays = Math.floor(diffHours / 24);

    const plural = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'} ago`;
    if (diffMins < 1) return 'Just now';
    if (diffMins < 60) return plural(diffMins, 'minute');
    if (diffHours < 24) return plural(diffHours, 'hour');
    if (diffDays < 7) return plural(diffDays, 'day');

    return date.toLocaleDateString();
  }
}
