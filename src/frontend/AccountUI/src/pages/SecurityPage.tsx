import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation } from '@tanstack/react-query';
import { useForm } from 'react-hook-form';
import toast from 'react-hot-toast';
import { ShieldCheckIcon, KeyIcon, FingerPrintIcon } from '@heroicons/react/24/outline';
import { apiClient } from '../services/api';

interface SecurityOverview {
  hasPassword: boolean;
  twoFactorEnabled: boolean;
  mfaEnabled: boolean;
  emailVerified: boolean;
  phoneNumberVerified: boolean;
  lastPasswordChange?: string;
  passkeyCount: number;
}

interface ChangePasswordForm {
  currentPassword: string;
  newPassword: string;
  confirmPassword: string;
}

export function SecurityPage() {
  const [showPasswordForm, setShowPasswordForm] = useState(false);

  const { data: security, isLoading } = useQuery({
    queryKey: ['security'],
    queryFn: async () => {
      const response = await apiClient.get<SecurityOverview>('/api/account/security');
      return response.data;
    },
  });

  const { register, handleSubmit, reset, watch, formState: { errors } } = useForm<ChangePasswordForm>();
  const newPassword = watch('newPassword');

  const changePasswordMutation = useMutation({
    mutationFn: async (data: ChangePasswordForm) => {
      await apiClient.post('/api/account/security/password', {
        currentPassword: data.currentPassword,
        newPassword: data.newPassword,
      });
    },
    onSuccess: () => {
      toast.success('Password changed successfully');
      setShowPasswordForm(false);
      reset();
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to change password');
    },
  });

  const onSubmitPassword = (data: ChangePasswordForm) => {
    changePasswordMutation.mutate(data);
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
        <h1 className="text-2xl font-bold text-gray-900">Security</h1>
        <p className="mt-1 text-sm text-gray-500">
          Manage your account security settings.
        </p>
      </div>

      <div className="space-y-6">
        {/* Password section */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-5 border-b border-gray-200">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-x-3">
                <KeyIcon className="h-6 w-6 text-gray-400" />
                <div>
                  <h3 className="text-base font-semibold text-gray-900">Password</h3>
                  <p className="text-sm text-gray-500">
                    {security?.lastPasswordChange
                      ? `Last changed ${new Date(security.lastPasswordChange).toLocaleDateString()}`
                      : 'Manage your password'}
                  </p>
                </div>
              </div>
              {!showPasswordForm && (
                <button
                  onClick={() => setShowPasswordForm(true)}
                  className="rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50"
                >
                  Change password
                </button>
              )}
            </div>
          </div>

          {showPasswordForm && (
            <form onSubmit={handleSubmit(onSubmitPassword)} className="px-6 py-5 space-y-4">
              <div>
                <label htmlFor="currentPassword" className="block text-sm font-medium text-gray-700">
                  Current password
                </label>
                <input
                  type="password"
                  id="currentPassword"
                  {...register('currentPassword', { required: 'Current password is required' })}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
                {errors.currentPassword && (
                  <p className="mt-1 text-sm text-red-600">{errors.currentPassword.message}</p>
                )}
              </div>

              <div>
                <label htmlFor="newPassword" className="block text-sm font-medium text-gray-700">
                  New password
                </label>
                <input
                  type="password"
                  id="newPassword"
                  {...register('newPassword', {
                    required: 'New password is required',
                    minLength: { value: 8, message: 'Password must be at least 8 characters' },
                  })}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
                {errors.newPassword && (
                  <p className="mt-1 text-sm text-red-600">{errors.newPassword.message}</p>
                )}
              </div>

              <div>
                <label htmlFor="confirmPassword" className="block text-sm font-medium text-gray-700">
                  Confirm new password
                </label>
                <input
                  type="password"
                  id="confirmPassword"
                  {...register('confirmPassword', {
                    required: 'Please confirm your password',
                    validate: value => value === newPassword || 'Passwords do not match',
                  })}
                  className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                />
                {errors.confirmPassword && (
                  <p className="mt-1 text-sm text-red-600">{errors.confirmPassword.message}</p>
                )}
              </div>

              <div className="flex justify-end gap-x-3 pt-2">
                <button
                  type="button"
                  onClick={() => {
                    setShowPasswordForm(false);
                    reset();
                  }}
                  className="rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={changePasswordMutation.isPending}
                  className="rounded-md bg-indigo-600 px-3 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
                >
                  {changePasswordMutation.isPending ? 'Changing...' : 'Change password'}
                </button>
              </div>
            </form>
          )}
        </div>

        {/* Two-factor authentication */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-5">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-x-3">
                <ShieldCheckIcon className="h-6 w-6 text-gray-400" />
                <div>
                  <h3 className="text-base font-semibold text-gray-900">Two-factor authentication</h3>
                  <p className="text-sm text-gray-500">
                    {security?.mfaEnabled
                      ? 'Two-factor authentication is enabled'
                      : 'Add an extra layer of security to your account'}
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-3">
                {security?.mfaEnabled && (
                  <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                    Enabled
                  </span>
                )}
                <Link
                  to="/security/two-factor"
                  className="rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50"
                >
                  Manage
                </Link>
              </div>
            </div>
          </div>
        </div>

        {/* Passkeys */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-5">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-x-3">
                <FingerPrintIcon className="h-6 w-6 text-gray-400" />
                <div>
                  <h3 className="text-base font-semibold text-gray-900">Passkeys</h3>
                  <p className="text-sm text-gray-500">
                    {security?.passkeyCount
                      ? `${security.passkeyCount} passkey${security.passkeyCount > 1 ? 's' : ''} registered`
                      : 'Sign in without a password using biometrics or security keys'}
                  </p>
                </div>
              </div>
              <Link
                to="/security/passkeys"
                className="rounded-md bg-white px-3 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50"
              >
                Manage
              </Link>
            </div>
          </div>
        </div>

        {/* Verification status */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-5">
            <h3 className="text-base font-semibold text-gray-900 mb-4">Verification status</h3>
            <div className="space-y-3">
              <div className="flex items-center justify-between">
                <span className="text-sm text-gray-500">Email</span>
                {security?.emailVerified ? (
                  <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                    Verified
                  </span>
                ) : (
                  <Link to="/security/verify?type=email" className="text-sm text-indigo-600 hover:text-indigo-500">
                    Verify now
                  </Link>
                )}
              </div>
              <div className="flex items-center justify-between">
                <span className="text-sm text-gray-500">Phone number</span>
                {security?.phoneNumberVerified ? (
                  <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                    Verified
                  </span>
                ) : (
                  <Link to="/security/verify?type=phone" className="text-sm text-indigo-600 hover:text-indigo-500">
                    Verify now
                  </Link>
                )}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
