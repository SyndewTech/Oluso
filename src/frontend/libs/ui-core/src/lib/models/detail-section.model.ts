import { Type } from '@angular/core';

export interface DetailSection {
  id: string;
  title: string;
  component: Type<unknown>;
  entityType: string;
  order?: number;
  permission?: string;
  feature?: string;
}
