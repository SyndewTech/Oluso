import { Component, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  IconComponent,
} from '@oluso/ui-core';
import { ModuleSeatsService, ModuleSeatSummary, ModuleSeatDetail, ModuleSeatUser } from '../../services/module-seats.service';
import { UserService } from '../../services/user.service';
import { UserDto } from '../../models/user.model';

@Component({
  selector: 'admin-module-seats-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent],
  templateUrl: './module-seats-page.component.html',
})
export class ModuleSeatsPageComponent implements OnInit {
  private readonly moduleSeatsService = inject(ModuleSeatsService);
  private readonly userService = inject(UserService);

  readonly loading = signal(true);
  readonly modules = signal<ModuleSeatSummary[]>([]);

  // Detail view
  readonly selectedModule = signal<ModuleSeatDetail | null>(null);
  readonly detailLoading = signal(false);

  // Add user modal
  readonly showAddUserModal = signal(false);
  readonly showRemoveUserModal = signal(false);
  readonly selectedUser = signal<ModuleSeatUser | null>(null);
  readonly userSearchResults = signal<UserDto[]>([]);
  readonly selectedUserForAdd = signal<UserDto | null>(null);
  readonly saving = signal(false);
  userSearchTerm = '';
  private searchTimeout: ReturnType<typeof setTimeout> | undefined;

  ngOnInit(): void {
    this.loadModules();
  }

  loadModules(): void {
    this.loading.set(true);
    this.moduleSeatsService.getAllModuleSeats().subscribe({
      next: (modules) => {
        this.modules.set(modules);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load module seats:', error);
        this.loading.set(false);
      },
    });
  }

  selectModule(featureKey: string): void {
    this.detailLoading.set(true);
    this.moduleSeatsService.getModuleSeats(featureKey).subscribe({
      next: (detail) => {
        this.selectedModule.set(detail);
        this.detailLoading.set(false);
      },
      error: (error) => {
        console.error('Failed to load module detail:', error);
        this.detailLoading.set(false);
      },
    });
  }

  closeDetail(): void {
    this.selectedModule.set(null);
  }

  // User search
  onUserSearch(term: string): void {
    this.userSearchTerm = term;
    if (this.searchTimeout) {
      clearTimeout(this.searchTimeout);
    }
    if (!term || term.length < 2) {
      this.userSearchResults.set([]);
      return;
    }
    this.searchTimeout = setTimeout(() => {
      this.userService.getUsers({ page: 1, pageSize: 8 }, { search: term }).subscribe({
        next: (response) => this.userSearchResults.set(response.items),
        error: () => this.userSearchResults.set([]),
      });
    }, 300);
  }

  selectUserForAdd(user: UserDto): void {
    this.selectedUserForAdd.set(user);
    this.userSearchResults.set([]);
    this.userSearchTerm = '';
  }

  clearSelectedUser(): void {
    this.selectedUserForAdd.set(null);
    this.userSearchTerm = '';
  }

  // Add user modal
  openAddUserModal(): void {
    this.selectedUserForAdd.set(null);
    this.userSearchResults.set([]);
    this.userSearchTerm = '';
    this.showAddUserModal.set(true);
  }

  closeAddUserModal(): void {
    this.showAddUserModal.set(false);
    this.clearSelectedUser();
  }

  grantSeat(): void {
    const module = this.selectedModule();
    const user = this.selectedUserForAdd();
    if (!module || !user) return;

    this.saving.set(true);
    this.moduleSeatsService.grantSeat(module.featureKey, user.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeAddUserModal();
        this.selectModule(module.featureKey);
        this.loadModules();
      },
      error: (error) => {
        console.error('Failed to grant seat:', error);
        this.saving.set(false);
      },
    });
  }

  // Remove user
  openRemoveUserModal(user: ModuleSeatUser): void {
    this.selectedUser.set(user);
    this.showRemoveUserModal.set(true);
  }

  closeRemoveUserModal(): void {
    this.showRemoveUserModal.set(false);
    this.selectedUser.set(null);
  }

  confirmRemoveUser(): void {
    const module = this.selectedModule();
    const user = this.selectedUser();
    if (!module || !user) return;

    this.moduleSeatsService.revokeSeat(module.featureKey, user.userId).subscribe({
      next: () => {
        this.closeRemoveUserModal();
        this.selectModule(module.featureKey);
        this.loadModules();
      },
      error: (error) => {
        console.error('Failed to revoke seat:', error);
      },
    });
  }

  getUsagePercent(module: ModuleSeatSummary): number {
    if (!module.maxSeats || module.maxSeats === 0) return 0;
    return Math.round((module.usedSeats / module.maxSeats) * 100);
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
