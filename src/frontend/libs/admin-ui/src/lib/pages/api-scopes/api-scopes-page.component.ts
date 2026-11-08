import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CardComponent, ButtonComponent, BadgeComponent, IconComponent } from '@oluso/ui-core';
import { ApiScopeService } from '../../services/api-scope.service';
import { ApiScopeDto } from '../../models/api-scope.model';
import { PaginationParams } from '../../models/pagination-params.model';

@Component({
  selector: 'admin-api-scopes-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent],
  templateUrl: './api-scopes-page.component.html',
})
export class ApiScopesPageComponent {
  private readonly apiScopeService = inject(ApiScopeService);
  private readonly router = inject(Router);

  readonly loading = signal(true);
  readonly scopes = signal<ApiScopeDto[]>([]);
  readonly currentPage = signal(1);
  readonly totalPages = signal(1);
  readonly totalCount = signal(0);
  readonly pageSize = 10;

  constructor() {
    this.loadScopes();
  }

  loadScopes(): void {
    this.loading.set(true);
    const pagination: PaginationParams = {
      page: this.currentPage(),
      pageSize: this.pageSize,
    };

    this.apiScopeService.getAll(pagination).subscribe({
      next: (response) => {
        this.scopes.set(response.items);
        this.totalPages.set(response.totalPages);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load API scopes:', error);
        this.loading.set(false);
      },
    });
  }

  onRowClick(scope: ApiScopeDto): void {
    this.router.navigate(['/api-scopes', scope.id]);
  }

  onAddScope(): void {
    this.router.navigate(['/api-scopes', 'new']);
  }

  onPreviousPage(): void {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
      this.loadScopes();
    }
  }

  onNextPage(): void {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update((page) => page + 1);
      this.loadScopes();
    }
  }

  min(a: number, b: number): number {
    return Math.min(a, b);
  }
}
