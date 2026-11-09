import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  CardComponent,
  ButtonComponent,
  BadgeComponent,
  ModalComponent,
  IconComponent,
  DataTableComponent,
} from '@syndew/oluso-ui-core';
import { OrganizationService } from '../../services/organization.service';
import {
  Organization,
  UpdateOrganizationRequest,
  OrganizationTenant,
  OrganizationFeature,
  TenantEnvironment,
} from '../../models/organization.model';
import { PaginationParams } from '../../models/pagination-params.model';
import { OrgMembersComponent } from '../../components/org-members/org-members.component';
import { OrgInvitationsComponent } from '../../components/org-invitations/org-invitations.component';

interface Tab {
  id: string;
  label: string;
  icon: string;
}

const TABS: Tab[] = [
  { id: 'general', label: 'General', icon: 'cog' },
  { id: 'features', label: 'Features', icon: 'puzzle-piece' },
  { id: 'tenants', label: 'Tenants', icon: 'building' },
  { id: 'members', label: 'Members', icon: 'user-group' },
  { id: 'invitations', label: 'Invitations', icon: 'envelope' },
];

const ENVIRONMENTS: TenantEnvironment[] = ['Production', 'Staging', 'UAT', 'QA', 'Development'];

@Component({
  selector: 'admin-organization-detail-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, IconComponent, DataTableComponent, OrgMembersComponent, OrgInvitationsComponent],
  templateUrl: './organization-detail-page.component.html',
})
export class OrganizationDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly organizationService = inject(OrganizationService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly hasChanges = signal(false);

  readonly organization = signal<Organization | null>(null);

  // Tenants tab state
  readonly tenants = signal<OrganizationTenant[]>([]);
  readonly tenantsLoading = signal(false);
  readonly tenantsPage = signal(1);
  readonly tenantsTotalPages = signal(1);
  readonly tenantsTotalCount = signal(0);
  readonly tenantsPageSize = 20;
  tenantsSearchTerm = '';

  // Features tab state
  readonly features = signal<OrganizationFeature[]>([]);
  readonly featuresLoading = signal(false);
  readonly featuresSaving = signal(false);
  readonly featuresHasChanges = signal(false);
  readonly hasFeaturesConfigured = signal(false);
  featureToggles: Record<string, boolean> = {};
  featureMaxSeats: Record<string, number | null> = {};

  readonly activeTab = signal<string>('general');
  readonly tabs = TABS;

  // Modals
  readonly showCreateTenantModal = signal(false);

  // Form data
  orgData: UpdateOrganizationRequest = {};
  tenantFormData = { name: '', identifier: '', displayName: '', description: '', environment: 'Production' as TenantEnvironment };
  readonly environments = ENVIRONMENTS;

  readonly orgId = computed(() => this.route.snapshot.paramMap.get('id') || '');

  ngOnInit(): void {
    const id = this.orgId();
    if (id) {
      this.loadOrganization(id);
    }
  }

  loadOrganization(id: string): void {
    this.loading.set(true);
    this.organizationService.getById(id).subscribe({
      next: (org) => {
        this.organization.set(org);
        this.orgData = {
          name: org.name,
          slug: org.slug,
          description: org.description,
          logoUrl: org.logoUrl,
          websiteUrl: org.websiteUrl,
          enabled: org.enabled,
          maxMembers: org.maxMembers,
          maxTenants: org.maxTenants,
        };
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load organization:', error);
        this.loading.set(false);
      },
    });
  }

  loadTenants(): void {
    const id = this.orgId();
    if (!id) return;
    this.tenantsLoading.set(true);
    const pagination: PaginationParams = {
      page: this.tenantsPage(),
      pageSize: this.tenantsPageSize,
    };
    const filters: Record<string, string> = {};
    if (this.tenantsSearchTerm) {
      filters['search'] = this.tenantsSearchTerm;
    }
    this.organizationService.getTenants(id, pagination, Object.keys(filters).length ? filters : undefined).subscribe({
      next: (response) => {
        this.tenants.set(response.items);
        this.tenantsTotalPages.set(response.totalPages);
        this.tenantsTotalCount.set(response.totalCount);
        this.tenantsLoading.set(false);
      },
      error: (error) => {
        console.error('Failed to load tenants:', error);
        this.tenantsLoading.set(false);
      },
    });
  }

  onTenantsSearch(term: string): void {
    this.tenantsSearchTerm = term;
    this.tenantsPage.set(1);
    this.loadTenants();
  }

  onTenantsPageChange(page: number): void {
    this.tenantsPage.set(page);
    this.loadTenants();
  }

  setActiveTab(tabId: string): void {
    this.activeTab.set(tabId);
    if (tabId === 'features') {
      this.loadFeatures();
    }
    if (tabId === 'tenants') {
      this.loadTenants();
    }
    // Members tab loads itself via OrgMembersComponent.ngOnInit
  }

  loadFeatures(): void {
    const id = this.orgId();
    if (!id) return;
    this.featuresLoading.set(true);
    this.organizationService.getFeatures(id).subscribe({
      next: (response) => {
        this.features.set(response.features);
        this.hasFeaturesConfigured.set(response.hasFeaturesConfigured);
        this.featureToggles = {};
        this.featureMaxSeats = {};
        for (const f of response.features) {
          this.featureToggles[f.key] = f.enabled;
          this.featureMaxSeats[f.key] = f.maxSeats ?? null;
        }
        this.featuresLoading.set(false);
        this.featuresHasChanges.set(false);
      },
      error: (error) => {
        console.error('Failed to load features:', error);
        this.featuresLoading.set(false);
      },
    });
  }

  onFeatureToggle(featureKey: string, enabled: boolean): void {
    this.featureToggles[featureKey] = enabled;
    this.featuresHasChanges.set(true);
  }

  onFeatureMaxSeatsChange(featureKey: string, value: string): void {
    const num = value ? parseInt(value, 10) : null;
    this.featureMaxSeats[featureKey] = num && num > 0 ? num : null;
    this.featuresHasChanges.set(true);
  }

  saveFeatures(): void {
    const id = this.orgId();
    if (!id) return;

    this.featuresSaving.set(true);
    const featureUpdates = Object.entries(this.featureToggles).map(([key, enabled]) => ({
      featureKey: key,
      enabled,
      maxSeats: this.featureMaxSeats[key] ?? null,
    }));
    this.organizationService.setFeatures(id, { features: featureUpdates }).subscribe({
      next: () => {
        this.featuresSaving.set(false);
        this.featuresHasChanges.set(false);
        this.loadFeatures();
      },
      error: (error) => {
        console.error('Failed to save features:', error);
        this.featuresSaving.set(false);
      },
    });
  }

  goBack(): void {
    this.router.navigate(['/organizations']);
  }

  goToTenantSettings(tenant: OrganizationTenant): void {
    this.router.navigate(['/tenants', tenant.id]);
  }

  onOrgChange(): void {
    this.hasChanges.set(true);
  }

  onSave(): void {
    const id = this.orgId();
    if (!id) return;

    this.saving.set(true);
    this.organizationService.update(id, this.orgData).subscribe({
      next: () => {
        this.saving.set(false);
        this.hasChanges.set(false);
        this.loadOrganization(id);
      },
      error: (error) => {
        console.error('Failed to save organization:', error);
        this.saving.set(false);
      },
    });
  }

  // Tenant modal
  openCreateTenantModal(): void {
    this.tenantFormData = { name: '', identifier: '', displayName: '', description: '', environment: 'Production' };
    this.showCreateTenantModal.set(true);
  }

  closeCreateTenantModal(): void {
    this.showCreateTenantModal.set(false);
  }

  onTenantIdentifierInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.tenantFormData.identifier = input.value.toLowerCase().replace(/[^a-z0-9-]/g, '');
  }

  createTenant(): void {
    const id = this.orgId();
    if (!id) return;

    this.saving.set(true);
    this.organizationService.createTenant(id, {
      name: this.tenantFormData.name,
      identifier: this.tenantFormData.identifier,
      displayName: this.tenantFormData.displayName || undefined,
      description: this.tenantFormData.description || undefined,
      environment: this.tenantFormData.environment,
    }).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeCreateTenantModal();
        this.loadTenants();
      },
      error: (error) => {
        console.error('Failed to create tenant:', error);
        this.saving.set(false);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
