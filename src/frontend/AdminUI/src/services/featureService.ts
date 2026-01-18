import api from './api';
import type {
  TenantFeaturesResponse,
  FeatureCheckResponse,
  LimitCheckResponse,
  FeatureDefinitionsResponse,
} from '../types/features';

// These endpoints use /api/admin/features (Admin API)
export const featureService = {
  /**
   * Get all features and limits for the current tenant
   */
  async getTenantFeatures(): Promise<TenantFeaturesResponse> {
    const response = await api.get<TenantFeaturesResponse>('/features');
    return response.data;
  },

  /**
   * Check if a specific feature is available
   */
  async checkFeature(featureKey: string): Promise<FeatureCheckResponse> {
    const response = await api.get<FeatureCheckResponse>(`/features/${featureKey}`);
    return response.data;
  },

  /**
   * Check if the tenant is within a specific limit
   */
  async checkLimit(limitType: string, requestedAmount = 1): Promise<LimitCheckResponse> {
    const response = await api.get<LimitCheckResponse>(`/features/limits/${limitType}`, {
      params: { requestedAmount },
    });
    return response.data;
  },

  /**
   * Get all feature and limit definitions
   */
  async getDefinitions(): Promise<FeatureDefinitionsResponse> {
    const response = await api.get<FeatureDefinitionsResponse>('/features/definitions');
    return response.data;
  },
};

export default featureService;
