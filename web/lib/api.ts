const API = process.env.NEXT_PUBLIC_API_BASE ?? "";

export interface UserProfile {
  username: string;
  fullName: string;
  role: string;
}

export const TOKEN_KEY = "kiosk_token";
export const USER_KEY = "kiosk_user";

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...authHeaders(),
      ...(init?.headers as Record<string, string> | undefined),
    },
  });
  if (!res.ok) {
    let message = `HTTP ${res.status}`;
    try {
      const body = (await res.json()) as { error?: string };
      if (body.error) message = body.error;
    } catch {
      // non-JSON error body — keep the HTTP status message
    }
    throw new Error(message);
  }
  return (await res.json()) as T;
}

export function authHeaders(): Record<string, string> {
  if (typeof window === "undefined") return {};
  const token = sessionStorage.getItem(TOKEN_KEY);
  return token ? { Authorization: `Bearer ${token}` } : {};
}

export async function login(username: string, password: string): Promise<UserProfile> {
  const data = await request<{ token: string; user: UserProfile }>("/api/auth/login", {
    method: "POST",
    body: JSON.stringify({ username, password }),
  });
  sessionStorage.setItem(TOKEN_KEY, data.token);
  sessionStorage.setItem(USER_KEY, JSON.stringify(data.user));
  return data.user;
}

export function logout(): void {
  sessionStorage.removeItem(TOKEN_KEY);
  sessionStorage.removeItem(USER_KEY);
}

export function isLoggedIn(): boolean {
  if (typeof window === "undefined") return false;
  return sessionStorage.getItem(TOKEN_KEY) !== null;
}

export type ScanResponse =
  | { result: "PASS"; unitId: number; transactionId: number; nextStationCode?: string }
  | { result: "REJECTED"; reason: string };

/**
 * POST /api/production/scan. The backend answers 200 PASS or 422 REJECTED
 * (both are typed results); any other failure throws like the rest of the API.
 */
export async function scan(serialNumber: string, stationId: number): Promise<ScanResponse> {
  const res = await fetch(`${API}/api/production/scan`, {
    method: "POST",
    headers: { "Content-Type": "application/json", ...authHeaders() },
    body: JSON.stringify({ serialNumber, stationId }),
  });
  if (res.status === 422) {
    const body = (await res.json()) as { result: "REJECTED"; reason: string };
    return { result: "REJECTED", reason: body.reason ?? "rejected" };
  }
  if (!res.ok) {
    let message = `HTTP ${res.status}`;
    try {
      const body = (await res.json()) as { error?: string };
      if (body.error) message = body.error;
    } catch {
      // non-JSON error body — keep the HTTP status message
    }
    throw new Error(message);
  }
  return (await res.json()) as ScanResponse;
}

export interface StationSummary {
  stationId: number;
  stationCode: string;
  output: number;
  ng: number;
  yieldPercent: number | null;
}

export async function getStationSummary(stationId: number, date: string): Promise<StationSummary> {
  return request<StationSummary>(`/api/reports/station-summary?stationId=${stationId}&date=${date}`);
}

export interface NgListItem {
  sn: string;
  stationCode: string;
  ngCode: string | null;
  notes: string | null;
  checkedAtUtc: string;
}

export interface NgListResponse {
  total: number;
  page: number;
  items: NgListItem[];
}

export async function getNgList(params: {
  date: string;
  stationId?: number;
  page?: number;
  pageSize?: number;
}): Promise<NgListResponse> {
  const query = new URLSearchParams({ date: params.date });
  if (params.stationId !== undefined) query.set("stationId", String(params.stationId));
  if (params.page !== undefined) query.set("page", String(params.page));
  if (params.pageSize !== undefined) query.set("pageSize", String(params.pageSize));
  return request<NgListResponse>(`/api/reports/ng-list?${query.toString()}`);
}

export interface LineGridStation {
  stationId: number;
  stationCode: string;
  name: string;
  outputToday: number;
  ngToday: number;
  lastEventAtUtc: string | null;
  status: string;
}
export interface LineGridLine {
  lineCode: string;
  stations: LineGridStation[];
}
export interface LineGridDto {
  lines: LineGridLine[];
}
export async function getLineGrid(): Promise<LineGridDto> {
  return request<LineGridDto>("/api/reports/line-grid");
}

export interface ThresholdsDto {
  minYieldPercent: number;
  yieldDropPercent: number;
  ngSpikePerHour: number;
  evaluationIntervalMinutes: number;
}
export async function getThresholds(): Promise<ThresholdsDto> {
  return request<ThresholdsDto>("/api/insights/thresholds");
}
