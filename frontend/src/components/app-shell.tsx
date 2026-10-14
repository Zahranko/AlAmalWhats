"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { createContext, useContext, useEffect, useRef, useState, useSyncExternalStore, type ReactNode } from "react";
import { api, hardNavigate, type InboxResult, type Me } from "@/lib/api";

const MeContext = createContext<Me | null>(null);
const LiveContext = createContext(0);

/** The signed-in user. Only usable inside the (app) layout, where it is always loaded. */
export function useMe() {
  const me = useContext(MeContext);
  if (!me) throw new Error("useMe must be used inside AppShell");
  return me;
}

/**
 * Changes whenever messages or conversations change on the server (new message, status update,
 * send). Pages refresh their data when it changes; see the long poll in AppShell.
 */
export function useLiveVersion() {
  return useContext(LiveContext);
}

type NavItem = { href: string; label: string; icon: string };

const nav: NavItem[] = [
  { href: "/inbox", label: "Inbox", icon: "ph-chats-circle" },
  { href: "/", label: "Dashboard", icon: "ph-squares-four" },
  { href: "/customers", label: "Patients", icon: "ph-address-book" },
  { href: "/send", label: "Single Send", icon: "ph-paper-plane-tilt" },
  { href: "/campaigns", label: "Bulk Send", icon: "ph-megaphone-simple" },
  { href: "/history", label: "Message History", icon: "ph-clock-counter-clockwise" },
  { href: "/templates", label: "Message Templates", icon: "ph-note" },
];

const adminNav: NavItem[] = [
  { href: "/users", label: "Users", icon: "ph-users-three" },
  { href: "/settings", label: "WhatsApp Settings", icon: "ph-gear-six" },
  { href: "/audit", label: "Audit Log", icon: "ph-shield-check" },
];

export const adminOnlyPaths = adminNav.map((n) => n.href);

export function AppShell({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const [unread, setUnread] = useState(0);
  const [live, setLive] = useState(0);
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    api<Me>("/auth/me").then(setMe).catch(() => {});
  }, []);

  useEffect(() => {
    if (me?.mustChangePassword && pathname !== "/account") router.replace("/account");
  }, [me, pathname, router]);

  // Long poll: the server answers as soon as anything changes (or after 25 s with no change).
  // A periodic bump also covers changes the server can't announce (and a dropped connection).
  useEffect(() => {
    if (!me || me.mustChangePassword) return;
    const abort = new AbortController();
    let since: number | null = null;
    (async () => {
      while (!abort.signal.aborted) {
        try {
          const r: { version: number } = await api<{ version: number }>(`/inbox/changes${since === null ? "" : `?since=${since}`}`, { signal: abort.signal });
          if (r.version !== since) { since = r.version; setLive((n) => n + 1); }
        } catch {
          if (abort.signal.aborted) return;
          await new Promise((ok) => setTimeout(ok, 5000));
        }
      }
    })();
    const t = setInterval(() => { if (document.visibilityState === "visible") setLive((n) => n + 1); }, 60_000);
    return () => { abort.abort(); clearInterval(t); };
  }, [me]);

  // Badge on the Inbox icon: conversations with unread messages.
  useEffect(() => {
    if (!me || me.mustChangePassword || !live) return;
    api<InboxResult>("/inbox?pageSize=1").then((r) => setUnread(r.counts.unread)).catch(() => {});
  }, [me, live, pathname]);

  if (!me) {
    return (
      <div className="app-loading" role="status" aria-live="polite">
        <span className="spin" aria-hidden="true" /><span className="sr-only">Loading…</span>
      </div>
    );
  }

  const isAdmin = me.role === "admin";
  const blocked = !isAdmin && adminOnlyPaths.some((p) => pathname.startsWith(p));
  const flush = pathname.startsWith("/inbox");

  return (
    <MeContext.Provider value={me}>
      <LiveContext.Provider value={live}>
      <div className="app">
        <nav className="rail" aria-label="Main navigation">
          <Link href="/inbox" className="mark" title="Al Amal Hospital" translate="no">
            <i className="ph-fill ph-heartbeat" aria-hidden="true" />
            <span className="sr-only">Al Amal Hospital</span>
          </Link>
          {nav.map((item) => (
            <RailLink key={item.href} item={item} active={isActive(pathname, item.href)}
              count={item.href === "/inbox" ? unread : 0} />
          ))}
          {isAdmin && (
            <>
              <span className="rail-sep" aria-hidden="true" />
              {adminNav.map((item) => <RailLink key={item.href} item={item} active={isActive(pathname, item.href)} />)}
            </>
          )}
          <div className="rail-grow" />
          <ThemeButton />
          <AccountMenu me={me} />
        </nav>
        {flush && !blocked ? (
          <div className="app-flush view" key={pathname}>{children}</div>
        ) : (
          <main className="app-main">
            <div className="app-main-inner view" key={pathname}>
              {blocked ? (
                <div className="card p-8 text-center">
                  <h1 className="text-lg font-semibold">Admins only</h1>
                  <p className="mt-1 text-sm text-gray-500">You don&apos;t have access to this page.</p>
                </div>
              ) : (
                children
              )}
            </div>
          </main>
        )}
      </div>
      </LiveContext.Provider>
    </MeContext.Provider>
  );
}

