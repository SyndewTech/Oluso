import { Component, inject, input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WorkspaceApp, IconComponent } from '@oluso/ui-core';
import { AppSwitcherService } from '../../services/app-switcher.service';

/**
 * App Switcher Component
 *
 * A sleek slide-out panel for switching between workspace apps.
 * Displays internal apps, admin links, and external apps with modern icon styling.
 */
@Component({
  selector: 'workspace-app-switcher',
  standalone: true,
  imports: [CommonModule, IconComponent],
  template: `
    <!-- Trigger Button -->
    <button
      type="button"
      class="group flex items-center gap-2.5 px-2 py-1.5 rounded-lg transition-all duration-200"
      [class.hover:bg-white/10]="!appSwitcher.isPanelOpen()"
      [class.bg-white/10]="appSwitcher.isPanelOpen()"
      (click)="appSwitcher.togglePanel()"
    >
      <!-- Waffle Icon -->
      <div class="relative">
        <div class="grid grid-cols-3 gap-0.5 p-1">
          @for (i of [1,2,3,4,5,6,7,8,9]; track i) {
            <div
              class="w-1.5 h-1.5 rounded-[2px] transition-all duration-200"
              [class.bg-white/60]="!appSwitcher.isPanelOpen()"
              [class.group-hover:bg-white/80]="!appSwitcher.isPanelOpen()"
              [class.bg-white]="appSwitcher.isPanelOpen()"
            ></div>
          }
        </div>
      </div>

      @if (!collapsed()) {
        <!-- Current App -->
        <div class="flex items-center gap-2 min-w-0">
          <span class="text-sm font-medium text-white/90 truncate">
            {{ appSwitcher.selectedApp()?.name || 'Apps' }}
          </span>
          <ui-icon
            name="chevron-down"
            class="w-3.5 h-3.5 text-white/50 transition-transform duration-200"
            [class.rotate-180]="appSwitcher.isPanelOpen()"
          />
        </div>
      }
    </button>

    <!-- Slide-out Panel -->
    @if (appSwitcher.isPanelOpen()) {
      <!-- Backdrop -->
      <div
        class="fixed inset-0 bg-black/20 z-40 transition-opacity duration-200"
        (click)="appSwitcher.closePanel()"
      ></div>

      <!-- Panel - matches sidebar width (w-64 = 256px) -->
      <div
        class="fixed top-0 left-0 h-full w-64 bg-gradient-to-b from-slate-900 via-slate-900 to-slate-950 shadow-2xl z-50 transform transition-transform duration-300 ease-out overflow-hidden flex flex-col"
        [class.translate-x-0]="appSwitcher.isPanelOpen()"
      >
        <!-- Panel Header -->
        <div class="px-5 py-4 border-b border-white/5">
          <div class="flex items-center justify-between">
            <h2 class="text-base font-semibold text-white">Apps</h2>
            <button
              type="button"
              class="p-1.5 rounded-lg text-white/40 hover:text-white hover:bg-white/10 transition-colors"
              (click)="appSwitcher.closePanel()"
            >
              <ui-icon name="close" class="w-4 h-4" />
            </button>
          </div>
        </div>

        <!-- Apps List -->
        <div class="flex-1 overflow-y-auto py-3">
          <!-- Internal Apps -->
          @if (appSwitcher.internalApps().length > 0) {
            <div class="px-3 mb-4">
              <div class="grid grid-cols-2 gap-2">
                @for (app of appSwitcher.internalApps(); track app.id) {
                  <button
                    type="button"
                    class="group flex flex-col items-center gap-2 p-3 rounded-xl transition-all duration-200"
                    [class.bg-white/10]="app.id === appSwitcher.selectedAppId()"
                    [class.hover:bg-white/5]="app.id !== appSwitcher.selectedAppId()"
                    (click)="selectInternalApp(app)"
                  >
                    <div
                      class="w-11 h-11 rounded-xl flex items-center justify-center transition-transform duration-200 group-hover:scale-105"
                      [class]="getIconBgClass(app)"
                    >
                      <ui-icon [name]="app.icon || 'squares'" class="w-5 h-5 text-white" strokeWidth="1.5" />
                    </div>
                    <span class="text-xs font-medium text-white/80 text-center leading-tight">
                      {{ app.name }}
                    </span>
                  </button>
                }
              </div>
            </div>
          }

          <!-- Admin Apps -->
          @if (appSwitcher.adminApps().length > 0) {
            <div class="px-4 mb-2">
              <div class="h-px bg-white/5 mb-3"></div>
              <p class="text-[10px] font-semibold text-white/30 uppercase tracking-wider mb-2">Administration</p>
            </div>
            <div class="px-3 mb-4">
              @for (app of appSwitcher.adminApps(); track app.id) {
                <button
                  type="button"
                  class="group flex items-center gap-3 w-full px-3 py-2.5 rounded-xl hover:bg-white/5 transition-all duration-200"
                  (click)="openExternalApp(app)"
                >
                  <div
                    class="w-9 h-9 rounded-lg flex items-center justify-center transition-transform duration-200 group-hover:scale-105"
                    [class]="getIconBgClass(app)"
                  >
                    <ui-icon [name]="app.icon || 'cog'" class="w-4 h-4 text-white" strokeWidth="1.5" />
                  </div>
                  <div class="flex-1 min-w-0 text-left">
                    <p class="text-sm font-medium text-white/90">{{ app.name }}</p>
                    @if (app.description) {
                      <p class="text-xs text-white/40 truncate">{{ app.description }}</p>
                    }
                  </div>
                  <ui-icon name="arrow-top-right-on-square" class="w-4 h-4 text-white/30" />
                </button>
              }
            </div>
          }

          <!-- External Apps -->
          @if (appSwitcher.externalApps().length > 0) {
            <div class="px-4 mb-2">
              <div class="h-px bg-white/5 mb-3"></div>
              <p class="text-[10px] font-semibold text-white/30 uppercase tracking-wider mb-2">External Apps</p>
            </div>
            <div class="px-3">
              @for (app of appSwitcher.externalApps(); track app.id) {
                <button
                  type="button"
                  class="group flex items-center gap-3 w-full px-3 py-2.5 rounded-xl hover:bg-white/5 transition-all duration-200"
                  (click)="openExternalApp(app)"
                >
                  <div
                    class="w-9 h-9 rounded-lg flex items-center justify-center transition-transform duration-200 group-hover:scale-105"
                    [class]="getIconBgClass(app)"
                  >
                    <ui-icon [name]="app.icon || 'link'" class="w-4 h-4 text-white" strokeWidth="1.5" />
                  </div>
                  <div class="flex-1 min-w-0 text-left">
                    <p class="text-sm font-medium text-white/90">{{ app.name }}</p>
                    @if (app.description) {
                      <p class="text-xs text-white/40 truncate">{{ app.description }}</p>
                    }
                  </div>
                  <ui-icon name="arrow-top-right-on-square" class="w-4 h-4 text-white/30" />
                </button>
              }
            </div>
          }
        </div>

        <!-- Panel Footer -->
        <div class="absolute bottom-0 left-0 right-0 px-4 py-3 border-t border-white/5 bg-slate-950/50">
          <p class="text-[10px] text-white/20 text-center">
            Press <kbd class="px-1 py-0.5 bg-white/10 rounded text-white/40">Esc</kbd> to close
          </p>
        </div>
      </div>
    }
  `,
  host: {
    '(document:keydown.escape)': 'appSwitcher.closePanel()',
  },
})
export class AppSwitcherComponent {
  readonly appSwitcher = inject(AppSwitcherService);

