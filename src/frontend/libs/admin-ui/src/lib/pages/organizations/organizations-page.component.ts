import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent } from '@oluso/ui-core';
import { OrganizationService } from '../../services/organization.service';
import { Organization, CreateOrganizationRequest, ProvisionOrganizationRequest, ProvisionOrganizationResponse, TenantEnvironment } from '../../models/organization.model';
import { PaginationParams } from '../../models/pagination-params.model';

interface OrgFormData {
  name: string;
  slug: string;
  description: string;
  logoUrl: string;
  websiteUrl: string;
  maxMembers: number;
  maxTenants: number;
}

interface ProvisionFormData {
  name: string;
  slug: string;
  description: string;
  websiteUrl: string;
  maxMembers: number;
  maxTenants: number;
  tenantName: string;
  tenantIdentifier: string;
  tenantDescription: string;
  tenantEnvironment: TenantEnvironment;
  adminEmail: string;
  adminFirstName: string;
  adminLastName: string;
}

@Component({
  selector: 'admin-organizations-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent],
  templateUrl: './organizations-page.component.html',
})
export class OrganizationsPageComponent {
  private readonly organizationService = inject(OrganizationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly organizations = signal<Organization[]>([]);

  // Pagination
  readonly currentPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly pageSize = 20;
  searchTerm = '';

  readonly showCreateModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly showProvisionModal = signal(false);
  readonly provisionResult = signal<ProvisionOrganizationResponse | null>(null);
  readonly selectedOrg = signal<Organization | null>(null);

  formData: OrgFormData = {
    name: '',
    slug: '',
    description: '',
    logoUrl: '',
    websiteUrl: '',
    maxMembers: 0,
    maxTenants: 0,
  };

  provisionData: ProvisionFormData = {
    name: '',
    slug: '',
    description: '',
    websiteUrl: '',
    maxMembers: 0,
    maxTenants: 5,
    tenantName: '',
    tenantIdentifier: '',
    tenantDescription: '',
    tenantEnvironment: 'Production',
    adminEmail: '',
    adminFirstName: '',
    adminLastName: '',
  };

  constructor() {
    this.searchTerm = this.route.snapshot.queryParamMap.get('search') ?? '';
    this.loadOrganizations();
  }

  loadOrganizations(): void {
    this.loading.set(true);
    const pagination: PaginationParams = {
      page: this.currentPage(),
      pageSize: this.pageSize,
    };
    const filters: Record<string, string | boolean> = { includeDisabled: true };
    if (this.searchTerm) {
      filters['search'] = this.searchTerm;
    }

    this.organizationService.getAll(pagination, filters).subscribe({
      next: (response) => {
        this.organizations.set(response.items);
        this.totalPages.set(response.totalPages);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load organizations:', error);
        this.loading.set(false);
      },
    });
  }

  onSearch(term: string): void {
    this.searchTerm = term;
    this.currentPage.set(1);
    this.loadOrganizations();
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
    this.loadOrganizations();
  }

  openCreateModal(): void {
    this.formData = {
      name: '',
      slug: '',
      description: '',
      logoUrl: '',
      websiteUrl: '',
      maxMembers: 0,
      maxTenants: 0,
    };
    this.showCreateModal.set(true);
  }

  closeCreateModal(): void {
    this.showCreateModal.set(false);
  }

  openDeleteModal(org: Organization): void {
    this.selectedOrg.set(org);
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.selectedOrg.set(null);
  }

  goToDetail(org: Organization): void {
    this.router.navigate(['/organizations', org.id]);
  }

  onSlugInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.formData.slug = input.value.toLowerCase().replace(/[^a-z0-9-]/g, '');
  }

  onCreate(): void {
    this.saving.set(true);
    const request: CreateOrganizationRequest = {
      name: this.formData.name,
      slug: this.formData.slug,
      description: this.formData.description || undefined,
      logoUrl: this.formData.logoUrl || undefined,
      websiteUrl: this.formData.websiteUrl || undefined,
      maxMembers: this.formData.maxMembers || undefined,
      maxTenants: this.formData.maxTenants || undefined,
    };

    this.organizationService.create(request).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeCreateModal();
        this.loadOrganizations();
      },
      error: (error) => {
        console.error('Failed to create organization:', error);
        this.saving.set(false);
      },
    });
  }

  confirmDelete(): void {
    const org = this.selectedOrg();
    if (!org) return;

    this.organizationService.delete(org.id).subscribe({
      next: () => {
        this.closeDeleteModal();
        this.loadOrganizations();
      },
      error: (error) => {
        console.error('Failed to delete organization:', error);
      },
    });
  }

  // Provision modal
  openProvisionModal(): void {
    this.provisionData = {
      name: '',
      slug: '',
      description: '',
      websiteUrl: '',
      maxMembers: 0,
      maxTenants: 5,
      tenantName: '',
      tenantIdentifier: '',
      tenantDescription: '',
      tenantEnvironment: 'Production',
      adminEmail: '',
      adminFirstName: '',
      adminLastName: '',
    };
    this.provisionResult.set(null);
    this.showProvisionModal.set(true);
  }

  closeProvisionModal(): void {
    this.showProvisionModal.set(false);
    this.provisionResult.set(null);
  }

  onProvisionSlugInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.provisionData.slug = input.value.toLowerCase().replace(/[^a-z0-9-]/g, '');
  }

  onProvisionTenantIdentifierInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.provisionData.tenantIdentifier = input.value.toLowerCase().replace(/[^a-z0-9-]/g, '');
  }

  autoFillTenant(): void {
    if (!this.provisionData.tenantName) {
      this.provisionData.tenantName = this.provisionData.name;
    }
    if (!this.provisionData.tenantIdentifier) {
      this.provisionData.tenantIdentifier = this.provisionData.slug;
    }
  }

  onProvision(): void {
    this.saving.set(true);
    const request: ProvisionOrganizationRequest = {
      name: this.provisionData.name,
      slug: this.provisionData.slug,
      description: this.provisionData.description || undefined,
      websiteUrl: this.provisionData.websiteUrl || undefined,
      maxMembers: this.provisionData.maxMembers || undefined,
      maxTenants: this.provisionData.maxTenants || undefined,
      tenantName: this.provisionData.tenantName || undefined,
      tenantIdentifier: this.provisionData.tenantIdentifier,
      tenantDescription: this.provisionData.tenantDescription || undefined,
      tenantEnvironment: this.provisionData.tenantEnvironment,
      adminEmail: this.provisionData.adminEmail,
      adminFirstName: this.provisionData.adminFirstName || undefined,
      adminLastName: this.provisionData.adminLastName || undefined,
      // No baseUrl: the set-password link is served by the identity host, not by
      // this SPA, so the backend derives it from the new tenant's issuer.
    };

    this.organizationService.provision(request).subscribe({
      next: (result) => {
        this.saving.set(false);
        this.provisionResult.set(result);
        this.loadOrganizations();
      },
      error: (error) => {
        console.error('Failed to provision organization:', error);
        this.saving.set(false);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
