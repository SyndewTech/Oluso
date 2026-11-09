import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CardComponent, BadgeComponent, IconComponent, LocaleDatePipe } from '@syndew/oluso-ui-core';
import { ApiService } from '../../services/api.service';

interface LicenseStatus {
  tier: string;
  isValid: boolean;
  expiresAt: string | null;
  isExpired: boolean;
  isTrial: boolean;
  daysUntilExpiration: number;
  maxTenants: number;
  features: string[];
  addOns: string[];
  message: string | null;
}

@Component({
  selector: 'admin-license-status-page',
  standalone: true,
  imports: [CommonModule, CardComponent, BadgeComponent, IconComponent, LocaleDatePipe],
  template: `
    <div class="page-header">
      <h1>License Status</h1>
    </div>

    @if (loading()) {
      <ui-card>
        <div class="loading-placeholder">Loading license information...</div>
      </ui-card>
    } @else if (license(); as lic) {
      <!-- License Overview -->
      <ui-card>
        <div class="license-overview">
          <div class="license-tier">
            <span class="tier-label">License Tier</span>
            <span class="tier-value">{{ lic.tier | titlecase }}</span>
            @if (lic.isTrial) {
              <ui-badge variant="warning">Trial</ui-badge>
            }
            @if (lic.isExpired) {
              <ui-badge variant="danger">Expired</ui-badge>
            } @else if (lic.isValid) {
              <ui-badge variant="success">Active</ui-badge>
            }
          </div>

          <div class="license-details">
            @if (lic.expiresAt) {
              <div class="detail-item">
                <span class="detail-label">Expires</span>
                <span class="detail-value">
                  {{ lic.expiresAt | localeDate:'mediumDate' }}
                  @if (!lic.isExpired && lic.daysUntilExpiration <= 30) {
                    <span class="expiry-warning">({{ lic.daysUntilExpiration }} days remaining)</span>
                  }
                </span>
              </div>
            }

            <div class="detail-item">
              <span class="detail-label">Max Tenants</span>
              <span class="detail-value">{{ lic.maxTenants === -1 ? 'Unlimited' : lic.maxTenants }}</span>
            </div>
          </div>
        </div>
      </ui-card>

      <!-- Licensed Features -->
      <ui-card>
        <h2 class="section-title">Licensed Features</h2>
        @if (lic.features.length === 0) {
          <p class="empty-state">No features licensed</p>
        } @else {
          <div class="feature-grid">
            @for (feature of lic.features; track feature) {
              <div class="feature-item">
                <ui-icon name="check-circle" class="feature-icon licensed"></ui-icon>
                <span>{{ feature }}</span>
              </div>
            }
          </div>
        }
      </ui-card>

      <!-- Add-Ons -->
      @if (lic.addOns.length > 0) {
        <ui-card>
          <h2 class="section-title">Add-Ons</h2>
          <div class="feature-grid">
            @for (addon of lic.addOns; track addon) {
              <div class="feature-item">
                <ui-icon name="puzzle-piece" class="feature-icon addon"></ui-icon>
                <span>{{ addon }}</span>
              </div>
            }
          </div>
        </ui-card>
      }
    } @else {
      <ui-card>
        <div class="empty-state">
          <p>No license information available.</p>
          <p class="empty-hint">Contact support to obtain a license key.</p>
        </div>
      </ui-card>
    }
  `,
  styles: [`
    .page-header { margin-bottom: 1.5rem; }
    .page-header h1 { font-size: 1.5rem; font-weight: 600; }

    .license-overview { display: flex; flex-direction: column; gap: 1.5rem; }
    .license-tier { display: flex; align-items: center; gap: 0.75rem; }
    .tier-label { font-size: 0.875rem; color: var(--text-secondary); }
    .tier-value { font-size: 1.5rem; font-weight: 700; }

    .license-details { display: grid; grid-template-columns: repeat(auto-fill, minmax(200px, 1fr)); gap: 1rem; }
    .detail-item { display: flex; flex-direction: column; gap: 0.25rem; }
    .detail-label { font-size: 0.75rem; color: var(--text-secondary); text-transform: uppercase; letter-spacing: 0.05em; }
    .detail-value { font-size: 0.875rem; font-weight: 500; }
    .expiry-warning { color: var(--color-warning); font-size: 0.75rem; }

    .section-title { font-size: 1rem; font-weight: 600; margin-bottom: 1rem; }
    .feature-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 0.5rem; }
    .feature-item { display: flex; align-items: center; gap: 0.5rem; padding: 0.5rem; border-radius: 0.375rem; background: var(--bg-secondary); font-size: 0.875rem; }
    .feature-icon { width: 1rem; height: 1rem; flex-shrink: 0; }
    .feature-icon.licensed { color: var(--color-success); }
    .feature-icon.addon { color: var(--color-primary); }

    .loading-placeholder { padding: 2rem; text-align: center; color: var(--text-secondary); }
    .empty-state { text-align: center; padding: 2rem; color: var(--text-secondary); }
    .empty-hint { font-size: 0.875rem; margin-top: 0.5rem; }

    ui-card { display: block; margin-bottom: 1rem; }
  `],
})
export class LicenseStatusPageComponent implements OnInit {
  private readonly api = inject(ApiService);

  readonly loading = signal(true);
  readonly license = signal<LicenseStatus | null>(null);

  ngOnInit(): void {
    this.api.get<LicenseStatus>('/settings/license').subscribe({
      next: (status) => {
        this.license.set(status);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load license status:', err);
        this.loading.set(false);
      },
    });
  }
}
