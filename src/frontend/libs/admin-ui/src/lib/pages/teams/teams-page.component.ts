import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  DataTableComponent,
  IconComponent,
  PaginatedResponse,
} from '@syndew/oluso-ui-core';
import { TeamService } from '../../services/team.service';
import { Team, TeamType, TeamVisibility, CreateTeamRequest, managedByLabel } from '../../models/team.model';
import { PaginationParams } from '../../models/pagination-params.model';

interface TeamFormData {
  code: string;
  name: string;
  description: string;
  type: TeamType;
  parentTeamId: string;
  visibility: TeamVisibility;
}

@Component({
  selector: 'admin-teams-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent],
  templateUrl: './teams-page.component.html',
})
export class TeamsPageComponent {
  private readonly teamService = inject(TeamService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly teams = signal<Team[]>([]);

  // Pagination
  readonly currentPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly pageSize = 25;
  searchTerm = '';

  // Filters surfaced in the table header. Type is the most useful — drives
  // the "show me Billing teams only" view; the others are accessible via
  // the URL but not yet a dedicated UI.
  readonly typeFilter = signal<TeamType | ''>('');
  readonly includeArchived = signal(false);

  readonly showCreateModal = signal(false);
  readonly showArchiveModal = signal(false);
  readonly selectedTeam = signal<Team | null>(null);

  formData: TeamFormData = this.emptyForm();

  /** All team types — drives the type-filter chip row and the create modal dropdown. */
  readonly types: TeamType[] = ['Functional', 'Distribution', 'Project', 'Billing', 'Custom'];

  // Picker options for the "parent team" dropdown in the create modal. Loaded
  // lazily on modal-open so we don't fetch on every page render.
  readonly parentOptions = signal<Team[]>([]);

  ngOnInit(): void {
    this.searchTerm = this.route.snapshot.queryParamMap.get('search') ?? '';
    this.loadTeams();
  }

  loadTeams(): void {
    this.loading.set(true);
    const pagination: PaginationParams = {
      page: this.currentPage(),
      pageSize: this.pageSize,
    };

    const filters: Parameters<TeamService['query']>[1] = {};
    if (this.typeFilter()) filters.type = this.typeFilter() as TeamType;
    if (!this.includeArchived()) filters.isActive = true;
    if (this.searchTerm.trim()) filters.search = this.searchTerm.trim();

    this.teamService.query(pagination, filters).subscribe({
      next: (resp: PaginatedResponse<Team>) => {
        this.teams.set(resp.items);
        this.totalCount.set(resp.totalCount);
        this.totalPages.set(resp.totalPages);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load teams', err);
        this.loading.set(false);
      },
    });
  }

  onSearch(term: string): void {
    this.searchTerm = term;
    this.currentPage.set(1);
    this.loadTeams();
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
    this.loadTeams();
  }

  setTypeFilter(type: TeamType | ''): void {
    this.typeFilter.set(type);
    this.currentPage.set(1);
    this.loadTeams();
  }

  toggleArchivedFilter(): void {
    this.includeArchived.update((v) => !v);
    this.currentPage.set(1);
    this.loadTeams();
  }

  // ── Create ───────────────────────────────────────────────────────────

  openCreateModal(): void {
    this.formData = this.emptyForm();
    this.showCreateModal.set(true);
    // Parent picker shows active teams only — archiving a parent then
    // re-parenting under it would be a state we don't want to encourage.
    this.teamService.query({ page: 1, pageSize: 500 }, { isActive: true }).subscribe({
      // A module's teams can't take hand-made children.
      next: (resp) => this.parentOptions.set(resp.items.filter((t) => !t.managedBy)),
    });
  }

  closeCreateModal(): void {
    this.showCreateModal.set(false);
    this.formData = this.emptyForm();
  }

  createTeam(): void {
    if (!this.formData.code.trim() || !this.formData.name.trim()) return;
    this.saving.set(true);

    const request: CreateTeamRequest = {
      code: this.formData.code.trim(),
      name: this.formData.name.trim(),
      description: this.formData.description.trim() || undefined,
      type: this.formData.type,
      parentTeamId: this.formData.parentTeamId || undefined,
      visibility: this.formData.visibility,
    };

    this.teamService.create(request).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeCreateModal();
        this.loadTeams();
      },
      error: (err) => {
        console.error('Failed to create team', err);
        this.saving.set(false);
      },
    });
  }

  // ── Archive ──────────────────────────────────────────────────────────

  openArchiveModal(team: Team): void {
    this.selectedTeam.set(team);
    this.showArchiveModal.set(true);
  }

  closeArchiveModal(): void {
    this.showArchiveModal.set(false);
    this.selectedTeam.set(null);
  }

  confirmArchive(cascade: boolean): void {
    const team = this.selectedTeam();
    if (!team) return;
    this.saving.set(true);
    this.teamService.archive(team.id, cascade).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeArchiveModal();
        this.loadTeams();
      },
      error: (err) => {
        console.error('Failed to archive team', err);
        this.saving.set(false);
      },
    });
  }

  restoreTeam(team: Team): void {
    this.teamService.restore(team.id).subscribe({
      next: () => this.loadTeams(),
    });
  }

  // ── Navigation ───────────────────────────────────────────────────────

  readonly managedByLabel = managedByLabel;

  goToDetail(team: Team): void {
    this.router.navigate(['/teams', team.id]);
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString();
  }

  /**
   * Human-friendly badge variant for the team type chip. Functional/Billing
   * get distinct colours since they're the two most consequential types in
   * downstream gating (approval routing, entitlement).
   */
  typeBadgeVariant(type: TeamType): 'info' | 'success' | 'warning' | 'default' {
    switch (type) {
      case 'Billing':
        return 'success';
      case 'Functional':
        return 'info';
      case 'Project':
        return 'warning';
      default:
        return 'default';
    }
  }

  private emptyForm(): TeamFormData {
    return {
      code: '',
      name: '',
      description: '',
      type: 'Functional',
      parentTeamId: '',
      visibility: 'TenantWide',
    };
  }
}
