// Mirrors the backend DTOs (backend/src/Claims.Api/Features/Dtos.cs). The backend owns the
// contract and publishes it as OpenAPI at /openapi/v1.json; next step would be generating
// these types from that document instead of maintaining them by hand.

export type Role = 'Claimant' | 'Handler';
export type ClaimType = 'Motor' | 'Property';
export type ClaimStatus =
  | 'Submitted'
  | 'UnderAssessment'
  | 'AwaitingInformation'
  | 'Approved'
  | 'Settled'
  | 'Rejected';

export interface User {
  id: string;
  username: string;
  name: string;
  role: Role;
}

export interface LoginResponse {
  token: string;
  user: User;
}

export interface Market {
  code: string;
  name: string;
  currency: string;
}

export interface PagedResult<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ClaimSummary {
  id: string;
  reference: string;
  type: ClaimType;
  marketCode: string;
  currency: string;
  status: ClaimStatus;
  claimantName: string;
  assigneeId: string | null;
  assigneeName: string | null;
  claimedAmount: number | null;
  reserveAmount: number | null;
  openInformationRequests: number;
  submittedAt: string;
  updatedAt: string;
  version: number;
}

export interface InformationRequest {
  id: string;
  question: string;
  requestedByName: string;
  requestedAt: string;
  status: 'Open' | 'Answered';
  response: string | null;
  respondedAt: string | null;
}

export interface HistoryEntry {
  version: number;
  eventType: string;
  actorName: string;
  occurredAt: string;
  detail: string | null;
}

export interface ClaimDetail {
  id: string;
  reference: string;
  type: ClaimType;
  marketCode: string;
  currency: string;
  status: ClaimStatus;
  claimantId: string;
  claimantName: string;
  assigneeId: string | null;
  assigneeName: string | null;
  incidentDate: string;
  incidentLocation: string;
  description: string;
  claimedAmount: number | null;
  reserveAmount: number | null;
  settlementAmount: number | null;
  decisionReason: string | null;
  submittedAt: string;
  updatedAt: string;
  closedAt: string | null;
  version: number;
  informationRequests: InformationRequest[];
  history: HistoryEntry[];
}

export interface SubmitClaimRequest {
  type: ClaimType;
  marketCode: string;
  incidentDate: string;
  incidentLocation: string;
  description: string;
  claimedAmount: number | null;
}

export interface Notification {
  id: number;
  claimId: string;
  claimReference: string;
  message: string;
  createdAt: string;
  read: boolean;
}

export interface HandlerWorkload {
  handlerId: string;
  handlerName: string;
  open: number;
  underAssessment: number;
  awaitingInformation: number;
  approved: number;
  oldestOpenAgeDays: number | null;
  closedLast30Days: number;
  avgDaysToCloseLast30Days: number | null;
}

export interface Workload {
  asOf: string | null;
  queue: { unassigned: number; oldestUnassignedAgeDays: number | null };
  handlers: HandlerWorkload[];
}

export interface MarketExposure {
  marketCode: string;
  marketName: string;
  currency: string;
  openClaims: number;
  reserve: number;
  reserveInReportingCurrency: number;
  byType: { type: ClaimType; openClaims: number; reserve: number }[];
}

export interface Exposure {
  asOf: string | null;
  reportingCurrency: string;
  fxNote: string;
  openClaims: number;
  totalReserveInReportingCurrency: number;
  markets: MarketExposure[];
  byStatus: { status: ClaimStatus; openClaims: number; reserveInReportingCurrency: number }[];
}

/** RFC 7807 problem details returned by the API on 4xx/5xx. */
export interface Problem {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
