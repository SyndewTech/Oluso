import { Component, Input, inject, computed, Injector, InjectionToken } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { PluginService } from '../../services/plugin.service';
import { PageSlot } from '../../models/page-slot.model';

/**
 * Context passed to page slot components
 */
export interface PageSlotContext {
  pageId: string;
  slotId: string;
  data?: Record<string, unknown>;
}

/**
 * Injection token for page slot context
 */
export const PAGE_SLOT_CONTEXT = new InjectionToken<PageSlotContext>('PAGE_SLOT_CONTEXT');

/**
 * PageSlotHost Component
 *
 * Renders plugin-provided components for a specific page slot.
 * Place this component in pages where you want plugins to inject content.
 *
 * @example
 * ```html
 * <!-- At the top of a page -->
 * <ui-page-slot-host pageId="user-detail" slotId="before-header" [data]="{ userId: user().id }" />
 *
 * <!-- After actions -->
 * <ui-page-slot-host pageId="user-detail" slotId="after-actions" />
 *
 * <!-- In a sidebar -->
 * <ui-page-slot-host pageId="dashboard" slotId="sidebar" />
 * ```
 *
 * Plugins register slots like:
 * ```typescript
 * pluginService.registerAdminPlugin({
 *   name: 'my-plugin',
 *   pageSlots: [{
 *     pageId: 'user-detail',
 *     slotId: 'before-header',
 *     component: MyBannerComponent,
 *     order: 10,
 *   }],
 * });
 * ```
 */
@Component({
  selector: 'ui-page-slot-host',
  standalone: true,
  imports: [CommonModule, NgComponentOutlet],
  // Wrap each contribution in a spaced block so adjacent module cards
  // don't visually collide. Plugins that want a no-gap layout can pass
  // `gap="none"` (e.g. when hosting tightly-packed sub-tiles inside a
  // single section); the default matches the workspace's standard
  // section rhythm.
  template: `
    @if (slots().length > 0) {
      <div [class]="containerClass">
        @for (slot of slots(); track slot.component) {
          <ng-container *ngComponentOutlet="slot.component; injector: getInjector(slot)"></ng-container>
        }
      </div>
    }
  `,
})
export class PageSlotHostComponent {
  private readonly pluginService = inject(PluginService);
  private readonly injector = inject(Injector);

  /** Page identifier (e.g., 'user-detail', 'dashboard') */
  @Input({ required: true }) pageId!: string;

  /** Slot identifier within the page (e.g., 'before-header', 'after-actions') */
  @Input({ required: true }) slotId!: string;

  /** Optional data to pass to slot components */
  @Input() data?: Record<string, unknown>;

  /**
   * Vertical rhythm between contributions. <c>md</c> = Tailwind
   * <c>space-y-6</c> (workspace section default). Set to <c>none</c>
   * for tightly-packed slots where contributions are meant to render
   * flush against each other.
   */
  @Input() gap: 'none' | 'sm' | 'md' | 'lg' = 'md';

  /** Resolves the gap input to the Tailwind class applied on the wrapper. */
  get containerClass(): string {
    switch (this.gap) {
      case 'none': return '';
      case 'sm':   return 'space-y-3';
      case 'lg':   return 'space-y-8';
      case 'md':
      default:     return 'space-y-6';
    }
  }

  /** Get slots for this page/slot combination */
  readonly slots = computed(() => {
    return this.pluginService.getPageSlots(this.pageId, this.slotId);
  });

  /** Create an injector with the slot context for a specific slot */
  getInjector(slot: PageSlot): Injector {
    const context: PageSlotContext = {
      pageId: this.pageId,
      slotId: this.slotId,
      data: this.data,
    };
    return Injector.create({
      providers: [{ provide: PAGE_SLOT_CONTEXT, useValue: context }],
      parent: this.injector,
    });
  }
}
