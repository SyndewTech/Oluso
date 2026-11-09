import { Component, inject, signal, computed, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Subject, takeUntil } from 'rxjs';
import { FileUploadComponent, AuthFilePipe } from '@syndew/oluso-ui-core';
import { apiUrl } from '../../auth/oidc.config';

interface MyFile {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  folder: string | null;
  createdAt: string;
}

@Component({
  selector: 'workspace-my-files-page',
  standalone: true,
  imports: [CommonModule, FileUploadComponent, AuthFilePipe],
  template: `
    <div class="space-y-6">
      <!-- Header -->
      <div class="flex items-center justify-between">
        <div>
          <h1 class="text-2xl font-bold text-gray-900">My Files</h1>
          <p class="mt-1 text-sm text-gray-500">Your personal files — only you can see these</p>
        </div>
      </div>

      <!-- Breadcrumb -->
      <div class="flex items-center justify-between">
        <nav class="flex items-center gap-1 text-sm">
          <button
            (click)="navigateToFolder(null)"
            class="text-primary-600 hover:text-primary-800 font-medium"
            [class.text-gray-900]="!currentFolder()"
          >
            All Files
          </button>
          @for (crumb of breadcrumbs(); track crumb.path) {
            <span class="text-gray-400">/</span>
            <button
              (click)="navigateToFolder(crumb.path)"
              class="text-primary-600 hover:text-primary-800"
              [class.text-gray-900]="crumb.path === currentFolder()"
            >
              {{ crumb.name }}
            </button>
          }
        </nav>
      </div>

      <!-- Upload area -->
      <div class="bg-white rounded-lg shadow p-4">
        <ui-file-upload
          [showFolder]="true"
          [multiple]="true"
          [defaultFolder]="currentFolder() ?? undefined"
          [uploadUrl]="baseUrl"
          accept="image/*,.pdf,.doc,.docx,.xls,.xlsx,.csv,.txt"
          label="Upload Files"
          folderPlaceholder="e.g. Receipts/2026"
          (fileUploaded)="onFileUploaded()"
        />
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center h-32">
          <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-primary-600"></div>
        </div>
      } @else {

        <!-- Sub-folders at current level -->
        @if (childFolders().length > 0) {
          <div>
            <h2 class="text-sm font-medium text-gray-500 mb-3">Folders</h2>
            <div class="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-3">
              @for (folder of childFolders(); track folder.path) {
                <button
                  (click)="navigateToFolder(folder.path)"
                  class="flex flex-col items-center gap-2 p-4 bg-white rounded-lg shadow hover:shadow-md transition-shadow text-center"
                >
                  <svg class="h-10 w-10 text-yellow-500" fill="currentColor" viewBox="0 0 24 24">
                    <path d="M19.5 21a3 3 0 003-3v-4.5a3 3 0 00-3-3h-15a3 3 0 00-3 3V18a3 3 0 003 3h15zM1.5 10.146V6a3 3 0 013-3h5.379a2.25 2.25 0 011.59.659l2.122 2.121c.14.141.331.22.53.22H19.5a3 3 0 013 3v1.146A4.483 4.483 0 0019.5 9h-15a4.483 4.483 0 00-3 1.146z" />
                  </svg>
                  <span class="text-sm font-medium text-gray-700 truncate w-full">{{ folder.name }}</span>
                </button>
              }
            </div>
          </div>
        }

        <!-- Files at current level -->
        @if (currentFiles().length > 0) {
          <div>
            <h2 class="text-sm font-medium text-gray-500 mb-3">
              Files
              <span class="text-gray-400">({{ currentFiles().length }})</span>
            </h2>
            <div class="bg-white rounded-lg shadow overflow-hidden">
              <table class="min-w-full divide-y divide-gray-200">
                <thead class="bg-gray-50">
                  <tr>
                    <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">File</th>
                    <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Folder</th>
                    <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Size</th>
                    <th class="px-4 py-3 text-left text-xs font-medium text-gray-500 uppercase">Uploaded</th>
                    <th class="px-4 py-3"></th>
                  </tr>
                </thead>
                <tbody class="divide-y divide-gray-200">
                  @for (file of currentFiles(); track file.id) {
                    <tr class="hover:bg-gray-50">
                      <td class="px-4 py-3">
                        <div class="flex items-center gap-3">
                          @if (isImage(file.contentType)) {
                            <img
                              [src]="getDownloadUrl(file.id) | authFile"
                              [alt]="file.fileName"
                              class="h-10 w-10 object-cover rounded border border-gray-200"
                            />
                          } @else {
                            <div class="h-10 w-10 rounded bg-gray-100 flex items-center justify-center">
                              <svg class="h-5 w-5 text-gray-400" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                                <path stroke-linecap="round" stroke-linejoin="round" d="M19.5 14.25v-2.625a3.375 3.375 0 00-3.375-3.375h-1.5A1.125 1.125 0 0113.5 7.125v-1.5a3.375 3.375 0 00-3.375-3.375H8.25m2.25 0H5.625c-.621 0-1.125.504-1.125 1.125v17.25c0 .621.504 1.125 1.125 1.125h12.75c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9z" />
                              </svg>
                            </div>
                          }
                          <span class="text-sm font-medium text-gray-900">{{ file.fileName }}</span>
                        </div>
                      </td>
                      <td class="px-4 py-3 text-sm text-gray-500">{{ file.folder || '—' }}</td>
                      <td class="px-4 py-3 text-sm text-gray-500">{{ formatSize(file.sizeBytes) }}</td>
                      <td class="px-4 py-3 text-sm text-gray-500">{{ formatDate(file.createdAt) }}</td>
                      <td class="px-4 py-3 text-right">
                        <div class="flex items-center justify-end gap-2">
                          <button
                            (click)="downloadFile(file)"
                            class="text-primary-600 hover:text-primary-800 text-sm"
                          >
                            Download
                          </button>
                          <button
                            (click)="deleteFile(file)"
                            class="text-gray-400 hover:text-red-500"
                          >
                            <svg class="h-4 w-4" fill="none" viewBox="0 0 24 24" stroke-width="1.5" stroke="currentColor">
                              <path stroke-linecap="round" stroke-linejoin="round" d="M14.74 9l-.346 9m-4.788 0L9.26 9m9.968-3.21c.342.052.682.107 1.022.166m-1.022-.165L18.16 19.673a2.25 2.25 0 01-2.244 2.077H8.084a2.25 2.25 0 01-2.244-2.077L4.772 5.79m14.456 0a48.108 48.108 0 00-3.478-.397m-12 .562c.34-.059.68-.114 1.022-.165m0 0a48.11 48.11 0 013.478-.397m7.5 0v-.916c0-1.18-.91-2.164-2.09-2.201a51.964 51.964 0 00-3.32 0c-1.18.037-2.09 1.022-2.09 2.201v.916m7.5 0a48.667 48.667 0 00-7.5 0" />
                            </svg>
                          </button>
                        </div>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        } @else if (!loading() && childFolders().length === 0) {
          <div class="bg-white rounded-lg shadow p-12 text-center">
            <svg class="mx-auto h-12 w-12 text-gray-300" fill="none" viewBox="0 0 24 24" stroke-width="1" stroke="currentColor">
              <path stroke-linecap="round" stroke-linejoin="round" d="M2.25 12.75V12A2.25 2.25 0 014.5 9.75h15A2.25 2.25 0 0121.75 12v.75m-8.69-6.44l-2.12-2.12a1.5 1.5 0 00-1.061-.44H4.5A2.25 2.25 0 002.25 6v12a2.25 2.25 0 002.25 2.25h15A2.25 2.25 0 0021.75 18V9a2.25 2.25 0 00-2.25-2.25h-5.379a1.5 1.5 0 01-1.06-.44z" />
            </svg>
            <h3 class="mt-4 text-lg font-medium text-gray-900">No files yet</h3>
            <p class="mt-2 text-sm text-gray-500">Upload your first file above to get started.</p>
          </div>
        }
      }

      <!-- Image preview lightbox -->
      @if (previewUrl) {
        <div class="fixed inset-0 z-50 flex items-center justify-center bg-black/70" (click)="previewUrl = ''">
          <img [src]="previewUrl | authFile" class="max-h-[85vh] max-w-[90vw] rounded-lg shadow-2xl" (click)="$event.stopPropagation()" />
        </div>
      }
    </div>
  `,
})
export class MyFilesPageComponent implements OnInit, OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly destroy$ = new Subject<void>();
  readonly baseUrl = `${apiUrl}/api/workspace/my-files`;

  loading = signal(true);
  allFiles = signal<MyFile[]>([]);
  allFolders = signal<string[]>([]);
  currentFolder = signal<string | null>(null);
  previewUrl = '';

  breadcrumbs = signal<{ name: string; path: string }[]>([]);

  /**
   * Derive immediate child folders from the flat folder list.
   * At root (null): top-level names (first segment) e.g. "Receipts" from "Receipts/2026"
   * Inside "Receipts": child names e.g. "2026" from "Receipts/2026"
   */
  childFolders = computed(() => {
    const current = this.currentFolder();
    const prefix = current ? current + '/' : '';
    const seen = new Set<string>();
    const result: { name: string; path: string }[] = [];

    for (const folder of this.allFolders()) {
      if (current === null) {
        // At root: extract first segment
        const firstSegment = folder.split('/')[0];
        if (!seen.has(firstSegment)) {
          seen.add(firstSegment);
          result.push({ name: firstSegment, path: firstSegment });
        }
      } else if (folder.startsWith(prefix) && folder !== current) {
        // Inside a folder: extract next segment after prefix
        const rest = folder.substring(prefix.length);
        const nextSegment = rest.split('/')[0];
        if (nextSegment && !seen.has(nextSegment)) {
          seen.add(nextSegment);
          result.push({ name: nextSegment, path: prefix + nextSegment });
        }
      }
    }

    return result.sort((a, b) => a.name.localeCompare(b.name));
  });

  /**
   * Files at and below the current folder level.
   * At root: all files.
   * Inside a folder: files whose folder matches exactly or starts with folder/.
   */
  currentFiles = computed(() => {
    const current = this.currentFolder();
    if (current === null) return this.allFiles();
    const prefix = current + '/';
    return this.allFiles().filter(f =>
      f.folder === current || (f.folder != null && f.folder.startsWith(prefix))
    );
  });

  ngOnInit(): void {
    this.loadData();
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }

  navigateToFolder(folder: string | null): void {
    this.currentFolder.set(folder);
    this.updateBreadcrumbs();
    this.loadData();
  }

  private updateBreadcrumbs(): void {
    const folder = this.currentFolder();
    if (!folder) {
      this.breadcrumbs.set([]);
      return;
    }
    const parts = folder.split('/');
    this.breadcrumbs.set(
      parts.map((part, i) => ({
        name: part,
        path: parts.slice(0, i + 1).join('/'),
      }))
    );
  }

  loadData(): void {
    this.loading.set(true);

    // Always load all personal files (no folder filter) so we can derive the hierarchy client-side
    this.http.get<MyFile[]>(this.baseUrl)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (files) => {
          this.allFiles.set(files);
          this.loading.set(false);
        },
        error: () => {
          this.allFiles.set([]);
          this.loading.set(false);
        },
      });

    // Always load all folders
    this.http.get<string[]>(`${this.baseUrl}/folders`)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: (folders) => this.allFolders.set(folders),
        error: () => this.allFolders.set([]),
      });
  }

  onFileUploaded(): void {
    this.loadData();
  }

  downloadFile(file: MyFile): void {
    this.http.get(`${this.baseUrl}/${file.id}/download`, { responseType: 'blob' })
      .pipe(takeUntil(this.destroy$))
      .subscribe(blob => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = file.fileName;
        document.body.appendChild(a);
        a.click();
        URL.revokeObjectURL(url);
        document.body.removeChild(a);
      });
  }

  deleteFile(file: MyFile): void {
    if (!confirm(`Delete "${file.fileName}"?`)) return;

    this.http.delete(`${this.baseUrl}/${file.id}`)
      .pipe(takeUntil(this.destroy$))
      .subscribe(() => {
        this.allFiles.update(list => list.filter(f => f.id !== file.id));
      });
  }

  getDownloadUrl(id: string): string {
    return `${this.baseUrl}/${id}/download`;
  }

  isImage(contentType: string): boolean {
    return contentType.startsWith('image/');
  }

  formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
