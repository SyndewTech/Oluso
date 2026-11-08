import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  OnInit,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { apiUrl } from '../../auth/oidc.config';

/**
 * Generic anonymous landing page for action-token magic links. The email
 * the user clicked contains `/m#tk=<token>` — the token lives in the URL
 * fragment, which the browser never sends to the server, so it never
 * appears in access logs or Referer headers.
 *
 * On load this reads `window.location.hash`, POSTs the token to
 * `/api/public/links/preview` to fetch the action-specific view-model
 * (title / summary / options), and renders one button per option. Picking
 * an option POSTs to `/api/public/links/redeem` which atomically consumes
 * the token and dispatches to whichever `IActionTokenHandler` is
 * registered server-side.
 *
 * Adding a new magic-link flow (vendor invoice confirm, candidate offer
 * accept, etc.) requires zero frontend changes — only a new handler.
 */
interface ActionTokenOption {
  key: string;
  label: string;
  variant: string;
  requiresComments?: boolean;
  commentsRequired?: boolean;
}

interface ActionTokenPreview {
  title: string;
  summary?: string;
  description?: string;
  options: ActionTokenOption[];
  alreadyActioned: boolean;
  alreadyActionedReason?: string;
}

interface ActionTokenExecuteResult {
  message: string;
  redirectUrl?: string;
}

