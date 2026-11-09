import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  IconComponent,
} from '@syndew/oluso-ui-core';
import { EntitlementService } from '../../services/entitlement.service';
import { EntitlementDto } from '../../models/entitlement.model';

interface EntitlementFormData {
  name: string;
  displayName: string;
  description: string;
  group: string;
  enabled: boolean;
}

@Component({
  selector: 'admin-entitlements-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent],
  templateUrl: './entitlements-page.component.html',
})
export class EntitlementsPageComponent {
  private readonly entitlementService = inject(EntitlementService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly deleting = signal(false);
  readonly entitlements = signal<EntitlementDto[]>([]);
  readonly error = signal<string | null>(null);

  readonly showCreateEditModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly selected = signal<EntitlementDto | null>(null);

  formData: EntitlementFormData = this.emptyForm();

  constructor() {
    this.load();
  }

  private emptyForm(): EntitlementFormData {
    return { name: '', displayName: '', description: '', group: '', enabled: true };
  }

  load(): void {
    this.loading.set(true);
    this.entitlementService.getAll().subscribe({
      next: (entitlements) => {
        this.entitlements.set(entitlements);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Failed to load entitlements:', err);
        this.loading.set(false);
      },
    });
  }

  /** Grouped for display; ungrouped entries fall under "General". */
  groups(): { name: string; entitlements: EntitlementDto[] }[] {
    const groups: { name: string; entitlements: EntitlementDto[] }[] = [];
    for (const entitlement of this.entitlements()) {
      const groupName = entitlement.group || 'General';
      const group = groups.find((g) => g.name === groupName);
      if (group) {
        group.entitlements.push(entitlement);
      } else {
        groups.push({ name: groupName, entitlements: [entitlement] });
      }
    }
    return groups;
  }

  openCreateModal(): void {
    this.selected.set(null);
    this.formData = this.emptyForm();
    this.error.set(null);
    this.showCreateEditModal.set(true);
  }

  openEditModal(entitlement: EntitlementDto): void {
    this.selected.set(entitlement);
    this.formData = {
      name: entitlement.name,
      displayName: entitlement.displayName,
      description: entitlement.description || '',
      group: entitlement.group || '',
      enabled: entitlement.enabled,
    };
    this.error.set(null);
    this.showCreateEditModal.set(true);
  }

  closeCreateEditModal(): void {
    this.showCreateEditModal.set(false);
    this.selected.set(null);
    this.error.set(null);
  }

  onSave(): void {
    const existing = this.selected();
    this.saving.set(true);
    this.error.set(null);

    const done = {
      next: () => {
        this.saving.set(false);
        this.closeCreateEditModal();
        this.load();
      },
      error: (err: { error?: { error?: string } }) => {
        this.error.set(err?.error?.error || 'Save failed');
        this.saving.set(false);
      },
    };

    if (existing) {
      // Name is immutable — it's already in issued tokens and on roles.
      this.entitlementService
        .update(existing.id, {
          displayName: this.formData.displayName || this.formData.name,
          description: this.formData.description,
          group: this.formData.group,
          enabled: this.formData.enabled,
        })
        .subscribe(done);
    } else {
      this.entitlementService
        .create({
          name: this.formData.name.trim(),
          displayName: this.formData.displayName || undefined,
          description: this.formData.description || undefined,
          group: this.formData.group || undefined,
          enabled: this.formData.enabled,
        })
        .subscribe(done);
    }
  }

  openDeleteModal(entitlement: EntitlementDto): void {
    this.selected.set(entitlement);
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.selected.set(null);
  }

  confirmDelete(): void {
    const entitlement = this.selected();
    if (!entitlement) return;

    this.deleting.set(true);
    this.entitlementService.delete(entitlement.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.closeDeleteModal();
        this.load();
      },
      error: (err) => {
        console.error('Failed to delete entitlement:', err);
        this.deleting.set(false);
      },
    });
  }
}
