import { Type } from '@angular/core';

export interface PageSlot {
  pageId: string;
  slotId: string;
  component: Type<unknown>;
  order?: number;
  permission?: string;
  feature?: string;
  /**
   * Per-user module-seat requirement. The slot is filtered out (and the
   * component never instantiated) when the user holds no seat for this
   * feature key — same gate the app switcher / module routes use.
   */
  requiresSeat?: string;
}
