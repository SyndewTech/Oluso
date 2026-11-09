import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  JourneyPolicy,
  JourneyPolicyListItem,
  JourneyStep,
  StepTypeInfo,
  StepTestResult,
  CreatePolicyRequest,
  UpdatePolicyRequest,
  ValidationResult,
  ImportPolicyResult,
  ImportPolicyOptions,
  JourneyOwnerModule,
} from '../models/journey.model';
import { AvailablePlugin } from '../models/plugin.model';

@Injectable({
  providedIn: 'root',
})
export class JourneyService {
  private readonly api = inject(ApiService);

  // Get all policies
  getPolicies(includeGlobal = true): Observable<JourneyPolicyListItem[]> {
    return this.api.get<JourneyPolicyListItem[]>('/journeys', { includeGlobal });
  }

  // Get single policy
  getPolicy(policyId: string): Observable<JourneyPolicy> {
    return this.api.get<JourneyPolicy>(`/journeys/${policyId}`);
  }

  // Create policy
  createPolicy(request: CreatePolicyRequest): Observable<JourneyPolicy> {
    return this.api.post<JourneyPolicy>('/journeys', request);
  }

  // Update policy
  updatePolicy(policyId: string, request: UpdatePolicyRequest): Observable<JourneyPolicy> {
    return this.api.put<JourneyPolicy>(`/journeys/${policyId}`, request);
  }

  // Delete policy
  deletePolicy(policyId: string): Observable<void> {
    return this.api.delete<void>(`/journeys/${policyId}`);
  }

  // Clone policy
  // A slug in use is a 409 (PolicySlugConflict); omit newSlug to derive one from the name
  clonePolicy(policyId: string, newSlug?: string, newName?: string): Observable<JourneyPolicy> {
    return this.api.post<JourneyPolicy>(`/journeys/${policyId}/clone`, { newSlug, newName });
  }

  // Download a policy as a portable .journey file (secrets and API key stripped server-side)
  exportPolicy(policyId: string): Observable<Blob> {
    return this.api.getBlob(`/journeys/${policyId}/export`, new HttpParams());
  }

  // Create a new, disabled policy in this tenant from a .journey file's content
  // Keeps the file's slug unless options say otherwise; a slug in use is a 409 (PolicySlugConflict)
  importPolicy(content: string, options: ImportPolicyOptions = {}): Observable<ImportPolicyResult> {
    return this.api.post<ImportPolicyResult>('/journeys/import', { content, ...options });
  }

  // Enable/disable policy
  setStatus(policyId: string, enabled: boolean): Observable<{ enabled: boolean }> {
    return this.api.patch<{ enabled: boolean }>(`/journeys/${policyId}/status`, { enabled });
  }

  /** Modules the current admin may make a journey's owner. */
  getOwnerModules(): Observable<JourneyOwnerModule[]> {
    return this.api.get<JourneyOwnerModule[]>('/journeys/owner-modules');
  }

  // Get available step types
  getStepTypes(): Observable<StepTypeInfo[]> {
    return this.api.get<StepTypeInfo[]>('/journeys/step-types');
  }

  /** Runs the step's own settings check; secrets left redacted are taken from the saved step. */
  testStep(request: { type: string; configuration?: Record<string, unknown>; policyId?: string; stepId?: string }): Observable<StepTestResult> {
    return this.api.post<StepTestResult>('/journeys/test-step', request);
  }

  // Validate policy
  validatePolicy(steps: JourneyStep[]): Observable<ValidationResult> {
    return this.api.post<ValidationResult>('/journeys/validate', { steps });
  }

  // Get available plugins for CustomPlugin steps
  getAvailablePlugins(): Observable<AvailablePlugin[]> {
    return this.api.get<AvailablePlugin[]>('/journeys/available-plugins');
  }

  // Get supported UI locale codes for localizable step config fields
  getUiLocales(): Observable<string[]> {
    return this.api.get<string[]>('/journeys/ui-locales');
  }
}
