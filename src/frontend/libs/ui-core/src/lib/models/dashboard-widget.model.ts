import { Type } from '@angular/core';

export interface DashboardWidget {
  id: string;
  title: string;
  component: Type<unknown>;
  order?: number;
  permission?: string;
  feature?: string;
  cols?: number;
  rows?: number;
}
