/**
 * A permission the tenant's own applications enforce. Assignable to roles and
 * delivered in the `entitlements` claim — Oluso attaches no meaning to it.
 */
export interface EntitlementDto {
  id: string;
  /** Value carried in the token (e.g. `invoices.approve`). */
  name: string;
  displayName: string;
  description?: string;
  group?: string;
  enabled: boolean;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateEntitlementRequest {
  name: string;
  displayName?: string;
  description?: string;
  group?: string;
  enabled?: boolean;
}

export interface UpdateEntitlementRequest {
  displayName?: string;
  description?: string;
  group?: string;
  enabled?: boolean;
}
