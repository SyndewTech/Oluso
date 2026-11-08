import { Injectable, inject } from '@angular/core';
import { HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { PaginatedResponse } from '@oluso/ui-core';
import { FeedbackDto, FeedbackFilter } from '../models/feedback.model';

@Injectable({
  providedIn: 'root',
})
export class FeedbackAdminService {
  private readonly api = inject(ApiService);

  private readonly endpoint = '/feedback';

  getAll(filter: FeedbackFilter): Observable<PaginatedResponse<FeedbackDto>> {
    let params = new HttpParams()
      .set('page', filter.pageNumber.toString())
      .set('pageSize', filter.pageSize.toString());

    if (filter.category) params = params.set('category', filter.category);
    if (filter.status) params = params.set('status', filter.status);

    return this.api.getWithParams<PaginatedResponse<FeedbackDto>>(this.endpoint, params);
  }

  getById(id: string): Observable<FeedbackDto> {
    return this.api.get<FeedbackDto>(`${this.endpoint}/${id}`);
  }

  updateStatus(id: string, status: string): Observable<void> {
    return this.api.patch<void>(`${this.endpoint}/${id}/status`, { status });
  }

  getScreenshotUrl(id: string): string {
    return `${this.api.baseUrl}${this.endpoint}/${id}/screenshot`;
  }
}