function isActive(pathname: string, href: string) {
  return href === "/" ? pathname === "/" : pathname.startsWith(href);
}

function RailLink({ item, active, count = 0 }: { item: NavItem; active: boolean; count?: number }) {
  return (
    <Link href={item.href} className="rail-btn" aria-current={active ? "page" : undefined}
      aria-label={count ? `${item.label}, ${count} unread` : item.label} data-tip={item.label}>
      <i className={`ph ${item.icon}`} aria-hidden="true" />
      {count > 0 && <span className="count num">{count > 99 ? "99+" : count}</span>}
    </Link>
  );
}

// The root layout's inline script sets <html data-theme> before paint; this follows it.
function subscribeTheme(onChange: () => void) {
  const o = new MutationObserver(onChange);
  o.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
  return () => o.disconnect();
}

function ThemeButton() {
  const dark = useSyncExternalStore(subscribeTheme, () => document.documentElement.dataset.theme === "dark", () => false);

  function toggle() {
    const next = dark ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    try { localStorage.setItem("theme", next); } catch {}
  }

  const label = dark ? "Switch to Light Theme" : "Switch to Dark Theme";
  return (
    <button type="button" className="rail-btn" onClick={toggle} aria-label={label} data-tip={label}>
      <i className={`ph ${dark ? "ph-sun" : "ph-moon"}`} aria-hidden="true" />
    </button>
  );
}

function AccountMenu({ me }: { me: Me }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return;
    const onDown = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    const onKey = (e: KeyboardEvent) => { if (e.key === "Escape") setOpen(false); };
    document.addEventListener("mousedown", onDown);
    document.addEventListener("keydown", onKey);
    return () => { document.removeEventListener("mousedown", onDown); document.removeEventListener("keydown", onKey); };
  }, [open]);

  async function logout() {
    await api("/auth/logout", { method: "POST" }).catch(() => {});
    hardNavigate("/login");
  }

  const initials = me.name.trim().split(/\s+/).slice(0, 2).map((w) => w[0]).join("").toUpperCase();
  return (
    <div className="me-wrap" ref={ref}>
      <button type="button" className="me" onClick={() => setOpen(!open)} aria-expanded={open} aria-haspopup="menu"
        aria-label={`Signed in as ${me.name}`} title={me.name}>
        {initials}
      </button>
      {open && (
        <div className="me-menu" role="menu">
          <div className="me-who">
            <strong>{me.name}</strong>
            <span>{me.email}</span>
            <span className="me-role">{me.role === "admin" ? "Admin" : "Employee"}</span>
          </div>
          <Link href="/account" role="menuitem" onClick={() => setOpen(false)}><i className="ph ph-key" aria-hidden="true" />Change Password</Link>
          <button type="button" role="menuitem" onClick={logout}><i className="ph ph-sign-out" aria-hidden="true" />Sign Out</button>
        </div>
      )}
    </div>
  );
}
