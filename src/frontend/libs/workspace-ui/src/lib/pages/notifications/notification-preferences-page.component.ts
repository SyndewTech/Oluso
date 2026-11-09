import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Subject, takeUntil } from 'rxjs';
import { apiUrl } from '../../auth/oidc.config';

interface NotificationPreferenceDto {
  workflowStepId: string;
  stepName: string;
  workflowName: string;
  lastReceivedAt: string;
  emailEnabled: boolean;
  smsEnabled: boolean;
  whatsAppEnabled: boolean;
}

interface PreferenceGroup {
  workflowName: string;
  preferences: NotificationPreferenceDto[];
}

/** Which channels the tenant can actually deliver on; toggles for the rest are hidden. */
interface ChannelAvailability {
  email: boolean;
  sms: boolean;
  whatsApp: boolean;
  push: boolean;
}

@Component({
  selector: 'workspace-notification-preferences-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div>
        <h1 class="text-2xl font-bold text-gray-900">Notification Preferences</h1>
        <p class="mt-1 text-sm text-gray-500">
          Choose how you want to be notified for each automation step
        </p>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center h-32">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
        </div>
      } @else if (groups().length === 0) {
        <div class="bg-white rounded-lg shadow p-12 text-center">
          <svg class="mx-auto h-12 w-12 text-gray-300" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
            <path stroke-linecap="round" stroke-linejoin="round" d="M14.857 17.082a23.848 23.848 0 005.454-1.31A8.967 8.967 0 0118 9.75v-.7V9A6 6 0 006 9v.75a8.967 8.967 0 01-2.312 6.022c1.733.64 3.56 1.085 5.455 1.31m5.714 0a24.255 24.255 0 01-5.714 0m5.714 0a3 3 0 11-5.714 0" />
          </svg>
          <h3 class="mt-4 text-lg font-medium text-gray-900">No notification preferences available</h3>
          <p class="mt-2 text-sm text-gray-500">
            You'll see options here once you start receiving notifications from automations.
          </p>
        </div>
      } @else {
        @for (group of groups(); track group.workflowName) {
          <div class="bg-white rounded-lg shadow overflow-hidden">
            <!-- Group header -->
            <div class="px-6 py-4 bg-gray-50 border-b border-gray-200">
              <h2 class="text-sm font-semibold text-gray-700">{{ group.workflowName }}</h2>
            </div>

            <!-- Preference rows -->
            <div class="divide-y divide-gray-100">
              @for (pref of group.preferences; track pref.workflowStepId) {
                <div class="px-6 py-4 flex items-center justify-between gap-4">
                  <!-- Step info -->
                  <div class="min-w-0 flex-1">
                    <p class="text-sm font-medium text-gray-900">{{ pref.stepName }}</p>
                    @if (pref.lastReceivedAt) {
                      <p class="text-xs text-gray-500 mt-0.5">
                        Last received: {{ formatDate(pref.lastReceivedAt) }}
                      </p>
                    }
                  </div>

                  <!-- Toggles -->
                  <div class="flex items-center gap-6 shrink-0">
                    <!-- Email toggle -->
                    <label class="flex items-center gap-2 cursor-pointer">
                      <span class="text-xs font-medium text-gray-500">Email</span>
                      <button
                        type="button"
                        role="switch"
                        [attr.aria-checked]="pref.emailEnabled"
                        class="relative inline-flex h-5 w-9 shrink-0 rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
                        [class.bg-primary-600]="pref.emailEnabled"
                        [class.bg-gray-200]="!pref.emailEnabled"
                        (click)="toggleEmail(pref)"
                      >
                        <span
                          class="pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out"
                          [class.translate-x-4]="pref.emailEnabled"
                          [class.translate-x-0]="!pref.emailEnabled"
                        ></span>
                      </button>
                    </label>

                    <!-- SMS toggle -->
                    @if (channels().sms) {
                    <label class="flex items-center gap-2 cursor-pointer">
                      <span class="text-xs font-medium text-gray-500">SMS</span>
                      <button
                        type="button"
                        role="switch"
                        [attr.aria-checked]="pref.smsEnabled"
                        class="relative inline-flex h-5 w-9 shrink-0 rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
                        [class.bg-primary-600]="pref.smsEnabled"
                        [class.bg-gray-200]="!pref.smsEnabled"
                        (click)="toggleSms(pref)"
                      >
                        <span
                          class="pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out"
                          [class.translate-x-4]="pref.smsEnabled"
                          [class.translate-x-0]="!pref.smsEnabled"
                        ></span>
                      </button>
                    </label>

                    }

                    <!-- WhatsApp toggle -->
                    @if (channels().whatsApp) {
                    <label class="flex items-center gap-2 cursor-pointer">
                      <span class="text-xs font-medium text-gray-500">WhatsApp</span>
                      <button
                        type="button"
                        role="switch"
                        [attr.aria-checked]="pref.whatsAppEnabled"
                        class="relative inline-flex h-5 w-9 shrink-0 rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2"
                        [class.bg-primary-600]="pref.whatsAppEnabled"
                        [class.bg-gray-200]="!pref.whatsAppEnabled"
                        (click)="toggleWhatsApp(pref)"
                      >
                        <span
                          class="pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow ring-0 transition duration-200 ease-in-out"
                          [class.translate-x-4]="pref.whatsAppEnabled"
                          [class.translate-x-0]="!pref.whatsAppEnabled"
                        ></span>
                      </button>
                    </label>
                    }
                  </div>
                </div>
              }
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class NotificationPreferencesPageComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly destroy$ = new Subject<void>();
  private readonly baseUrl = `${apiUrl}/api/workspace/notifications/preferences`;

  loading = signal(true);
  preferences = signal<NotificationPreferenceDto[]>([]);
  channels = signal<ChannelAvailability>({ email: true, sms: true, whatsApp: true, push: false });

  groups = computed<PreferenceGroup[]>(() => {
    const prefs = this.preferences();
    const map = new Map<string, NotificationPreferenceDto[]>();

    for (const pref of prefs) {
      const existing = map.get(pref.workflowName);
      if (existing) {
        existing.push(pref);
      } else {
        map.set(pref.workflowName, [pref]);
      }
    }

    return Array.from(map.entries()).map(([workflowName, preferences]) => ({
      workflowName,
      preferences,
    }));
  });

  ngOnInit(): void {
    this.loadChannels();
    this.loadPreferences();
  }

  private loadChannels(): void {
    this.http
      .get<ChannelAvailability>(`${apiUrl}/api/workspace/notifications/channels`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (c) => this.channels.set(c),
        // Older servers have no channels endpoint: keep every toggle visible.
        error: () => this.channels.set({ email: true, sms: true, whatsApp: true, push: false }),
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  toggleEmail(pref: NotificationPreferenceDto): void {
    pref.emailEnabled = !pref.emailEnabled;
    this.savePreference(pref);
  }

  toggleSms(pref: NotificationPreferenceDto): void {
    pref.smsEnabled = !pref.smsEnabled;
    this.savePreference(pref);
  }

  toggleWhatsApp(pref: NotificationPreferenceDto): void {
    pref.whatsAppEnabled = !pref.whatsAppEnabled;
    this.savePreference(pref);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }

  private loadPreferences(): void {
    this.loading.set(true);
    this.http
      .get<NotificationPreferenceDto[]>(this.baseUrl)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (prefs) => {
          this.preferences.set(prefs);
          this.loading.set(false);
        },
        error: () => {
          this.preferences.set([]);
          this.loading.set(false);
        },
      });
  }

  private savePreference(pref: NotificationPreferenceDto): void {
    this.http
      .put(`${this.baseUrl}/${pref.workflowStepId}`, {
        emailEnabled: pref.emailEnabled,
        smsEnabled: pref.smsEnabled,
        whatsAppEnabled: pref.whatsAppEnabled,
      })
      .pipe(takeUntil(this.destroy$))
      .subscribe();
  }
}
