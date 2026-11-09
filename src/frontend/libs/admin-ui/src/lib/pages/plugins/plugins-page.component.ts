import { Component, inject, signal, ElementRef, ViewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { CardComponent, ButtonComponent, ModalComponent, IconComponent, LocaleService } from '@syndew/oluso-ui-core';
import { PluginService } from '../../services/plugin.service';
import {
  Plugin,
  PluginListItem,
  UploadPluginMetadata,
  PluginHookBinding,
  CreateHookBindingRequest,
  UpdateHookBindingRequest,
  PluginExecutionLog,
  PluginNetworkConfig,
  UpdateNetworkConfigRequest,
  NetworkAccessMode,
} from '../../models/plugin.model';

type DetailTab = 'info' | 'hooks' | 'network' | 'executions';

@Component({
  selector: 'admin-plugins-page',
  standalone: true,
  imports: [CommonModule, FormsModule, CardComponent, ButtonComponent, ModalComponent, IconComponent],
  templateUrl: './plugins-page.component.html',
})
export class PluginsPageComponent {
  private readonly pluginService = inject(PluginService);
  private readonly locale = inject(LocaleService);

  @ViewChild('fileInput') fileInputRef!: ElementRef<HTMLInputElement>;

  readonly loading = signal(true);
  readonly plugins = signal<PluginListItem[]>([]);
  readonly selectedPlugin = signal<Plugin | null>(null);
  readonly error = signal<string | null>(null);

  readonly showUploadModal = signal(false);
  readonly showDetailsModal = signal(false);
  readonly showDeleteModal = signal(false);
  readonly showHookModal = signal(false);

  readonly pluginToDelete = signal<string | null>(null);
  readonly uploading = signal(false);
  readonly reloadingPlugin = signal<string | null>(null);

  // Detail tabs
  readonly activeTab = signal<DetailTab>('info');

  // Hook bindings
  readonly hooks = signal<PluginHookBinding[]>([]);
  readonly loadingHooks = signal(false);
  readonly editingHook = signal<PluginHookBinding | null>(null);
  readonly savingHook = signal(false);

  // Execution logs
  readonly executions = signal<PluginExecutionLog[]>([]);
  readonly loadingExecutions = signal(false);

  // Network config
  readonly networkConfig = signal<PluginNetworkConfig | null>(null);
  readonly loadingNetwork = signal(false);
  readonly savingNetwork = signal(false);

  // Hook form
  hookForm: CreateHookBindingRequest = {
    domain: 'Journey',
    entityType: '*',
    eventType: '*',
    functionName: 'handle',
    order: 0,
    isActive: true,
    failureMode: 'Continue',
    timeoutMs: 5000,
  };

  // Network form
  networkForm: UpdateNetworkConfigRequest = {
    accessMode: 'None',
    allowedHosts: [],
    httpTimeoutMs: 5000,
    maxHttpRequestsPerExecution: 5,
  };
  allowedHostsText = '';

  // Upload form
  uploadFile: File | null = null;
  uploadName = '';
  uploadDisplayName = '';
  uploadDescription = '';
  uploadVersion = '1.0.0';
  uploadAuthor = '';

  readonly failureModeOptions: Array<'Continue' | 'Reject'> = ['Continue', 'Reject'];
  readonly accessModeOptions: NetworkAccessMode[] = ['None', 'Self', 'Whitelist', 'All'];

  constructor() {
    this.loadPlugins();
  }

  loadPlugins(): void {
    this.loading.set(true);
    this.pluginService.getPlugins().subscribe({
      next: (plugins) => {
        this.plugins.set(plugins);
        this.error.set(null);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set('Failed to load plugins');
        console.error(err);
        this.loading.set(false);
      },
    });
  }

  viewDetails(pluginName: string): void {
    this.pluginService.getPlugin(pluginName).subscribe({
      next: (plugin) => {
        this.selectedPlugin.set(plugin);
        this.activeTab.set('info');
        this.showDetailsModal.set(true);
      },
      error: (err) => {
        this.error.set('Failed to load plugin details');
        console.error(err);
      },
    });
  }

  onTabChange(tab: DetailTab): void {
    this.activeTab.set(tab);
    const plugin = this.selectedPlugin();
    if (!plugin) return;

    if (tab === 'hooks' && this.hooks().length === 0) {
      this.loadHooks(plugin.name);
    } else if (tab === 'executions' && this.executions().length === 0) {
      this.loadExecutions(plugin.name);
    } else if (tab === 'network' && !this.networkConfig()) {
      this.loadNetworkConfig(plugin.name);
    }
  }

  // ============ Hook Bindings ============

  loadHooks(pluginName: string): void {
    this.loadingHooks.set(true);
    this.pluginService.getHooks(pluginName).subscribe({
      next: (hooks) => {
        this.hooks.set(hooks);
        this.loadingHooks.set(false);
      },
      error: (err) => {
        console.error('Failed to load hooks', err);
        this.loadingHooks.set(false);
      },
    });
  }

  openCreateHookModal(): void {
    this.editingHook.set(null);
    this.hookForm = {
      domain: 'Journey',
      entityType: '*',
      eventType: '*',
      functionName: 'handle',
      order: 0,
      isActive: true,
      failureMode: 'Continue',
      timeoutMs: 5000,
    };
    this.showHookModal.set(true);
  }

  openEditHookModal(hook: PluginHookBinding): void {
    this.editingHook.set(hook);
    this.hookForm = {
      domain: hook.domain,
      entityType: hook.entityType,
      eventType: hook.eventType,
      functionName: hook.functionName,
      order: hook.order,
      isActive: hook.isActive,
      failureMode: hook.failureMode,
      timeoutMs: hook.timeoutMs,
      filterConditions: hook.filterConditions,
    };
    this.showHookModal.set(true);
  }

  closeHookModal(): void {
    this.showHookModal.set(false);
    this.editingHook.set(null);
  }

  saveHook(): void {
    const plugin = this.selectedPlugin();
    if (!plugin) return;

    this.savingHook.set(true);
    const editing = this.editingHook();

    if (editing) {
      const request: UpdateHookBindingRequest = this.hookForm;
      this.pluginService.updateHook(plugin.name, editing.id, request).subscribe({
        next: () => {
          this.savingHook.set(false);
          this.closeHookModal();
          this.loadHooks(plugin.name);
        },
        error: (err) => {
          console.error('Failed to update hook', err);
          this.savingHook.set(false);
        },
      });
    } else {
      this.pluginService.createHook(plugin.name, this.hookForm).subscribe({
        next: () => {
          this.savingHook.set(false);
          this.closeHookModal();
          this.loadHooks(plugin.name);
        },
        error: (err) => {
          console.error('Failed to create hook', err);
          this.savingHook.set(false);
        },
      });
    }
  }

  deleteHook(hook: PluginHookBinding): void {
    const plugin = this.selectedPlugin();
    if (!plugin) return;

    if (!confirm(`Delete hook binding for ${hook.domain}/${hook.entityType}/${hook.eventType}?`)) {
      return;
    }

    this.pluginService.deleteHook(plugin.name, hook.id).subscribe({
      next: () => {
        this.loadHooks(plugin.name);
      },
      error: (err) => {
        console.error('Failed to delete hook', err);
      },
    });
  }

  toggleHookActive(hook: PluginHookBinding): void {
    const plugin = this.selectedPlugin();
    if (!plugin) return;

    const request: UpdateHookBindingRequest = {
      domain: hook.domain,
      entityType: hook.entityType,
      eventType: hook.eventType,
      functionName: hook.functionName,
      order: hook.order,
      isActive: !hook.isActive,
      failureMode: hook.failureMode,
      timeoutMs: hook.timeoutMs,
      filterConditions: hook.filterConditions,
    };

    this.pluginService.updateHook(plugin.name, hook.id, request).subscribe({
      next: () => {
        this.loadHooks(plugin.name);
      },
      error: (err) => {
        console.error('Failed to toggle hook', err);
      },
    });
  }

  // ============ Execution Logs ============

  loadExecutions(pluginName: string): void {
    this.loadingExecutions.set(true);
    this.pluginService.getExecutions(pluginName).subscribe({
      next: (executions) => {
        this.executions.set(executions);
        this.loadingExecutions.set(false);
      },
      error: (err) => {
        console.error('Failed to load executions', err);
        this.loadingExecutions.set(false);
      },
    });
  }

  refreshExecutions(): void {
    const plugin = this.selectedPlugin();
    if (plugin) {
      this.loadExecutions(plugin.name);
    }
  }

  // ============ Network Configuration ============

  loadNetworkConfig(pluginName: string): void {
    this.loadingNetwork.set(true);
    this.pluginService.getNetworkConfig(pluginName).subscribe({
      next: (config) => {
        this.networkConfig.set(config);
        this.networkForm = {
          accessMode: config.accessMode,
          allowedHosts: config.allowedHosts,
          httpTimeoutMs: config.httpTimeoutMs,
          maxHttpRequestsPerExecution: config.maxHttpRequestsPerExecution,
        };
        this.allowedHostsText = config.allowedHosts.join('\n');
        this.loadingNetwork.set(false);
      },
      error: (err) => {
        console.error('Failed to load network config', err);
        this.loadingNetwork.set(false);
      },
    });
  }

  saveNetworkConfig(): void {
    const plugin = this.selectedPlugin();
    if (!plugin) return;

    this.savingNetwork.set(true);
    this.networkForm.allowedHosts = this.allowedHostsText
      .split('\n')
      .map((h) => h.trim())
      .filter((h) => h.length > 0);

    this.pluginService.updateNetworkConfig(plugin.name, this.networkForm).subscribe({
      next: (config) => {
        this.networkConfig.set(config);
        this.savingNetwork.set(false);
      },
      error: (err) => {
        console.error('Failed to save network config', err);
        this.savingNetwork.set(false);
      },
    });
  }

  // ============ Plugin Management ============

  reloadPlugin(pluginName: string): void {
    this.reloadingPlugin.set(pluginName);
    this.pluginService.reloadPlugin(pluginName).subscribe({
      next: () => {
        this.reloadingPlugin.set(null);
        this.loadPlugins();
      },
      error: (err) => {
        this.error.set('Failed to reload plugin');
        console.error(err);
        this.reloadingPlugin.set(null);
      },
    });
  }

  openDeleteModal(pluginName: string): void {
    this.pluginToDelete.set(pluginName);
    this.showDeleteModal.set(true);
  }

  confirmDelete(): void {
    const pluginName = this.pluginToDelete();
    if (!pluginName) return;

    this.pluginService.deletePlugin(pluginName).subscribe({
      next: () => {
        this.showDeleteModal.set(false);
        this.pluginToDelete.set(null);
        this.loadPlugins();
      },
      error: (err) => {
        this.error.set('Failed to delete plugin');
        console.error(err);
      },
    });
  }

  openUploadModal(): void {
    this.resetUploadForm();
    this.showUploadModal.set(true);
  }

  closeUploadModal(): void {
    this.showUploadModal.set(false);
    this.resetUploadForm();
  }

  closeDetailsModal(): void {
    this.showDetailsModal.set(false);
    this.selectedPlugin.set(null);
    this.hooks.set([]);
    this.executions.set([]);
    this.networkConfig.set(null);
  }

  closeDeleteModal(): void {
    this.showDeleteModal.set(false);
    this.pluginToDelete.set(null);
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.uploadFile = input.files?.[0] || null;
  }

  uploadPlugin(): void {
    if (!this.uploadFile) return;

    this.uploading.set(true);
    const metadata: UploadPluginMetadata = {
      name: this.uploadName || undefined,
      displayName: this.uploadDisplayName || undefined,
      description: this.uploadDescription || undefined,
      version: this.uploadVersion || undefined,
      author: this.uploadAuthor || undefined,
    };

    this.pluginService.uploadPlugin(this.uploadFile, metadata).subscribe({
      next: () => {
        this.uploading.set(false);
        this.closeUploadModal();
        this.loadPlugins();
      },
      error: (err) => {
        this.error.set('Failed to upload plugin');
        console.error(err);
        this.uploading.set(false);
      },
    });
  }

  private resetUploadForm(): void {
    this.uploadFile = null;
    this.uploadName = '';
    this.uploadDisplayName = '';
    this.uploadDescription = '';
    this.uploadVersion = '1.0.0';
    this.uploadAuthor = '';
    if (this.fileInputRef?.nativeElement) {
      this.fileInputRef.nativeElement.value = '';
    }
  }

  formatBytes(bytes: number): string {
    if (bytes === 0) return '0 Bytes';
    const k = 1024;
    const sizes = ['Bytes', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  }

  formatDate(dateString: string): string {
    return new Date(dateString).toLocaleDateString(this.locale.locale(), {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  formatDuration(ms: number): string {
    if (ms < 1000) return `${ms}ms`;
    return `${(ms / 1000).toFixed(2)}s`;
  }

  getResultBadgeClass(result: string): string {
    switch (result) {
      case 'Success':
        return 'bg-green-100 text-green-800';
      case 'Error':
        return 'bg-red-100 text-red-800';
      case 'Timeout':
        return 'bg-yellow-100 text-yellow-800';
      case 'Skipped':
        return 'bg-gray-100 text-gray-800';
      default:
        return 'bg-gray-100 text-gray-800';
    }
  }
}
