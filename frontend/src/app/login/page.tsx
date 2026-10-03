"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, hardNavigate, type Me } from "@/lib/api";
import { ErrorText } from "@/components/ui";

export default function LoginPage() {
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const me = await api<Me>("/auth/login", { method: "POST", body: { email, password }, redirectOn401: false });
      const next = new URLSearchParams(window.location.search).get("next");
      // Only follow local paths, never an absolute URL from the query string.
      const target = me.mustChangePassword ? "/account" : next?.startsWith("/") && !next.startsWith("//") ? next : "/";
      hardNavigate(target);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not reach the server.");
      setBusy(false);
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <div className="card w-full max-w-sm p-8 shadow-sm">
        <div className="mb-6 flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-full bg-brand-600 text-lg font-bold text-white">W</div>
          <div>
            <h1 className="text-lg font-semibold">Messaging Platform</h1>
            <p className="text-sm text-gray-500">Sign in to continue</p>
          </div>
        </div>
        <form onSubmit={onSubmit} className="space-y-4">
          <div>
            <label className="label" htmlFor="email">Email</label>
            <input id="email" type="email" autoComplete="username" required className="input"
              value={email} onChange={(e) => setEmail(e.target.value)} />
          </div>
          <div>
            <label className="label" htmlFor="password">Password</label>
            <input id="password" type="password" autoComplete="current-password" required className="input"
              value={password} onChange={(e) => setPassword(e.target.value)} />
          </div>
          <ErrorText error={error} />
          <button type="submit" className="btn-primary w-full" disabled={busy}>
            {busy ? "Signing in…" : "Sign in"}
          </button>
        </form>
      </div>
    </main>
  );
}
