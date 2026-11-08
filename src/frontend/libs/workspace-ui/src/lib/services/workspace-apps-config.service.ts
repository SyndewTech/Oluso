import { Injectable, inject, signal, computed } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { WorkspaceApiService } from './workspace-api.service';
import { accountUrl as hostAccountUrl, adminUrl as hostAdminUrl } from '../auth/oidc.config';

export interface CustomWorkspaceApp {
  id: string;
  name: string;
  description?: string;
  externalUrl: string;
  icon: string;
  color: string;
  order: number;
}

export interface WorkspaceAppsSettings {
  builtInApps: Record<string, boolean>;
  customApps: CustomWorkspaceApp[];
  /** Tenant URL overrides for built-in apps, e.g. { admin: '/admin', account: '/account' } */
  appUrls?: Record<string, string>;
}

/** Where a built-in app lives when neither the tenant nor the host says otherwise. */
const DEFAULT_APP_PATHS: Record<string, string> = {
  admin: '/admin',
  account: '/account',
};

/** Host-level defaults from the runtime config script. */
const HOST_APP_URLS: Record<string, string> = {
  admin: hostAdminUrl,
  account: hostAccountUrl,
};

@Injectable({ providedIn: 'root' })
export class WorkspaceAppsConfigService {
  private readonly api = inject(WorkspaceApiService);
  private readonly _settings = signal<WorkspaceAppsSettings>({ builtInApps: {}, customApps: [] });

  readonly settings = this._settings.asReadonly();
  readonly builtInApps = computed(() => this._settings().builtInApps);
  readonly customApps = computed(() => this._settings().customApps);

  loadSettings(): Observable<WorkspaceAppsSettings> {
    return this.api.getAbsolute<WorkspaceAppsSettings>('/api/workspace/apps').pipe(
      tap(settings => this._settings.set(settings))
    );
  }

  isAppEnabled(appId: string): boolean {
    return this._settings().builtInApps[appId] !== false;
  }

  /**
   * Resolve the URL for a built-in app: tenant override, then the host's
   * runtime config, then the same-origin default path.
   */
  getAppUrl(appId: string, defaultUrl: string = DEFAULT_APP_PATHS[appId] ?? ''): string {
    return this._settings().appUrls?.[appId] || HOST_APP_URLS[appId] || defaultUrl;
  }

  getAdminUrl(): string {
    return this.getAppUrl('admin');
  }

  getAccountUrl(): string {
    return this.getAppUrl('account');
  }
}
