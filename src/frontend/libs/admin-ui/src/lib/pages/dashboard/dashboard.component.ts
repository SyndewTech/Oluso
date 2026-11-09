import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { CardComponent, IconComponent } from '@syndew/oluso-ui-core';
import { PluginService } from '@syndew/oluso-ui-core';
import { DashboardService, StatCard, DashboardStats } from '../../services/dashboard.service';
import { AuditLogService } from '../../services/audit-log.service';
import { AuditLog } from '../../models/audit-log.model';

type ActivityState = 'loading' | 'ready' | 'unavailable' | 'forbidden' | 'error';

@Component({
  selector: 'admin-dashboard',
  standalone: true,
  imports: [CommonModule, RouterModule, CardComponent, IconComponent],
  templateUrl: './dashboard.component.html',
})
export class DashboardComponent implements OnInit {
  private readonly pluginService = inject(PluginService);
  private readonly dashboardService = inject(DashboardService);
  private readonly auditLogService = inject(AuditLogService);

  /** Newest audit entries for this tenant; the card used to be a placeholder. */
  readonly recentActivity = signal<AuditLog[]>([]);
  readonly activityState = signal<ActivityState>('loading');

  readonly dashboardWidgets = this.pluginService.adminDashboardWidgets;

  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly stats = signal<StatCard[]>([]);

  ngOnInit(): void {
    this.loadStats();
    this.loadRecentActivity();
  }

  loadStats(): void {
    this.loading.set(true);
    this.error.set(null);

    this.dashboardService.getStats().subscribe({
      next: (apiStats: DashboardStats) => {
        const cards = this.dashboardService.mapStatsToCards(apiStats);
        this.stats.set(cards);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load dashboard stats:', err);
        this.error.set('Failed to load dashboard statistics');
        this.loading.set(false);
        // Set fallback empty stats
        this.stats.set([]);
      },
    });
  }

  loadRecentActivity(): void {
    this.activityState.set('loading');
    this.auditLogService.getLogs({ pageNumber: 1, pageSize: 8 }).subscribe({
      next: (response) => {
        this.recentActivity.set(response.items ?? []);
        this.activityState.set('ready');
      },
      error: (err) => {
        // 402 = audit logging not enabled on this deployment; 403 = no audit permission.
        if (err?.status === 402) this.activityState.set('unavailable');
        else if (err?.status === 403) this.activityState.set('forbidden');
        else {
          console.error('Failed to load recent activity:', err);
          this.activityState.set('error');
        }
      },
    });
  }

  /** "LoginFailed" → "Login failed"; "user.updated" → "user updated". */
  actionLabel(action: string): string {
    if (!action) return '';
    const spaced = action.replace(/[._-]/g, ' ').replace(/([a-z0-9])([A-Z])/g, '$1 $2');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1).toLowerCase();
  }

  relativeTime(timestamp: string): string {
    const then = new Date(timestamp).getTime();
    const elapsedMs = Date.now() - then;
    const minutes = Math.floor(elapsedMs / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.floor(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    return new Date(timestamp).toLocaleString(undefined, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' });
  }
}
