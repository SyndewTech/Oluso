import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CardComponent, ButtonComponent, BadgeComponent, IconComponent } from '@oluso/ui-core';
import { IdentityResourceService } from '../../services/identity-resource.service';
import { IdentityResourceDto } from '../../models/identity-resource.model';

@Component({
  selector: 'admin-identity-resources-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent],
  templateUrl: './identity-resources-page.component.html',
})
export class IdentityResourcesPageComponent {
  private readonly identityResourceService = inject(IdentityResourceService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly resources = signal<IdentityResourceDto[]>([]);

  constructor() {
    this.loadResources();
  }

  loadResources(): void {
    this.loading.set(true);
    this.identityResourceService.getAll().subscribe({
      next: (resources) => {
        this.resources.set(resources);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load identity resources:', error);
        this.loading.set(false);
      },
    });
  }

  onRowClick(resource: IdentityResourceDto): void {
    if (resource.builtIn) return;
    this.router.navigate(['/identity-resources', resource.id]);
  }

  onAddResource(): void {
    this.router.navigate(['/identity-resources', 'new']);
  }
}
