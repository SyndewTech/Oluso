import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import {
  UpdateWhatsAppSettingsRequest,
  WhatsAppContact,
  WhatsAppContactList,
  WhatsAppSendTestRequest,
  WhatsAppSendTestResult,
  WhatsAppSettings,
  WhatsAppTemplate,
  WhatsAppTemplateSyncResult,
  WhatsAppTestConnectionResult,
} from '../models/whatsapp.model';

/** Tenant WhatsApp Business configuration (api/admin/messaging/whatsapp). */
@Injectable({ providedIn: 'root' })
export class WhatsAppService {
  private readonly api = inject(ApiService);
  private readonly endpoint = '/messaging/whatsapp';

  getSettings(): Observable<WhatsAppSettings> {
    return this.api.get<WhatsAppSettings>(`${this.endpoint}/settings`);
  }

  updateSettings(input: UpdateWhatsAppSettingsRequest): Observable<WhatsAppSettings> {
    return this.api.put<WhatsAppSettings>(`${this.endpoint}/settings`, input);
  }

  testConnection(): Observable<WhatsAppTestConnectionResult> {
    return this.api.post<WhatsAppTestConnectionResult>(`${this.endpoint}/test`, {});
  }

  listTemplates(approvedOnly = false): Observable<WhatsAppTemplate[]> {
    return this.api.get<WhatsAppTemplate[]>(`${this.endpoint}/templates`, { approvedOnly });
  }

  syncTemplates(): Observable<WhatsAppTemplateSyncResult> {
    return this.api.post<WhatsAppTemplateSyncResult>(`${this.endpoint}/templates/sync`, {});
  }

  listContacts(params: { search?: string; consent?: string; page?: number; pageSize?: number } = {}): Observable<WhatsAppContactList> {
    const query: Record<string, string | number> = {};
    if (params.search) query['search'] = params.search;
    if (params.consent) query['consent'] = params.consent;
    if (params.page) query['page'] = params.page;
    if (params.pageSize) query['pageSize'] = params.pageSize;
    return this.api.get<WhatsAppContactList>(`${this.endpoint}/contacts`, query);
  }

  setConsent(contactId: string, consentStatus: 'OptedIn' | 'OptedOut'): Observable<WhatsAppContact> {
    return this.api.put<WhatsAppContact>(`${this.endpoint}/contacts/${contactId}/consent`, { consentStatus });
  }

  sendTest(input: WhatsAppSendTestRequest): Observable<WhatsAppSendTestResult> {
    return this.api.post<WhatsAppSendTestResult>(`${this.endpoint}/send-test`, input);
  }
}
