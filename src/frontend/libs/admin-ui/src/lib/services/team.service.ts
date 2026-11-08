import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { PaginatedResponse } from '@oluso/ui-core';
import { ApiService } from './api.service';
import { PaginationParams } from '../models/pagination-params.model';
import {
  Team,
  TeamSummary,
  TeamMembership,
  TeamType,
  CreateTeamRequest,
  UpdateTeamRequest,
  AddMemberRequest,
  ChangeMemberRoleRequest,
} from '../models/team.model';

/**
 * Client for the tenant-Teams admin API. Maps 1:1 onto the endpoints exposed
 * by `Oluso.Admin/Controllers/TeamsController.cs` — every method here has a
 * direct controller-action counterpart.
 */
@Injectable({ providedIn: 'root' })
export class TeamService {
  private readonly api = inject(ApiService);
  private readonly basePath = '/teams';

  /** Paginated list with optional type / parent / active / search filters. */
  query(
    pagination: PaginationParams,
    filters?: {
      type?: TeamType;
      parentTeamId?: string;
      isActive?: boolean;
      search?: string;
    },
  ): Observable<PaginatedResponse<Team>> {
    return this.api.getPaginated<Team>(
      this.basePath,
      pagination,
      filters as Record<string, string | number | boolean>,
    );
  }

  /**
   * Lightweight picker payload. Used by every team-picker dropdown across
   * the platform (e.g. approval workflow step config, project owner picker).
   */
  picker(type?: TeamType, activeOnly = true): Observable<TeamSummary[]> {
    const params: Record<string, string | number | boolean> = { activeOnly };
    if (type) params['type'] = type;
    return this.api.get<TeamSummary[]>(`${this.basePath}/picker`, params);
  }

  getById(id: string): Observable<Team> {
    return this.api.get<Team>(`${this.basePath}/${id}`);
  }

  create(request: CreateTeamRequest): Observable<Team> {
    return this.api.post<Team>(this.basePath, request);
  }

  update(id: string, request: UpdateTeamRequest): Observable<Team> {
    return this.api.put<Team>(`${this.basePath}/${id}`, request);
  }

  /**
   * Soft-archive. `cascadeChildren=true` archives the whole subtree —
   * useful for retiring a department + its sub-teams in one call.
   */
  archive(id: string, cascadeChildren = false): Observable<void> {
    return this.api.post<void>(
      `${this.basePath}/${id}/archive?cascadeChildren=${cascadeChildren}`,
      null,
    );
  }

  restore(id: string): Observable<void> {
    return this.api.post<void>(`${this.basePath}/${id}/restore`, null);
  }

  delete(id: string): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/${id}`);
  }

  getChildren(id: string, activeOnly = true): Observable<Team[]> {
    return this.api.get<Team[]>(`${this.basePath}/${id}/children`, { activeOnly });
  }

  // ── Members ──────────────────────────────────────────────────────────

  getMembers(teamId: string, activeOnly = true): Observable<TeamMembership[]> {
    return this.api.get<TeamMembership[]>(
      `${this.basePath}/${teamId}/members`,
      { activeOnly },
    );
  }

  addMember(teamId: string, request: AddMemberRequest): Observable<TeamMembership> {
    return this.api.post<TeamMembership>(
      `${this.basePath}/${teamId}/members`,
      request,
    );
  }

  changeRole(
    teamId: string,
    userId: string,
    request: ChangeMemberRoleRequest,
  ): Observable<TeamMembership> {
    return this.api.put<TeamMembership>(
      `${this.basePath}/${teamId}/members/${userId}/role`,
      request,
    );
  }

  removeMember(teamId: string, userId: string): Observable<void> {
    return this.api.delete<void>(`${this.basePath}/${teamId}/members/${userId}`);
  }
}
