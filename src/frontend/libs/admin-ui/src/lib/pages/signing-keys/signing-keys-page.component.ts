import { Component, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardComponent, ButtonComponent, BadgeComponent, ModalComponent, ConfirmDialogComponent, IconComponent, LocaleService } from '@oluso/ui-core';
import { SigningKeyService } from '../../services/signing-key.service';
import {
  SigningKey,
  KeyRotationConfig,
  GenerateKeyRequest,
  ALGORITHM_OPTIONS,
  KEY_SIZE_OPTIONS,
  KeyType,
} from '../../models/signing-key.model';

@Component({
  selector: 'admin-signing-keys-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, BadgeComponent, ModalComponent, ConfirmDialogComponent, IconComponent],
  templateUrl: './signing-keys-page.component.html',
})
export class SigningKeysPageComponent {
  private readonly signingKeyService = inject(SigningKeyService);
  private readonly locale = inject(LocaleService);

  readonly loading = signal(true);
  readonly keys = signal<SigningKey[]>([]);
  readonly rotationConfig = signal<KeyRotationConfig | null>(null);
  readonly error = signal<string | null>(null);

  readonly showGenerateModal = signal(false);
  readonly showConfigModal = signal(false);
  readonly showRotateModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly showRevokeModal = signal(false);
  readonly generating = signal(false);
  readonly savingConfig = signal(false);
  readonly rotating = signal(false);
  readonly deleting = signal(false);
  readonly revoking = signal(false);
  readonly selectedKey = signal<SigningKey | null>(null);
  revokeReason = '';

  // Generate form
  generateRequest: GenerateKeyRequest = {
    keyType: 'RSA',
    algorithm: 'RS256',
    keySize: 2048,
    lifetimeDays: 90,
    activateImmediately: true,
  };

  // Config form
  configForm: KeyRotationConfig = {};

  constructor() {
    this.loadData();
  }

  loadData(): void {
    this.loading.set(true);
    this.error.set(null);

    this.signingKeyService.getKeys().subscribe({
      next: (keys) => {
        this.keys.set(keys);
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to load signing keys');
        this.loading.set(false);
      },
    });

    this.signingKeyService.getRotationConfig().subscribe({
      next: (config) => {
        this.rotationConfig.set(config);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to load rotation config');
        this.loading.set(false);
      },
    });
  }

  openRotateModal(): void {
    this.showRotateModal.set(true);
  }

  closeRotateModal(): void {
    this.showRotateModal.set(false);
  }

  confirmRotateKeys(): void {
    this.rotating.set(true);
    this.signingKeyService.rotateKeys().subscribe({
      next: () => {
        this.rotating.set(false);
        this.closeRotateModal();
        this.loadData();
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to rotate keys');
        this.rotating.set(false);
      },
    });
  }

  openRevokeModal(key: SigningKey): void {
    this.selectedKey.set(key);
    this.revokeReason = '';
    this.showRevokeModal.set(true);
  }

  closeRevokeModal(): void {
    this.showRevokeModal.set(false);
    this.selectedKey.set(null);
    this.revokeReason = '';
  }

  confirmRevokeKey(): void {
    const key = this.selectedKey();
    if (!key || !this.revokeReason.trim()) return;

    this.revoking.set(true);
    this.signingKeyService.revokeKey(key.id, this.revokeReason).subscribe({
      next: () => {
        this.revoking.set(false);
        this.closeRevokeModal();
        this.loadData();
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to revoke key');
        this.revoking.set(false);
      },
    });
  }

  openDeleteModal(key: SigningKey): void {
    this.selectedKey.set(key);
    this.showDeleteModal.set(true);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.selectedKey.set(null);
  }

  confirmDeleteKey(): void {
    const key = this.selectedKey();
    if (!key) return;

    this.deleting.set(true);
    this.signingKeyService.deleteKey(key.id).subscribe({
      next: () => {
        this.deleting.set(false);
        this.closeDeleteModal();
        this.loadData();
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to delete key');
        this.deleting.set(false);
      },
    });
  }

  openGenerateModal(): void {
    this.generateRequest = {
      keyType: 'RSA',
      algorithm: 'RS256',
      keySize: 2048,
      lifetimeDays: 90,
      activateImmediately: true,
    };
    this.showGenerateModal.set(true);
  }

  closeGenerateModal(): void {
    this.showGenerateModal.set(false);
  }

  openConfigModal(): void {
    const config = this.rotationConfig();
    this.configForm = config ? { ...config } : {};
    this.showConfigModal.set(true);
  }

  closeConfigModal(): void {
    this.showConfigModal.set(false);
  }

  onKeyTypeChange(): void {
    const keyType = this.generateRequest.keyType as KeyType;
    this.generateRequest.algorithm = ALGORITHM_OPTIONS[keyType][0];
    this.generateRequest.keySize = KEY_SIZE_OPTIONS[keyType][0];
  }

  getAlgorithmOptions(): string[] {
    const keyType = (this.generateRequest.keyType || 'RSA') as KeyType;
    return ALGORITHM_OPTIONS[keyType];
  }

  getKeySizeOptions(): number[] {
    const keyType = (this.generateRequest.keyType || 'RSA') as KeyType;
    return KEY_SIZE_OPTIONS[keyType];
  }

  generateKey(): void {
    this.generating.set(true);
    this.signingKeyService.generateKey(this.generateRequest).subscribe({
      next: () => {
        this.generating.set(false);
        this.closeGenerateModal();
        this.loadData();
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to generate key');
        this.generating.set(false);
      },
    });
  }

  saveConfig(): void {
    this.savingConfig.set(true);
    this.signingKeyService.updateRotationConfig(this.configForm).subscribe({
      next: () => {
        this.savingConfig.set(false);
        this.closeConfigModal();
        this.loadData();
      },
      error: (err) => {
        this.error.set(err.message || 'Failed to save config');
        this.savingConfig.set(false);
      },
    });
  }

  hasExpiringKeys(): boolean {
    return this.keys().some((k) => k.isExpiringSoon);
  }

  formatDate(date?: string): string {
    if (!date) return '-';
    return new Date(date).toLocaleDateString(this.locale.locale(), {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  /** A key past its expiry date is shown as Expired even before the maintenance job updates its stored status. */
  effectiveStatus(key: { status: string; isExpired: boolean }): string {
    return key.isExpired && (key.status === 'Active' || key.status === 'Pending') ? 'Expired' : key.status;
  }

  getStatusVariant(status: string): 'default' | 'success' | 'warning' | 'danger' | 'info' {
    switch (status) {
      case 'Active':
        return 'success';
      case 'Pending':
        return 'warning';
      case 'Revoked':
        return 'danger';
      case 'Expired':
        return 'warning';
      case 'Archived':
      default:
        return 'default';
    }
  }
}
