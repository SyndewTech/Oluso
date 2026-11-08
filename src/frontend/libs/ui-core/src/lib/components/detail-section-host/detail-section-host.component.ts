import { Component, Input, inject, computed, Injector, InjectionToken } from '@angular/core';
import { CommonModule, NgComponentOutlet } from '@angular/common';
import { PluginService } from '../../services/plugin.service';
import { DetailSection } from '../../models/detail-section.model';
import { CardComponent } from '../card/card.component';

/**
 * Context passed to detail section components
 */
export interface DetailSectionContext {
  entityId: string;
  entityType: string;
  data?: Record<string, unknown>;
}

/**
 * Injection token for detail section context
 */
export const DETAIL_SECTION_CONTEXT = new InjectionToken<DetailSectionContext>('DETAIL_SECTION_CONTEXT');

/**
 * DetailSectionHost Component
 *
 * Renders plugin-provided card sections for a specific entity type.
 * Place this component in detail pages where you want plugins to add custom sections.
 *
 * @example
 * ```html
 * <!-- In a user detail page -->
 * <ui-detail-section-host
 *   entityType="user"
 *   [entityId]="user().id"
 *   [data]="{ user: user(), roles: roles() }"
 * />
 *
 * <!-- In a client detail page -->
 * <ui-detail-section-host
 *   entityType="client"
 *   [entityId]="client().clientId"
 *   [data]="{ client: client() }"
 * />
 * ```
 *
 * Plugins register sections like:
 * ```typescript
 * pluginService.registerAdminPlugin({
 *   name: 'my-plugin',
 *   detailSections: [{
 *     id: 'user-audit-log',
 *     title: 'Audit Log',
 *     entityType: 'user',
 *     component: UserAuditLogSection,
 *     order: 100,
 *   }],
 * });
 * ```
 */
@Component({
  selector: 'ui-detail-section-host',
  standalone: true,
  imports: [CommonModule, NgComponentOutlet, CardComponent],
  template: `
    @for (section of sections(); track section.id) {
      <ui-card [title]="section.title" class="detail-section">
        <ng-container *ngComponentOutlet="section.component; injector: getInjector(section)"></ng-container>
      </ui-card>
    }
  `,
  styles: [`
    :host {
      display: contents;
    }

    .detail-section {
      margin-bottom: 1.5rem;
    }
  `],
})
export class DetailSectionHostComponent {
  private readonly pluginService = inject(PluginService);
  private readonly injector = inject(Injector);

  /** Entity type (e.g., 'user', 'client', 'tenant') */
  @Input({ required: true }) entityType!: string;

  /** Entity ID for the detail page */
  @Input({ required: true }) entityId!: string;

  /** Optional data to pass to section components */
  @Input() data?: Record<string, unknown>;

  /** Get sections for this entity type */
  readonly sections = computed(() => {
    return this.pluginService.getDetailSections(this.entityType);
  });

  /** Create an injector with the section context for a specific section */
  getInjector(section: DetailSection): Injector {
    const context: DetailSectionContext = {
      entityId: this.entityId,
      entityType: this.entityType,
      data: this.data,
    };
    return Injector.create({
      providers: [{ provide: DETAIL_SECTION_CONTEXT, useValue: context }],
      parent: this.injector,
    });
  }
}
