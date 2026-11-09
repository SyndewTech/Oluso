import { Component, inject, signal, HostListener, ElementRef } from '@angular/core';
import { HelpPanelService } from '../../services/help-panel.service';
import { MarkdownPipe } from '../../pipes/markdown.pipe';
import { IconComponent } from '../icon/icon.component';

@Component({
  selector: 'ui-help-panel',
  standalone: true,
  imports: [MarkdownPipe, IconComponent],
  template: `
    @if (helpService.isOpen()) {
      <!-- Drag handle -->
      <div
        class="fixed top-0 bottom-0 z-[70] w-1.5 cursor-col-resize hover:bg-primary-300 active:bg-primary-400 transition-colors"
        [style.right.px]="helpService.width()"
        (mousedown)="onDragStart($event)"
      >
        <div class="absolute inset-y-0 -left-1 -right-1"></div>
      </div>

      <!-- Panel -->
      <div
        class="fixed top-0 right-0 bottom-0 z-[65] flex flex-col bg-white border-l border-gray-200 shadow-lg"
        [style.width.px]="helpService.width()"
      >
        <!-- Header -->
        <div class="flex items-center justify-between gap-2 px-4 py-3 border-b border-gray-200 bg-gray-50 shrink-0">
          <div class="flex items-center gap-1.5 min-w-0">
            <!-- Back / Forward -->
            <button
              type="button"
              class="p-1 rounded-md transition-colors"
              [class.text-gray-400]="!helpService.canGoBack()"
              [class.cursor-not-allowed]="!helpService.canGoBack()"
              [class.text-gray-600]="helpService.canGoBack()"
              [class.hover:bg-gray-100]="helpService.canGoBack()"
              [disabled]="!helpService.canGoBack()"
              (click)="helpService.goBack()"
              title="Go back"
            >
              <ui-icon name="chevron-left" class="h-4 w-4 block" />
            </button>
            <button
              type="button"
              class="p-1 rounded-md transition-colors"
              [class.text-gray-400]="!helpService.canGoForward()"
              [class.cursor-not-allowed]="!helpService.canGoForward()"
              [class.text-gray-600]="helpService.canGoForward()"
              [class.hover:bg-gray-100]="helpService.canGoForward()"
              [disabled]="!helpService.canGoForward()"
              (click)="helpService.goForward()"
              title="Go forward"
            >
              <ui-icon name="chevron-right" class="h-4 w-4 block" />
            </button>
            <h3 class="text-sm font-semibold text-gray-900 truncate">
              {{ helpService.content()?.title || 'Help' }}
            </h3>
          </div>
          <div class="flex items-center gap-1 shrink-0">
            <!-- Pin toggle -->
            <button
              type="button"
              class="p-1.5 rounded-md transition-colors"
              [class.text-primary-600]="helpService.mode() === 'pin'"
              [class.bg-primary-50]="helpService.mode() === 'pin'"
              [class.text-gray-400]="helpService.mode() !== 'pin'"
              [class.hover:text-gray-600]="helpService.mode() !== 'pin'"
              [class.hover:bg-gray-100]="helpService.mode() !== 'pin'"
              (click)="helpService.toggleMode()"
              [title]="helpService.mode() === 'pin' ? 'Unpin (follow route)' : 'Pin to this topic'"
            >
              <ui-icon name="map-pin" class="h-4 w-4" />
            </button>
            <!-- Close -->
            <button
              type="button"
              class="p-1.5 rounded-md text-gray-400 hover:text-gray-600 hover:bg-gray-100 transition-colors"
              (click)="helpService.close()"
              title="Close help panel"
            >
              <ui-icon name="close" class="h-4 w-4" />
            </button>
          </div>
        </div>

        <!-- Body -->
        <div class="flex-1 overflow-y-auto px-5 py-4">
          @if (helpService.isLoading()) {
            <div class="flex items-center gap-2 text-sm text-gray-500">
              <div class="h-4 w-4 border-2 border-gray-300 border-t-primary-500 rounded-full animate-spin"></div>
              Loading...
            </div>
          } @else if (helpService.content(); as content) {
            <div
              class="prose prose-sm max-w-none prose-headings:text-gray-900 prose-a:text-primary-600 prose-img:rounded-md prose-img:shadow-sm [&_video]:max-w-full [&_video]:rounded-md [&_iframe]:max-w-full [&_iframe]:rounded-md"
              [innerHTML]="content.body | markdown"
            ></div>

            <!-- Related topics -->
            @if (content.relatedTopics?.length) {
              <div class="mt-6 pt-4 border-t border-gray-200">
                <h4 class="text-xs font-semibold text-gray-500 uppercase tracking-wider mb-2">Related</h4>
                <div class="space-y-1">
                  @for (topic of content.relatedTopics; track topic.helpContext) {
                    <button
                      type="button"
                      class="flex items-center gap-2 w-full text-left px-2 py-1.5 text-sm text-primary-600 hover:text-primary-700 hover:bg-primary-50 rounded-md transition-colors"
                      (click)="helpService.navigateToContext(topic.helpContext)"
                    >
                      <ui-icon name="arrow-right" class="h-3.5 w-3.5 shrink-0" />
                      {{ topic.label }}
                    </button>
                  }
                </div>
              </div>
            }
          }
        </div>

        <!-- Footer -->
        <div class="shrink-0 px-4 py-2 border-t border-gray-200 bg-gray-50 flex items-center justify-between">
          <span class="text-xs text-gray-400">
            @if (helpService.mode() === 'pin') {
              Pinned
            } @else {
              Following route
            }
          </span>
          <kbd class="text-xs text-gray-400 bg-gray-100 border border-gray-200 rounded px-1.5 py-0.5 font-mono">?</kbd>
        </div>
      </div>
    }
  `,
})
export class HelpPanelComponent {
  readonly helpService = inject(HelpPanelService);
  private dragging = false;

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    if (event.key !== '?') return;
    const target = event.target as HTMLElement;
    const tag = target.tagName.toLowerCase();
    if (tag === 'input' || tag === 'textarea' || tag === 'select' || target.isContentEditable) return;
    event.preventDefault();
    this.helpService.toggle();
  }

  onDragStart(event: MouseEvent): void {
    event.preventDefault();
    this.dragging = true;

    const onMove = (e: MouseEvent) => {
      if (!this.dragging) return;
      const newWidth = window.innerWidth - e.clientX;
      this.helpService.setWidth(newWidth);
    };

    const onUp = () => {
      this.dragging = false;
      document.removeEventListener('mousemove', onMove);
      document.removeEventListener('mouseup', onUp);
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    };

    document.body.style.cursor = 'col-resize';
    document.body.style.userSelect = 'none';
    document.addEventListener('mousemove', onMove);
    document.addEventListener('mouseup', onUp);
  }
}
