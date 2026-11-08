import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';

export interface ModuleSeatSummary {
  featureKey: string;
  maxSeats: number | null;
  usedSeats: number;
  seatBased: boolean;
}

export interface ModuleSeatUser {
  userId: string;
  userName?: string;
  displayName?: string;
  email?: string;
  grantedAt: string;
  grantedByUserId?: string;
}

export interface ModuleSeatDetail {
  featureKey: string;
  maxSeats: number | null;
  usedSeats: number;
  seatBased: boolean;
  users: ModuleSeatUser[];
}

export interface UserModuleInfo {
  featureKey: string;
  grantedAt: string;
  grantedByUserId?: string;
}

export interface BulkGrantResult {
  featureKey: string;
  granted: number;
  alreadyAssigned: number;
}

@Injectable({
  providedIn: 'root',
})
export class ModuleSeatsService {
  private readonly api = inject(ApiService);

  getAllModuleSeats(): Observable<ModuleSeatSummary[]> {
    return this.api.get<ModuleSeatSummary[]>('/module-seats');
  }

  getModuleSeats(featureKey: string): Observable<ModuleSeatDetail> {
    return this.api.get<ModuleSeatDetail>(`/module-seats/${featureKey}`);
  }

  grantSeat(featureKey: string, userId: string): Observable<{ featureKey: string; userId: string; granted: boolean }> {
    return this.api.post(`/module-seats/${featureKey}/users/${userId}`, {});
  }

  revokeSeat(featureKey: string, userId: string): Observable<{ featureKey: string; userId: string; revoked: boolean }> {
    return this.api.delete(`/module-seats/${featureKey}/users/${userId}`);
  }

  bulkGrantSeats(featureKey: string, userIds: string[]): Observable<BulkGrantResult> {
    return this.api.post<BulkGrantResult>(`/module-seats/${featureKey}/users`, { userIds });
  }

  getUserModules(userId: string): Observable<{ userId: string; modules: UserModuleInfo[] }> {
    return this.api.get(`/module-seats/users/${userId}`);
  }
}
