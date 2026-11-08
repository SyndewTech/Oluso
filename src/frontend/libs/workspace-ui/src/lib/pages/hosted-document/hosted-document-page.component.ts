import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { LocaleDatePipe } from '@oluso/ui-core';
import { apiUrl } from '../../auth/oidc.config';

/**
 * Anonymous hosted view of a customer-facing document (an invoice today,
 * quotes next). The magic-link token comes from the URL fragment
 * (#tk=…&dl=1) so it never reaches server logs. The page previews the token
 * through the public links API, renders the document it carries, offers the
 * PDF through the file endpoint, and shows any actions the handler exposes.
 */
interface DocumentParty {
  name: string;
  contactName?: string;
  addressLines: string[];
  email?: string;
  phone?: string;
  taxId?: string;
}

interface DocumentLine {
  lineNumber: number;
  description: string;
  productCode?: string;
  quantity: number;
  unitPrice: number;
  discount: number;
  taxRate?: number;
  tax: number;
  amount: number;
}

interface HostedDocument {
  kind: string;
  number: string;
  status: string;
  statusLabel?: string;
  payable?: boolean;
  acceptedByName?: string;
  purchaseOrderNumber?: string;
  currencyCode: string;
  issueDate: string;
  dueDate?: string;
  dueLabel: string;
  company: DocumentParty;
  customer: DocumentParty;
  lines: DocumentLine[];
  surcharges: DocumentLine[];
  subtotal: number;
  discount: number;
  tax: number;
  fees: number;
  total: number;
  amountPaid: number;
  balanceDue: number;
  notes?: string;
  terms?: string;
  paymentInstructions?: string;
  footer?: string;
  branding?: { accentColor?: string; logoUrl?: string };
}

interface DocumentField {
  key: string;
  label: string;
  required?: boolean;
  placeholder?: string;
  maxLength?: number;
}

interface DocumentOption {
  key: string;
  label: string;
  variant: string;
  requiresComments?: boolean;
  commentsRequired?: boolean;
  fields?: DocumentField[];
}

interface DocumentPreview {
  title: string;
  summary?: string;
  description?: string;
  options: DocumentOption[];
  alreadyActioned: boolean;
  alreadyActionedReason?: string;
  locked?: boolean;
  lockMode?: 'email' | 'code';
  data?: { document?: HostedDocument; branding?: { accentColor?: string; logoUrl?: string } };
}

/** The unlock proof is kept per link in this browser so reopening does not ask again. */
function unlockStorageKey(token: string): string {
  return `oluso.link-unlock.${token.slice(0, 16)}`;
}

function readUnlock(token: string): string | null {
  try {
    return localStorage.getItem(unlockStorageKey(token));
  } catch {
    return null;
  }
}

function storeUnlock(token: string, proof: string): void {
  try {
    localStorage.setItem(unlockStorageKey(token), proof);
  } catch {
    // Private windows can refuse storage; the visitor unlocks again next time.
  }
}

interface UnlockResponse {
  unlock?: string;
  codeSent?: boolean;
  message?: string;
}

interface ExecuteResult {
  message: string;
}

