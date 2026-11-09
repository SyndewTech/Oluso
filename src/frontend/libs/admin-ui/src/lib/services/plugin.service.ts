import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  Plugin,
  PluginListItem,
  AvailablePlugin,
  UploadPluginMetadata,
  UpdatePluginMetadataRequest,
  PluginHookBinding,
  CreateHookBindingRequest,
  UpdateHookBindingRequest,
  PluginExecutionLog,
  PluginNetworkConfig,
  UpdateNetworkConfigRequest,
} from '../models/plugin.model';

@Injectable({
  providedIn: 'root',
})
export class PluginService {
  private readonly api = inject(ApiService);
  private readonly http = inject(HttpClient);

  getPlugins(): Observable<PluginListItem[]> {
    return this.api.get<PluginListItem[]>('/plugins');
  }

  getPlugin(pluginName: string): Observable<Plugin> {
    return this.api.get<Plugin>(`/plugins/${pluginName}`);
  }

  uploadPlugin(file: File, metadata?: UploadPluginMetadata): Observable<Plugin> {
    const formData = new FormData();
    formData.append('file', file);

    if (metadata?.name) formData.append('name', metadata.name);
    if (metadata?.displayName) formData.append('displayName', metadata.displayName);
    if (metadata?.description) formData.append('description', metadata.description);
    if (metadata?.version) formData.append('version', metadata.version);
    if (metadata?.author) formData.append('author', metadata.author);
    if (metadata?.requiredClaims) {
      metadata.requiredClaims.forEach((claim) => formData.append('requiredClaims', claim));
    }
    if (metadata?.outputClaims) {
      metadata.outputClaims.forEach((claim) => formData.append('outputClaims', claim));
    }
    if (metadata?.configSchema) {
      formData.append('configSchema', JSON.stringify(metadata.configSchema));
    }

    return this.http.post<Plugin>(`${this.api.baseUrl}/plugins`, formData);
  }

  updatePluginMetadata(pluginName: string, request: UpdatePluginMetadataRequest): Observable<Plugin> {
    return this.api.patch<Plugin>(`/plugins/${pluginName}`, request);
  }

  deletePlugin(pluginName: string): Observable<void> {
    return this.api.delete<void>(`/plugins/${pluginName}`);
  }

  reloadPlugin(pluginName: string): Observable<{ reloaded: boolean }> {
    return this.api.post<{ reloaded: boolean }>(`/plugins/${pluginName}/reload`, {});
  }

  getAvailablePlugins(): Observable<AvailablePlugin[]> {
    return this.api.get<AvailablePlugin[]>('/journeys/available-plugins');
  }

  // ============ Hook Bindings ============

  getHooks(pluginName: string): Observable<PluginHookBinding[]> {
    return this.api.get<PluginHookBinding[]>(`/plugins/${pluginName}/hooks`);
  }

  createHook(pluginName: string, request: CreateHookBindingRequest): Observable<PluginHookBinding> {
    return this.api.post<PluginHookBinding>(`/plugins/${pluginName}/hooks`, request);
  }

  updateHook(pluginName: string, hookId: string, request: UpdateHookBindingRequest): Observable<PluginHookBinding> {
    return this.api.put<PluginHookBinding>(`/plugins/${pluginName}/hooks/${hookId}`, request);
  }

  deleteHook(pluginName: string, hookId: string): Observable<void> {
    return this.api.delete<void>(`/plugins/${pluginName}/hooks/${hookId}`);
  }

  // ============ Execution Logs ============

  getExecutions(pluginName: string, limit = 100): Observable<PluginExecutionLog[]> {
    return this.api.get<PluginExecutionLog[]>(`/plugins/${pluginName}/executions`, { limit });
  }

  // ============ Network Configuration ============

  getNetworkConfig(pluginName: string): Observable<PluginNetworkConfig> {
    return this.api.get<PluginNetworkConfig>(`/plugins/${pluginName}/network`);
  }

  updateNetworkConfig(pluginName: string, request: UpdateNetworkConfigRequest): Observable<PluginNetworkConfig> {
    return this.api.put<PluginNetworkConfig>(`/plugins/${pluginName}/network`, request);
  }
}
