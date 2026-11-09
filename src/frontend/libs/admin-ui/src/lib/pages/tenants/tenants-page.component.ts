import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent, ToastService, extractErrorMessage } from '@syndew/oluso-ui-core';
import { AuthStore } from '@syndew/oluso-auth';
import { OrganizationService } from '../../services/organization.service';
import { TenantService } from '../../services/tenant.service';
import { ApiService } from '../../services/api.service';
import { Organization, UpdateOrganizationRequest, OrganizationTenant, TenantEnvironment } from '../../models/organization.model';
import { OrgMembersComponent } from '../../components/org-members/org-members.component';
import { OrgInvitationsComponent } from '../../components/org-invitations/org-invitations.component';
import { PaginationParams } from '../../models/pagination-params.model';

interface Tab {
  id: string;
  label: string;
  icon: string;
}

const TABS: Tab[] = [
  { id: 'tenants', label: 'Tenants', icon: 'building' },
  { id: 'members', label: 'Members', icon: 'user-group' },
  { id: 'invitations', label: 'Invitations', icon: 'envelope' },
  { id: 'settings', label: 'Settings', icon: 'cog' },
];

const ENVIRONMENTS: TenantEnvironment[] = ['Production', 'Staging', 'UAT', 'QA', 'Development'];

interface TenantFormData {
  name: string;
  displayName: string;
  identifier: string;
  description: string;
  customDomain: string;
  environment: TenantEnvironment;
}

@Component({
  selector: 'admin-tenants-page',
  standalone: true,
  imports: [CommonModule, FormsModule, ButtonComponent, BadgeComponent, ModalComponent, DataTableComponent, IconComponent, OrgMembersComponent, OrgInvitationsComponent],
  templateUrl: './tenants-page.component.html',
})
export class TenantsPageComponent {
  private readonly authStore = inject(AuthStore);
  private readonly route = inject(ActivatedRoute);
  private readonly organizationService = inject(OrganizationService);
  private readonly tenantService = inject(TenantService);
  private readonly router = inject(Router);
  private readonly api = inject(ApiService);
  private readonly toast = inject(ToastService);

  readonly orgId = computed(() => this.authStore.currentOrganization()?.id);
  readonly orgName = computed(() => this.authStore.currentOrganization()?.name ?? 'Organization');

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly tenants = signal<OrganizationTenant[]>([]);

  // Tabs
  readonly activeTab = signal<string>('tenants');
  readonly tabs = TABS;
  readonly environments = ENVIRONMENTS;

  // Pagination
  readonly currentPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly pageSize = 20;
  searchTerm = '';

  readonly showCreateModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly selectedTenant = signal<OrganizationTenant | null>(null);

  // Settings tab state
  readonly organization = signal<Organization | null>(null);
  readonly settingsLoading = signal(false);
  readonly settingsSaving = signal(false);
  readonly settingsHasChanges = signal(false);
  orgData: { description?: string; websiteUrl?: string } = {};

  formData: TenantFormData = {
    name: '',
    displayName: '',
    identifier: '',
    description: '',
    customDomain: '',
    environment: 'Production',
  };

  constructor() {
    this.searchTerm = this.route.snapshot.queryParamMap.get('search') ?? '';
    this.loadTenants();
  }

  setActiveTab(tabId: string): void {
    this.activeTab.set(tabId);
    if (tabId === 'settings' && !this.organization()) {
      this.loadOrganization();
    }
  }

  loadOrganization(): void {
    const currentOrgId = this.orgId();
    if (!currentOrgId) return;

    this.settingsLoading.set(true);
    this.organizationService.getById(currentOrgId).subscribe({
      next: (org) => {
        this.organization.set(org);
        this.orgData = {
          description: org.description,
          websiteUrl: org.websiteUrl,
        };
        this.settingsLoading.set(false);
      },
      error: (error) => {
        console.error('Failed to load organization:', error);
        this.settingsLoading.set(false);
      },
    });
  }

