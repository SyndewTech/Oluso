/**
 * Allowlists a custom /authorize request parameter this client may pass and
 * have surfaced as a token claim. claimType must not be a reserved/system claim.
 */
export interface ClientAuthorizeParameter {
  parameter: string;
  claimType: string;
  required: boolean;
}
