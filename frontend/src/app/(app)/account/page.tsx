"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, hardNavigate } from "@/lib/api";
import { useMe } from "@/components/app-shell";
import { ErrorText, PageHeader } from "@/components/ui";

export default function AccountPage() {
  const me = useMe();
  const [current, setCurrent] = useState("");
  const [next, setNext] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (next !== confirm) return setError("The new passwords don't match.");
    setBusy(true);
    setError(null);
    try {
      await api("/auth/change-password", { method: "POST", body: { currentPassword: current, newPassword: next } });
      // Full reload so the shell picks up the cleared "must change password" flag.
      hardNavigate("/");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
      setBusy(false);
    }
  }

  return (
    <>
      <PageHeader title="Change password" description={me.email} />
      {me.mustChangePassword && (
        <p className="mb-4 max-w-md rounded-md bg-amber-50 px-4 py-3 text-sm text-amber-800">
          Your password was set by an admin. Choose your own password to continue.
        </p>
      )}
      <form onSubmit={onSubmit} className="card max-w-md space-y-4 p-6">
        <div>
          <label className="label" htmlFor="current">Current password</label>
          <input id="current" type="password" autoComplete="current-password" required className="input"
            value={current} onChange={(e) => setCurrent(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="new">New password</label>
          <input id="new" type="password" autoComplete="new-password" required minLength={8} className="input"
            value={next} onChange={(e) => setNext(e.target.value)} />
          <p className="mt-1 text-xs text-gray-500">At least 8 characters, with letters and numbers.</p>
        </div>
        <div>
          <label className="label" htmlFor="confirm">Confirm new password</label>
          <input id="confirm" type="password" autoComplete="new-password" required className="input"
            value={confirm} onChange={(e) => setConfirm(e.target.value)} />
        </div>
        <ErrorText error={error} />
        <button type="submit" className="btn-primary" disabled={busy}>{busy ? "Saving…" : "Change password"}</button>
      </form>
    </>
  );
}
