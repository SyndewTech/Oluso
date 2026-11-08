import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  Input,
  inject,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml, SafeResourceUrl } from '@angular/platform-browser';
import { firstValueFrom } from 'rxjs';
import { IconComponent } from '../icon/icon.component';
import { AuthFilePipe, FileUploadService } from '../../services/file-upload.service';
import { openFileUrl } from '../../utils/open-file';

export interface AttachmentLike {
  url: string;
  name: string;
  contentType?: string;
  sizeBytes?: number;
}

type Renderer = 'image' | 'pdf' | 'docx' | 'xlsx' | 'pptx' | 'unsupported';

/**
 * Single-attachment row with on-demand inline preview. Header (filename, size,
 * open-in-tab link, expand toggle) is always visible; the preview body is
 * collapsed by default and rendered only when the user clicks "Preview" so the
 * heavy doc-rendering libs (<c>mammoth</c>, <c>xlsx</c>) are loaded lazily.
 *
 * <para>Supported renderers:</para>
 * <list type="bullet">
 *   <item><c>image/*</c>: native <c>&lt;img&gt;</c></item>
 *   <item><c>application/pdf</c>: native <c>&lt;object&gt;</c></item>
 *   <item><c>.docx</c> via <c>mammoth</c> → HTML</item>
 *   <item><c>.xls / .xlsx</c> via <c>SheetJS</c> → HTML table per sheet</item>
 *   <item><c>.ppt / .pptx</c>: download-only (no viable client-side renderer)</item>
 *   <item>everything else: download-only</item>
 * </list>
 *
 * <para>URLs must already be SAS-signed / publicly fetchable by the time they
 * reach this component — it does no auth itself. For approval attachments the
 * controller mints fresh signed URLs on each read, so this contract holds.</para>
 */
