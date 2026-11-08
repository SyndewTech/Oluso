import { Component, HostListener, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { KeyboardShortcutsService } from './keyboard-shortcuts.service';

/**
 * Cheat-sheet overlay that shows all registered shortcuts, grouped by module.
 * Toggle with `?` (built-in) or by setting `shortcutsHelpOpen` on the service.
 *
 * Place once in your app shell:
 * ```html
 * <ui-shortcuts-help />
 * ```
 */
@Component({
  selector: 'ui-shortcuts-help',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (shortcuts.shortcutsHelpOpen()) {
      <div
        class="fixed inset-0 z-[70] flex items-center justify-center bg-slate-900/40 backdrop-blur-sm p-4"
        (click)="close()"
      >
        <div
          class="w-full max-w-2xl overflow-hidden rounded-xl bg-white shadow-2xl ring-1 ring-black/5"
          (click)="$event.stopPropagation()"
        >
          <div class="flex items-center justify-between border-b border-slate-100 px-5 py-3">
            <h2 class="text-sm font-semibold text-slate-900">Keyboard Shortcuts</h2>
            <button
              type="button"
              class="rounded p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
              (click)="close()"
              aria-label="Close"
            >
              <svg class="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12" />
              </svg>
            </button>
          </div>
          <div class="max-h-[70vh] overflow-y-auto px-5 py-4">
            @if (groups().length === 0) {
              <p class="text-sm text-slate-500">No shortcuts registered.</p>
            }
            @for (group of groups(); track group.name) {
              <section class="mb-5 last:mb-0">
                <h3 class="mb-2 text-[10px] font-semibold uppercase tracking-wider text-slate-400">{{ group.name }}</h3>
                <dl class="grid grid-cols-1 gap-1 md:grid-cols-2">
                  @for (s of group.items; track s.id) {
                    <div class="flex items-center justify-between gap-4 rounded px-2 py-1.5 hover:bg-slate-50">
                      <dt class="text-sm text-slate-700">{{ s.description }}</dt>
                      <dd class="flex gap-1">
                        @for (key of splitDisplay(s.display); track $index) {
                          <kbd class="inline-flex min-w-[1.5rem] items-center justify-center rounded border border-slate-200 bg-slate-50 px-1.5 py-0.5 text-[11px] font-mono text-slate-700 shadow-sm">{{ key }}</kbd>
                          @if (!$last) { <span class="text-[10px] text-slate-400">then</span> }
                        }
                      </dd>
                    </div>
                  }
                </dl>
              </section>
            }
          </div>
          <div class="border-t border-slate-100 bg-slate-50 px-5 py-2 text-right text-[11px] text-slate-500">
            Press <kbd class="rounded border border-slate-200 bg-white px-1 py-0.5 font-mono">?</kbd> to toggle this panel
          </div>
        </div>
      </div>
    }
  `,
})
export class ShortcutsHelpComponent {
  protected readonly shortcuts = inject(KeyboardShortcutsService);

  readonly groups = computed(() => {
    const all = this.shortcuts.getAll();
    const byGroup = new Map<string, typeof all>();
    for (const s of all) {
      if (!byGroup.has(s.group)) byGroup.set(s.group, []);
      byGroup.get(s.group)!.push(s);
    }
    return Array.from(byGroup.entries()).map(([name, items]) => ({ name, items }));
  });

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.shortcuts.shortcutsHelpOpen()) this.shortcuts.shortcutsHelpOpen.set(false);
  }

  close(): void {
    this.shortcuts.shortcutsHelpOpen.set(false);
  }

  splitDisplay(display: string): string[] {
    // Split on whitespace for two-key sequences like "G P"
    return display.split(/\s+/).filter(Boolean);
  }
}
