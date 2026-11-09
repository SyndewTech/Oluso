import { Provider } from '@angular/core';

export interface BasePlugin {
  name: string;
  version?: string;
  description?: string;
  providers?: Provider[];
  initialize?: () => void | Promise<void>;
}
