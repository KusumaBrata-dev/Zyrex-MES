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

export async function scan(serialNumber: string, stationId: number) {
  return request<{ result: string; reason?: string }>("/api/production/scan", {
    method: "POST",
    body: JSON.stringify({ serialNumber, stationId }),
  });
}

export async function getStationSummary(stationId: number, date: string) {
  return request<{
    stationId: number;
    stationCode: string;
    output: number;
    ng: number;
    yieldPercent: number | null;
  }>(`/api/reports/station-summary?stationId=${stationId}&date=${date}`);
}
