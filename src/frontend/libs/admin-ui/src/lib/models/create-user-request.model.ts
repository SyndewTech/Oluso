import { UserClaimDto } from './user-claim.model';

export interface CreateUserRequest {
  email: string;
  username?: string;
  /** Leave empty with {@link sendInvite}: the user sets their own. */
  password?: string;
  /** Email the user a link to set their own password, valid for the tenant's account-link lifetime. */
  sendInvite?: boolean;
  firstName?: string;
  lastName?: string;
  phoneNumber?: string;
  emailVerified?: boolean;
  enabled?: boolean;
  roleIds?: string[];
  claims?: UserClaimDto[];
  sendVerificationEmail?: boolean;
}