  onSettingsChange(): void {
    this.settingsHasChanges.set(true);
  }

  saveSettings(): void {
    const currentOrgId = this.orgId();
    if (!currentOrgId) return;

    this.settingsSaving.set(true);
    this.organizationService.update(currentOrgId, this.orgData as UpdateOrganizationRequest).subscribe({
      next: (org) => {
        this.organization.set(org);
        this.settingsSaving.set(false);
        this.settingsHasChanges.set(false);
      },
      error: (error) => {
        console.error('Failed to save settings:', error);
        this.settingsSaving.set(false);
      },
    });
  }

  loadTenants(): void {
    const currentOrgId = this.orgId();
    if (!currentOrgId) {
      console.error('No organization context available');
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    const pagination: PaginationParams = {
      page: this.currentPage(),
      pageSize: this.pageSize,
    };
    const filters = this.searchTerm ? { search: this.searchTerm } : undefined;

    this.organizationService.getTenants(currentOrgId, pagination, filters).subscribe({
      next: (response) => {
        this.tenants.set(response.items);
        this.totalPages.set(response.totalPages);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load tenants:', error);
        this.loading.set(false);
      },
    });
  }

  onSearch(term: string): void {
    this.searchTerm = term;
    this.currentPage.set(1);
    this.loadTenants();
  }

  onPageChange(page: number): void {
    this.currentPage.set(page);
    this.loadTenants();
  }

  openCreateModal(): void {
    this.formData = {
      name: '',
      displayName: '',
      identifier: '',
      description: '',
      customDomain: '',
      environment: 'Production',
    };
    this.showCreateModal.set(true);
  }

  closeCreateModal(): void {
    this.showCreateModal.set(false);
  }

  openDeleteModal(tenant: OrganizationTenant): void {
    this.selectedTenant.set(tenant);
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.selectedTenant.set(null);
  }

  goToSettings(tenant: OrganizationTenant): void {
    this.router.navigate(['/tenants', tenant.id]);
  }

  onIdentifierInput(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.formData.identifier = input.value.toLowerCase().replace(/[^a-z0-9-]/g, '');
  }

  onCreate(): void {
    const currentOrgId = this.orgId();
    if (!currentOrgId) return;

    const customDomain = this.formData.customDomain?.trim();
    this.saving.set(true);
    this.organizationService.createTenant(currentOrgId, {
      name: this.formData.name,
      displayName: this.formData.displayName || undefined,
      identifier: this.formData.identifier,
      description: this.formData.description || undefined,
      environment: this.formData.environment,
    }).subscribe({
      next: (tenant) => {
        this.saving.set(false);
        this.closeCreateModal();
        this.loadTenants();
        if (customDomain) {
          this.startDomainVerification(tenant.id, customDomain);
        } else {
          this.toast.success(`Tenant ${tenant.name} created`);
        }
      },
      error: (error) => {
        this.saving.set(false);
        this.toast.error(extractErrorMessage(error, 'The tenant could not be created.'));
      },
    });
  }

  /** A custom domain is only used once its DNS is verified, so creating the tenant starts that verification. */
  private startDomainVerification(tenantId: string, domain: string): void {
    this.api.post(`/tenants/${tenantId}/domains`, { domain }).subscribe({
      next: () => this.toast.success(`Tenant created. To use ${domain}, add the DNS TXT record shown under the tenant's Domains settings.`),
      error: (error) => this.toast.error(
        `Tenant created, but ${domain} could not be added: ${extractErrorMessage(error, 'unknown error')}. Add it from the tenant's Domains settings.`),
    });
  }

  confirmDelete(): void {
    const tenant = this.selectedTenant();
    if (!tenant) return;

    this.tenantService.delete(tenant.id).subscribe({
      next: () => {
        this.closeDeleteModal();
        this.loadTenants();
      },
      error: (error) => {
        console.error('Failed to delete tenant:', error);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }
}
