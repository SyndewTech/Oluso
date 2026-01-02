import { createContext, useContext, useState, useEffect, ReactNode } from 'react';
import { apiClient } from '../services/api';

interface Tenant {
  id: string;
  identifier: string;
  name: string;
  displayName?: string;
  logoUrl?: string;
  isCurrent: boolean;
}

interface TenantContextValue {
  currentTenant: Tenant | null;
  tenants: Tenant[];
  isMultiTenant: boolean;
  isLoading: boolean;
  switchTenant: (tenantId: string) => Promise<void>;
  refreshTenants: () => Promise<void>;
}

const TenantContext = createContext<TenantContextValue | null>(null);

// Key for storing selected tenant in localStorage
const TENANT_STORAGE_KEY = 'oluso:selected_tenant';

export function TenantProvider({ children }: { children: ReactNode }) {
  const [tenants, setTenants] = useState<Tenant[]>([]);
  const [currentTenant, setCurrentTenant] = useState<Tenant | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const fetchTenants = async () => {
    try {
      const response = await apiClient.get('/api/account/tenants');
      const { tenants: fetchedTenants, currentTenantId, isMultiTenant } = response.data;

      setTenants(fetchedTenants);

      // Determine current tenant
      if (isMultiTenant) {
        // Check localStorage for previously selected tenant
        const savedTenantId = localStorage.getItem(TENANT_STORAGE_KEY);
        const savedTenant = fetchedTenants.find((t: Tenant) => t.id === savedTenantId);

        if (savedTenant) {
          setCurrentTenant(savedTenant);
          // Update API client header
          apiClient.defaults.headers.common['X-Tenant-Id'] = savedTenant.id;
        } else if (currentTenantId) {
          const current = fetchedTenants.find((t: Tenant) => t.id === currentTenantId);
          setCurrentTenant(current || fetchedTenants[0]);
        } else if (fetchedTenants.length > 0) {
          setCurrentTenant(fetchedTenants[0]);
        }
      } else if (fetchedTenants.length > 0) {
        setCurrentTenant(fetchedTenants[0]);
      }
    } catch (error) {
      console.error('Failed to fetch tenants:', error);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchTenants();
  }, []);

  const switchTenant = async (tenantId: string) => {
    // Validate access to tenant
    const response = await apiClient.get(`/api/account/tenants/${tenantId}/validate`);

    if (!response.data.isValid) {
      throw new Error(response.data.error || 'Cannot switch to this organization');
    }

    const newTenant = response.data.tenant;

    // Update state
    setCurrentTenant(newTenant);

    // Save to localStorage
    localStorage.setItem(TENANT_STORAGE_KEY, tenantId);

    // Update API client header for subsequent requests
    apiClient.defaults.headers.common['X-Tenant-Id'] = tenantId;

    // Update tenants list to reflect current
    setTenants(prev =>
      prev.map(t => ({
        ...t,
        isCurrent: t.id === tenantId,
      }))
    );
  };

  const refreshTenants = async () => {
    setIsLoading(true);
    await fetchTenants();
  };

  return (
    <TenantContext.Provider
      value={{
        currentTenant,
        tenants,
        isMultiTenant: tenants.length > 1,
        isLoading,
        switchTenant,
        refreshTenants,
      }}
    >
      {children}
    </TenantContext.Provider>
  );
}

export function useTenant() {
  const context = useContext(TenantContext);
  if (!context) {
    throw new Error('useTenant must be used within a TenantProvider');
  }
  return context;
}
