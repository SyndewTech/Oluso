import { useAuth } from '../contexts/AuthContext';
import {
  UserIcon,
  EnvelopeIcon,
  BuildingOfficeIcon,
} from '@heroicons/react/24/outline';

export default function ProfilePage() {
  const { user } = useAuth();

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900">My Profile</h1>
        <p className="mt-1 text-sm text-gray-500">View and manage your personal information</p>
      </div>

      <div className="bg-white shadow rounded-lg overflow-hidden">
        {/* Header */}
        <div className="bg-gradient-to-r from-primary-600 to-primary-700 px-6 py-8">
          <div className="flex items-center gap-4">
            {user?.profilePictureUrl ? (
              <img
                src={user.profilePictureUrl}
                alt={user.displayName}
                className="h-20 w-20 rounded-full border-4 border-white object-cover"
              />
            ) : (
              <div className="h-20 w-20 rounded-full bg-white flex items-center justify-center">
                <span className="text-primary-600 text-2xl font-bold">
                  {user?.firstName?.[0]}
                  {user?.lastName?.[0]}
                </span>
              </div>
            )}
            <div className="text-white">
              <h2 className="text-xl font-bold">{user?.displayName}</h2>
              <p className="text-primary-100">{user?.email}</p>
            </div>
          </div>
        </div>

        {/* Info sections */}
        <div className="p-6">
          <h3 className="text-lg font-semibold text-gray-900 mb-4">Personal Information</h3>
          <dl className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <div className="flex items-start gap-3">
              <UserIcon className="h-5 w-5 text-gray-400 mt-0.5" />
              <div>
                <dt className="text-sm text-gray-500">Full Name</dt>
                <dd className="text-sm font-medium text-gray-900">{user?.displayName}</dd>
              </div>
            </div>
            <div className="flex items-start gap-3">
              <EnvelopeIcon className="h-5 w-5 text-gray-400 mt-0.5" />
              <div>
                <dt className="text-sm text-gray-500">Email</dt>
                <dd className="text-sm font-medium text-gray-900">{user?.email}</dd>
              </div>
            </div>
            {user?.employeeId && (
              <div className="flex items-start gap-3">
                <BuildingOfficeIcon className="h-5 w-5 text-gray-400 mt-0.5" />
                <div>
                  <dt className="text-sm text-gray-500">Employee ID</dt>
                  <dd className="text-sm font-medium text-gray-900">{user.employeeId}</dd>
                </div>
              </div>
            )}
          </dl>
        </div>

        {/* Actions */}
        <div className="px-6 py-4 bg-gray-50 border-t border-gray-200">
          <p className="text-sm text-gray-500">
            To update your profile information, please contact HR or use the self-service portal.
          </p>
        </div>
      </div>
    </div>
  );
}
