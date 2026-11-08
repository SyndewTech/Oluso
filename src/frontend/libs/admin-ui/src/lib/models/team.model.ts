// Wire shapes for the Teams admin API. Mirror the BE DTOs in
// src/backend/Oluso.Admin/Controllers/TeamDtos.cs — keep these in sync when
// the BE shape changes.

export type TeamType =
  | 'Functional'
  | 'Distribution'
  | 'Project'
  | 'Billing'
  | 'Custom';

export type TeamMemberRole = 'Member' | 'Lead';

export type TeamVisibility = 'TenantWide' | 'Private';

export interface Team {
  id: string;
  code: string;
  name: string;
  description?: string;
  type: TeamType;
  parentTeamId?: string;
  ownerUserId?: string;
  visibility: TeamVisibility;
  isActive: boolean;
  displayOrder: number;
  memberCount: number;
  /** Feature key of the module that maintains this team; absent for teams managed by hand. */
  managedBy?: string;
  /** That module's display name, e.g. "Education". */
  managedByName?: string;
  createdAt: string;
  createdBy?: string;
  updatedAt?: string;
  updatedBy?: string;
}

export interface TeamSummary {
  id: string;
  code: string;
  name: string;
  type: TeamType;
  parentTeamId?: string;
  isActive: boolean;
}

export interface TeamMembership {
  id: string;
  teamId: string;
  userId: string;
  role: TeamMemberRole;
  isActive: boolean;
  joinedAt: string;
  leftAt?: string;
}

export interface CreateTeamRequest {
  code: string;
  name: string;
  description?: string;
  type?: TeamType;
  parentTeamId?: string;
  ownerUserId?: string;
  visibility?: TeamVisibility;
  isActive?: boolean;
  displayOrder?: number;
}

/**
 * Patch-style update. Undefined leaves the value unchanged; null on
 * `parentTeamId` clears the parent and promotes the team to a root.
 */
export interface UpdateTeamRequest {
  code?: string;
  name?: string;
  description?: string;
  type?: TeamType;
  parentTeamId?: string | null;
  ownerUserId?: string | null;
  visibility?: TeamVisibility;
  displayOrder?: number;
}

export interface AddMemberRequest {
  userId: string;
  role?: TeamMemberRole;
}

export interface ChangeMemberRoleRequest {
  role: TeamMemberRole;
}

/** "Education" for a team the Education module maintains; empty for hand-managed teams. */
export function managedByLabel(team: Pick<Team, 'managedBy' | 'managedByName'> | null | undefined): string {
  if (!team?.managedBy) return '';
  return team.managedByName || team.managedBy.charAt(0).toUpperCase() + team.managedBy.slice(1);
}
