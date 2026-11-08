import { Component, computed, inject, signal, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { CardComponent, ButtonComponent, BadgeComponent, IconComponent, InputComponent } from '@oluso/ui-core';
import { IdentityProviderService } from '../../services/identity-provider.service';
import { ExternalProviderType, IdentityProvider, ProviderTypeInfo, ProviderTypes } from '../../models/identity-provider.model';

@Component({
  selector: 'admin-identity-providers-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent, InputComponent],
  templateUrl: './identity-providers-page.component.html',
})
export class IdentityProvidersPageComponent implements OnInit {
  private readonly identityProviderService = inject(IdentityProviderService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly providers = signal<IdentityProvider[]>([]);
  readonly searchTerm = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  readonly visibleProviders = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.providers();
    return this.providers().filter(x => (x.scheme ?? '').toLowerCase().includes(term) || (x.displayName ?? '').toLowerCase().includes(term) || (x.providerType ?? '').toLowerCase().includes(term));
  });
  readonly providerTypes = signal<ProviderTypeInfo[]>([]);

  ngOnInit(): void {
    this.loadProviders();
    this.loadProviderTypes();
  }

  loadProviders(): void {
    this.loading.set(true);
    this.identityProviderService.getAll().subscribe({
      next: (providers) => {
        this.providers.set(providers);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load identity providers:', error);
        this.loading.set(false);
      },
    });
  }

  loadProviderTypes(): void {
    this.identityProviderService.getProviderTypes().subscribe({
      next: (types) => this.providerTypes.set(types),
      error: (error) => console.error('Failed to load provider types:', error),
    });
  }

  onAddProvider(): void {
    this.router.navigate(['/identity-providers/new']);
  }

  onRowClick(provider: IdentityProvider): void {
    this.router.navigate(['/identity-providers', provider.id]);
  }

  onToggle(provider: IdentityProvider, event: Event): void {
    event.stopPropagation();
    this.identityProviderService.toggle(provider.id).subscribe({
      next: () => this.loadProviders(),
      error: (error) => console.error('Failed to toggle provider:', error),
    });
  }

  getProviderTypeName(type: ExternalProviderType): string {
    const typeInfo = this.providerTypes().find((t) => t.type === type);
    return typeInfo?.name ?? `Type ${type}`;
  }

  getProviderIcon(type: ExternalProviderType): string {
    switch (type) {
      case ProviderTypes.Google:
        return 'google';
      case ProviderTypes.Microsoft:
        return 'microsoft';
      case ProviderTypes.GitHub:
        return 'github';
      case ProviderTypes.Facebook:
        return 'facebook';
      case ProviderTypes.Apple:
        return 'apple';
      case ProviderTypes.LinkedIn:
        return 'linkedin';
      case ProviderTypes.Twitter:
        return 'twitter';
      case ProviderTypes.Oidc:
        return 'openid';
      case ProviderTypes.OAuth2:
        return 'oauth';
      case ProviderTypes.Saml2:
        return 'saml';
      case ProviderTypes.Ldap:
        return 'ldap';
      default:
        return 'default';
    }
  }

  formatClients(provider: IdentityProvider): string {
    if (provider.allowedClientIds.length > 0) {
      return `${provider.allowedClientIds.length} client(s)`;
    }
    return 'All clients';
  }
}
