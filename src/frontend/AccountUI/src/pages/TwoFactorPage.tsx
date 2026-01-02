import { useState } from 'react';
import { Link } from 'react-router-dom';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import {
  ShieldCheckIcon,
  DevicePhoneMobileIcon,
  EnvelopeIcon,
  ArrowLeftIcon,
  CheckCircleIcon,
  XCircleIcon,
  ClipboardDocumentIcon,
} from '@heroicons/react/24/outline';
import { apiClient } from '../services/api';

interface MfaStatus {
  enabled: boolean;
  twoFactorEnabled: boolean;
  availableMethods: string[];
  enabledMethods: string[];
  hasTotp: boolean;
  hasSms: boolean;
  hasEmail: boolean;
  phoneNumber?: string;
  email?: string;
}

interface TotpSetup {
  secret: string;
  qrCodeUri: string;
}

interface MfaEnableResult {
  success: boolean;
  recoveryCodes?: string[];
}

type SetupStep = 'none' | 'totp-setup' | 'totp-verify' | 'sms-setup' | 'sms-verify' | 'email-setup' | 'email-verify' | 'recovery-codes' | 'disable';

export function TwoFactorPage() {
  const queryClient = useQueryClient();
  const [setupStep, setSetupStep] = useState<SetupStep>('none');
  const [totpSetup, setTotpSetup] = useState<TotpSetup | null>(null);
  const [verificationCode, setVerificationCode] = useState('');
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [password, setPassword] = useState('');
  const [methodToDisable, setMethodToDisable] = useState<string | null>(null);

  const { data: mfaStatus, isLoading } = useQuery({
    queryKey: ['mfa-status'],
    queryFn: async () => {
      const response = await apiClient.get<MfaStatus>('/api/account/security/mfa');
      return response.data;
    },
  });

  // TOTP Setup
  const totpSetupMutation = useMutation({
    mutationFn: async () => {
      const response = await apiClient.post<TotpSetup>('/api/account/security/mfa/totp/setup');
      return response.data;
    },
    onSuccess: (data) => {
      setTotpSetup(data);
      setSetupStep('totp-verify');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to start TOTP setup');
    },
  });

  const totpVerifyMutation = useMutation({
    mutationFn: async (code: string) => {
      const response = await apiClient.post<MfaEnableResult>('/api/account/security/mfa/totp/verify', { code });
      return response.data;
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['mfa-status'] });
      queryClient.invalidateQueries({ queryKey: ['security'] });
      if (data.recoveryCodes && data.recoveryCodes.length > 0) {
        setRecoveryCodes(data.recoveryCodes);
        setSetupStep('recovery-codes');
      } else {
        toast.success('Authenticator app enabled successfully');
        resetSetup();
      }
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Invalid verification code');
    },
  });

  // SMS Setup
  const smsSetupMutation = useMutation({
    mutationFn: async () => {
      const response = await apiClient.post('/api/account/security/mfa/sms/setup', {});
      return response.data;
    },
    onSuccess: () => {
      setSetupStep('sms-verify');
      toast.success('Verification code sent to your phone');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to send SMS code');
    },
  });

  const smsVerifyMutation = useMutation({
    mutationFn: async (code: string) => {
      const response = await apiClient.post<MfaEnableResult>('/api/account/security/mfa/sms/verify', { code });
      return response.data;
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['mfa-status'] });
      queryClient.invalidateQueries({ queryKey: ['security'] });
      if (data.recoveryCodes && data.recoveryCodes.length > 0) {
        setRecoveryCodes(data.recoveryCodes);
        setSetupStep('recovery-codes');
      } else {
        toast.success('SMS authentication enabled successfully');
        resetSetup();
      }
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Invalid verification code');
    },
  });

  // Email Setup
  const emailSetupMutation = useMutation({
    mutationFn: async () => {
      const response = await apiClient.post('/api/account/security/mfa/email/setup');
      return response.data;
    },
    onSuccess: () => {
      setSetupStep('email-verify');
      toast.success('Verification code sent to your email');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to send email code');
    },
  });

  const emailVerifyMutation = useMutation({
    mutationFn: async (code: string) => {
      const response = await apiClient.post<MfaEnableResult>('/api/account/security/mfa/email/verify', { code });
      return response.data;
    },
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['mfa-status'] });
      queryClient.invalidateQueries({ queryKey: ['security'] });
      if (data.recoveryCodes && data.recoveryCodes.length > 0) {
        setRecoveryCodes(data.recoveryCodes);
        setSetupStep('recovery-codes');
      } else {
        toast.success('Email authentication enabled successfully');
        resetSetup();
      }
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Invalid verification code');
    },
  });

  // Disable MFA
  const disableMutation = useMutation({
    mutationFn: async ({ method, password }: { method: string; password: string }) => {
      await apiClient.delete(`/api/account/security/mfa/${method}`, { data: { password } });
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['mfa-status'] });
      queryClient.invalidateQueries({ queryKey: ['security'] });
      toast.success('Two-factor authentication disabled');
      resetSetup();
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to disable 2FA');
    },
  });

  const resetSetup = () => {
    setSetupStep('none');
    setTotpSetup(null);
    setVerificationCode('');
    setRecoveryCodes([]);
    setPassword('');
    setMethodToDisable(null);
  };

  const handleStartSetup = (method: string) => {
    setVerificationCode('');
    if (method === 'totp') {
      totpSetupMutation.mutate();
    } else if (method === 'sms') {
      smsSetupMutation.mutate();
    } else if (method === 'email') {
      emailSetupMutation.mutate();
    }
  };

  const handleVerify = () => {
    if (!verificationCode) {
      toast.error('Please enter the verification code');
      return;
    }
    if (setupStep === 'totp-verify') {
      totpVerifyMutation.mutate(verificationCode);
    } else if (setupStep === 'sms-verify') {
      smsVerifyMutation.mutate(verificationCode);
    } else if (setupStep === 'email-verify') {
      emailVerifyMutation.mutate(verificationCode);
    }
  };

  const handleDisable = (method: string) => {
    setMethodToDisable(method);
    setSetupStep('disable');
  };

  const confirmDisable = () => {
    if (!password) {
      toast.error('Please enter your password');
      return;
    }
    if (methodToDisable) {
      disableMutation.mutate({ method: methodToDisable, password });
    }
  };

  const copyRecoveryCodes = () => {
    navigator.clipboard.writeText(recoveryCodes.join('\n'));
    toast.success('Recovery codes copied to clipboard');
  };

  if (isLoading) {
    return (
      <div className="flex items-center justify-center h-64">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-600" />
      </div>
    );
  }

  // Recovery codes view
  if (setupStep === 'recovery-codes') {
    return (
      <div className="max-w-2xl">
        <div className="mb-6">
          <Link
            to="/security"
            className="inline-flex items-center text-sm text-gray-500 hover:text-gray-700"
          >
            <ArrowLeftIcon className="h-4 w-4 mr-1" />
            Back to Security
          </Link>
        </div>

        <div className="bg-white shadow rounded-lg p-6">
          <div className="text-center mb-6">
            <CheckCircleIcon className="mx-auto h-12 w-12 text-green-500" />
            <h2 className="mt-4 text-xl font-semibold text-gray-900">
              Two-factor authentication enabled
            </h2>
            <p className="mt-2 text-sm text-gray-500">
              Save these recovery codes in a safe place. You can use them to access your account if you lose your device.
            </p>
          </div>

          <div className="bg-gray-50 rounded-lg p-4 mb-6">
            <div className="grid grid-cols-2 gap-2">
              {recoveryCodes.map((code, index) => (
                <code key={index} className="text-sm font-mono text-gray-700 bg-white px-3 py-2 rounded border">
                  {code}
                </code>
              ))}
            </div>
          </div>

          <div className="flex justify-center gap-4">
            <button
              onClick={copyRecoveryCodes}
              className="inline-flex items-center px-4 py-2 border border-gray-300 text-sm font-medium rounded-md shadow-sm text-gray-700 bg-white hover:bg-gray-50"
            >
              <ClipboardDocumentIcon className="h-5 w-5 mr-2" />
              Copy codes
            </button>
            <button
              onClick={resetSetup}
              className="inline-flex items-center px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700"
            >
              Done
            </button>
          </div>
        </div>
      </div>
    );
  }

  // Disable confirmation view
  if (setupStep === 'disable') {
    return (
      <div className="max-w-2xl">
        <div className="mb-6">
          <button
            onClick={resetSetup}
            className="inline-flex items-center text-sm text-gray-500 hover:text-gray-700"
          >
            <ArrowLeftIcon className="h-4 w-4 mr-1" />
            Back
          </button>
        </div>

        <div className="bg-white shadow rounded-lg p-6">
          <div className="text-center mb-6">
            <XCircleIcon className="mx-auto h-12 w-12 text-red-500" />
            <h2 className="mt-4 text-xl font-semibold text-gray-900">
              Disable two-factor authentication
            </h2>
            <p className="mt-2 text-sm text-gray-500">
              Enter your password to confirm disabling 2FA. This will make your account less secure.
            </p>
          </div>

          <div className="space-y-4">
            <div>
              <label htmlFor="password" className="block text-sm font-medium text-gray-700">
                Password
              </label>
              <input
                type="password"
                id="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
              />
            </div>

            <div className="flex justify-end gap-3">
              <button
                onClick={resetSetup}
                className="px-4 py-2 border border-gray-300 text-sm font-medium rounded-md shadow-sm text-gray-700 bg-white hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={confirmDisable}
                disabled={disableMutation.isPending}
                className="px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-red-600 hover:bg-red-700 disabled:opacity-50"
              >
                {disableMutation.isPending ? 'Disabling...' : 'Disable 2FA'}
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // TOTP verification view
  if (setupStep === 'totp-verify' && totpSetup) {
    return (
      <div className="max-w-2xl">
        <div className="mb-6">
          <button
            onClick={resetSetup}
            className="inline-flex items-center text-sm text-gray-500 hover:text-gray-700"
          >
            <ArrowLeftIcon className="h-4 w-4 mr-1" />
            Back
          </button>
        </div>

        <div className="bg-white shadow rounded-lg p-6">
          <h2 className="text-xl font-semibold text-gray-900 mb-4">
            Set up authenticator app
          </h2>

          <div className="space-y-6">
            <div>
              <p className="text-sm text-gray-500 mb-4">
                Scan this QR code with your authenticator app (Google Authenticator, Authy, 1Password, etc.)
              </p>

              <div className="flex justify-center mb-4">
                <div className="p-4 bg-white border rounded-lg">
                  <img
                    src={`https://api.qrserver.com/v1/create-qr-code/?size=200x200&data=${encodeURIComponent(totpSetup.qrCodeUri)}`}
                    alt="QR Code"
                    className="w-48 h-48"
                  />
                </div>
              </div>

              <div className="bg-gray-50 rounded-lg p-4 mb-4">
                <p className="text-xs text-gray-500 mb-1">Or enter this code manually:</p>
                <code className="text-sm font-mono text-gray-700">{totpSetup.secret}</code>
              </div>
            </div>

            <div>
              <label htmlFor="code" className="block text-sm font-medium text-gray-700">
                Enter the 6-digit code from your app
              </label>
              <input
                type="text"
                id="code"
                value={verificationCode}
                onChange={(e) => setVerificationCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                placeholder="000000"
                maxLength={6}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm font-mono text-center text-2xl tracking-widest"
              />
            </div>

            <div className="flex justify-end gap-3">
              <button
                onClick={resetSetup}
                className="px-4 py-2 border border-gray-300 text-sm font-medium rounded-md shadow-sm text-gray-700 bg-white hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleVerify}
                disabled={totpVerifyMutation.isPending || verificationCode.length !== 6}
                className="px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50"
              >
                {totpVerifyMutation.isPending ? 'Verifying...' : 'Verify & Enable'}
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // SMS/Email verification view
  if (setupStep === 'sms-verify' || setupStep === 'email-verify') {
    const isSms = setupStep === 'sms-verify';
    const isPending = isSms ? smsVerifyMutation.isPending : emailVerifyMutation.isPending;

    return (
      <div className="max-w-2xl">
        <div className="mb-6">
          <button
            onClick={resetSetup}
            className="inline-flex items-center text-sm text-gray-500 hover:text-gray-700"
          >
            <ArrowLeftIcon className="h-4 w-4 mr-1" />
            Back
          </button>
        </div>

        <div className="bg-white shadow rounded-lg p-6">
          <h2 className="text-xl font-semibold text-gray-900 mb-4">
            Verify {isSms ? 'phone number' : 'email address'}
          </h2>

          <div className="space-y-6">
            <p className="text-sm text-gray-500">
              We sent a verification code to your {isSms ? 'phone' : 'email'}. Enter it below to enable {isSms ? 'SMS' : 'email'} authentication.
            </p>

            <div>
              <label htmlFor="code" className="block text-sm font-medium text-gray-700">
                Verification code
              </label>
              <input
                type="text"
                id="code"
                value={verificationCode}
                onChange={(e) => setVerificationCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                placeholder="000000"
                maxLength={6}
                className="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm font-mono text-center text-2xl tracking-widest"
              />
            </div>

            <div className="flex justify-end gap-3">
              <button
                onClick={resetSetup}
                className="px-4 py-2 border border-gray-300 text-sm font-medium rounded-md shadow-sm text-gray-700 bg-white hover:bg-gray-50"
              >
                Cancel
              </button>
              <button
                onClick={handleVerify}
                disabled={isPending || verificationCode.length !== 6}
                className="px-4 py-2 border border-transparent text-sm font-medium rounded-md shadow-sm text-white bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50"
              >
                {isPending ? 'Verifying...' : 'Verify & Enable'}
              </button>
            </div>
          </div>
        </div>
      </div>
    );
  }

  // Main view - show available methods
  return (
    <div className="max-w-2xl">
      <div className="mb-6">
        <Link
          to="/security"
          className="inline-flex items-center text-sm text-gray-500 hover:text-gray-700"
        >
          <ArrowLeftIcon className="h-4 w-4 mr-1" />
          Back to Security
        </Link>
      </div>

      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Two-Factor Authentication</h1>
        <p className="mt-1 text-sm text-gray-500">
          Add an extra layer of security to your account by requiring a second form of verification.
        </p>
      </div>

      <div className="space-y-4">
        {/* Authenticator App */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-5">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-x-4">
                <div className="flex-shrink-0">
                  <ShieldCheckIcon className="h-10 w-10 text-indigo-600" />
                </div>
                <div>
                  <h3 className="text-base font-semibold text-gray-900">Authenticator App</h3>
                  <p className="text-sm text-gray-500">
                    Use an app like Google Authenticator, Authy, or 1Password
                  </p>
                </div>
              </div>
              {mfaStatus?.hasTotp ? (
                <div className="flex items-center gap-3">
                  <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                    Enabled
                  </span>
                  <button
                    onClick={() => handleDisable('totp')}
                    className="text-sm text-red-600 hover:text-red-500"
                  >
                    Disable
                  </button>
                </div>
              ) : (
                <button
                  onClick={() => handleStartSetup('totp')}
                  disabled={totpSetupMutation.isPending}
                  className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
                >
                  {totpSetupMutation.isPending ? 'Setting up...' : 'Set up'}
                </button>
              )}
            </div>
          </div>
        </div>

        {/* SMS */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-5">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-x-4">
                <div className="flex-shrink-0">
                  <DevicePhoneMobileIcon className="h-10 w-10 text-indigo-600" />
                </div>
                <div>
                  <h3 className="text-base font-semibold text-gray-900">SMS</h3>
                  <p className="text-sm text-gray-500">
                    {mfaStatus?.phoneNumber
                      ? `Receive codes at ${mfaStatus.phoneNumber}`
                      : 'Receive codes via text message'}
                  </p>
                </div>
              </div>
              {mfaStatus?.hasSms ? (
                <div className="flex items-center gap-3">
                  <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                    Enabled
                  </span>
                  <button
                    onClick={() => handleDisable('sms')}
                    className="text-sm text-red-600 hover:text-red-500"
                  >
                    Disable
                  </button>
                </div>
              ) : mfaStatus?.availableMethods.includes('sms') ? (
                <button
                  onClick={() => handleStartSetup('sms')}
                  disabled={smsSetupMutation.isPending}
                  className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
                >
                  {smsSetupMutation.isPending ? 'Setting up...' : 'Set up'}
                </button>
              ) : (
                <span className="text-sm text-gray-400">
                  Verify phone number first
                </span>
              )}
            </div>
          </div>
        </div>

        {/* Email */}
        <div className="bg-white shadow rounded-lg">
          <div className="px-6 py-5">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-x-4">
                <div className="flex-shrink-0">
                  <EnvelopeIcon className="h-10 w-10 text-indigo-600" />
                </div>
                <div>
                  <h3 className="text-base font-semibold text-gray-900">Email</h3>
                  <p className="text-sm text-gray-500">
                    {mfaStatus?.email
                      ? `Receive codes at ${mfaStatus.email}`
                      : 'Receive codes via email'}
                  </p>
                </div>
              </div>
              {mfaStatus?.hasEmail ? (
                <div className="flex items-center gap-3">
                  <span className="inline-flex items-center rounded-full bg-green-100 px-2.5 py-0.5 text-xs font-medium text-green-800">
                    Enabled
                  </span>
                  <button
                    onClick={() => handleDisable('email')}
                    className="text-sm text-red-600 hover:text-red-500"
                  >
                    Disable
                  </button>
                </div>
              ) : mfaStatus?.availableMethods.includes('email') ? (
                <button
                  onClick={() => handleStartSetup('email')}
                  disabled={emailSetupMutation.isPending}
                  className="rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
                >
                  {emailSetupMutation.isPending ? 'Setting up...' : 'Set up'}
                </button>
              ) : (
                <span className="text-sm text-gray-400">
                  Verify email first
                </span>
              )}
            </div>
          </div>
        </div>
      </div>

      {/* Info section */}
      <div className="mt-8 bg-blue-50 rounded-lg p-4">
        <h4 className="text-sm font-medium text-blue-800">Why use two-factor authentication?</h4>
        <p className="mt-1 text-sm text-blue-700">
          Two-factor authentication adds an extra layer of security to your account. Even if someone
          obtains your password, they won't be able to access your account without the second factor.
        </p>
      </div>
    </div>
  );
}
