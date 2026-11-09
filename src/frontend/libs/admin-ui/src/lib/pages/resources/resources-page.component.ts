import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CardComponent, ButtonComponent, BadgeComponent, IconComponent } from '@syndew/oluso-ui-core';
import { ResourceService } from '../../services/resource.service';
import { ResourceDto } from '../../models/resource.model';
import { PaginationParams } from '../../models/pagination-params.model';

@Component({
  selector: 'admin-resources-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent],
  templateUrl: './resources-page.component.html',
})
export class ResourcesPageComponent {
  private readonly resourceService = inject(ResourceService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly resources = signal<ResourceDto[]>([]);
  readonly currentPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly pageSize = 10;

  constructor() {
    this.loadResources();
  }

  loadResources(): void {
    this.loading.set(true);
    const pagination: PaginationParams = {
      page: this.currentPage(),
      pageSize: this.pageSize,
    };

    this.resourceService.getAll(pagination).subscribe({
      next: (response) => {
        this.resources.set(response.items);
        this.totalPages.set(response.totalPages);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load resources:', error);
        this.loading.set(false);
      },
    });
  }

  onRowClick(resource: ResourceDto): void {
    this.router.navigate(['/resources', resource.id]);
  }

  onAddResource(): void {
    this.router.navigate(['/resources', 'new']);
  }

  onPreviousPage(): void {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
      this.loadResources();
    }
  }

  onNextPage(): void {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update((page) => page + 1);
      this.loadResources();
    }
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }

  min(a: number, b: number): number {
    return Math.min(a, b);
  }
}
