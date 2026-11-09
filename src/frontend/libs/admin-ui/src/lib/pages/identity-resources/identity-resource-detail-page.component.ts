import { Component, inject, signal, OnInit, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  PageSlotHostComponent,
  DetailSectionHostComponent,
  IconComponent,
} from '@syndew/oluso-ui-core';
import { IdentityResourceService } from '../../services/identity-resource.service';
import {
  IdentityResourceDto,
  CreateIdentityResourceRequest,
} from '../../models/identity-resource.model';

@Component({
  selector: 'admin-identity-resource-detail-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    CardComponent,
    ButtonComponent,
    BadgeComponent,
    ModalComponent,
    PageSlotHostComponent,
    DetailSectionHostComponent,
    IconComponent,
  ],
  templateUrl: './identity-resource-detail-page.component.html',
})
export class IdentityResourceDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly identityResourceService = inject(IdentityResourceService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly resource = signal<IdentityResourceDto | null>(null);

  readonly showDeleteModal = signal(false);
  readonly showEditModal = signal(false);
  readonly showClaimsModal = signal(false);

  readonly isNew = computed(() => this.route.snapshot.paramMap.get('id') === 'new');

  // Form data for new resource
  formData: CreateIdentityResourceRequest = {
    name: '',
    displayName: '',
    description: '',
    enabled: true,
    required: false,
    emphasize: false,
    showInDiscoveryDocument: true,
  };

  // Form data for editing
  editFormData: Partial<IdentityResourceDto> = {};

  // Claims management
  newClaim = '';

  // Common identity claims suggestions
  readonly commonClaims = [
    'name',
    'given_name',
    'family_name',
    'preferred_username',
    'picture',
    'email',
    'email_verified',
    'phone_number',
    'phone_number_verified',
  ];

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id && id !== 'new') {
      this.loadResource(parseInt(id, 10));
    } else {
      this.loading.set(false);
    }
  }

  loadResource(id: number): void {
    this.loading.set(true);
    this.identityResourceService.getById(id).subscribe({
      next: (resource) => {
        this.resource.set(resource);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load identity resource:', error);
        this.loading.set(false);
      },
    });
  }

  onBack(): void {
    this.router.navigate(['/identity-resources']);
  }

  onCreate(): void {
    if (!this.formData.name) return;

    this.saving.set(true);
    this.identityResourceService.create(this.formData).subscribe({
      next: (created) => {
        this.saving.set(false);
        this.router.navigate(['/identity-resources', created.id]);
      },
      error: (error) => {
        console.error('Failed to create identity resource:', error);
        this.saving.set(false);
      },
    });
  }

  openEditModal(): void {
    const resource = this.resource();
    if (!resource) return;

    this.editFormData = { ...resource };
    this.showEditModal.set(true);
  }

  closeEditModal(): void {
    this.showEditModal.set(false);
  }

  onUpdate(): void {
    const resource = this.resource();
    if (!resource) return;

    this.saving.set(true);
    this.identityResourceService
      .update(resource.id, { ...resource, ...this.editFormData } as IdentityResourceDto)
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.closeEditModal();
          this.loadResource(resource.id);
        },
        error: (error) => {
          console.error('Failed to update identity resource:', error);
          this.saving.set(false);
        },
      });
  }

  openDeleteModal(): void {
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
  }

  confirmDelete(): void {
    const resource = this.resource();
    if (!resource) return;

    this.identityResourceService.delete(resource.id).subscribe({
      next: () => {
        this.router.navigate(['/identity-resources']);
      },
      error: (error) => {
        console.error('Failed to delete identity resource:', error);
      },
    });
  }

  openClaimsModal(): void {
    this.newClaim = '';
    this.showClaimsModal.set(true);
  }

  closeClaimsModal(): void {
    this.showClaimsModal.set(false);
    this.newClaim = '';
  }

  addClaim(): void {
    const resource = this.resource();
    if (!resource || !this.newClaim) return;

    const currentClaims = resource.userClaims || [];
    if (currentClaims.includes(this.newClaim)) {
      this.newClaim = '';
      return;
    }

    this.saving.set(true);
    this.identityResourceService
      .update(resource.id, { ...resource, userClaims: [...currentClaims, this.newClaim] })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.newClaim = '';
          this.loadResource(resource.id);
        },
        error: (error) => {
          console.error('Failed to add claim:', error);
          this.saving.set(false);
        },
      });
  }

  removeClaim(claim: string): void {
    const resource = this.resource();
    if (!resource) return;

    const currentClaims = resource.userClaims || [];
    this.saving.set(true);
    this.identityResourceService
      .update(resource.id, { ...resource, userClaims: currentClaims.filter((c) => c !== claim) })
      .subscribe({
        next: () => {
          this.saving.set(false);
          this.loadResource(resource.id);
        },
        error: (error) => {
          console.error('Failed to remove claim:', error);
          this.saving.set(false);
        },
      });
  }

  selectCommonClaim(claim: string): void {
    this.newClaim = claim;
  }

  onClaimKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.addClaim();
    }
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleString();
  }
}
