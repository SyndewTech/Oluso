import axios, { AxiosInstance, AxiosError } from 'axios';
import toast from 'react-hot-toast';
import { useAuthStore } from '../store/slices/authSlice';

let API_BASE_URL = import.meta.env.VITE_API_URL || 'http://localhost:5050/api/admin';
let API_BASE_ORIGIN = '';

// Initialize base origin
try {
  const url = new URL(API_BASE_URL, window.location.origin);
  API_BASE_ORIGIN = url.origin;
} catch {
  API_BASE_ORIGIN = window.location.origin;
}

const api: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
});

/**
 * Set the API base URL for the admin UI library.
 * Call this before making any API requests.
 */
export function setApiBaseUrl(baseUrl: string): void {
  API_BASE_URL = baseUrl;
  api.defaults.baseURL = baseUrl;
  // Update the origin when base URL changes
  try {
    const url = new URL(baseUrl, window.location.origin);
    API_BASE_ORIGIN = url.origin;
  } catch {
    API_BASE_ORIGIN = window.location.origin;
  }
}

/**
 * Get an absolute URL that bypasses the API baseURL.
 * Use this for endpoints that are NOT under /api/admin.
 * @param path The absolute path (e.g., '/api/tenant/features')
 */
export function absoluteUrl(path: string): string {
  return `${API_BASE_ORIGIN}${path}`;
}

// Request interceptor - add auth token and tenant header
api.interceptors.request.use(
  (config) => {
    const { accessToken, currentTenantId } = useAuthStore.getState();
    if (accessToken) {
      config.headers.Authorization = `Bearer ${accessToken}`;
    }
    // Add tenant header for SuperAdmin users operating in a specific tenant context
    if (currentTenantId) {
      config.headers['X-Tenant-ID'] = currentTenantId;
    }
    return config;
  },
  (error) => Promise.reject(error)
);

// Helper to extract error message from response
const getErrorMessage = (error: AxiosError): string => {
  const data = error.response?.data as Record<string, unknown> | undefined;

  // Try common error message formats
  if (data?.error && typeof data.error === 'string') {
    return data.error;
  }
  if (data?.message && typeof data.message === 'string') {
    return data.message;
  }
  if (data?.title && typeof data.title === 'string') {
    return data.title;
  }
  // Handle validation errors
  if (data?.errors && typeof data.errors === 'object') {
    const errors = data.errors as Record<string, string[]>;
    const firstError = Object.values(errors)[0];
    if (Array.isArray(firstError) && firstError.length > 0) {
      return firstError[0];
    }
  }

  return error.message || 'An error occurred';
};

// Response interceptor - handle errors
api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const status = error.response?.status;

    // Show toast for 400 Bad Request errors
    if (status === 400) {
      const message = getErrorMessage(error);
      toast.error(message, {
        duration: 5000,
        id: `error-${Date.now()}`, // Prevent duplicate toasts
      });
    }

    // Only redirect to login on 401 Unauthorized
    if (status === 401) {
      // Don't redirect if already on login page or if this is the login request
      const isLoginRequest = error.config?.url?.includes('/auth/login');
      if (!isLoginRequest && window.location.pathname !== '/login') {
        useAuthStore.getState().logout();
        window.location.href = '/login';
      }
    }

    // For 403 Forbidden - user is authenticated but not authorized
    // Don't redirect, let the component handle it

    // For 404 Not Found - resource doesn't exist
    // Don't redirect, let the component handle it

    // For 5xx Server errors - show toast
    if (status && status >= 500) {
      toast.error('Server error. Please try again later.', {
        duration: 5000,
        id: `server-error-${Date.now()}`,
      });
    }

    return Promise.reject(error);
  }
);

export { api as apiClient };
export default api;
