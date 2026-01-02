import toast from 'react-hot-toast';
import { BuildingOfficeIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { useTenant } from '../contexts/TenantContext';

export function TenantSelectorPage() {
  const { tenants, currentTenant, switchTenant, isLoading } = useTenant();

  const handleSwitch = async (tenantId: string) => {
    try {
      await switchTenant(tenantId);
      toast.success('Switched organization');
    } catch (error: any) {
      toast.error(error.message || 'Failed to switch organization');
    }
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  return (
    <div className="max-w-2xl">
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Organizations</h1>
        <p className="mt-1 text-sm text-gray-500">
          You belong to {tenants.length} organization{tenants.length !== 1 ? 's' : ''}.
          Switch between them to manage different accounts.
        </p>
      </div>

      <div className="space-y-4">
        {tenants.map((tenant) => (
          <div
            key={tenant.id}
            className={`bg-white shadow rounded-lg p-6 ${
              tenant.id === currentTenant?.id ? 'ring-2 ring-indigo-500' : ''
            }`}
          >
            <div className="flex items-center gap-x-4">
              <div className="flex-shrink-0">
                {tenant.logoUrl ? (
                  <img
                    src={tenant.logoUrl}
                    alt=""
                    className="h-12 w-12 rounded-lg object-cover"
                  />
                ) : (
                  <div className="h-12 w-12 rounded-lg bg-indigo-100 flex items-center justify-center">
                    <BuildingOfficeIcon className="h-6 w-6 text-indigo-600" />
                  </div>
                )}
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-x-2">
                  <h3 className="text-sm font-semibold text-gray-900">
                    {tenant.displayName || tenant.name}
                  </h3>
                  {tenant.id === currentTenant?.id && (
                    <span className="inline-flex items-center gap-x-1 rounded-full bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
                      <CheckCircleIcon className="h-3 w-3" />
                      Current
                    </span>
                  )}
                </div>
                <p className="text-sm text-gray-500">{tenant.identifier}</p>
              </div>
              {tenant.id !== currentTenant?.id && (
                <button
                  onClick={() => handleSwitch(tenant.id)}
                  className="rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500"
                >
                  Switch
                </button>
              )}
            </div>
          </div>
        ))}

        {tenants.length === 0 && (
          <div className="text-center py-12 bg-white rounded-lg shadow">
            <BuildingOfficeIcon className="mx-auto h-12 w-12 text-gray-400" />
            <h3 className="mt-2 text-sm font-semibold text-gray-900">No organizations</h3>
            <p className="mt-1 text-sm text-gray-500">
              You're not a member of any organization.
            </p>
          </div>
        )}
      </div>
    </div>
  );
}
