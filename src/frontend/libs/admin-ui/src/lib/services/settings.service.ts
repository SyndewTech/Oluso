import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { ServerSettings, BrandingSettings } from '../models/server-settings.model';

@Injectable({
  providedIn: 'root',
})
export class SettingsService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/settings';

  /**
   * Get server settings
   */
  getSettings(): Observable<ServerSettings> {
    return this.api.get<ServerSettings>(this.endpoint);
  }

  /**
   * Update server settings
   */
  updateSettings(settings: ServerSettings): Observable<ServerSettings> {
    return this.api.put<ServerSettings>(this.endpoint, settings);
  }

  /**
   * Send a test email through the tenant's default SenderProfile (or the
   * platform-global EmailOptions fallback when no profile is configured).
   * Per-profile test sends live on SenderProfilesService.
   */
  sendTestEmail(to: string): Observable<{ message: string; messageId?: string }> {
    return this.api.post<{ message: string; messageId?: string }>(`${this.endpoint}/email/test`, { to });
  }

  /** Get branding settings (logo, colours, support email, etc.) for the current tenant. */
  getBrandingSettings(): Observable<BrandingSettings> {
    return this.api.get<BrandingSettings>(`${this.endpoint}/branding`);
  }

  /** Update branding settings for the current tenant. Server also syncs visual fields to Tenant.Branding for legacy entity readers. */
  updateBrandingSettings(settings: BrandingSettings): Observable<BrandingSettings> {
    return this.api.put<BrandingSettings>(`${this.endpoint}/branding`, settings);
  }
}
