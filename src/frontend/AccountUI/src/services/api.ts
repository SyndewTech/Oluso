import axios from 'axios';
import { apiBaseUrl as configuredApiBaseUrl } from '../config/oidc';

// API base URL from runtime/environment config
let apiBaseUrl = configuredApiBaseUrl;

export const apiClient = axios.create({
  baseURL: apiBaseUrl,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
  },
});

/**
 * Set the API base URL for the account UI library.
 * Call this before making any API requests.
 */
export function setApiBaseUrl(baseUrl: string): void {
  apiBaseUrl = baseUrl;
  apiClient.defaults.baseURL = baseUrl;
}

// Note: Authorization header is set by AuthContext when user is authenticated
// 401 handling is also managed by the OIDC library's automatic token refresh
