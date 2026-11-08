import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { ButtonComponent, IconComponent, ToastService, extractErrorMessage } from '@oluso/ui-core';
import { VerificationService } from '../services/verification.service';
import {
  BusinessVerificationCountry,
  IdentityVerificationSettings,
  VerificationConnectionTestResult,
  VerificationEnvironment,
  VerificationProviderInfo,
} from '../models/verification.model';

interface ProviderForm {
  info: VerificationProviderInfo;
  enabled: boolean;
  environment: VerificationEnvironment;
  credentials: Record<string, string>;
  storedSecrets: string[];
  options: Record<string, string>;
}

@Component({
  selector: 'verification-settings-tab',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, IconComponent],
  template: `
    @if (loading()) {
      <div class="flex items-center justify-center py-12">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
      </div>
    } @else if (loadError()) {
      <div class="bg-red-50 border border-red-200 rounded-md p-4"><p class="text-sm text-red-600">{{ loadError() }}</p></div>
    } @else {
      <form (ngSubmit)="save()" class="space-y-6">
        <div class="bg-blue-50 border border-blue-200 rounded-lg p-4">
          <div class="flex items-start gap-3">
            <ui-icon name="information-circle" class="h-5 w-5 text-blue-500 mt-0.5 flex-shrink-0" />
            <div class="text-sm text-blue-800">
              <p class="font-medium">Business verification (KYB)</p>
              <p class="mt-1">
                The Business Verification journey step checks a business against its company registry, or through the
                provider's hosted flow, using the provider you route its country to. Credentials are stored encrypted and
                never shown again. Use sandbox keys while testing: sandbox lookups use test data and aren't billed.
              </p>
            </div>
          </div>
        </div>

        @for (p of providers(); track p.info.id) {
          <div class="rounded-lg border border-gray-200 p-4 space-y-4">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div>
                <p class="text-sm font-semibold text-gray-900">{{ p.info.displayName }}</p>
                <p class="text-xs text-gray-500">Company registries: {{ countryNames(p.info.businessCountries) }}</p>
                @if (p.info.hostedCountries.length > 0) {
                  <p class="text-xs text-gray-500">Hosted verification: {{ countryNames(p.info.hostedCountries) }}</p>
                }
              </div>
              <label class="flex items-center gap-2">
                <input type="checkbox" [(ngModel)]="p.enabled" [name]="p.info.id + '-enabled'" class="rounded border-gray-300 text-primary-600" />
                <span class="text-sm text-gray-700">Enabled</span>
              </label>
            </div>

            <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label class="block text-sm font-medium text-gray-700" [for]="p.info.id + '-environment'">Environment</label>
                <select [id]="p.info.id + '-environment'" [(ngModel)]="p.environment" [name]="p.info.id + '-environment'" class="mt-1 w-full">
                  <option value="Sandbox">Sandbox (test data, not billed)</option>
                  <option value="Live">Live (billed per lookup)</option>
                </select>
                <p class="mt-1 text-xs text-gray-500">Switching environment clears stored secrets; enter the keys for the new one.</p>
              </div>

              @for (o of p.info.options; track o.name) {
                <div>
                  <label class="block text-sm font-medium text-gray-700" [for]="p.info.id + '-' + o.name">{{ o.label }}</label>
                  <select [id]="p.info.id + '-' + o.name" [(ngModel)]="p.options[o.name]" [name]="p.info.id + '-' + o.name" class="mt-1 w-full">
                    @for (c of o.choices; track c) {
                      <option [value]="c">{{ c }}</option>
                    }
                  </select>
                  @if (o.description) { <p class="mt-1 text-xs text-gray-500">{{ o.description }}</p> }
                </div>
              }

              @for (f of p.info.credentialFields; track f.name) {
                <div>
                  <label class="block text-sm font-medium text-gray-700" [for]="p.info.id + '-' + f.name">
                    {{ f.label }}
                    @if (!f.required) { <span class="text-xs font-normal text-gray-500">(hosted verification only)</span> }
                  </label>
                  @if (f.secret) {
                    <input type="password" [id]="p.info.id + '-' + f.name" [(ngModel)]="p.credentials[f.name]" [name]="p.info.id + '-' + f.name"
                           class="mt-1 w-full" autocomplete="new-password"
                           [placeholder]="p.storedSecrets.includes(f.name) ? 'Stored. Leave blank to keep.' : 'Not set'" />
                  } @else {
                    <input type="text" [id]="p.info.id + '-' + f.name" [(ngModel)]="p.credentials[f.name]" [name]="p.info.id + '-' + f.name"
                           class="mt-1 w-full" autocomplete="off" />
                  }
                  @if (f.description) { <p class="mt-1 text-xs text-gray-500">{{ f.description }}</p> }
                </div>
              }
            </div>

            @if (p.info.webhookUrl) {
              <div class="rounded-md bg-gray-50 border border-gray-200 p-3">
                <p class="text-xs font-medium text-gray-700">Webhook URL for {{ p.info.displayName }}</p>
                <div class="mt-1 flex items-center gap-2">
                  <code class="text-xs break-all text-gray-800">{{ p.info.webhookUrl }}</code>
                  <ui-button type="button" variant="ghost" size="sm" (click)="copy(p.info.webhookUrl)">Copy</ui-button>
                </div>
                <p class="mt-1 text-xs text-gray-500">Give this to the provider for hosted verification results. Requests are accepted only when their signature verifies with the webhook secret above.</p>
              </div>
            }

            <div class="flex items-center gap-3">
              <ui-button type="button" variant="secondary" (click)="test(p.info.id)" [disabled]="testing() === p.info.id">
                {{ testing() === p.info.id ? 'Testing…' : 'Test saved credentials' }}
              </ui-button>
              @if (testResults()[p.info.id]; as r) {
                <span class="text-sm" [class.text-green-700]="r.success" [class.text-red-600]="!r.success">{{ r.message }}</span>
              }
            </div>
          </div>
        }

        <div class="rounded-lg border border-gray-200 p-4 space-y-4">
          <div>
            <p class="text-sm font-semibold text-gray-900">Country routing</p>
            <p class="text-xs text-gray-500">Which provider verifies businesses registered in each country. A country with no provider can't be verified; the journey step fails instead of trying another provider.</p>
          </div>
          <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
            @for (c of countries(); track c.code) {
              <div>
                <label class="block text-sm font-medium text-gray-700" [for]="'route-' + c.code">{{ c.name }} ({{ c.code }})</label>
                <select [id]="'route-' + c.code" [(ngModel)]="routing[c.code]" [name]="'route-' + c.code" class="mt-1 w-full">
                  <option value="">Not routed</option>
                  @for (id of c.providerIds; track id) {
                    <option [value]="id">{{ providerName(id) }}</option>
                  }
                </select>
              </div>
            }
          </div>
        </div>

        <div class="rounded-lg border border-gray-200 p-4 space-y-2 max-w-md">
          <label class="block text-sm font-semibold text-gray-900" for="reuse-days">Reuse registry results for (days)</label>
          <input id="reuse-days" type="number" min="0" max="365" [(ngModel)]="reuseDays" name="reuseDays" class="w-32" />
          <p class="text-xs text-gray-500">When the same registration number is verified again within this many days, the earlier registry result is reused instead of paying for a new lookup. 0 always looks up again.</p>
        </div>

        <div class="flex items-center gap-3">
          <ui-button type="submit" [disabled]="saving()">{{ saving() ? 'Saving…' : 'Save' }}</ui-button>
        </div>
      </form>
    }
  `,
})
export class VerificationSettingsTabComponent implements OnInit {
  private readonly service = inject(VerificationService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly testing = signal<string | null>(null);
  readonly loadError = signal<string | null>(null);
  readonly providers = signal<ProviderForm[]>([]);
  readonly countries = signal<BusinessVerificationCountry[]>([]);
  readonly testResults = signal<Record<string, VerificationConnectionTestResult>>({});

  routing: Record<string, string> = {};
  reuseDays = 0;

  ngOnInit(): void {
    forkJoin({
      providers: this.service.getProviders(),
      countries: this.service.getCountries(),
      settings: this.service.getSettings(),
    }).subscribe({
      next: ({ providers, countries, settings }) => {
        this.countries.set(countries);
        this.apply(providers, settings);
        this.loading.set(false);
      },
      error: (err) => {
        this.loadError.set(extractErrorMessage(err, 'Failed to load business verification settings'));
        this.loading.set(false);
      },
    });
  }

  countryNames(codes: string[]): string {
    return codes.map((code) => this.countries().find((c) => c.code === code)?.name ?? code).join(', ');
  }

  providerName(id: string): string {
    return this.providers().find((p) => p.info.id === id)?.info.displayName ?? id;
  }

  copy(text: string): void {
    navigator.clipboard.writeText(text).then(
      () => this.toast.success('Webhook URL copied'),
      () => this.toast.error('Copy failed; select the URL and copy it manually'),
    );
  }

  save(): void {
    if (this.saving()) return;
    this.saving.set(true);
    const businessRouting: Record<string, string> = {};
    for (const [country, providerId] of Object.entries(this.routing)) {
      if (providerId) businessRouting[country] = providerId;
    }
    this.service
      .updateSettings({
        providers: this.providers().map((p) => ({
          providerId: p.info.id,
          enabled: p.enabled,
          environment: p.environment,
          credentials: { ...p.credentials },
          options: { ...p.options },
        })),
        businessRouting,
        businessReuseMaxAgeDays: Number(this.reuseDays) || 0,
      })
      .subscribe({
        next: (settings) => {
          this.apply(this.providers().map((p) => p.info), settings);
          this.saving.set(false);
          this.toast.success('Business verification settings saved');
        },
        error: (err) => {
          this.saving.set(false);
          this.toast.error(extractErrorMessage(err, 'Failed to save business verification settings'));
        },
      });
  }

  test(providerId: string): void {
    this.testing.set(providerId);
    this.service.testProvider(providerId).subscribe({
      next: (result) => {
        this.testResults.update((all) => ({ ...all, [providerId]: result }));
        this.testing.set(null);
      },
      error: (err) => {
        this.testResults.update((all) => ({ ...all, [providerId]: { success: false, message: extractErrorMessage(err, 'Test failed') } }));
        this.testing.set(null);
      },
    });
  }

  private apply(infos: VerificationProviderInfo[], settings: IdentityVerificationSettings): void {
    this.providers.set(
      infos.map((info) => {
        const saved = settings.providers.find((p) => p.providerId === info.id);
        const credentials: Record<string, string> = {};
        for (const field of info.credentialFields) {
          credentials[field.name] = field.secret ? '' : saved?.credentials[field.name] ?? '';
        }
        const options: Record<string, string> = {};
        for (const option of info.options) {
          options[option.name] = saved?.options[option.name] ?? option.default;
        }
        return {
          info,
          enabled: saved?.enabled ?? false,
          environment: saved?.environment ?? 'Sandbox',
          credentials,
          storedSecrets: saved?.storedSecrets ?? [],
          options,
        };
      }),
    );
    this.routing = {};
    for (const c of this.countries()) {
      this.routing[c.code] = settings.businessRouting[c.code] ?? '';
    }
    this.reuseDays = settings.businessReuseMaxAgeDays;
  }
}
