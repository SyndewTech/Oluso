import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { HelpPanelService } from './help-panel.service';
import { FileUploadService } from './file-upload.service';
import { API_BASE_URL } from '../tokens/api-base-url.token';
import { FeedbackSubmitResponse } from '../components/feedback/feedback.models';

@Injectable({ providedIn: 'root' })
export class FeedbackService {
  private readonly http = inject(HttpClient);
  private readonly helpService = inject(HelpPanelService);
  private readonly fileUploadService = inject(FileUploadService);
  private readonly apiBaseUrl = inject(API_BASE_URL, { optional: true }) ?? '';

  readonly isOpen = signal(false);
  readonly isCapturing = signal(false);
  readonly screenshot = signal<string | null>(null);
  readonly selectedElement = signal<string | null>(null);
  readonly isSubmitting = signal(false);
  readonly submitSuccess = signal(false);
  readonly submitError = signal<string | null>(null);

  /** Which portal: 'admin' or 'workspace' — set by the layout that includes the widget */
  readonly portal = signal<'admin' | 'workspace'>('workspace');

  toggle(): void {
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  open(): void {
    this.isOpen.set(true);
    this.submitSuccess.set(false);
    this.submitError.set(null);
  }

  close(): void {
    this.isOpen.set(false);
    this.isCapturing.set(false);
    this.reset();
  }

  reset(): void {
    this.screenshot.set(null);
    this.selectedElement.set(null);
    this.submitSuccess.set(false);
    this.submitError.set(null);
  }

  async captureScreenshot(): Promise<void> {
    this.isCapturing.set(true);
    try {
      const { toPng } = await import('html-to-image');
      const dataUrl = await toPng(document.body, {
        pixelRatio: 1,
        cacheBust: true,
      });
      this.screenshot.set(dataUrl);
    } catch (err) {
      console.error('[Feedback] Screenshot capture failed:', err);
      this.screenshot.set(null);
    } finally {
      this.isCapturing.set(false);
    }
  }

  async submit(data: { title: string; description: string; category: 'bug' | 'feature' | 'note' }): Promise<void> {
    this.isSubmitting.set(true);
    this.submitError.set(null);

    try {
      const formData = new FormData();
      formData.append('title', data.title);
      formData.append('description', data.description);
      formData.append('category', data.category);
      formData.append('url', window.location.href);
      formData.append('viewportSize', `${window.innerWidth}x${window.innerHeight}`);

      const el = this.selectedElement();
      if (el) {
        formData.append('elementSelector', el);
      }

      const screenshotData = this.screenshot();
      if (screenshotData) {
        const blob = await this.dataUrlToBlob(screenshotData);
        const file = new File([blob], 'screenshot.png', { type: 'image/png' });
        const result = await firstValueFrom(this.fileUploadService.upload(file, 'feedback'));
        formData.append('screenshotPath', result.storagePath!);
      }

      const url = `${this.apiBaseUrl}/feedback`;

      await new Promise<void>((resolve, reject) => {
        this.http.post<FeedbackSubmitResponse>(url, formData).subscribe({
          next: () => {
            this.submitSuccess.set(true);
            this.isSubmitting.set(false);
            resolve();
          },
          error: (err) => {
            this.submitError.set(err?.error?.message || 'Failed to submit feedback');
            this.isSubmitting.set(false);
            reject(err);
          },
        });
      });
    } catch {
      this.isSubmitting.set(false);
    }
  }

  /** Offset for the FAB when help panel is open */
  get fabRightOffset(): number {
    return this.helpService.isOpen() ? this.helpService.width() + 24 : 24;
  }

  private async dataUrlToBlob(dataUrl: string): Promise<Blob> {
    const res = await fetch(dataUrl);
    return res.blob();
  }
}