@Component({
  selector: 'workspace-hosted-document-page',
  standalone: true,
  imports: [CommonModule, LocaleDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="min-h-screen bg-gray-100 py-8 px-4" [style]="brandStyle()">
      <div class="mx-auto max-w-3xl">
        @if (loading()) {
          <div class="bg-white rounded-lg shadow p-8 text-center">
            <div class="inline-block h-8 w-8 rounded-full border-2 border-primary-600 border-t-transparent animate-spin"></div>
            <p class="mt-3 text-sm text-gray-500">Loading…</p>
          </div>
        } @else if (loadError()) {
          <div class="bg-white rounded-lg shadow p-8 text-center">
            <h1 class="text-lg font-semibold text-gray-900 mb-2">Link unavailable</h1>
            <p class="text-sm text-gray-600">{{ loadError() }}</p>
          </div>
        } @else if (preview()?.locked) {
          @if (branding()?.logoUrl && !logoFailed()) {
            <div class="mb-4">
              <img [src]="branding()!.logoUrl" alt="" (error)="logoFailed.set(true)" class="max-h-12 max-w-[12rem] object-contain" />
            </div>
          }
          <div class="bg-white rounded-lg shadow p-6 sm:p-8 max-w-md">
            <h1 class="text-lg font-semibold text-gray-900">{{ preview()?.title }}</h1>
            <p class="mt-1 text-sm text-gray-600">
              @if (codeSent()) {
                Enter the 6-digit code we emailed to {{ unlockEmail }}.
              } @else {
                To keep it private, confirm the email address this was sent to.
              }
            </p>
            <form class="mt-5 space-y-3" (submit)="submitUnlock($event)">
              @if (!codeSent()) {
                <div>
                  <label for="unlock-email" class="block text-sm font-medium text-gray-700 mb-1">Email address</label>
                  <input id="unlock-email" type="email" autocomplete="email" [value]="unlockEmail" (input)="unlockEmail = inputValue($event)"
                    class="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:ring-primary-500" />
                </div>
              } @else {
                <div>
                  <label for="unlock-code" class="block text-sm font-medium text-gray-700 mb-1">Code</label>
                  <input id="unlock-code" inputmode="numeric" autocomplete="one-time-code" maxlength="6" [value]="unlockCode" (input)="unlockCode = inputValue($event)"
                    class="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm tracking-widest focus:border-primary-500 focus:ring-primary-500" />
                </div>
              }
              @if (unlockMessage()) {
                <p class="text-sm" [class]="unlockFailed() ? 'text-red-700' : 'text-gray-600'">{{ unlockMessage() }}</p>
              }
              <div class="flex items-center gap-3">
                <button type="submit" [disabled]="unlocking() || (codeSent() ? unlockCode.trim().length !== 6 : !unlockEmail.trim())"
                  class="px-3 py-2 rounded-md text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 disabled:opacity-50">
                  {{ unlocking() ? 'Checking…' : codeSent() ? 'Open' : preview()?.lockMode === 'code' ? 'Send code' : 'Open' }}
                </button>
                @if (codeSent()) {
                  <button type="button" (click)="resendCode()" [disabled]="unlocking()" class="text-sm text-gray-600 hover:underline">Send a new code</button>
                  <button type="button" (click)="changeEmail()" [disabled]="unlocking()" class="text-sm text-gray-600 hover:underline">Use a different email</button>
                }
              </div>
            </form>
          </div>
        } @else if (document(); as doc) {
          @if (doc.branding?.logoUrl && !logoFailed()) {
            <div class="mb-4">
              <img [src]="doc.branding!.logoUrl" [alt]="doc.company.name" (error)="logoFailed.set(true)" class="max-h-12 max-w-[12rem] object-contain" />
            </div>
          }
          <!-- Phones: title, then the response buttons side by side, then the download full width. -->
          <div class="flex flex-col gap-3 mb-4 sm:flex-row sm:items-center sm:justify-between">
            <div class="min-w-0">
              <h1 class="text-xl font-semibold text-gray-900 break-words">{{ doc.kind }} {{ doc.number }}</h1>
              @if (preview()?.summary) {
                <p class="text-sm text-gray-600">{{ preview()?.summary }}</p>
              }
            </div>
            <div class="grid grid-cols-2 gap-2 sm:flex sm:items-center sm:shrink-0">
              <button
                type="button"
                (click)="downloadPdf()"
                [disabled]="downloading()"
                class="col-span-2 order-last sm:order-none px-3 py-2.5 sm:py-2 rounded-md text-sm font-medium border border-primary-600 text-primary-700 bg-white hover:bg-primary-50 disabled:opacity-50 whitespace-nowrap"
              >
                {{ downloading() ? 'Preparing…' : 'Download PDF' }}
              </button>
              @for (opt of preview()?.options ?? []; track opt.key) {
                <button
                  type="button"
                  (click)="act(opt)"
                  [disabled]="submitting()"
                  class="px-3 py-2.5 sm:py-2 rounded-md text-sm font-medium text-white disabled:opacity-50 whitespace-nowrap"
                  [class.col-span-2]="(preview()?.options?.length ?? 0) === 1"
                  [class]="variantClass(opt.variant)"
                >
                  {{ opt.label }}
                </button>
              }
            </div>
          </div>

          @if (pendingOption(); as pending) {
            <div class="bg-gray-50 border border-gray-200 rounded-md p-4 mb-4 space-y-3">
              @for (field of pending.fields ?? []; track field.key) {
                <div>
                  <label [for]="'field-' + field.key" class="block text-sm font-medium text-gray-700 mb-1">{{ field.label }}</label>
                  <input
                    [id]="'field-' + field.key"
                    type="text"
                    [value]="fieldValues[field.key] ?? ''"
                    (input)="onFieldInput(field.key, $event)"
                    [attr.maxlength]="field.maxLength ?? 200"
                    [placeholder]="field.placeholder ?? ''"
                    class="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:ring-primary-500"
                  />
                </div>
              }
              @if (pending.requiresComments) {
                <div>
                  <label for="option-comments" class="block text-sm font-medium text-gray-700 mb-1">
                    {{ pending.label }} — add a note{{ pending.commentsRequired ? '' : ' (optional)' }}
                  </label>
                  <textarea
                    id="option-comments"
                    rows="3"
                    [value]="comments"
                    (input)="onCommentsInput($event)"
                    class="block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-primary-500 focus:ring-primary-500"
                    placeholder="Let them know why"
                  ></textarea>
                </div>
              }
              <div class="flex items-center gap-2">
                <button
                  type="button"
                  (click)="submit(pending)"
                  [disabled]="submitting() || !canSubmit(pending)"
                  class="px-3 py-2 rounded-md text-sm font-medium text-white disabled:opacity-50"
                  [class]="variantClass(pending.variant)"
                >
                  {{ submitting() ? 'Sending…' : 'Confirm ' + pending.label.toLowerCase() }}
                </button>
                <button
                  type="button"
                  (click)="cancelPending()"
                  [disabled]="submitting()"
                  class="px-3 py-2 rounded-md text-sm font-medium border border-gray-300 text-gray-700 bg-white hover:bg-gray-50 disabled:opacity-50"
                >
                  Cancel
                </button>
              </div>
            </div>
          }
          @if (actionResult(); as r) {
            <div class="bg-emerald-50 border border-emerald-200 rounded-md p-4 mb-4 text-sm text-emerald-800">{{ r.message }}</div>
          }
          @if (actionError(); as err) {
            <div class="bg-red-50 border border-red-200 rounded-md p-4 mb-4 text-sm text-red-700">{{ err }}</div>
          }
          @if (preview()?.alreadyActioned) {
            <div class="bg-gray-50 border border-gray-200 rounded-md p-4 mb-4 text-sm text-gray-600">
              {{ preview()?.alreadyActionedReason || 'This document has already been actioned.' }}
            </div>
          }

          <div class="bg-white rounded-lg shadow p-4 sm:p-8">
            <div class="flex flex-col gap-4 mb-6 sm:flex-row sm:justify-between sm:gap-8 sm:mb-8">
              <div class="text-sm">
                <div class="text-base font-semibold text-gray-900">{{ doc.company.name }}</div>
                @for (line of doc.company.addressLines; track $index) {
                  <div class="text-gray-600">{{ line }}</div>
                }
                @if (doc.company.taxId) {
                  <div class="text-gray-600">Tax ID: {{ doc.company.taxId }}</div>
                }
              </div>
              <div class="text-sm sm:text-right">
                <div class="text-2xl font-bold text-primary-700 uppercase">{{ doc.kind }}</div>
                <div class="font-semibold text-gray-900">{{ doc.number }}</div>
                <div class="text-gray-600 mt-1">Date: {{ doc.issueDate | localeDate: 'mediumDate' }}</div>
                @if (doc.dueDate) {
                  <div class="text-gray-600">{{ doc.dueLabel }}: {{ doc.dueDate | localeDate: 'mediumDate' }}</div>
                }
                <div class="text-gray-500">Status: {{ doc.statusLabel || doc.status }}</div>
                @if (doc.purchaseOrderNumber) {
                  <div class="text-gray-500">PO: {{ doc.purchaseOrderNumber }}</div>
                }
              </div>
            </div>

            <div class="text-sm mb-6">
              <div class="text-xs font-medium uppercase tracking-wide text-gray-500 mb-1">Bill to</div>
              <div class="font-semibold text-gray-900">{{ doc.customer.name }}</div>
              @if (doc.customer.contactName) {
                <div class="text-gray-700">{{ doc.customer.contactName }}</div>
              }
              @for (line of doc.customer.addressLines; track $index) {
                <div class="text-gray-600">{{ line }}</div>
              }
              @if (doc.customer.email) {
                <div class="text-gray-600">{{ doc.customer.email }}</div>
              }
            </div>

            <!-- Phones keep two columns: quantity, price and tax move under the description. -->
            <table class="w-full text-sm">
              <thead>
                <tr class="border-b border-gray-300 text-left text-gray-600">
                  <th class="hidden sm:table-cell py-2 pr-2 w-8">#</th>
                  <th class="py-2 pr-2">Description</th>
                  <th class="hidden sm:table-cell py-2 pr-2 text-right">Qty</th>
                  <th class="hidden sm:table-cell py-2 pr-2 text-right">Unit price</th>
                  <th class="hidden sm:table-cell py-2 pr-2 text-right">Tax</th>
                  <th class="py-2 text-right">Amount</th>
                </tr>
              </thead>
              <tbody>
                @for (line of doc.lines; track line.lineNumber) {
                  <tr class="border-b border-gray-100">
                    <td class="hidden sm:table-cell py-2 pr-2 text-gray-500">{{ line.lineNumber }}</td>
                    <td class="py-2 pr-2">
                      <div class="text-gray-900">{{ line.description }}</div>
                      @if (line.productCode) {
                        <div class="text-xs text-gray-500">{{ line.productCode }}</div>
                      }
                      @if (line.discount > 0) {
                        <div class="text-xs text-gray-500">Discount {{ money(line.discount, doc.currencyCode) }}</div>
                      }
                      <div class="sm:hidden text-xs text-gray-500">
                        {{ line.quantity }} × {{ money(line.unitPrice, doc.currencyCode) }} · Tax {{ money(line.tax, doc.currencyCode) }}{{ line.taxRate ? ' (' + line.taxRate + '%)' : '' }}
                      </div>
                    </td>
                    <td class="hidden sm:table-cell py-2 pr-2 text-right">{{ line.quantity }}</td>
                    <td class="hidden sm:table-cell py-2 pr-2 text-right">{{ money(line.unitPrice, doc.currencyCode) }}</td>
                    <td class="hidden sm:table-cell py-2 pr-2 text-right">
                      {{ money(line.tax, doc.currencyCode) }}
                      @if (line.taxRate) {
                        <div class="text-xs text-gray-500">{{ line.taxRate }}%</div>
                      }
                    </td>
                    <td class="py-2 text-right whitespace-nowrap align-top">{{ money(line.amount, doc.currencyCode) }}</td>
                  </tr>
                }
                @for (s of doc.surcharges; track $index) {
                  <tr class="border-b border-gray-100">
                    <td class="hidden sm:table-cell py-2 pr-2"></td>
                    <td class="py-2 pr-2 text-gray-900">
                      {{ s.description }}
                      <div class="sm:hidden text-xs text-gray-500">Tax {{ money(s.tax, doc.currencyCode) }}</div>
                    </td>
                    <td class="hidden sm:table-cell py-2 pr-2 text-right">1</td>
                    <td class="hidden sm:table-cell py-2 pr-2 text-right">{{ money(s.unitPrice, doc.currencyCode) }}</td>
                    <td class="hidden sm:table-cell py-2 pr-2 text-right">{{ money(s.tax, doc.currencyCode) }}</td>
                    <td class="py-2 text-right whitespace-nowrap align-top">{{ money(s.amount, doc.currencyCode) }}</td>
                  </tr>
                }
              </tbody>
            </table>

            <div class="flex justify-end mt-6">
              <div class="w-full sm:w-72 text-sm">
                <div class="flex justify-between py-1 border-b border-gray-100"><span>Subtotal</span><span>{{ money(doc.subtotal, doc.currencyCode) }}</span></div>
                @if (doc.discount > 0) {
                  <div class="flex justify-between py-1 border-b border-gray-100"><span>Discount</span><span>-{{ money(doc.discount, doc.currencyCode) }}</span></div>
                }
                @if (doc.fees > 0) {
                  <div class="flex justify-between py-1 border-b border-gray-100"><span>Fees</span><span>{{ money(doc.fees, doc.currencyCode) }}</span></div>
                }
                <div class="flex justify-between py-1 border-b border-gray-100"><span>Tax</span><span>{{ money(doc.tax, doc.currencyCode) }}</span></div>
                <div class="flex justify-between py-1 border-b border-gray-100 font-semibold"><span>Total</span><span>{{ money(doc.total, doc.currencyCode) }}</span></div>
                @if (doc.amountPaid > 0) {
                  <div class="flex justify-between py-1 border-b border-gray-100"><span>Paid</span><span>-{{ money(doc.amountPaid, doc.currencyCode) }}</span></div>
                }
                @if (doc.kind === 'Invoice') {
                  <div class="flex justify-between py-1 font-semibold text-primary-700"><span>Balance due</span><span>{{ money(doc.balanceDue, doc.currencyCode) }}</span></div>
                }
              </div>
            </div>

            @if (doc.paymentInstructions && doc.payable !== false) {
              <div class="mt-8 text-sm">
                <div class="text-xs font-medium uppercase tracking-wide text-gray-500 mb-1">How to pay</div>
                <div class="text-gray-800 whitespace-pre-line">{{ doc.paymentInstructions }}</div>
              </div>
            }
            @if (doc.notes) {
              <div class="mt-6 text-sm">
                <div class="text-xs font-medium uppercase tracking-wide text-gray-500 mb-1">Notes</div>
                <div class="text-gray-800 whitespace-pre-line">{{ doc.notes }}</div>
              </div>
            }
            @if (doc.terms) {
              <div class="mt-6 text-sm">
                <div class="text-xs font-medium uppercase tracking-wide text-gray-500 mb-1">Terms</div>
                <div class="text-gray-800 whitespace-pre-line">{{ doc.terms }}</div>
              </div>
            }
            @if (doc.footer) {
              <div class="mt-8 pt-4 border-t border-gray-200 text-center text-xs text-gray-500">{{ doc.footer }}</div>
            }
          </div>
        }
      </div>
    </div>
  `,
})
export class HostedDocumentPageComponent implements OnInit {
  /** Bypasses the app's interceptors: the token is the only credential, so no session token or tenant header rides along. */
  private readonly http = new HttpClient(inject(HttpBackend));
  private readonly cdr = inject(ChangeDetectorRef);

  readonly loading = signal(true);
  readonly loadError = signal<string | null>(null);
  readonly preview = signal<DocumentPreview | null>(null);
  readonly document = signal<HostedDocument | null>(null);
  readonly downloading = signal(false);
  readonly submitting = signal(false);
  readonly pendingOption = signal<DocumentOption | null>(null);
  comments = '';
  fieldValues: Record<string, string> = {};
  readonly logoFailed = signal(false);
  readonly branding = computed(() => this.document()?.branding ?? this.preview()?.data?.branding ?? null);

  unlockEmail = '';
  unlockCode = '';
  private unlockProof: string | null = null;
  readonly unlocking = signal(false);
  readonly codeSent = signal(false);
  readonly unlockMessage = signal<string | null>(null);
  readonly unlockFailed = signal(false);

  /**
   * The tenant's colour in place of the host app's theme on this page: the
   * primary palette is derived from the one accent colour the tenant set.
   */
  readonly brandStyle = computed(() => {
    const accent = this.branding()?.accentColor;
    if (!accent || !/^#([0-9a-f]{3}|[0-9a-f]{6})$/i.test(accent)) return '';
    return [
      `--color-primary-50: color-mix(in srgb, ${accent} 8%, white)`,
      `--color-primary-100: color-mix(in srgb, ${accent} 16%, white)`,
      `--color-primary-500: color-mix(in srgb, ${accent} 85%, white)`,
      `--color-primary-600: ${accent}`,
      `--color-primary-700: color-mix(in srgb, ${accent} 82%, black)`,
    ].join('; ');
  });
  readonly actionResult = signal<ExecuteResult | null>(null);
  readonly actionError = signal<string | null>(null);

  private token = '';
  private autoDownload = false;

  ngOnInit(): void {
    const fragment = readFragment();
    this.token = fragment.get('tk') ?? '';
    this.autoDownload = fragment.get('dl') === '1';

    if (!this.token) {
      this.loading.set(false);
      this.loadError.set('This link is missing or malformed.');
      return;
    }

    this.unlockProof = readUnlock(this.token);
    this.loadPreview();
  }

  private loadPreview(): void {
    this.http.post<DocumentPreview>(`${apiUrl}/api/public/links/preview`, { token: this.token, unlock: this.unlockProof }).subscribe({
      next: (dto) => {
        this.preview.set(dto);
        this.document.set(dto.data?.document ?? null);
        if (!dto.locked && !dto.data?.document) {
          this.loadError.set(dto.alreadyActionedReason || 'This link does not carry a document.');
        }
        this.loading.set(false);
        this.cdr.markForCheck();
        if (this.autoDownload && dto.data?.document) {
          this.downloadPdf();
        }
      },
      error: (err) => {
        this.loadError.set(err?.error?.message || 'This link is invalid, expired, or has already been used.');
        this.loading.set(false);
        this.cdr.markForCheck();
      },
    });
  }

  inputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  submitUnlock(event: Event): void {
    event.preventDefault();
    this.unlock(this.codeSent() ? this.unlockCode.trim() : null);
  }

  resendCode(): void {
    this.unlockCode = '';
    this.unlock(null);
  }

  changeEmail(): void {
    this.codeSent.set(false);
    this.unlockCode = '';
    this.unlockMessage.set(null);
    this.unlockFailed.set(false);
  }

  private unlock(code: string | null): void {
    if (this.unlocking()) return;
    this.unlocking.set(true);
    this.unlockMessage.set(null);
    this.unlockFailed.set(false);
    this.http
      .post<UnlockResponse>(`${apiUrl}/api/public/links/unlock`, { token: this.token, email: this.unlockEmail.trim(), code })
      .subscribe({
        next: (res) => {
          this.unlocking.set(false);
          if (res.unlock) {
            this.unlockProof = res.unlock;
            storeUnlock(this.token, res.unlock);
            this.loading.set(true);
            this.loadPreview();
          } else if (res.codeSent) {
            this.codeSent.set(true);
            this.unlockMessage.set(res.message ?? null);
          } else {
            this.loadPreview();
          }
          this.cdr.markForCheck();
        },
        error: (err) => {
          this.unlocking.set(false);
          this.unlockFailed.set(true);
          this.unlockMessage.set(err?.error?.message || err?.error?.Message || 'That did not work. Try again.');
          this.cdr.markForCheck();
        },
      });
  }

  downloadPdf(): void {
    if (this.downloading()) return;
    this.downloading.set(true);
    this.http
      .post(`${apiUrl}/api/public/links/file`, { token: this.token, unlock: this.unlockProof }, { responseType: 'blob', observe: 'response' })
      .subscribe({
        next: (res) => {
          const doc = this.document();
          const name = fileNameFrom(res.headers.get('content-disposition')) ?? `${doc?.kind ?? 'document'}-${doc?.number ?? ''}.pdf`;
          const url = URL.createObjectURL(res.body as Blob);
          const a = window.document.createElement('a');
          a.href = url;
          a.download = name;
          a.click();
          URL.revokeObjectURL(url);
          this.downloading.set(false);
          this.cdr.markForCheck();
        },
        error: () => {
          this.actionError.set('The PDF could not be downloaded.');
          this.downloading.set(false);
          this.cdr.markForCheck();
        },
      });
  }

  act(option: DocumentOption): void {
    if (this.submitting()) return;
    if (option.requiresComments || (option.fields?.length ?? 0) > 0) {
      this.comments = '';
      this.fieldValues = {};
      this.pendingOption.set(option);
      return;
    }
    this.submit(option);
  }

  cancelPending(): void {
    this.pendingOption.set(null);
    this.comments = '';
    this.fieldValues = {};
  }

  onFieldInput(key: string, event: Event): void {
    this.fieldValues = { ...this.fieldValues, [key]: (event.target as HTMLInputElement).value };
  }

  canSubmit(option: DocumentOption): boolean {
    if (option.commentsRequired && !this.comments.trim()) return false;
    return (option.fields ?? []).every((f) => !f.required || (this.fieldValues[f.key] ?? '').trim() !== '');
  }

  onCommentsInput(event: Event): void {
    this.comments = (event.target as HTMLTextAreaElement).value;
  }

  submit(option: DocumentOption): void {
    if (this.submitting()) return;
    if (!this.canSubmit(option)) return;
    this.submitting.set(true);
    this.actionError.set(null);
    const comments = this.comments.trim() || null;
    const fields: Record<string, string> = {};
    for (const field of option.fields ?? []) {
      const value = (this.fieldValues[field.key] ?? '').trim();
      if (value) fields[field.key] = value;
    }
    this.http
      .post<ExecuteResult>(`${apiUrl}/api/public/links/redeem`, { token: this.token, option: option.key, comments, fields, unlock: this.unlockProof })
      .subscribe({
        next: (res) => {
          this.pendingOption.set(null);
          this.comments = '';
          this.fieldValues = {};
          this.actionResult.set(res);
          this.preview.update((p) => (p ? { ...p, options: [], alreadyActioned: true, alreadyActionedReason: res.message } : p));
          this.submitting.set(false);
          this.cdr.markForCheck();
        },
        error: (err) => {
          this.actionError.set(err?.error?.message || err?.error?.error || 'The action could not be completed.');
          this.submitting.set(false);
          this.cdr.markForCheck();
        },
      });
  }

  /** Symbol formatting in the viewer's locale ("£1,234.56"); falls back to the code when the browser does not know it. */
  money(amount: number, currency: string): string {
    try {
      return new Intl.NumberFormat(undefined, { style: 'currency', currency, currencyDisplay: 'narrowSymbol' }).format(amount);
    } catch {
      return `${amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`;
    }
  }

  variantClass(variant: string): string {
    switch (variant) {
      case 'danger':
        return 'bg-red-600 hover:bg-red-700';
      case 'secondary':
        return 'bg-gray-600 hover:bg-gray-700';
      default:
        return 'bg-primary-600 hover:bg-primary-700';
    }
  }
}

function readFragment(): URLSearchParams {
  if (typeof window === 'undefined') return new URLSearchParams();
  return new URLSearchParams(window.location.hash.replace(/^#/, ''));
}

function fileNameFrom(contentDisposition: string | null): string | null {
  if (!contentDisposition) return null;
  const utf8 = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (utf8) return decodeURIComponent(utf8[1]);
  const plain = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return plain ? plain[1] : null;
}
