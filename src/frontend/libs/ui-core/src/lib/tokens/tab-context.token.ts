import { InjectionToken } from '@angular/core';
import { TabContext } from '../models/tab-definition.model';

/**
 * Injection token for providing tab context to dynamic tab components.
 *
 * Plugin tab components can inject this token to access:
 * - entityId: The ID of the entity being viewed (e.g., tenant ID)
 * - entityType: The type of entity (e.g., 'tenant', 'user')
 * - data: Additional contextual data
 *
 * @example
 * ```typescript
 * @Component({...})
 * export class MyPluginTabComponent {
 *   private readonly context = inject(TAB_CONTEXT);
 *
 *   ngOnInit() {
 *     const tenantId = this.context.entityId;
 *     // Load plugin-specific data for this tenant
 *   }
 * }
 * ```
 */
export const TAB_CONTEXT = new InjectionToken<TabContext>('TAB_CONTEXT');
