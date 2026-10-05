import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from './api.service';
import { ApiResponse } from '../models/api.model';

export type ApprovalStatus = 'Pending' | 'Approved' | 'Rejected' | 'Cancelled';
export interface ApprovalRequest { id: string; organizationId: string; storeId: string; storeName: string; requestedByUserId: string; requestedBy: string; entityType: string; entityId: string; approvalType: string; status: ApprovalStatus; approvedByUserId?: string; approvedAt?: string; rejectedAt?: string; rejectionReason?: string; createdAt: string; }
export interface CreateApprovalRequest { entityType: string; entityId: string; approvalType: string; storeId: string; }

@Injectable({ providedIn: 'root' })
export class ApprovalService {
  constructor(private readonly api: ApiService) {}
  mine(): Observable<ApiResponse<ApprovalRequest[]>> { return this.api.get<ApprovalRequest[]>('/approval-requests/mine'); }
  pending(): Observable<ApiResponse<ApprovalRequest[]>> { return this.api.get<ApprovalRequest[]>('/approval-requests/pending'); }
  create(request: CreateApprovalRequest): Observable<ApiResponse<ApprovalRequest>> { return this.api.post<ApprovalRequest>('/approval-requests', request); }
  approve(id: string): Observable<ApiResponse<ApprovalRequest>> { return this.api.post<ApprovalRequest>(`/approval-requests/${id}/approve`); }
  reject(id: string, rejectionReason: string): Observable<ApiResponse<ApprovalRequest>> { return this.api.post<ApprovalRequest>(`/approval-requests/${id}/reject`, { rejectionReason }); }
}