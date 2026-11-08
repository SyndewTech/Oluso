export interface UserProfile {
  id: string;
  email: string;
  userName?: string;
  firstName?: string;
  lastName?: string;
  displayName?: string;
  picture?: string;
  phoneNumber?: string;
  emailVerified: boolean;
  phoneNumberVerified: boolean;
}

export interface UpdateProfileRequest {
  firstName?: string;
  lastName?: string;
  displayName?: string;
  phoneNumber?: string;
  picture?: string;
}
