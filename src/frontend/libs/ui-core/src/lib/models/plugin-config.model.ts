export interface PluginConfig {
  apiBaseUrl?: string;
  features?: Record<string, boolean>;
  [key: string]: unknown;
}
