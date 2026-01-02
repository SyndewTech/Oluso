import { useState } from 'react';
import { useSearchParams, useNavigate } from 'react-router-dom';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import toast from 'react-hot-toast';
import { EnvelopeIcon, PhoneIcon, ArrowLeftIcon, CheckCircleIcon } from '@heroicons/react/24/outline';
import { apiClient } from '../services/api';

type VerificationType = 'email' | 'phone';

interface VerificationSentDto {
  maskedEmail?: string;
  maskedPhone?: string;
  codeSent: boolean;
}

export function VerifyPage() {
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const type = (searchParams.get('type') as VerificationType) || 'email';
  const [step, setStep] = useState<'send' | 'verify' | 'success'>('send');
  const [code, setCode] = useState('');
  const [maskedValue, setMaskedValue] = useState<string | null>(null);
  const [newValue, setNewValue] = useState('');
  const [showUpdateForm, setShowUpdateForm] = useState(false);

  const sendCodeMutation = useMutation({
    mutationFn: async (data?: { phoneNumber?: string; email?: string }) => {
      const endpoint = type === 'email'
        ? '/api/account/security/email/send-verification'
        : '/api/account/security/phone/send-verification';
      const response = await apiClient.post<VerificationSentDto>(endpoint, data || {});
      return response.data;
    },
    onSuccess: (data) => {
      setMaskedValue(data.maskedEmail || data.maskedPhone || null);
      setStep('verify');
      toast.success('Verification code sent!');
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to send verification code');
    },
  });

  const verifyCodeMutation = useMutation({
    mutationFn: async (verificationCode: string) => {
      const endpoint = type === 'email'
        ? '/api/account/security/email/verify'
        : '/api/account/security/phone/verify';
      await apiClient.post(endpoint, { code: verificationCode });
    },
    onSuccess: () => {
      setStep('success');
      queryClient.invalidateQueries({ queryKey: ['security'] });
      queryClient.invalidateQueries({ queryKey: ['profile'] });
      toast.success(`${type === 'email' ? 'Email' : 'Phone number'} verified successfully!`);
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Invalid verification code');
    },
  });

  const updateValueMutation = useMutation({
    mutationFn: async (value: string) => {
      const endpoint = type === 'email'
        ? '/api/account/security/email'
        : '/api/account/security/phone';
      const response = await apiClient.put<VerificationSentDto>(endpoint,
        type === 'email' ? { email: value } : { phoneNumber: value }
      );
      return response.data;
    },
    onSuccess: (data) => {
      setMaskedValue(data.maskedEmail || data.maskedPhone || null);
      setShowUpdateForm(false);
      setNewValue('');
      if (data.codeSent) {
        setStep('verify');
        toast.success('Verification code sent to new address!');
      }
    },
    onError: (error: any) => {
      toast.error(error.response?.data?.error || 'Failed to update');
    },
  });

  const handleSendCode = () => {
    sendCodeMutation.mutate({});
  };

  const handleVerify = (e: React.FormEvent) => {
    e.preventDefault();
    if (code.length === 6) {
      verifyCodeMutation.mutate(code);
    }
  };

  const handleUpdate = (e: React.FormEvent) => {
    e.preventDefault();
    if (newValue) {
      updateValueMutation.mutate(newValue);
    }
  };

  if (step === 'success') {
    return (
      <div className="max-w-md mx-auto">
        <div className="bg-white shadow rounded-lg p-8 text-center">
          <CheckCircleIcon className="h-16 w-16 text-green-500 mx-auto mb-4" />
          <h2 className="text-2xl font-bold text-gray-900 mb-2">
            {type === 'email' ? 'Email' : 'Phone Number'} Verified!
          </h2>
          <p className="text-gray-600 mb-6">
            Your {type === 'email' ? 'email address' : 'phone number'} has been successfully verified.
          </p>
          <button
            onClick={() => navigate('/security')}
            className="w-full rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500"
          >
            Back to Security Settings
          </button>
        </div>
      </div>
    );
  }

  return (
    <div className="max-w-md mx-auto">
      <button
        onClick={() => navigate('/security')}
        className="flex items-center text-sm text-gray-500 hover:text-gray-700 mb-6"
      >
        <ArrowLeftIcon className="h-4 w-4 mr-1" />
        Back to Security
      </button>

      <div className="bg-white shadow rounded-lg">
        <div className="px-6 py-5 border-b border-gray-200">
          <div className="flex items-center gap-x-3">
            {type === 'email' ? (
              <EnvelopeIcon className="h-6 w-6 text-gray-400" />
            ) : (
              <PhoneIcon className="h-6 w-6 text-gray-400" />
            )}
            <div>
              <h2 className="text-lg font-semibold text-gray-900">
                Verify {type === 'email' ? 'Email Address' : 'Phone Number'}
              </h2>
              <p className="text-sm text-gray-500">
                {step === 'send'
                  ? `We'll send a verification code to your ${type === 'email' ? 'email' : 'phone'}`
                  : `Enter the 6-digit code sent to ${maskedValue || 'your ' + type}`
                }
              </p>
            </div>
          </div>
        </div>

        <div className="px-6 py-5">
          {step === 'send' && !showUpdateForm && (
            <div className="space-y-4">
              <button
                onClick={handleSendCode}
                disabled={sendCodeMutation.isPending}
                className="w-full rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
              >
                {sendCodeMutation.isPending ? 'Sending...' : 'Send Verification Code'}
              </button>

              <div className="text-center">
                <button
                  onClick={() => setShowUpdateForm(true)}
                  className="text-sm text-indigo-600 hover:text-indigo-500"
                >
                  {type === 'email' ? 'Use a different email' : 'Use a different phone number'}
                </button>
              </div>
            </div>
          )}

          {step === 'send' && showUpdateForm && (
            <form onSubmit={handleUpdate} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  {type === 'email' ? 'New Email Address' : 'New Phone Number'}
                </label>
                <input
                  type={type === 'email' ? 'email' : 'tel'}
                  value={newValue}
                  onChange={(e) => setNewValue(e.target.value)}
                  placeholder={type === 'email' ? 'you@example.com' : '+1 (555) 123-4567'}
                  className="w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 sm:text-sm"
                  required
                />
              </div>

              <div className="flex gap-3">
                <button
                  type="button"
                  onClick={() => {
                    setShowUpdateForm(false);
                    setNewValue('');
                  }}
                  className="flex-1 rounded-md bg-white px-4 py-2 text-sm font-semibold text-gray-900 shadow-sm ring-1 ring-inset ring-gray-300 hover:bg-gray-50"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={updateValueMutation.isPending || !newValue}
                  className="flex-1 rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
                >
                  {updateValueMutation.isPending ? 'Updating...' : 'Update & Verify'}
                </button>
              </div>
            </form>
          )}

          {step === 'verify' && (
            <form onSubmit={handleVerify} className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-700 mb-1">
                  Verification Code
                </label>
                <input
                  type="text"
                  value={code}
                  onChange={(e) => setCode(e.target.value.replace(/\D/g, '').slice(0, 6))}
                  placeholder="000000"
                  className="w-full rounded-md border-gray-300 shadow-sm focus:border-indigo-500 focus:ring-indigo-500 text-center text-2xl tracking-widest font-mono"
                  maxLength={6}
                  autoFocus
                />
                <p className="mt-2 text-sm text-gray-500 text-center">
                  Code sent to {maskedValue}
                </p>
              </div>

              <button
                type="submit"
                disabled={verifyCodeMutation.isPending || code.length !== 6}
                className="w-full rounded-md bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
              >
                {verifyCodeMutation.isPending ? 'Verifying...' : 'Verify'}
              </button>

              <div className="text-center">
                <button
                  type="button"
                  onClick={() => {
                    setCode('');
                    sendCodeMutation.mutate({});
                  }}
                  disabled={sendCodeMutation.isPending}
                  className="text-sm text-indigo-600 hover:text-indigo-500"
                >
                  Didn't receive a code? Resend
                </button>
              </div>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
