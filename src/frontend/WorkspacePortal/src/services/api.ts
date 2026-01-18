import axios, { type AxiosInstance } from 'axios';
import { apiUrl } from '../config';

let baseUrl = apiUrl;

/**
 * Set the base URL for API calls
 */
export function setApiBaseUrl(url: string): void {
  baseUrl = url;
  apiClient.defaults.baseURL = url;
}

/**
 * Get the current base URL
 */
export function getApiBaseUrl(): string {
  return baseUrl;
}

/**
 * Configured Axios instance for API calls
 */
export const apiClient: AxiosInstance = axios.create({
  baseURL: baseUrl,
  headers: {
    'Content-Type': 'application/json',
  },
});

/**
 * Set the authentication token for API calls
 */
export function setAuthToken(token: string | null): void {
  if (token) {
    apiClient.defaults.headers.common['Authorization'] = `Bearer ${token}`;
  } else {
    delete apiClient.defaults.headers.common['Authorization'];
  }
}

/**
 * Set the tenant ID header for multi-tenant support
 */
export function setTenantId(tenantId: string | null): void {
  if (tenantId) {
    apiClient.defaults.headers.common['X-Tenant-Id'] = tenantId;
  } else {
    delete apiClient.defaults.headers.common['X-Tenant-Id'];
  }
}

// Response interceptor for error handling
apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    // Handle 401 - redirect to login
    if (error.response?.status === 401) {
      // Let the auth context handle this
      window.dispatchEvent(new CustomEvent('auth:unauthorized'));
    }

    // Handle 403 - permission denied
    if (error.response?.status === 403) {
      window.dispatchEvent(new CustomEvent('auth:forbidden'));
    }

    return Promise.reject(error);
  }
);

export default apiClient;
