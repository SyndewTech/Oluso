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
  ToastService,
  extractErrorMessage,
} from '@oluso/ui-core';
import { ApiScopeService } from '../../services/api-scope.service';
import { SettingsService } from '../../services/settings.service';
import { ApiScopeDto, CreateApiScopeRequest } from '../../models/api-scope.model';

/**
 * The built-in default longest suffix (ApiScopePrefixes.DefaultSuffixMaxLength), shown as the placeholder until the
 * tenant's own default (Settings → Protocol → Prefix Scopes) has loaded, or when it can't be read.
 */
export const DEFAULT_SUFFIX_MAX_LENGTH = 256;

/**
 * Why a suffix pattern can't be used, or null. The server compiles it as a .NET regular expression and is the judge;
 * this is the browser's early warning, so a pattern only .NET understands may be flagged here and still save.
 */
export function suffixPatternError(pattern: string | null | undefined): string | null {
  if (!pattern || !pattern.trim()) return null;
  try {
    new RegExp(`^(?:${pattern})$`);
    return null;
  } catch {
    return 'This is not a valid regular expression.';
  }
}

/** Why a maximum suffix length can't be used, or null. Empty means the server default. */
export function suffixMaxLengthError(value: number | string | null | undefined): string | null {
  if (value === null || value === undefined || value === '') return null;
  const n = Number(value);
  return Number.isInteger(n) && n > 0 ? null : 'Enter a whole number above 0, or leave it empty.';
}

/** The prefix settings as the API takes them: cleared fields are null (the server then removes the property). */
function prefixSettings(form: { isPrefix?: boolean; suffixPattern?: string | null; suffixMaxLength?: number | string | null }) {
  const pattern = form.suffixPattern?.trim() ? form.suffixPattern : null;
  const max = form.suffixMaxLength === '' || form.suffixMaxLength === undefined ? null : form.suffixMaxLength;
  return {
    isPrefix: !!form.isPrefix,
    suffixPattern: form.isPrefix ? pattern : null,
    suffixMaxLength: form.isPrefix && max !== null ? Number(max) : null,
  };
}

@Component({
  selector: 'admin-api-scope-detail-page',
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
  templateUrl: './api-scope-detail-page.component.html',
})
export class ApiScopeDetailPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly apiScopeService = inject(ApiScopeService);
  private readonly toast = inject(ToastService);
  private readonly settingsService = inject(SettingsService);

  /** The tenant's default for scopes that don't set a maximum (its protocol settings). */
  readonly defaultSuffixMaxLength = signal(DEFAULT_SUFFIX_MAX_LENGTH);
  readonly suffixPatternError = suffixPatternError;
  readonly suffixMaxLengthError = suffixMaxLengthError;

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly scope = signal<ApiScopeDto | null>(null);

  readonly showDeleteModal = signal(false);
  readonly showEditModal = signal(false);
  readonly showClaimsModal = signal(false);

  readonly isNew = computed(() => this.route.snapshot.paramMap.get('id') === 'new');

  // Form data for new scope
  formData: CreateApiScopeRequest = {
    name: '',
    displayName: '',
    description: '',
    enabled: true,
    required: false,
    emphasize: false,
    showInDiscoveryDocument: true,
    isPrefix: false,
    suffixPattern: '',
    suffixMaxLength: null,
  };

  // Form data for editing
  editFormData: Partial<ApiScopeDto> = {};

  // Claims management
  newClaim = '';

  // Common claims suggestions
  readonly commonClaims = ['sub', 'name', 'email', 'role', 'groups', 'tenant_id'];

  ngOnInit(): void {
    this.loadDefaultSuffixMaxLength();
    const id = this.route.snapshot.paramMap.get('id');

    if (id && id !== 'new') {
      this.loadScope(parseInt(id, 10));
    } else {
      this.loading.set(false);
    }
  }

  /** Reads the tenant's default; without access to settings the built-in default stays. */
  private loadDefaultSuffixMaxLength(): void {
    this.settingsService.getSettings().subscribe({
      next: (settings) => {
        const value = settings?.protocolSettings?.prefixScopeSuffixMaxLength;
        if (typeof value === 'number' && Number.isInteger(value) && value > 0) {
          this.defaultSuffixMaxLength.set(value);
        }
      },
      error: () => undefined,
    });
  }

  loadScope(id: number): void {
    this.loading.set(true);
    this.apiScopeService.getById(id).subscribe({
      next: (scope) => {
        this.scope.set(scope);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load API scope:', error);
        this.loading.set(false);
      },
    });
  }

  onBack(): void {
    this.router.navigate(['/api-scopes']);
  }

  onCreate(): void {
    if (!this.formData.name) return;

    if (this.formData.isPrefix && this.suffixMaxLengthError(this.formData.suffixMaxLength)) return;

    this.saving.set(true);
    this.apiScopeService.create({ ...this.formData, ...prefixSettings(this.formData) }).subscribe({
      next: (created) => {
        this.saving.set(false);
        this.router.navigate(['/api-scopes', created.id]);
      },
      error: (error) => {
        console.error('Failed to create API scope:', error);
        this.toast.error(extractErrorMessage(error, 'Failed to create API scope'));
        this.saving.set(false);
      },
    });
  }

  openEditModal(): void {
    const scope = this.scope();
    if (!scope) return;

    this.editFormData = { ...scope, suffixPattern: scope.suffixPattern ?? '', suffixMaxLength: scope.suffixMaxLength ?? null };
    this.showEditModal.set(true);
  }

  closeEditModal(): void {
    this.showEditModal.set(false);
  }

  onUpdate(): void {
    const scope = this.scope();
    if (!scope) return;

    if (this.editFormData.isPrefix && this.suffixMaxLengthError(this.editFormData.suffixMaxLength)) return;

    this.saving.set(true);
    const fields = { ...scope, ...this.editFormData };
    this.apiScopeService.update(scope.id, { ...fields, ...prefixSettings(fields) } as ApiScopeDto).subscribe({
      next: () => {
        this.saving.set(false);
        this.closeEditModal();
        this.loadScope(scope.id);
      },
      error: (error) => {
        console.error('Failed to update API scope:', error);
        this.toast.error(extractErrorMessage(error, 'Failed to update API scope'));
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
    const scope = this.scope();
    if (!scope) return;

    this.apiScopeService.delete(scope.id).subscribe({
      next: () => {
        this.router.navigate(['/api-scopes']);
      },
      error: (error) => {
        console.error('Failed to delete API scope:', error);
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
    const scope = this.scope();
    if (!scope || !this.newClaim) return;

    const currentClaims = scope.userClaims || [];
    if (currentClaims.includes(this.newClaim)) {
      this.newClaim = '';
      return;
    }

    this.saving.set(true);
    this.apiScopeService.update(scope.id, { ...scope, userClaims: [...currentClaims, this.newClaim] }).subscribe({
      next: () => {
        this.saving.set(false);
        this.newClaim = '';
        this.loadScope(scope.id);
      },
      error: (error) => {
        console.error('Failed to add claim:', error);
        this.saving.set(false);
      },
    });
  }

  removeClaim(claim: string): void {
    const scope = this.scope();
    if (!scope) return;

    const currentClaims = scope.userClaims || [];
    this.saving.set(true);
    this.apiScopeService.update(scope.id, { ...scope, userClaims: currentClaims.filter((c) => c !== claim) }).subscribe({
      next: () => {
        this.saving.set(false);
        this.loadScope(scope.id);
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
