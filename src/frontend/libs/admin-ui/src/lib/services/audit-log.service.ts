import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PaginatedResponse } from '@syndew/oluso-ui-core';
import { AuditLog, AuditLogFilter } from '../models/audit-log.model';

@Injectable({
  providedIn: 'root',
})
export class AuditLogService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/auditlogs';

  /**
   * Get audit logs with filters and pagination
   */
  getLogs(filter: AuditLogFilter): Observable<PaginatedResponse<AuditLog>> {
    let params = new HttpParams()
      .set('page', filter.pageNumber.toString())
      .set('pageSize', filter.pageSize.toString());

    if (filter.search) params = params.set('search', filter.search);
    if (filter.category) params = params.set('category', filter.category);
    if (filter.action) params = params.set('action', filter.action);
    if (filter.subjectId) params = params.set('subjectId', filter.subjectId);
    if (filter.clientId) params = params.set('clientId', filter.clientId);
    if (filter.originKind) params = params.set('originKind', filter.originKind);
    if (filter.originId) params = params.set('originId', filter.originId);
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);
    if (filter.success !== undefined) params = params.set('success', filter.success.toString());

    return this.api.getWithParams<PaginatedResponse<AuditLog>>(this.endpoint, params);
  }

  /**
   * Export audit logs to CSV or JSON
   */
  export(filter: Partial<AuditLogFilter>, format: 'csv' | 'json'): Observable<Blob> {
    let params = new HttpParams().set('format', format);

    if (filter.search) params = params.set('search', filter.search);
    if (filter.category) params = params.set('category', filter.category);
    if (filter.action) params = params.set('action', filter.action);
    if (filter.subjectId) params = params.set('subjectId', filter.subjectId);
    if (filter.clientId) params = params.set('clientId', filter.clientId);
    if (filter.originKind) params = params.set('originKind', filter.originKind);
    if (filter.originId) params = params.set('originId', filter.originId);
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);

    return this.api.getBlob(`${this.endpoint}/export`, params);
  }
}
