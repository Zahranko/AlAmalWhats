export type Role = "admin" | "employee";

export interface Me {
  id: number;
  name: string;
  email: string;
  role: Role;
  mustChangePassword: boolean;
}

export interface UserRow {
  id: number;
  name: string;
  email: string;
  role: Role;
  isActive: boolean;
  isLocked: boolean;
  mustChangePassword: boolean;
  lastLoginAt: string | null;
  createdAt: string;
}

export interface AuditEntry {
  id: number;
  userId: number | null;
  userEmail: string | null;
  action: string;
  target: string | null;
  details: string | null;
  ip: string | null;
  createdAt: string;
}

export interface WhatsAppStatus {
  configured: boolean;
  missing: string[];
  phoneNumber: { id: string; displayPhoneNumber: string | null; verifiedName: string | null; qualityRating: string | null } | null;
  error: string | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export class ApiError extends Error {
  constructor(message: string, public status: number, public code?: string) {
    super(message);
  }
}

type Options = { method?: string; body?: unknown; redirectOn401?: boolean };

export async function api<T = void>(path: string, { method = "GET", body, redirectOn401 = true }: Options = {}): Promise<T> {
  const res = await fetch(`/api${path}`, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: "same-origin",
  });

  if (res.ok) {
    return (res.status === 204 ? undefined : await res.json()) as T;
  }

  const data = await res.json().catch(() => null);

  if (res.status === 401 && redirectOn401) {
    hardNavigate(`/login?next=${encodeURIComponent(window.location.pathname)}`);
  }
  if (res.status === 403 && data?.code === "password_change_required" && window.location.pathname !== "/account") {
    hardNavigate("/account");
  }

  throw new ApiError(errorMessage(res.status, data), res.status, data?.code);
}

function errorMessage(status: number, data: { error?: string; title?: string; errors?: Record<string, string[]> } | null) {
  if (data?.error) return data.error;
  if (data?.errors) return Object.values(data.errors).flat().join(" ");
  if (status === 403) return "You don't have permission to do this.";
  if (status === 429) return "Too many attempts. Wait a minute and try again.";
  return data?.title ?? `Request failed (${status}).`;
}

export function formatDate(iso: string | null) {
  return iso ? new Date(iso).toLocaleString("en-GB", { dateStyle: "medium", timeStyle: "short" }) : "—";
}

/**
 * Full page load instead of client-side routing. Used after login, logout, password changes
 * and auth failures, so no state from the previous session survives in memory.
 */
export function hardNavigate(path: string) {
  window.location.href = path;
}
