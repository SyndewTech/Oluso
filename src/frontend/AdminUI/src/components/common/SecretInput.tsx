import { useState } from 'react';
import { EyeIcon, EyeSlashIcon } from '@heroicons/react/24/outline';

interface SecretInputProps {
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
  helperText?: string;
  onReveal?: () => Promise<string | null>;
  providerId?: number;
  secretKey?: string;
  disabled?: boolean;
  rows?: number;
  isTextArea?: boolean;
}

export default function SecretInput({
  label,
  value,
  onChange,
  placeholder,
  helperText,
  onReveal,
  disabled,
  rows = 1,
  isTextArea = false,
}: SecretInputProps) {
  const [showSecret, setShowSecret] = useState(false);
  const [isRevealing, setIsRevealing] = useState(false);
  const [revealedValue, setRevealedValue] = useState<string | null>(null);

  const isMasked = value === '••••••••';
  const displayValue = showSecret && revealedValue ? revealedValue : value;

  const handleReveal = async () => {
    if (!onReveal) {
      setShowSecret(!showSecret);
      return;
    }

    if (showSecret) {
      setShowSecret(false);
      return;
    }

    if (revealedValue) {
      setShowSecret(true);
      return;
    }

    setIsRevealing(true);
    try {
      const secret = await onReveal();
      if (secret) {
        setRevealedValue(secret);
        setShowSecret(true);
      }
    } finally {
      setIsRevealing(false);
    }
  };

  const handleChange = (newValue: string) => {
    // Clear the revealed value when user starts typing
    if (revealedValue) {
      setRevealedValue(null);
    }
    onChange(newValue);
  };

  const inputType = showSecret || !isMasked ? 'text' : 'password';

  return (
    <div>
      <label className="block text-sm font-medium text-gray-700">{label}</label>
      <div className="mt-1 relative">
        {isTextArea ? (
          <textarea
            value={displayValue}
            onChange={(e) => handleChange(e.target.value)}
            className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm font-mono text-xs pr-10"
            placeholder={placeholder}
            disabled={disabled}
            rows={rows}
          />
        ) : (
          <input
            type={inputType}
            value={displayValue}
            onChange={(e) => handleChange(e.target.value)}
            className="block w-full rounded-md border-gray-300 shadow-sm focus:border-blue-500 focus:ring-blue-500 sm:text-sm pr-10"
            placeholder={placeholder}
            disabled={disabled}
          />
        )}
        {(isMasked || value) && (
          <button
            type="button"
            onClick={handleReveal}
            disabled={isRevealing}
            className="absolute inset-y-0 right-0 flex items-center pr-3 text-gray-400 hover:text-gray-600"
            title={showSecret ? 'Hide secret' : 'Reveal secret'}
          >
            {isRevealing ? (
              <span className="text-xs">...</span>
            ) : showSecret ? (
              <EyeSlashIcon className="h-4 w-4" />
            ) : (
              <EyeIcon className="h-4 w-4" />
            )}
          </button>
        )}
      </div>
      {helperText && <p className="mt-1 text-xs text-gray-500">{helperText}</p>}
    </div>
  );
}
