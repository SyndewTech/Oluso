import { Plugin } from './plugin.type';
import { PluginConfig } from './plugin-config.model';

export type PluginFactory<T extends Plugin = Plugin> = (config?: PluginConfig) => T;
