import type { NextConfig } from "next";

// The browser only ever talks to Next.js; /api/* is forwarded to the ASP.NET Core backend,
// so the session cookie is same-origin (HttpOnly, SameSite=Strict) and no CORS is needed.
// Rewrites are fixed at build time, so the production default lives here; API_URL overrides it.
const apiUrl =
  process.env.API_URL ??
  (process.env.NODE_ENV === "production" ? "https://whatsappapi.alamalhospitaljo.com" : "http://localhost:5075");

const nextConfig: NextConfig = {
  async rewrites() {
    return [{ source: "/api/:path*", destination: `${apiUrl}/api/:path*` }];
  },
};

export default nextConfig;