@Component({
  selector: 'app-magic-link-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-screen flex items-center justify-center bg-gray-50 px-4 py-10">
      <div class="max-w-lg w-full bg-white border border-gray-200 rounded-lg shadow-sm p-8 space-y-5">
        @if (loading()) {
          <div class="text-center py-6">
            <div class="inline-block h-8 w-8 rounded-full border-2 border-primary-600 border-t-transparent animate-spin"></div>
            <p class="mt-3 text-sm text-gray-500">Loading…</p>
          </div>
        } @else if (loadError()) {
          <div class="text-center">
            <div class="mx-auto h-12 w-12 rounded-full bg-amber-100 flex items-center justify-center mb-3">
              <svg class="h-6 w-6 text-amber-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m-9.303 3.376c-.866 1.5.217 3.374 1.948 3.374h14.71c1.73 0 2.813-1.874 1.948-3.374L13.949 3.378c-.866-1.5-3.032-1.5-3.898 0L2.697 16.126ZM12 15.75h.007v.008H12v-.008Z" />
              </svg>
            </div>
            <h1 class="text-lg font-semibold text-gray-900 mb-2">Link unavailable</h1>
            <p class="text-sm text-gray-600">{{ loadError() }}</p>
          </div>
        } @else if (result(); as r) {
          <div class="text-center">
            <div class="mx-auto h-12 w-12 rounded-full bg-emerald-100 flex items-center justify-center mb-3">
              <svg class="h-6 w-6 text-emerald-600" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                <path stroke-linecap="round" stroke-linejoin="round" d="m4.5 12.75 6 6 9-13.5" />
              </svg>
            </div>
            <h1 class="text-lg font-semibold text-gray-900 mb-1">{{ r.message }}</h1>
            <p class="text-sm text-gray-500">You can close this page.</p>
          </div>
        } @else if (preview(); as p) {
          <div>
            <h1 class="text-lg font-semibold text-gray-900">{{ p.title }}</h1>
            @if (p.summary) {
              <p class="mt-1 text-sm text-gray-700">{{ p.summary }}</p>
            }
            @if (p.description) {
              <p class="mt-2 text-xs text-gray-500">{{ p.description }}</p>
            }
          </div>

          @if (p.alreadyActioned) {
            <div class="bg-gray-50 border border-gray-200 rounded-md p-4 text-sm text-gray-600">
              {{ p.alreadyActionedReason || 'This link has already been actioned.' }}
            </div>
          } @else {
            @if (selectedOption(); as opt) {
              @if (opt.requiresComments) {
                <div>
                  <label class="block text-xs font-medium text-gray-700 mb-1">
                    {{ opt.commentsRequired ? 'Comment (required)' : 'Comment (optional)' }}
                  </label>
                  <textarea
                    [(ngModel)]="comments"
                    [disabled]="submitting()"
                    rows="3"
                    class="w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 disabled:opacity-50"
                    [placeholder]="opt.label + '…'"
                  ></textarea>
                </div>
              }
              @if (submitError(); as err) {
                <p class="text-sm text-red-600">{{ err }}</p>
              }
              <div class="flex items-center justify-end gap-2">
                <button
                  type="button"
                  (click)="cancelOption()"
                  [disabled]="submitting()"
                  class="px-3 py-1.5 rounded-md text-sm font-medium text-gray-700 hover:bg-gray-100 disabled:opacity-50"
                >
                  Back
                </button>
                <button
                  type="button"
                  (click)="confirmOption(opt)"
                  [disabled]="submitting() || (opt.commentsRequired && !comments.trim())"
                  class="px-3 py-1.5 rounded-md text-sm font-medium text-white disabled:opacity-50"
                  [class]="variantClass(opt.variant)"
                >
                  {{ submitting() ? 'Submitting…' : opt.label }}
                </button>
              </div>
            } @else {
              <div class="flex flex-wrap items-center gap-2">
                @for (opt of p.options; track opt.key) {
                  <button
                    type="button"
                    (click)="pickOption(opt)"
                    class="px-3 py-1.5 rounded-md text-sm font-medium text-white"
                    [class]="variantClass(opt.variant)"
                  >
                    {{ opt.label }}
                  </button>
                }
              </div>
            }
          }
        }
      </div>
    </div>
  `,
})
export class MagicLinkPageComponent implements OnInit {
  /** Bypasses the app's interceptors: the token is the only credential, so no session token or tenant header rides along. */
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly cdr = inject(ChangeDetectorRef);

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly preview = signal<ActionTokenPreview | null>(null);
  readonly submitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly result = signal<ActionTokenExecuteResult | null>(null);
  readonly selectedOption = signal<ActionTokenOption | null>(null);
  comments = '';

  // Token is read from the URL fragment so it never appears in server
  // logs, browser history auto-complete, or Referer headers.
  private token = '';

  ngOnInit(): void {
    this.token = extractFragmentToken();
    if (!this.token) {
      this.loading.set(false);
      this.loadError.set('This link is missing or malformed.');
      return;
    }

    this.http.post<ActionTokenPreview>(`${apiUrl}/api/public/links/preview`, { token: this.token })
      .subscribe({
        next: (dto) => {
          this.preview.set(dto);
          this.loading.set(false);
          this.cdr.markForCheck();
        },
        error: (err) => {
          this.loadError.set(err?.error?.message || 'This link is invalid, expired, or has already been used.');
          this.loading.set(false);
          this.cdr.markForCheck();
        },
      });
  }

  pickOption(option: ActionTokenOption): void {
    this.submitError.set(null);
    this.comments = '';
    this.selectedOption.set(option);
    // Auto-submit for options that don't need a comment.
    if (!option.requiresComments) {
      this.confirmOption(option);
    }
  }

  cancelOption(): void {
    if (this.submitting()) return;
    this.selectedOption.set(null);
    this.comments = '';
    this.submitError.set(null);
  }

  confirmOption(option: ActionTokenOption): void {
    if (this.submitting()) return;
    if (option.commentsRequired && !this.comments.trim()) {
      this.submitError.set('A comment is required.');
      return;
    }
    this.submitting.set(true);
    this.submitError.set(null);
    this.http
      .post<ActionTokenExecuteResult>(`${apiUrl}/api/public/links/redeem`, {
        token: this.token,
        option: option.key,
        comments: this.comments.trim() || undefined,
      })
      .subscribe({
        next: (res) => {
          this.result.set(res);
          this.submitting.set(false);
          this.cdr.markForCheck();
          // Drop the fragment from the URL so a refresh doesn't try to
          // re-redeem an already-consumed token.
          if (typeof window !== 'undefined' && window.history?.replaceState) {
            const cleanUrl = window.location.pathname + window.location.search;
            window.history.replaceState(null, '', cleanUrl);
          }
        },
        error: (err) => {
          this.submitting.set(false);
          this.submitError.set(err?.error?.message || 'Could not submit. The link may have just been used.');
          this.cdr.markForCheck();
        },
      });
  }

  variantClass(variant: string): string {
    switch (variant) {
      case 'danger':
        return 'bg-red-600 hover:bg-red-700';
      case 'warning':
        return 'bg-amber-600 hover:bg-amber-700';
      case 'neutral':
        return 'bg-gray-600 hover:bg-gray-700';
      default:
        return 'bg-primary-600 hover:bg-primary-700';
    }
  }
}

function extractFragmentToken(): string {
  if (typeof window === 'undefined') return '';
  // Fragment looks like "#tk=…" — parse robustly so future params
  // (signature, version, etc.) can be added without breaking older links.
  const hash = window.location.hash.replace(/^#/, '');
  if (!hash) return '';
  const params = new URLSearchParams(hash);
  return params.get('tk') ?? '';
}