  /** Whether the sidebar is collapsed */
  readonly collapsed = input<boolean>(false);

  /**
   * Select an internal app
   */
  selectInternalApp(app: WorkspaceApp): void {
    this.appSwitcher.selectApp(app.id);
  }

  /**
   * Open an external or admin app
   */
  openExternalApp(app: WorkspaceApp): void {
    this.appSwitcher.openExternalApp(app);
  }

  /**
   * Get gradient background class for app icon
   */
  getIconBgClass(app: WorkspaceApp): string {
    const colorMap: Record<string, string> = {
      slate: 'bg-gradient-to-br from-slate-500 to-slate-700',
      blue: 'bg-gradient-to-br from-blue-500 to-blue-700',
      green: 'bg-gradient-to-br from-emerald-500 to-emerald-700',
      purple: 'bg-gradient-to-br from-purple-500 to-purple-700',
      orange: 'bg-gradient-to-br from-orange-500 to-orange-700',
      red: 'bg-gradient-to-br from-red-500 to-red-700',
      indigo: 'bg-gradient-to-br from-indigo-500 to-indigo-700',
      teal: 'bg-gradient-to-br from-teal-500 to-teal-700',
      pink: 'bg-gradient-to-br from-pink-500 to-pink-700',
    };
    return colorMap[app.color || 'slate'] || colorMap['slate'];
  }
}
