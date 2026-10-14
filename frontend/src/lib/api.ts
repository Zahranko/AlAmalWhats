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

export interface ButtonParam { index: number; subType: string; text: string }

export interface Template {
  id: number;
  name: string;
  language: string;
  category: string;
  status: string;
  headerFormat: string | null;
  headerText: string | null;
  headerParams: string[];
  bodyText: string | null;
  bodyParams: string[];
  footerText: string | null;
  buttons: { type: string; text: string }[];
  buttonParams: ButtonParam[];
  syncedAt: string;
}

export interface Customer {
  id: number;
  name: string;
  phone: string;
  fileNumber: string | null;
  notes: string | null;
  optedOut: boolean;
  optedOutAt: string | null;
  lastInboundAt: string | null;
  lastMessageAt: string | null;
  /** Staff have read the conversation up to this message time. */
  lastReadAt: string | null;
  canReply: boolean;
  createdAt: string;
}

export interface MediaFile { id: number; fileName: string; contentType: string; size: number; kind: string }

export type MessageStatus = "queued" | "sending" | "sent" | "delivered" | "read" | "failed" | "cancelled" | "received";

export interface Message {
  id: number;
  direction: "in" | "out";
  customerId: number | null;
  customerName: string | null;
  phone: string;
  type: string;
  templateName: string | null;
  language: string | null;
  body: string | null;
  mediaFileName: string | null;
  hasMedia: boolean;
  status: MessageStatus;
  error: string | null;
  source: string;
  campaignId: number | null;
  campaignName: string | null;
  sentBy: string | null;
  createdAt: string;
  scheduledAt: string | null;
  sentAt: string | null;
  deliveredAt: string | null;
  readAt: string | null;
  failedAt: string | null;
}

export interface StatusCounts { queued: number; sending: number; sent: number; delivered: number; read: number; failed: number; cancelled: number }

export interface Campaign {
  id: number;
  name: string;
  templateId: number;
  templateName: string | null;
  templateLanguage: string | null;
  status: "scheduled" | "sending" | "completed" | "cancelled";
  scheduledAt: string | null;
  totalRecipients: number;
  createdBy: string | null;
  createdAt: string;
  completedAt: string | null;
  counts: StatusCounts;
}

export interface InboxItem {
  customer: Customer;
  last: {
    direction: "in" | "out";
    type: string;
    templateName: string | null;
    body: string | null;
    mediaFileName: string | null;
    status: MessageStatus;
    createdAt: string;
  } | null;
  /** The customer's messages since our last message to them. */
  unanswered: number;
  /** The customer's messages nobody has opened the conversation to read yet. */
  unread: number;
}

export interface InboxResult {
  items: InboxItem[];
  total: number;
  page: number;
  pageSize: number;
  counts: { all: number; unanswered: number; open: number; optedOut: number; unread: number };
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export class ApiError extends Error {
  constructor(message: string, public status: number, public code?: string, public data?: unknown) {
    super(message);
  }
}

type Options = { method?: string; body?: unknown; redirectOn401?: boolean; signal?: AbortSignal };

export async function api<T = void>(path: string, { method = "GET", body, redirectOn401 = true, signal }: Options = {}): Promise<T> {
  const res = await withSession(redirectOn401, () => fetch(`/api${path}`, {
    method,
    headers: body === undefined ? undefined : { "Content-Type": "application/json" },
    body: body === undefined ? undefined : JSON.stringify(body),
    credentials: "same-origin",
    signal,
  }));

  if (res.ok) {
    return (res.status === 204 ? undefined : await res.json()) as T;
  }

  const data = await res.json().catch(() => null);

  if (res.status === 403 && data?.code === "password_change_required" && window.location.pathname !== "/account") {
    hardNavigate("/account");
  }

  throw new ApiError(errorMessage(res.status, data), res.status, data?.code, data);
}

/** Multipart upload (files etc.); same error handling as api(). */
export async function upload<T>(path: string, form: FormData): Promise<T> {
  const res = await withSession(true, () => fetch(`/api${path}`, { method: "POST", body: form, credentials: "same-origin" }));
  if (res.ok) return (await res.json()) as T;
  const data = await res.json().catch(() => null);
  if (res.status === 413) throw new ApiError("The files are too large to upload at once.", 413);
  throw new ApiError(errorMessage(res.status, data), res.status, data?.code, data);
}

/**
 * Runs a request; if it answers 401 because the 15-minute access token ran out, renews the
 * session with the refresh token (good for 14 days) and runs it once more. Only when the session
 * itself is over does the user go to the login page.
 */
async function withSession(redirectOn401: boolean, send: () => Promise<Response>): Promise<Response> {
  const res = await send();
  if (res.status !== 401 || !redirectOn401) return res;

  const renewed = await refreshSession();
  if (renewed === "renewed") {
    const again = await send();
    if (again.status !== 401) return again;
  } else if (renewed === "unavailable") {
    throw new ApiError("Can't reach the server right now. Check the connection and try again.", 503);
  }
  hardNavigate(`/login?next=${encodeURIComponent(window.location.pathname)}`);
  return res;
}

let refreshing: Promise<"renewed" | "ended" | "unavailable"> | null = null;

/**
 * One renewal at a time: requests that fail together share it, and the browser-wide lock keeps
 * two tabs from spending the same refresh token (each new token replaces the last; the server
 * treats an old one coming back as stolen and ends the session).
 */
function refreshSession() {
  refreshing ??= (async () => {
    const exchange = async () => {
      try {
        const r = await fetch("/api/auth/refresh", { method: "POST", credentials: "same-origin" });
        // Only a busy or unreachable server leaves the session alone; anything else means sign in again.
        return r.ok ? "renewed" as const : r.status === 429 || r.status >= 500 ? "unavailable" as const : "ended" as const;
      } catch {
        return "unavailable" as const;
      }
    };
    try {
      return navigator.locks ? await navigator.locks.request("wa-session-refresh", exchange) : await exchange();
    } finally {
      refreshing = null;
    }
  })();
  return refreshing;
}

/** Query string from an object, skipping empty values. */
export function qs(params: Record<string, string | number | boolean | null | undefined>) {
  const p = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) if (v !== undefined && v !== null && v !== "") p.set(k, String(v));
  const s = p.toString();
  return s ? `?${s}` : "";
}

export function formatSize(bytes: number) {
  return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
}

/** Shows a phone in international form: 962791234567 → +962 79 123 4567. */
export function formatPhone(phone: string) {
  return phone.startsWith("962") && phone.length === 12
    ? `+962 ${phone.slice(3, 5)} ${phone.slice(5, 8)} ${phone.slice(8)}`
    : `+${phone}`;
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
