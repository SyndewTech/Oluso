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
import { ResourceService } from '../../services/resource.service';
import { ResourceDto, CreateResourceRequest } from '../../models/resource.model';
import { ApiScopeSummaryDto } from '../../models/api-scope.model';

@Component({
  selector: 'admin-resource-detail-page',
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
  templateUrl: './resource-detail-page.component.html',
})
export class ResourceDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly resourceService = inject(ResourceService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly resource = signal<ResourceDto | null>(null);
  readonly availableScopes = signal<ApiScopeSummaryDto[]>([]);

  readonly showDeleteModal = signal(false);
  readonly showEditModal = signal(false);
  readonly showScopeModal = signal(false);
  readonly showClaimsModal = signal(false);

  readonly isNew = computed(() => this.route.snapshot.paramMap.get('id') === 'new');

  // Form data for new resource
  formData: CreateResourceRequest = {
    uri: '',
    displayName: '',
    description: '',
    enabled: true,
    showInDiscoveryDocument: true,
  };

  // Form data for editing
  editFormData: Partial<ResourceDto> = {};

  // URI validation error
  uriError: string | null = null;

  // Claims management
  newClaim = '';

  // Common claims suggestions
  readonly commonClaims = ['sub', 'name', 'email', 'role', 'groups', 'tenant_id'];

  // Computed: scopes not already assigned to this resource
  get unassignedScopes(): ApiScopeSummaryDto[] {
    const resource = this.resource();
    const scopes = this.availableScopes();
    if (!resource) return scopes;
    return scopes.filter((scope) => !resource.allowedScopes?.includes(scope.name));
  }

  ngOnInit(): void {
    const id = this.route.snapshot.paramMap.get('id');

    if (id && id !== 'new') {
      const numericId = parseInt(id, 10);
      this.loadResource(numericId);
      this.loadAvailableScopes();
    } else {
      this.loading.set(false);
    }
  }

  loadResource(id: number): void {
    this.loading.set(true);
    this.resourceService.getById(id).subscribe({
      next: (resource) => {
        this.resource.set(resource);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load resource:', error);
        this.loading.set(false);
      },
    });
  }

  loadAvailableScopes(): void {
    this.resourceService.getAvailableScopes().subscribe({
      next: (scopes) => this.availableScopes.set(scopes),
      error: (error) => console.error('Failed to load available scopes:', error),
    });
  }

  onBack(): void {
    this.router.navigate(['/resources']);
  }

  validateUri(uri: string): boolean {
    this.uriError = null;
    if (!uri) {
      this.uriError = 'URI is required';
      return false;
    }
    try {
      const parsed = new URL(uri);
      if (!['http:', 'https:'].includes(parsed.protocol)) {
        this.uriError = 'URI must use http or https protocol';
        return false;
      }
      if (parsed.hash) {
        this.uriError = 'URI must not contain a fragment component (RFC 8707)';
        return false;
      }
      return true;
    } catch {
      this.uriError = 'URI must be a valid absolute URI';
      return false;
    }
  }

  onUriChange(): void {
    this.uriError = null;
  }

  onUriBlur(): void {
    if (this.formData.uri) {
      this.validateUri(this.formData.uri);
    }
  }

  onCreate(): void {
    if (!this.validateUri(this.formData.uri)) return;

    this.saving.set(true);
    this.resourceService.create(this.formData).subscribe({
      next: (created) => {
        this.saving.set(false);
        this.router.navigate(['/resources', created.id]);
      },
      error: (error) => {
        console.error('Failed to create resource:', error);
        this.uriError = error.error?.error || 'Failed to create resource';
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
    this.resourceService.update(resource.id, { ...resource, ...this.editFormData } as ResourceDto).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeEditModal();
        this.loadResource(resource.id);
      },
      error: (error) => {
        console.error('Failed to update resource:', error);
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

    this.resourceService.delete(resource.id).subscribe({
      next: () => {
        this.router.navigate(['/resources']);
      },
      error: (error) => {
        console.error('Failed to delete resource:', error);
      },
    });
  }

  openScopeModal(): void {
    this.showScopeModal.set(true);
  }

  closeScopeModal(): void {
    this.showScopeModal.set(false);
  }

  addScope(scopeName: string): void {
    const resource = this.resource();
    if (!resource) return;

    this.saving.set(true);
    this.resourceService.addScope(resource.id, scopeName).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeScopeModal();
        this.loadResource(resource.id);
        this.loadAvailableScopes();
      },
      error: (error) => {
        console.error('Failed to add scope:', error);
        this.saving.set(false);
      },
    });
  }

  removeScope(scopeName: string): void {
    const resource = this.resource();
    if (!resource) return;

    this.saving.set(true);
    this.resourceService.removeScope(resource.id, scopeName).subscribe({
      next: () => {
        this.saving.set(false);
        this.loadResource(resource.id);
      },
      error: (error) => {
        console.error('Failed to remove scope:', error);
        this.saving.set(false);
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
    this.resourceService.update(resource.id, { ...resource, userClaims: [...currentClaims, this.newClaim] }).subscribe({
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
    this.resourceService.update(resource.id, { ...resource, userClaims: currentClaims.filter((c) => c !== claim) }).subscribe({
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
