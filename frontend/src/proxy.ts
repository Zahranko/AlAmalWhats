import { NextResponse } from "next/server";
import type { NextRequest } from "next/server";

export function proxy(request: NextRequest) {
  if (request.nextUrl.pathname.startsWith("/api/")) return forwardToApi(request);

  // Optimistic check only: send visitors without a session cookie to /login.
  // Real authentication and role checks happen in the backend on every API call.
  if (!request.cookies.has("wa_session")) {
    const url = new URL("/login", request.url);
    if (request.nextUrl.pathname !== "/") url.searchParams.set("next", request.nextUrl.pathname);
    return NextResponse.redirect(url);
  }
}

/**
 * /api/* is rewritten to the backend (next.config.ts), which would otherwise only ever see this
 * server's IP. Pass the visitor's IP along with a shared secret the backend checks, so its audit
 * log and login rate limit apply per user. Headers a client sends itself are always dropped.
 */
function forwardToApi(request: NextRequest) {
  const headers = new Headers(request.headers);
  headers.delete("x-wa-proxy-secret");
  headers.delete("x-wa-client-ip");

  const secret = process.env.PROXY_SECRET;
  // The last X-Forwarded-For entry is the one added by the hosting provider's own proxy.
  const ip = request.headers.get("x-forwarded-for")?.split(",").at(-1)?.trim() || request.headers.get("x-real-ip");
  if (secret && ip) {
    headers.set("x-wa-proxy-secret", secret);
    headers.set("x-wa-client-ip", ip);
  }
  return NextResponse.next({ request: { headers } });
}

export const config = {
  matcher: ["/((?!login|_next/static|_next/image|favicon.ico|.*\.svg$).*)"],
};
