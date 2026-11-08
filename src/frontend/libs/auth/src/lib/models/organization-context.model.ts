export interface OrganizationContext {
  id: string;
  name: string;
  slug?: string;
  type?: 'personal' | 'team' | 'enterprise';
  role?: string;
  permissions?: string[];
}
