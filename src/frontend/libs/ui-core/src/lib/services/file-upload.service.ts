import { Injectable, Pipe, PipeTransform, inject, ChangeDetectorRef, OnDestroy } from '@angular/core';
import { HttpClient, HttpContext } from '@angular/common/http';
import { Observable, of, switchMap, catchError, map, Subscription } from 'rxjs';
import { DomSanitizer, SafeUrl } from '@angular/platform-browser';
import { API_BASE_URL } from '../tokens/api-base-url.token';
import { SKIP_ERROR_TOAST } from '../tokens/http-context.token';

export interface FileUploadResult {
  imageUrl: string;
  fileName: string;
  sizeBytes: number;
  /**
   * Where the file is stored; keep this rather than imageUrl when the server reads the file back later.
   * Set by upload(); absent for uploads to a custom URL, which identify the file by id.
   */
  storagePath?: string;
}

interface SasUploadResponse {
  uploadUrl: string;
  imageUrl: string;
  storagePath: string;
  expiresInSeconds: number;
}

/**
 * Shared file upload service. Works with both admin and workspace portals
 * by reading the API_BASE_URL injection token.
 *
 * Tries client-side direct-to-blob upload via SAS token first,
 * falls back to server-side upload when SAS is not available (e.g. local storage).
 */
@Injectable({ providedIn: 'root' })
export class FileUploadService {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);

  private get filesUrl(): string {
    return `${this.apiBase}/files`;
  }

  /**
   * Upload a file.
   *
   * Private uploads try SAS direct-to-blob first, falling back to server upload.
   * PUBLIC uploads (e.g. `profile-pictures`) always go
   * server-side: the SAS flow is private-only, and the server returns a stable,
   * non-expiring public URL (direct CDN/blob URL, or the anonymous serve
   * endpoint in dev). Public-ness is derived server-side from the folder.
   *
   * `subPath` is placed after the (server-authoritative) tenant — e.g. a
   * record id, giving `{prefix}/{tenant}/{subPath}/{guid}.ext`.
   */
  upload(file: File, folder?: string, opts?: { access?: 'public' | 'private'; subPath?: string }): Observable<FileUploadResult> {
    if (opts?.access === 'public') {
      return this.serverUpload(file, folder, opts.subPath);
    }

    // The direct-to-blob attempt falls back to a server upload on any failure,
    // so its errors are not the user's errors.
    const fallbackContext = new HttpContext().set(SKIP_ERROR_TOAST, true);

    return this.http
      .post<SasUploadResponse>(`${this.filesUrl}/upload-url`, {
        fileName: file.name,
        contentType: file.type || undefined,
        folder,
      }, { context: fallbackContext })
      .pipe(
        switchMap((sas) =>
          this.http
            .put(sas.uploadUrl, file, {
              headers: {
                'x-ms-blob-type': 'BlockBlob',
                'Content-Type': file.type || 'application/octet-stream',
              },
              context: fallbackContext,
            })
            .pipe(
              switchMap(() =>
                of({
                  imageUrl: sas.imageUrl,
                  fileName: file.name,
                  sizeBytes: file.size,
                  storagePath: sas.storagePath,
                }),
              ),
            ),
        ),
        catchError(() => this.serverUpload(file, folder)),
      );
  }

  /**
   * Resolve a file URL to an openable absolute URL.
   * For `/api/workspace/file-attachments/{id}` URLs, calls the access-url
   * endpoint to get a SAS link the browser can open directly in a new tab.
   * For other URLs, falls back to {@link resolveUrl} so callers get either
   * a same-origin blob URL or the existing absolute URL.
   */
  getAccessUrl(url: string): Observable<string> {
    if (!url) return of('');

    const apiOrigin = this.apiBase.replace(/\/api\/.*$/, '');
    const path = url.startsWith('http') ? url.replace(apiOrigin, '') : url;
    const match = path.match(/^\/api\/workspace\/file-attachments\/([^/?]+)$/);
    if (!match) return this.resolveUrl(url);

    const accessUrl = `${apiOrigin}/api/workspace/file-attachments/${match[1]}/access-url`;
    return this.http.get<{ url: string | null }>(accessUrl).pipe(
      switchMap(r => r.url ? of(r.url) : this.resolveUrl(url)),
      catchError(() => this.resolveUrl(url)),
    );
  }

  /**
   * Resolve a file URL for display.
   * - URLs pointing to our own API are fetched through HttpClient (with auth interceptors)
   *   and converted to blob object URLs.
   * - External absolute URLs (SAS, public CDN) are returned as-is.
   * - Relative paths are resolved against the API origin.
   */
  resolveUrl(url: string): Observable<string> {
    if (!url) return of('');

    const apiOrigin = this.apiBase.replace(/\/api\/.*$/, '');

    // If the URL points to our own API, fetch with auth
    if (url.startsWith(apiOrigin)) {
      return this.http.get(url, { responseType: 'blob' }).pipe(
        map(blob => URL.createObjectURL(blob)),
        catchError(() => of('')),
      );
    }

    // External absolute URLs (SAS, public CDN) can be used directly
    if (url.startsWith('http://') || url.startsWith('https://')) return of(url);

    // Relative path — resolve against API origin
    const absoluteUrl = url.startsWith('/') ? `${apiOrigin}${url}` : `${apiOrigin}/${url}`;
    return this.http.get(absoluteUrl, { responseType: 'blob' }).pipe(
      map(blob => URL.createObjectURL(blob)),
      catchError(() => of('')),
    );
  }

  private serverUpload(file: File, folder?: string, subPath?: string): Observable<FileUploadResult> {
    const formData = new FormData();
    formData.append('file', file);
    const params: string[] = [];
    if (folder) params.push(`folder=${encodeURIComponent(folder)}`);
    if (subPath) params.push(`subPath=${encodeURIComponent(subPath)}`);
    const url = params.length ? `${this.filesUrl}/upload?${params.join('&')}` : `${this.filesUrl}/upload`;
    return this.http.post<FileUploadResult>(url, formData);
  }
}

/**
 * Async pipe that resolves file URLs through HttpClient (with auth).
 * Converts relative paths to blob object URLs; passes absolute URLs through.
 *
 * Usage: <img [src]="url | authFile" />
 */
@Pipe({ name: 'authFile', standalone: true, pure: false })
export class AuthFilePipe implements PipeTransform, OnDestroy {
  private readonly fileUpload = inject(FileUploadService);
  private readonly sanitizer = inject(DomSanitizer);
  private readonly cdr = inject(ChangeDetectorRef);

  private currentUrl = '';
  private resolvedUrl: SafeUrl | string = '';
  private objectUrl: string | null = null;
  private sub: Subscription | null = null;

  transform(url: string | undefined | null): SafeUrl | string {
    if (!url) return '';
    if (url === this.currentUrl) return this.resolvedUrl;

    this.cleanup();
    this.currentUrl = url;

    this.sub = this.fileUpload.resolveUrl(url).subscribe(resolved => {
      if (resolved.startsWith('blob:')) {
        this.objectUrl = resolved;
        this.resolvedUrl = this.sanitizer.bypassSecurityTrustUrl(resolved);
      } else {
        this.resolvedUrl = resolved;
      }
      this.cdr.markForCheck();
    });

    return this.resolvedUrl;
  }

  private cleanup(): void {
    this.sub?.unsubscribe();
    this.sub = null;
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
  }

  ngOnDestroy(): void {
    this.cleanup();
  }
}
