import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Subject, debounceTime, distinctUntilChanged, switchMap, of } from 'rxjs';
import {
  ButtonComponent,
  BadgeComponent,
  IconComponent,
  ModalComponent,
} from '@syndew/oluso-ui-core';
import { TeamService } from '../../services/team.service';
import { UserService } from '../../services/user.service';
import { Team, TeamMembership, TeamMemberRole, managedByLabel } from '../../models/team.model';
import { UserDto } from '../../models/user.model';

@Component({
  selector: 'admin-team-detail-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, BadgeComponent, IconComponent, ModalComponent],
  template: `
    <div class="p-6 space-y-6">
      <div class="flex items-center gap-3">
        <ui-button variant="ghost" (clicked)="onBack()">
          <ui-icon name="arrow-left" class="h-4 w-4 mr-2" />
          Back
        </ui-button>
      </div>

      @if (loading()) {
        <div class="flex items-center justify-center h-64 text-sm text-gray-500">Loading team…</div>
      } @else if (!team()) {
        <div class="text-sm text-red-600">Team not found.</div>
      } @else {
        <div class="bg-white border border-gray-200 rounded-lg p-6">
          <div class="flex items-start justify-between gap-4">
            <div>
              <div class="flex items-center gap-2">
                <h1 class="text-2xl font-semibold text-gray-900">{{ team()!.name }}</h1>
                <ui-badge [variant]="team()!.isActive ? 'success' : 'default'">
                  {{ team()!.isActive ? 'Active' : 'Archived' }}
                </ui-badge>
                @if (team()!.managedBy) {
                  <ui-badge variant="info">Managed by {{ managedByLabel() }}</ui-badge>
                }
              </div>
              <p class="mt-1 text-sm text-gray-500 font-mono">{{ team()!.code }} · {{ team()!.type }} · {{ team()!.visibility }}</p>
              @if (team()!.description) {
                <p class="mt-2 text-sm text-gray-700">{{ team()!.description }}</p>
              }
              @if (team()!.managedBy) {
                <p class="mt-2 text-sm text-gray-600">
                  {{ managedByLabel() }} creates this team and keeps its members in sync from its own records.
                  To change it, make the change in {{ managedByLabel() }}.
                </p>
              }
            </div>
          </div>
        </div>

        <div class="bg-white border border-gray-200 rounded-lg">
          <div class="flex items-center justify-between p-4 border-b border-gray-200">
            <div>
              <h2 class="text-sm font-medium text-gray-900">Members ({{ members().length }})</h2>
              <p class="mt-0.5 text-xs text-gray-500">Users who belong to this team. Add members to use the team as an approver or distribution target.</p>
            </div>
            <ui-button (clicked)="openAddMemberModal()" [disabled]="readOnly()">
              <ui-icon name="plus" class="h-4 w-4 mr-1.5" />
              Add member
            </ui-button>
          </div>

          @if (loadingMembers()) {
            <div class="px-4 py-8 text-sm text-center text-gray-500">Loading members…</div>
          } @else if (members().length === 0) {
            <div class="px-4 py-12 text-center">
              <p class="text-sm text-gray-500">No members yet.</p>
              <p class="mt-1 text-xs text-gray-400">Click "Add member" to assign users to this team.</p>
            </div>
          } @else {
            <table class="w-full text-sm">
              <thead class="bg-gray-50 text-xs uppercase tracking-wide text-gray-500">
                <tr>
                  <th class="px-4 py-2 text-left">User</th>
                  <th class="px-4 py-2 text-left">Role</th>
                  <th class="px-4 py-2 text-left">Joined</th>
                  <th class="px-4 py-2 text-right">Actions</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-gray-100">
                @for (m of members(); track m.id) {
                  <tr>
                    <td class="px-4 py-3">
                      <div class="font-medium text-gray-900">{{ memberDisplayName(m) }}</div>
                      <div class="text-xs text-gray-500 font-mono">{{ m.userId }}</div>
                    </td>
                    <td class="px-4 py-3">
                      <select
                        [ngModel]="m.role"
                        (ngModelChange)="onRoleChange(m, $event)"
                        [disabled]="readOnly() || rowBusy() === m.userId"
                        class="text-sm rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500"
                      >
                        <option value="Member">Member</option>
                        <option value="Lead">Lead</option>
                      </select>
                    </td>
                    <td class="px-4 py-3 text-gray-500">{{ formatDate(m.joinedAt) }}</td>
                    <td class="px-4 py-3 text-right">
                      <button
                        type="button"
                        (click)="openRemoveModal(m)"
                        [disabled]="readOnly() || rowBusy() === m.userId"
                        class="text-xs text-red-600 hover:text-red-800 disabled:opacity-50"
                      >
                        Remove
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </div>
      }
    </div>

    <ui-modal [open]="showAddModal()" title="Add member" (closed)="closeAddMemberModal()">
      <div class="space-y-4">
        <div>
          <label class="block text-xs font-medium text-gray-700">User</label>
          <input
            type="text"
            [ngModel]="addMemberSearchTerm"
            (ngModelChange)="onUserSearch($event)"
            placeholder="Search by name or email…"
            class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-sm"
            autocomplete="off"
          />
          @if (userSearchResults().length > 0) {
            <div class="mt-1 max-h-48 overflow-y-auto border border-gray-200 rounded-md divide-y divide-gray-100">
              @for (u of userSearchResults(); track u.id) {
                <button
                  type="button"
                  (click)="selectUser(u)"
                  class="w-full px-3 py-2 text-left text-sm hover:bg-gray-50"
                  [class.bg-primary-50]="selectedUser()?.id === u.id"
                >
                  <div class="font-medium text-gray-900">{{ u.displayName || u.email }}</div>
                  <div class="text-xs text-gray-500">{{ u.email }}</div>
                </button>
              }
            </div>
          }
          @if (selectedUser()) {
            <p class="mt-2 text-xs text-gray-600">Selected: <strong>{{ selectedUser()!.displayName || selectedUser()!.email }}</strong></p>
          }
        </div>

        <div>
          <label class="block text-xs font-medium text-gray-700">Role</label>
          <select
            [(ngModel)]="addMemberRole"
            class="mt-1 block w-full rounded-md border-gray-300 shadow-sm focus:border-primary-500 focus:ring-primary-500 text-sm"
          >
            <option value="Member">Member</option>
            <option value="Lead">Lead</option>
          </select>
        </div>

        @if (addMemberError()) {
          <p class="text-xs text-red-600">{{ addMemberError() }}</p>
        }
      </div>

      <div modal-footer class="flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="closeAddMemberModal()">Cancel</ui-button>
        <ui-button (clicked)="confirmAddMember()" [disabled]="!selectedUser() || saving()">
          {{ saving() ? 'Adding…' : 'Add member' }}
        </ui-button>
      </div>
    </ui-modal>

    <ui-modal [open]="showRemoveModal()" title="Remove member" (closed)="closeRemoveModal()">
      @if (memberToRemove()) {
        <p class="text-sm text-gray-700">
          Remove <strong>{{ memberDisplayName(memberToRemove()!) }}</strong> from this team?
        </p>
        <p class="mt-2 text-xs text-gray-500">
          The user keeps their account; only the team membership is ended.
        </p>
      }

      <div modal-footer class="flex justify-end gap-2">
        <ui-button variant="secondary" (clicked)="closeRemoveModal()">Cancel</ui-button>
        <ui-button variant="danger" (clicked)="confirmRemove()" [disabled]="saving()">
          {{ saving() ? 'Removing…' : 'Remove' }}
        </ui-button>
      </div>
    </ui-modal>
  `,
})
export class TeamDetailPageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly teamService = inject(TeamService);
  private readonly userService = inject(UserService);

  readonly teamId = signal<string>('');
  readonly team = signal<Team | null>(null);
  readonly members = signal<TeamMembership[]>([]);
  readonly memberUserById = signal<Map<string, UserDto>>(new Map());
  readonly loading = signal(true);
  readonly loadingMembers = signal(false);
  readonly saving = signal(false);
  readonly rowBusy = signal<string | null>(null);

  readonly showAddModal = signal(false);
  readonly showRemoveModal = signal(false);
  readonly selectedUser = signal<UserDto | null>(null);
  readonly userSearchResults = signal<UserDto[]>([]);
  readonly addMemberError = signal<string | null>(null);
  readonly memberToRemove = signal<TeamMembership | null>(null);

  addMemberSearchTerm = '';
  addMemberRole: TeamMemberRole = 'Member';

  private readonly userSearch$ = new Subject<string>();

  /** Archived teams, and teams a module maintains, can't be edited here. */
  readonly readOnly = computed(() => {
    const t = this.team();
    return !t || !t.isActive || !!t.managedBy;
  });
  readonly managedByLabel = computed(() => managedByLabel(this.team()));

  readonly memberUserIds = computed(() => new Set(this.members().map((m) => m.userId)));

  ngOnInit(): void {
    this.userSearch$
      .pipe(
        debounceTime(250),
        distinctUntilChanged(),
        switchMap((term) => {
          if (!term.trim()) {
            return of({ items: [] as UserDto[], totalCount: 0, page: 1, pageSize: 10, totalPages: 0 });
          }
          return this.userService.getUsers({ page: 1, pageSize: 10 }, { search: term.trim() });
        }),
      )
      .subscribe({
        next: (resp) => this.userSearchResults.set(resp.items),
        error: () => this.userSearchResults.set([]),
      });

    this.route.paramMap.subscribe((params) => {
      const id = params.get('id');
      if (!id) return;
      this.teamId.set(id);
      this.loadTeam();
      this.loadMembers();
    });
  }

  private loadTeam(): void {
    this.loading.set(true);
    this.teamService.getById(this.teamId()).subscribe({
      next: (t) => {
        this.team.set(t);
        this.loading.set(false);
      },
      error: () => {
        this.team.set(null);
        this.loading.set(false);
      },
    });
  }

  private loadMembers(): void {
    this.loadingMembers.set(true);
    this.teamService.getMembers(this.teamId()).subscribe({
      next: (list) => {
        this.members.set(list);
        this.loadingMembers.set(false);
        this.hydrateMemberNames(list);
      },
      error: () => this.loadingMembers.set(false),
    });
  }

  // Resolve display names for the members in one go so the table doesn't
  // show raw user IDs. Done after the membership list lands so we can do a
  // single search query per page rather than N round-trips.
  private hydrateMemberNames(members: TeamMembership[]): void {
    if (members.length === 0) return;
    this.userService.getUsers({ page: 1, pageSize: 200 }, {}).subscribe({
      next: (resp) => {
        const map = new Map<string, UserDto>();
        for (const u of resp.items) map.set(u.id, u);
        this.memberUserById.set(map);
      },
    });
  }

  memberDisplayName(m: TeamMembership): string {
    const u = this.memberUserById().get(m.userId);
    if (u) return u.displayName || u.email || m.userId;
    return m.userId;
  }

  openAddMemberModal(): void {
    this.addMemberSearchTerm = '';
    this.addMemberRole = 'Member';
    this.selectedUser.set(null);
    this.userSearchResults.set([]);
    this.addMemberError.set(null);
    this.showAddModal.set(true);
  }

  closeAddMemberModal(): void {
    this.showAddModal.set(false);
  }

  onUserSearch(term: string): void {
    this.addMemberSearchTerm = term;
    this.userSearch$.next(term);
  }

  selectUser(u: UserDto): void {
    if (this.memberUserIds().has(u.id)) {
      this.addMemberError.set(`${u.displayName || u.email} is already a member.`);
      return;
    }
    this.addMemberError.set(null);
    this.selectedUser.set(u);
    this.addMemberSearchTerm = u.displayName || u.email || '';
    this.userSearchResults.set([]);
  }

  confirmAddMember(): void {
    const user = this.selectedUser();
    if (!user) return;
    this.saving.set(true);
    this.addMemberError.set(null);
    this.teamService.addMember(this.teamId(), { userId: user.id, role: this.addMemberRole }).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeAddMemberModal();
        this.loadMembers();
      },
      error: (err) => {
        this.saving.set(false);
        this.addMemberError.set(err?.error?.message ?? 'Failed to add member');
      },
    });
  }

  onRoleChange(m: TeamMembership, newRole: TeamMemberRole): void {
    if (m.role === newRole) return;
    this.rowBusy.set(m.userId);
    this.teamService.changeRole(this.teamId(), m.userId, { role: newRole }).subscribe({
      next: (updated) => {
        this.members.update((list) => list.map((x) => (x.id === m.id ? updated : x)));
        this.rowBusy.set(null);
      },
      error: () => {
        this.rowBusy.set(null);
        this.loadMembers();
      },
    });
  }

  openRemoveModal(m: TeamMembership): void {
    this.memberToRemove.set(m);
    this.showRemoveModal.set(true);
  }

  closeRemoveModal(): void {
    this.showRemoveModal.set(false);
    this.memberToRemove.set(null);
  }

  confirmRemove(): void {
    const m = this.memberToRemove();
    if (!m) return;
    this.saving.set(true);
    this.teamService.removeMember(this.teamId(), m.userId).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeRemoveModal();
        this.loadMembers();
      },
      error: () => this.saving.set(false),
    });
  }

  onBack(): void {
    this.router.navigate(['/teams']);
  }

  formatDate(iso: string): string {
    return new Date(iso).toLocaleDateString();
  }
}
