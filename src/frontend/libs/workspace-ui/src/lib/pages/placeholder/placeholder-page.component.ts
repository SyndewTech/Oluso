import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonComponent, IconComponent } from '@syndew/oluso-ui-core';

@Component({
  selector: 'workspace-placeholder-page',
  standalone: true,
  imports: [CommonModule, RouterLink, ButtonComponent, IconComponent],
  template: `
    <div class="bg-white rounded-lg shadow p-8 text-center">
      <div class="mx-auto h-16 w-16 bg-gray-100 rounded-full flex items-center justify-center mb-4">
        <ui-icon name="archive-box" class="h-8 w-8 text-gray-400" />
      </div>
      <h1 class="text-2xl font-bold text-gray-900 mb-2">{{ title }}</h1>
      <p class="text-gray-600 mb-6">{{ message }}</p>
      <ui-button routerLink="/dashboard" variant="secondary">
        Back to Dashboard
      </ui-button>
    </div>
  `,
})
export class PlaceholderPageComponent {
  private readonly route = inject(ActivatedRoute);

  get title(): string {
    return this.route.snapshot.data['title'] || 'Coming Soon';
  }

  get message(): string {
    return this.route.snapshot.data['message'] || 'This feature is not yet available.';
  }
}
