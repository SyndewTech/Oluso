import { Component, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { CardComponent, ButtonComponent, BadgeComponent, IconComponent, ModalComponent, InputComponent } from '@syndew/oluso-ui-core';
import { ClientService } from '../../services/client.service';
import { Client } from '../../models/client.model';
import { ClientConnectionPanelComponent } from '../../components/client-connection-panel/client-connection-panel.component';

@Component({
  selector: 'admin-clients-page',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    InputComponent,
    CardComponent,
    ButtonComponent,
    BadgeComponent,
    IconComponent,
    ModalComponent,
    ClientConnectionPanelComponent,
  ],
  templateUrl: './clients-page.component.html',
})
export class ClientsPageComponent {
  private readonly clientService = inject(ClientService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  readonly loading = signal(true);
  private readonly allClients = signal<Client[]>([]);
  readonly searchTerm = signal(this.route.snapshot.queryParamMap.get('search') ?? '');
  readonly currentPage = signal(1);
  readonly pageSize = 10;

  /** Client whose connection panel is currently shown in the side modal. */
  readonly previewClient = signal<Client | null>(null);

  readonly filteredClients = computed(() => {
    const term = this.searchTerm().trim().toLowerCase();
    if (!term) return this.allClients();
    return this.allClients().filter(c =>
      c.clientId.toLowerCase().includes(term)
      || (c.clientName ?? '').toLowerCase().includes(term)
      || (c.description ?? '').toLowerCase().includes(term));
  });

  readonly totalPages = computed(() => Math.ceil(this.filteredClients().length / this.pageSize) || 1);

  readonly clients = computed(() => {
    const start = (this.currentPage() - 1) * this.pageSize;
    const end = start + this.pageSize;
    return this.filteredClients().slice(start, end);
  });

  onSearchChange(term: string): void {
    this.searchTerm.set(term);
    this.currentPage.set(1);
  }

  constructor() {
    this.loadClients();
  }

  loadClients(): void {
    this.loading.set(true);

    this.clientService.getClients().subscribe({
      next: (clients) => {
        this.allClients.set(clients);
        this.loading.set(false);
      },
      error: (error) => {
        console.error('Failed to load clients:', error);
        this.loading.set(false);
      },
    });
  }

  onRowClick(client: Client): void {
    this.router.navigate(['/clients', client.id]);
  }

  /** Open the read-only connection panel without leaving the list. */
  openConnectionPreview(event: Event, client: Client): void {
    event.stopPropagation();
    this.previewClient.set(client);
  }

  closeConnectionPreview(): void {
    this.previewClient.set(null);
  }

  onAddClient(): void {
    this.router.navigate(['/clients/new']);
  }

  onPreviousPage(): void {
    if (this.currentPage() > 1) {
      this.currentPage.update((page) => page - 1);
      this.loadClients();
    }
  }

  onNextPage(): void {
    if (this.currentPage() < this.totalPages()) {
      this.currentPage.update((page) => page + 1);
      this.loadClients();
    }
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString();
  }

  formatGrantTypes(grantTypes: string[]): string {
    return grantTypes?.join(', ') || '-';
  }
}
