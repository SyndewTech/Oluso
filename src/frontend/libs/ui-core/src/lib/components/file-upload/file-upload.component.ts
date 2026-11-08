import { Component, input, output, signal, inject, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { FormsModule } from '@angular/forms';
import { map, Observable, Subscription, forkJoin } from 'rxjs';
import { FileUploadService, FileUploadResult } from '../../services/file-upload.service';

export interface FileUploadEvent {
  attachment: FileUploadResult;
  folder?: string;
}

interface QueuedFile {
  file: File;
  preview: string | null;
}

@Component({
  selector: 'ui-file-upload',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <div class="file-upload space-y-3">
      @if (showFolder()) {
        <div class="flex items-center gap-2">
          <label class="text-xs font-medium text-gray-500 whitespace-nowrap">Folder</label>
          <input
            type="text"
            [(ngModel)]="folder"
            [placeholder]="folderPlaceholder()"
            class="block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-sm py-1 px-2"
          />
        </div>
      }

      <!-- Drop zone + file picker -->
      <div
        class="border-2 border-dashed rounded-lg p-6 text-center transition-colors"
        [class]="dragOver() ? 'border-primary-400 bg-primary-50' : 'border-gray-300 hover:border-gray-400'"
        (dragover)="onDragOver($event)"
        (dragleave)="dragOver.set(false)"
        (drop)="onDrop($event)"
      >
        <svg class="mx-auto h-10 w-10 text-gray-300" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
          <path stroke-linecap="round" stroke-linejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
        </svg>
        <p class="mt-2 text-sm text-gray-600">
          Drag & drop files here, or
          <label class="text-primary-600 hover:text-primary-800 font-medium cursor-pointer">
            browse
            <input
              type="file"
              class="hidden"
              [accept]="accept()"
              [multiple]="multiple()"
              [disabled]="uploading()"
              (change)="onFilesSelected($event)"
            />
          </label>
        </p>
        <p class="mt-1 text-xs text-gray-400">{{ accept() }}</p>
      </div>

      <!-- Queued files (not yet uploaded) -->
      @if (queue().length > 0) {
        <div class="space-y-2">
          <div class="flex items-center justify-between">
            <span class="text-sm font-medium text-gray-700">{{ queue().length }} file{{ queue().length > 1 ? 's' : '' }} ready</span>
            <div class="flex items-center gap-2">
              <button
                (click)="clearQueue()"
                class="text-xs text-gray-500 hover:text-gray-700"
              >Clear all</button>
              <button
                (click)="uploadAll()"
                [disabled]="uploading()"
                class="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm font-medium text-white bg-primary-600 hover:bg-primary-700 rounded-md disabled:opacity-50 transition-colors"
              >
                @if (uploading()) {
                  <div class="h-3.5 w-3.5 border-2 border-white border-t-transparent rounded-full animate-spin"></div>
                  Uploading...
                } @else {
                  <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M3 16.5v2.25A2.25 2.25 0 005.25 21h13.5A2.25 2.25 0 0021 18.75V16.5m-13.5-9L12 3m0 0l4.5 4.5M12 3v13.5" />
                  </svg>
                  Upload {{ queue().length > 1 ? 'All' : '' }}
                }
              </button>
            </div>
          </div>
          <ul class="space-y-1">
            @for (item of queue(); track $index) {
              <li class="flex items-center gap-2 text-sm text-gray-700 bg-gray-50 rounded px-2 py-1.5">
                @if (item.preview) {
                  <img [src]="item.preview" [alt]="item.file.name" class="h-8 w-8 object-cover rounded border border-gray-200" />
                } @else {
                  <svg class="h-5 w-5 text-gray-400 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z" />
                  </svg>
                }
                <span class="truncate flex-1">{{ item.file.name }}</span>
                <span class="text-xs text-gray-400 whitespace-nowrap">{{ formatSize(item.file.size) }}</span>
                <button (click)="removeFromQueue($index)" class="text-gray-400 hover:text-red-500 flex-shrink-0">
                  <svg class="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </li>
            }
          </ul>
        </div>
      }

      @if (errorMessage()) {
        <p class="text-xs text-red-500">{{ errorMessage() }}</p>
      }

      <!-- Already uploaded files -->
      @if (uploadedFiles().length > 0) {
        <div class="space-y-1">
          <span class="text-xs font-medium text-gray-500">Uploaded</span>
          <ul class="space-y-1">
            @for (file of uploadedFiles(); track file.imageUrl) {
              <li class="flex items-center gap-2 text-sm text-green-700 bg-green-50 rounded px-2 py-1.5">
                <svg class="h-4 w-4 text-green-500 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke-width="2" stroke="currentColor">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M4.5 12.75l6 6 9-13.5" />
                </svg>
                <span class="truncate flex-1">{{ file.fileName }}</span>
                <span class="text-xs text-green-400">{{ formatSize(file.sizeBytes) }}</span>
                <button (click)="removeFile(file)" class="text-green-400 hover:text-red-500 flex-shrink-0">
                  <svg class="h-3.5 w-3.5" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </li>
            }
          </ul>
        </div>
      }
    </div>
  `,
})
export class FileUploadComponent implements OnDestroy {
  private readonly fileUpload = inject(FileUploadService);
  private readonly http = inject(HttpClient);
  private subs: Subscription[] = [];

  /** Label for the upload button */
  readonly label = input('Choose File');

  /** Accepted file types (e.g. "image/*,.pdf") */
  readonly accept = input('image/*,.pdf');

  /** Allow multiple file selection */
  readonly multiple = input(false);

  /** Show the folder input field */
  readonly showFolder = input(false);

  /** Placeholder for folder input */
  readonly folderPlaceholder = input('e.g. Receipts/March');

  /** Default folder value */
  readonly defaultFolder = input<string | undefined>(undefined);

  /** Custom upload URL. When set, uploads via multipart POST to this URL instead of the default SAS/files flow. */
  readonly uploadUrl = input<string | undefined>(undefined);

  /**
   * Access level for the uploaded asset. 'public' produces a stable, anonymous,
   * CDN-frontable URL (for storefront images, avatars); 'private' (default) uses
   * the SAS/authorized flow. Public-ness is also enforced server-side by folder.
   */
  readonly access = input<'public' | 'private'>('private');

  /** Optional sub-path placed after the tenant in the storage key (e.g. a productId). */
  readonly subPath = input<string | undefined>(undefined);

  /** Emitted for each file successfully uploaded */
  readonly fileUploaded = output<FileUploadEvent>();

  /** Emitted when a file is removed from the uploaded list */
  readonly fileRemoved = output<FileUploadResult>();

  uploading = signal(false);
  errorMessage = signal('');
  queue = signal<QueuedFile[]>([]);
  uploadedFiles = signal<FileUploadResult[]>([]);
  dragOver = signal(false);

  folder = '';

  ngOnInit(): void {
    const def = this.defaultFolder();
    if (def) this.folder = def;
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver.set(true);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    event.stopPropagation();
    this.dragOver.set(false);
    const files = event.dataTransfer?.files;
    if (files && files.length > 0) {
      this.addToQueue(files);
    }
  }

  onFilesSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const files = input.files;
    if (!files || files.length === 0) return;
    this.addToQueue(files);
    input.value = '';
  }

  private addToQueue(files: FileList): void {
    this.errorMessage.set('');
    const items: QueuedFile[] = [];
    for (let i = 0; i < files.length; i++) {
      const file = files[i];
      const preview = this.isImage(file.name) ? URL.createObjectURL(file) : null;
      items.push({ file, preview });
    }
    this.queue.update(q => [...q, ...items]);
  }

  removeFromQueue(index: number): void {
    this.queue.update(q => {
      const item = q[index];
      if (item.preview) URL.revokeObjectURL(item.preview);
      return q.filter((_, i) => i !== index);
    });
  }

  clearQueue(): void {
    this.queue().forEach(item => {
      if (item.preview) URL.revokeObjectURL(item.preview);
    });
    this.queue.set([]);
  }

  uploadAll(): void {
    const items = this.queue();
    if (items.length === 0) return;

    this.uploading.set(true);
    this.errorMessage.set('');
    const folder = this.folder.trim() || undefined;

    const uploads$ = items.map(item => {
      return this.uploadUrl()
        ? this.uploadToCustomUrl(item.file, folder)
        : this.fileUpload.upload(item.file, folder, { access: this.access(), subPath: this.subPath() });
    });

    const sub = forkJoin(uploads$).subscribe({
      next: (results) => {
        for (const result of results) {
          this.uploadedFiles.update(list => [...list, result]);
          this.fileUploaded.emit({ attachment: result, folder });
        }
        this.clearQueue();
        this.uploading.set(false);
      },
      error: (err) => {
        console.error('Upload failed:', err);
        this.errorMessage.set('Some files failed to upload. Please try again.');
        this.uploading.set(false);
      },
    });
    this.subs.push(sub);
  }

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  private uploadToCustomUrl(file: File, folder?: string): Observable<FileUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    let url = this.uploadUrl()!;
    if (folder) url += `${url.includes('?') ? '&' : '?'}folder=${encodeURIComponent(folder)}`;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    return this.http.post<any>(url, formData).pipe(
      map((res: any): FileUploadResult => ({
        imageUrl: res.id ? `${this.uploadUrl()}/${res.id}/download` : '',
        fileName: (res.fileName as string) || file.name,
        sizeBytes: (res.sizeBytes as number) || file.size,
      })),
    );
  }

  removeFile(file: FileUploadResult): void {
    this.uploadedFiles.update(list => list.filter(f => f.imageUrl !== file.imageUrl));
    this.fileRemoved.emit(file);
  }

  isImage(fileName: string): boolean {
    const ext = fileName.split('.').pop()?.toLowerCase() ?? '';
    return ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg'].includes(ext);
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  ngOnDestroy(): void {
    this.subs.forEach(s => s.unsubscribe());
    this.queue().forEach(item => {
      if (item.preview) URL.revokeObjectURL(item.preview);
    });
  }
}
