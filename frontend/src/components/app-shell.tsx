"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { api, hardNavigate, type Me } from "@/lib/api";

const MeContext = createContext<Me | null>(null);

/** The signed-in user. Only usable inside the (app) layout, where it is always loaded. */
export function useMe() {
  const me = useContext(MeContext);
  if (!me) throw new Error("useMe must be used inside AppShell");
  return me;
}

type NavItem = { href: string; label: string; adminOnly?: boolean; phase?: number };

// Screens from the plan; ones with `phase` are not built yet and show as disabled.
const nav: NavItem[] = [
  { href: "/", label: "Dashboard" },
  { href: "/customers", label: "Customers" },
  { href: "/send", label: "Single send" },
  { href: "/campaigns", label: "Bulk send" },
  { href: "/history", label: "Message history" },
  { href: "/templates", label: "Templates" },
];

const adminNav: NavItem[] = [
  { href: "/users", label: "Users", adminOnly: true },
  { href: "/settings", label: "WhatsApp", adminOnly: true },
  { href: "/audit", label: "Audit log", adminOnly: true },
];

export const adminOnlyPaths = adminNav.map((n) => n.href);

export function AppShell({ children }: { children: ReactNode }) {
  const [me, setMe] = useState<Me | null>(null);
  const pathname = usePathname();
  const router = useRouter();

  useEffect(() => {
    api<Me>("/auth/me").then(setMe).catch(() => {});
  }, []);

  useEffect(() => {
    if (me?.mustChangePassword && pathname !== "/account") router.replace("/account");
  }, [me, pathname, router]);

  if (!me) {
    return <div className="flex min-h-screen items-center justify-center text-sm text-gray-500">Loading…</div>;
  }

  const isAdmin = me.role === "admin";
  const blocked = !isAdmin && adminOnlyPaths.some((p) => pathname.startsWith(p));

  async function logout() {
    await api("/auth/logout", { method: "POST" }).catch(() => {});
    hardNavigate("/login");
  }

  return (
    <MeContext.Provider value={me}>
      <div className="flex min-h-screen">
        <aside className="flex w-60 shrink-0 flex-col bg-brand-800 text-white">
          <div className="flex items-center gap-2 px-5 py-5">
            <div className="flex h-8 w-8 items-center justify-center rounded-full bg-white/15 font-bold">W</div>
            <span className="font-semibold">Messaging</span>
          </div>
          <nav className="flex-1 space-y-1 px-3" aria-label="Main">
            {nav.map((item) => <NavLink key={item.href} item={item} active={isActive(pathname, item.href)} />)}
            {isAdmin && (
              <>
                <p className="px-3 pt-5 pb-1 text-xs font-semibold uppercase tracking-wide text-white/50">Admin</p>
                {adminNav.map((item) => <NavLink key={item.href} item={item} active={isActive(pathname, item.href)} />)}
              </>
            )}
          </nav>
          <div className="border-t border-white/10 p-4 text-sm">
            <p className="truncate font-medium">{me.name}</p>
            <p className="truncate text-white/60">{me.email}</p>
            <p className="mt-1 text-xs uppercase tracking-wide text-white/50">{me.role}</p>
            <div className="mt-3 flex gap-3">
              <Link href="/account" className="text-white/80 hover:text-white">Password</Link>
              <button onClick={logout} className="text-white/80 hover:text-white">Sign out</button>
            </div>
          </div>
        </aside>
        <main className="min-w-0 flex-1 p-8">
          {blocked ? (
            <div className="card p-8 text-center">
              <h1 className="text-lg font-semibold">Admins only</h1>
              <p className="mt-1 text-sm text-gray-500">You don&apos;t have access to this page.</p>
            </div>
          ) : (
            children
          )}
        </main>
      </div>
    </MeContext.Provider>
  );
}

function isActive(pathname: string, href: string) {
  return href === "/" ? pathname === "/" : pathname.startsWith(href);
}

function NavLink({ item, active }: { item: NavItem; active: boolean }) {
  if (item.phase) {
    return (
      <span className="flex cursor-not-allowed items-center justify-between rounded-md px-3 py-2 text-sm text-white/40"
        title={`Coming in phase ${item.phase}`}>
        {item.label}
        <span className="rounded bg-white/10 px-1.5 text-[10px]">P{item.phase}</span>
      </span>
    );
  }
  return (
    <Link href={item.href}
      className={`block rounded-md px-3 py-2 text-sm ${active ? "bg-white/15 font-medium" : "text-white/85 hover:bg-white/10"}`}>
      {item.label}
    </Link>
  );
}
