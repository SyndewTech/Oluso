import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

/**
 * Dashboard statistics from API
 */
export interface DashboardStats {
  clientsCount: number;
  usersCount: number;
  resourcesCount: number;
  identityResourcesCount: number;
  activeSessionsCount: number;
  recentLoginsCount: number;
  // Journey & submission stats
  journeyPoliciesCount: number;
  activeJourneysCount: number;
  submissionsCount: number;
  pendingSubmissionsCount: number;
  // Recent activity counts (24h)
  failedLoginsCount: number;
  newUsersCount: number;
  // Comparison stats (vs previous period)
  previousLoginsCount: number;
  previousFailedLoginsCount: number;
  previousNewUsersCount: number;
  submissionsToday: number;
  submissionsYesterday: number;
}

/**
 * Stat card configuration for dashboard display
 */
export interface StatCard {
  id: string;
  title: string;
  value: string | number;
  change?: string;
  changeType?: 'increase' | 'decrease' | 'neutral';
  icon: string;
  iconBgColor: string;
  link?: string;
}

@Injectable({
  providedIn: 'root',
})
export class DashboardService {
  private readonly api = inject(ApiService);

  /**
   * Get dashboard statistics
   */
  getStats(): Observable<DashboardStats> {
    return this.api.get<DashboardStats>('/dashboard/stats');
  }

  /**
   * Convert API stats to display cards
   */
  mapStatsToCards(stats: DashboardStats): StatCard[] {
    // Calculate percentage changes
    const loginsChange = this.calculatePercentageChange(stats.recentLoginsCount, stats.previousLoginsCount);
    const failedLoginsChange = this.calculatePercentageChange(stats.failedLoginsCount, stats.previousFailedLoginsCount);
    const newUsersChange = this.calculatePercentageChange(stats.newUsersCount, stats.previousNewUsersCount);
    const submissionsChange = this.calculatePercentageChange(stats.submissionsToday, stats.submissionsYesterday);

    return [
      {
        id: 'users',
        title: 'Total Users',
        value: this.formatNumber(stats.usersCount),
        change: newUsersChange.text,
        changeType: newUsersChange.type,
        icon: 'users',
        iconBgColor: 'bg-blue-500',
        link: '/users',
      },
      {
        id: 'clients',
        title: 'Active Clients',
        value: stats.clientsCount,
        icon: 'window',
        iconBgColor: 'bg-purple-500',
        link: '/clients',
      },
      {
        id: 'sessions',
        title: 'Active Sessions',
        value: this.formatNumber(stats.activeSessionsCount),
        icon: 'clock',
        iconBgColor: 'bg-green-500',
        link: '/grants',
      },
      {
        id: 'logins',
        title: 'Logins (24h)',
        value: this.formatNumber(stats.recentLoginsCount),
        change: loginsChange.text,
        changeType: loginsChange.type,
        icon: 'login',
        iconBgColor: 'bg-teal-500',
      },
      {
        id: 'failed-logins',
        title: 'Failed Logins (24h)',
        value: stats.failedLoginsCount,
        change: failedLoginsChange.text,
        // For failed logins, decrease is good (green), increase is bad (red)
        changeType: failedLoginsChange.type === 'increase' ? 'decrease' : failedLoginsChange.type === 'decrease' ? 'increase' : 'neutral',
        icon: 'shield-exclamation',
        iconBgColor: 'bg-red-500',
      },
      {
        id: 'journeys',
        title: 'User Journeys',
        value: stats.journeyPoliciesCount,
        change: `${stats.activeJourneysCount} active`,
        changeType: 'neutral',
        icon: 'map',
        iconBgColor: 'bg-indigo-500',
        link: '/experience/journeys',
      },
      {
        id: 'submissions',
        title: 'Submissions',
        value: this.formatNumber(stats.submissionsCount),
        change: submissionsChange.text,
        changeType: submissionsChange.type,
        icon: 'inbox',
        iconBgColor: 'bg-amber-500',
        link: '/experience/submissions',
      },
    ];
  }

  /**
   * Calculate percentage change between current and previous values
   */
  private calculatePercentageChange(current: number, previous: number): { text?: string; type?: 'increase' | 'decrease' | 'neutral' } {
    // Handle null, undefined, or NaN values
    const curr = current ?? 0;
    const prev = previous ?? 0;

    if (isNaN(curr) || isNaN(prev)) {
      return {};
    }

    if (prev === 0 && curr === 0) {
      return {};
    }

    if (prev === 0) {
      if (curr > 0) {
        return { text: `+${curr} new`, type: 'increase' };
      }
      return {};
    }

    const percentChange = ((curr - prev) / prev) * 100;

    // Check for NaN or Infinity
    if (!isFinite(percentChange)) {
      return {};
    }

    if (percentChange === 0) {
      return { text: 'No change', type: 'neutral' };
    }

    const sign = percentChange > 0 ? '+' : '';
    const type: 'increase' | 'decrease' = percentChange > 0 ? 'increase' : 'decrease';

    return {
      text: `${sign}${percentChange.toFixed(0)}%`,
      type,
    };
  }

  private formatNumber(num: number): string {
    if (num >= 1000000) {
      return (num / 1000000).toFixed(1) + 'M';
    }
    if (num >= 1000) {
      return (num / 1000).toFixed(1) + 'k';
    }
    return num.toLocaleString();
  }
}