@Component({
  selector: 'ui-attachment-preview',
  standalone: true,
  imports: [CommonModule, IconComponent, AuthFilePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="border border-gray-200 rounded-lg overflow-hidden">
      <div class="flex items-center justify-between gap-2 px-3 py-2 bg-gray-50 border-b border-gray-200">
        <div class="flex items-center gap-2 min-w-0">
          <ui-icon name="paper-clip" class="h-4 w-4 text-gray-500 shrink-0" />
          <span class="text-sm text-gray-900 truncate">{{ attachment.name }}</span>
          @if (attachment.sizeBytes) {
            <span class="text-xs text-gray-500 shrink-0">{{ formatBytes(attachment.sizeBytes) }}</span>
          }
        </div>
        <div class="flex items-center gap-3 shrink-0">
          @if (canPreview()) {
            <button
              type="button"
              (click)="toggle()"
              class="inline-flex items-center gap-1 text-xs text-primary-600 hover:text-primary-800"
            >
              <ui-icon [name]="expanded() ? 'chevron-up' : 'chevron-down'" class="h-3 w-3" />
              {{ expanded() ? 'Hide preview' : 'Preview' }}
            </button>
          }
          <button
            type="button"
            (click)="open()"
            [disabled]="opening()"
            class="inline-flex items-center gap-1 text-xs text-primary-600 hover:text-primary-800 disabled:opacity-50"
          >
            Open <ui-icon name="arrow-top-right-on-square" class="h-3 w-3" />
          </button>
          <button
            type="button"
            (click)="download()"
            [disabled]="downloading()"
            class="inline-flex items-center gap-1 text-xs text-primary-600 hover:text-primary-800 disabled:opacity-50"
          >
            <ui-icon name="download" class="h-3 w-3" /> Download
          </button>
        </div>
      </div>

      @if (expanded()) {
        <div class="bg-white">
          @if (loading()) {
            <div class="px-3 py-6 text-center text-xs text-gray-500">Loading preview…</div>
          } @else if (loadError()) {
            <div class="px-3 py-4 text-xs text-red-600">{{ loadError() }}</div>
          } @else if (renderer() === 'image') {
            <div class="bg-gray-100 flex items-center justify-center">
              <img [src]="attachment.url | authFile" [alt]="attachment.name" class="max-h-96 w-full object-contain" />
            </div>
          } @else if (renderer() === 'pdf') {
            @if (pdfSrc(); as src) {
              <object [data]="src" type="application/pdf" class="w-full h-[500px] bg-gray-100">
                <div class="p-4 text-sm text-gray-600">
                  Inline preview isn't supported in this browser —
                  <button type="button" (click)="open()" class="text-primary-600 hover:text-primary-800">open in a new tab</button>.
                </div>
              </object>
            }
          } @else if (renderer() === 'docx') {
            <div class="prose prose-sm max-w-none p-4 overflow-auto max-h-[500px]" [innerHTML]="docxHtml()"></div>
          } @else if (renderer() === 'xlsx') {
            <div class="p-4 overflow-auto max-h-[500px] space-y-4 text-xs">
              @for (sheet of sheets(); track sheet.name) {
                <div>
                  <div class="text-[11px] font-semibold uppercase tracking-wide text-gray-500 mb-1">{{ sheet.name }}</div>
                  <div class="border border-gray-200 rounded" [innerHTML]="sheet.html"></div>
                </div>
              }
            </div>
          } @else if (renderer() === 'pptx') {
            <div class="px-3 py-6 text-xs text-gray-500">
              Inline preview isn't available for PowerPoint files —
              <a [href]="attachment.url" target="_blank" rel="noopener" class="text-primary-600 hover:text-primary-800">download to view</a>.
            </div>
          } @else {
            <div class="px-3 py-6 text-xs text-gray-500">
              No inline preview for <span class="font-mono">{{ attachment.contentType || 'this file type' }}</span> —
              <a [href]="attachment.url" target="_blank" rel="noopener" class="text-primary-600 hover:text-primary-800">download to view</a>.
            </div>
          }
        </div>
      }
    </div>
  `,
})
export class AttachmentPreviewComponent {
  private readonly cdr = inject(ChangeDetectorRef);
  private readonly fileUpload = inject(FileUploadService);
  private readonly sanitizer = inject(DomSanitizer);

  @Input({ required: true }) attachment!: AttachmentLike;

  readonly expanded = signal(false);
  readonly loading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly sheets = signal<{ name: string; html: string }[]>([]);
  readonly docxHtml = signal<SafeHtml | null>(null);
  readonly pdfSrc = signal<SafeResourceUrl | null>(null);
  readonly opening = signal(false);
  readonly downloading = signal(false);

  renderer(): Renderer {
    return classifyAttachment(this.attachment);
  }

  canPreview(): boolean {
    return this.renderer() !== 'unsupported';
  }

  toggle(): void {
    const next = !this.expanded();
    this.expanded.set(next);
    if (next) {
      this.loadIfNeeded();
    }
  }

  private async loadIfNeeded(): Promise<void> {
    const r = this.renderer();

    if (r === 'image' || r === 'pptx' || r === 'unsupported') return;

    this.loading.set(true);
    this.loadError.set(null);
    this.cdr.markForCheck();

    try {
      if (r === 'pdf') {
        if (this.pdfSrc() === null) {
          const resolved = await firstValueFrom(this.fileUpload.getAccessUrl(this.attachment.url));
          if (!resolved) throw new Error('Could not resolve PDF URL');
          this.pdfSrc.set(this.sanitizer.bypassSecurityTrustResourceUrl(resolved));
        }
      } else if (r === 'docx') {
        if (this.docxHtml() === null) {
          const buf = await this.fetchArrayBuffer();
          const mammoth = await import('mammoth');
          const result = await mammoth.convertToHtml({ arrayBuffer: buf });
          this.docxHtml.set(this.sanitizer.bypassSecurityTrustHtml(result.value));
        }
      } else if (r === 'xlsx') {
        const buf = await this.fetchArrayBuffer();
        const XLSX = await import('xlsx');
        const wb = XLSX.read(buf, { type: 'array' });
        const rendered = wb.SheetNames.map((name) => ({
          name,
          html: XLSX.utils.sheet_to_html(wb.Sheets[name], { editable: false }),
        }));
        this.sheets.set(rendered);
      }
    } catch (err) {
      console.error('Attachment preview failed', err);
      this.loadError.set('Failed to render preview. Use Download to save the file.');
    } finally {
      this.loading.set(false);
      this.cdr.markForCheck();
    }
  }

  async open(): Promise<void> {
    if (this.opening()) return;
    this.opening.set(true);
    try {
      const resolved = await firstValueFrom(this.fileUpload.getAccessUrl(this.attachment.url));
      if (!resolved) throw new Error('Could not resolve attachment URL');
      await openFileUrl(resolved, this.attachment.name);
    } catch (err) {
      console.error('Open attachment failed', err);
    } finally {
      this.opening.set(false);
      this.cdr.markForCheck();
    }
  }

  async download(): Promise<void> {
    if (this.downloading()) return;
    this.downloading.set(true);
    try {
      const resolved = await firstValueFrom(this.fileUpload.resolveUrl(this.attachment.url));
      if (!resolved) throw new Error('Could not resolve attachment URL');
      const a = document.createElement('a');
      a.href = resolved;
      a.download = this.attachment.name || '';
      a.rel = 'noopener';
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
    } catch (err) {
      console.error('Download attachment failed', err);
    } finally {
      this.downloading.set(false);
      this.cdr.markForCheck();
    }
  }

  /**
   * Get the file's bytes for client-side parsing. Routes through
   * <c>FileUploadService.resolveUrl</c> so same-origin URLs are fetched with
   * auth (matching the <c>authFile</c> pipe pattern used elsewhere), while
   * external SAS URLs pass through unchanged. resolveUrl returns either:
   *   - a <c>blob:</c> URL (for auth-fetched same-origin assets) — fetching
   *     a blob URL is in-memory, no second HTTP round trip;
   *   - the original absolute URL (SAS/public CDN) — direct fetch.
   * Either way the resulting ArrayBuffer feeds mammoth / xlsx.
   */
  private async fetchArrayBuffer(): Promise<ArrayBuffer> {
    const resolved = await firstValueFrom(this.fileUpload.resolveUrl(this.attachment.url));
    if (!resolved) throw new Error('Could not resolve attachment URL');
    const res = await fetch(resolved);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    return await res.arrayBuffer();
  }

  formatBytes(bytes: number): string {
    if (!bytes) return '';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}

function classifyAttachment(att: AttachmentLike): Renderer {
  const ct = (att.contentType || '').toLowerCase();
  const name = (att.name || '').toLowerCase();

  if (ct.startsWith('image/')) return 'image';
  if (ct === 'application/pdf' || name.endsWith('.pdf')) return 'pdf';
  if (
    ct === 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' ||
    name.endsWith('.docx')
  ) return 'docx';
  if (
    ct === 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' ||
    ct === 'application/vnd.ms-excel' ||
    name.endsWith('.xlsx') ||
    name.endsWith('.xls')
  ) return 'xlsx';
  if (
    ct === 'application/vnd.openxmlformats-officedocument.presentationml.presentation' ||
    ct === 'application/vnd.ms-powerpoint' ||
    name.endsWith('.pptx') ||
    name.endsWith('.ppt')
  ) return 'pptx';
  return 'unsupported';
}
