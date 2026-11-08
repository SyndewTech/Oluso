import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router } from '@angular/router';
import { CardComponent, ButtonComponent, BadgeComponent, IconComponent, ConfirmDialogComponent } from '@oluso/ui-core';
import { PlatformClientService } from '../../services/platform-client.service';
import { Client } from '../../models/client.model';

@Component({
  selector: 'admin-platform-clients-page',
  standalone: true,
  imports: [CommonModule, CardComponent, ButtonComponent, BadgeComponent, IconComponent, ConfirmDialogComponent],
  templateUrl: './platform-clients-page.component.html',
})
export class PlatformClientsPageComponent {
  private readonly router = inject(Router);
  private readonly platformClientService = inject(PlatformClientService);

  readonly loading = signal(true);
  readonly clients = signal<Client[]>([]);

  // Delete confirmation
  readonly showDeleteConfirm = signal(false);
  readonly deletingClient = signal<Client | null>(null);
  readonly saving = signal(false);

  constructor() {
    this.loadClients();
  }

  loadClients(): void {
    this.loading.set(true);
    this.platformClientService.getAll().subscribe({
      next: (clients) => {
        this.clients.set(clients);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load platform clients:', error);
        this.loading.set(false);
      },
    });
  }

  navigateToCreate(): void {
    this.router.navigate(['/platform/clients/new']);
  }

  navigateToEdit(client: Client): void {
    this.router.navigate(['/platform/clients', client.id]);
  }

  confirmDelete(client: Client): void {
    this.deletingClient.set(client);
    this.showDeleteConfirm.set(true);
  }

  cancelDelete(): void {
    this.showDeleteConfirm.set(false);
    this.deletingClient.set(null);
  }

  deleteClient(): void {
    const client = this.deletingClient();
    if (!client) return;

    this.saving.set(true);
    this.platformClientService.delete(client.id).subscribe({
      next: () => {
        this.showDeleteConfirm.set(false);
        this.deletingClient.set(null);
        this.saving.set(false);
        this.loadClients();
      },
      error: (error) => {
        console.error('Failed to delete client:', error);
        this.saving.set(false);
      },
    });
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }

  formatGrantTypes(grantTypes: string[]): string {
    return grantTypes?.join(', ') || '-';
  }
}
