import { InjectionToken } from '@angular/core';

/**
 * Injection token for providing the API base URL to shared ui-core services.
 *
 * Each portal provides this with its own API URL:
 * - Admin: 'http://localhost:5050/api/admin'
 * - Workspace: 'http://localhost:5050/api/workspace'
 *
 * @example
 * ```typescript
 * // In app.config.ts
 * providers: [
 *   { provide: API_BASE_URL, useValue: apiUrl },
 * ]
 * ```
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL');
