import api from './api';
import type { Plugin, PluginListItem, AvailablePlugin, UpdatePluginMetadataRequest } from '../types/plugin';

export const pluginService = {
  // Get all plugins for the current tenant
  async getPlugins(): Promise<PluginListItem[]> {
    const response = await api.get('/plugins');
    return response.data;
  },

  // Get plugin details
  async getPlugin(pluginName: string): Promise<Plugin> {
    const response = await api.get(`/plugins/${pluginName}`);
    return response.data;
  },

  // Upload a new plugin
  async uploadPlugin(
    file: File,
    metadata?: {
      name?: string;
      displayName?: string;
      description?: string;
      version?: string;
      author?: string;
      requiredClaims?: string[];
      outputClaims?: string[];
      configSchema?: Record<string, unknown>;
    }
  ): Promise<Plugin> {
    const formData = new FormData();
    formData.append('file', file);

    if (metadata?.name) formData.append('name', metadata.name);
    if (metadata?.displayName) formData.append('displayName', metadata.displayName);
    if (metadata?.description) formData.append('description', metadata.description);
    if (metadata?.version) formData.append('version', metadata.version);
    if (metadata?.author) formData.append('author', metadata.author);
    if (metadata?.requiredClaims) {
      metadata.requiredClaims.forEach(claim => formData.append('requiredClaims', claim));
    }
    if (metadata?.outputClaims) {
      metadata.outputClaims.forEach(claim => formData.append('outputClaims', claim));
    }
    if (metadata?.configSchema) {
      formData.append('configSchema', JSON.stringify(metadata.configSchema));
    }

    const response = await api.post('/plugins', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });
    return response.data;
  },

  // Update plugin metadata
  async updatePluginMetadata(pluginName: string, request: UpdatePluginMetadataRequest): Promise<Plugin> {
    const response = await api.patch(`/plugins/${pluginName}`, request);
    return response.data;
  },

  // Delete a plugin
  async deletePlugin(pluginName: string): Promise<void> {
    await api.delete(`/plugins/${pluginName}`);
  },

  // Reload a plugin in the executor
  async reloadPlugin(pluginName: string): Promise<{ reloaded: boolean }> {
    const response = await api.post(`/plugins/${pluginName}/reload`);
    return response.data;
  },

  // Get available plugins for journey steps (includes global + tenant plugins)
  async getAvailablePlugins(): Promise<AvailablePlugin[]> {
    const response = await api.get('/journeys/available-plugins');
    return response.data;
  },
};

export default pluginService;
