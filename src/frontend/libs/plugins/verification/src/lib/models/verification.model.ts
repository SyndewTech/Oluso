/** A verification vendor the platform supports (api/admin/business-verification/providers). */
export interface VerificationProviderInfo {
  id: string;
  displayName: string;
  businessCountries: string[];
  /** Countries its hosted (provider-run) business verification covers. */
  hostedCountries: string[];
  /** The URL to give the provider for webhooks, on this organisation's host. */
  webhookUrl?: string | null;
  credentialFields: VerificationCredentialField[];
  options: VerificationProviderOption[];
}

export interface VerificationCredentialField {
  name: string;
  label: string;
  secret: boolean;
  description?: string | null;
  /** Needed to enable the provider; the others serve hosted verification only. */
  required: boolean;
}

export interface VerificationProviderOption {
  name: string;
  label: string;
  choices: string[];
  default: string;
  description?: string | null;
}

export interface BusinessVerificationCountry {
  code: string;
  name: string;
  providerIds: string[];
  routedProviderId?: string | null;
  providerName: string;
}

export type VerificationEnvironment = 'Sandbox' | 'Live';

/** Secrets are never returned; storedSecrets names the ones that are set. */
export interface ConfiguredVerificationProvider {
  providerId: string;
  enabled: boolean;
  environment: VerificationEnvironment;
  credentials: Record<string, string>;
  storedSecrets: string[];
  options: Record<string, string>;
}

export interface IdentityVerificationSettings {
  providers: ConfiguredVerificationProvider[];
  businessRouting: Record<string, string>;
  businessReuseMaxAgeDays: number;
}

export interface UpdateVerificationProviderRequest {
  providerId: string;
  enabled: boolean;
  environment: VerificationEnvironment;
  /** A blank secret keeps the stored value. */
  credentials: Record<string, string>;
  options: Record<string, string>;
}

export interface UpdateIdentityVerificationSettingsRequest {
  providers: UpdateVerificationProviderRequest[];
  businessRouting: Record<string, string>;
  businessReuseMaxAgeDays: number;
}

export interface VerificationConnectionTestResult {
  success: boolean;
  message: string;
}

export type BusinessVerificationStatus = 'pending' | 'in_review' | 'verified' | 'rejected' | 'failed';

export interface VerifiedBusinessSummary {
  id: string;
  status: BusinessVerificationStatus;
  statusReason?: string | null;
  country: string;
  registrationNumber: string;
  registeredName?: string | null;
  registryProviderId?: string | null;
  registryCheckedAt?: string | null;
  applicantCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface BusinessPerson {
  name: string;
  role?: string | null;
}

export interface BusinessDocument {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
}

export interface BusinessApplicant {
  id: string;
  businessId: string;
  subjectId?: string | null;
  subjectName?: string | null;
  relationship: string;
  evidenceRequirement: string;
  status: BusinessVerificationStatus;
  statusReason?: string | null;
  statusMessage?: string | null;
  providedBusinessName?: string | null;
  nameMatched?: boolean | null;
  directorMatched?: boolean | null;
  matchedPersonName?: string | null;
  matchedPersonRole?: string | null;
  documents: BusinessDocument[];
  outcomeEmailRequested: boolean;
  createdAt: string;
  updatedAt: string;
  decidedAt?: string | null;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  reviewNote?: string | null;
}

export interface BusinessCheck {
  id: string;
  applicantId?: string | null;
  kind: 'lookup' | 'reuse' | 'hosted_session' | 'review';
  outcome: string;
  providerId?: string | null;
  environment?: string | null;
  providerReference?: string | null;
  sessionReference?: string | null;
  message?: string | null;
  reusedFromCheckId?: string | null;
  originKind?: string | null;
  originId?: string | null;
  originName?: string | null;
  createdAt: string;
  completedAt?: string | null;
}

export interface VerifiedBusinessDetail extends VerifiedBusinessSummary {
  registrationType?: string | null;
  statusMessage?: string | null;
  entityType?: string | null;
  registrationDate?: string | null;
  registeredAddress?: string | null;
  activityStatus: 'Unknown' | 'Active' | 'Inactive';
  rawStatus?: string | null;
  registryEnvironment?: string | null;
  people: BusinessPerson[];
  decidedAt?: string | null;
  reviewedBy?: string | null;
  reviewedAt?: string | null;
  reviewNote?: string | null;
  applicants: BusinessApplicant[];
  checks: BusinessCheck[];
}

export interface VerifiedBusinessList {
  items: VerifiedBusinessSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface BusinessReviewQueueItem {
  applicant: BusinessApplicant;
  business: VerifiedBusinessSummary;
}

export interface BusinessReviewQueue {
  items: BusinessReviewQueueItem[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export type ReviewDecision = 'approve' | 'reject';

export const STATUS_LABELS: Record<BusinessVerificationStatus, string> = {
  pending: 'Pending',
  in_review: 'In review',
  verified: 'Verified',
  rejected: 'Rejected',
  failed: 'Failed',
};

export const STATUS_CLASSES: Record<BusinessVerificationStatus, string> = {
  pending: 'bg-blue-100 text-blue-800',
  in_review: 'bg-amber-100 text-amber-800',
  verified: 'bg-green-100 text-green-800',
  rejected: 'bg-red-100 text-red-800',
  failed: 'bg-gray-200 text-gray-700',
};

export const REASON_LABELS: Record<string, string> = {
  not_found: 'Not found in the registry',
  inactive: 'Not trading',
  name_mismatch: 'Business name mismatch',
  director_mismatch: 'Not a listed director',
  provider_unavailable: 'Provider unavailable',
  awaiting_provider: 'Waiting on the provider',
  awaiting_review: 'Waiting on a reviewer',
  documents_required: 'Documents requested',
  rejected_by_provider: 'Rejected by the provider',
  rejected_by_reviewer: 'Rejected by a reviewer',
  abandoned: 'Applicant abandoned the provider flow',
};

export const RELATIONSHIP_LABELS: Record<string, string> = {
  registry_director_match: 'Director on the registry',
  authorized_representative_declared: 'Declared representative',
  provider_verified: 'Verified by the provider',
};
