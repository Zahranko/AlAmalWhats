"use client";

import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError, formatDate, type WhatsAppStatus } from "@/lib/api";
import { Badge, ErrorText, PageHeader } from "@/components/ui";

export default function WhatsAppSettingsPage() {
  const [status, setStatus] = useState<WhatsAppStatus | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    api<WhatsAppStatus>("/whatsapp/status")
      .then(setStatus)
      .catch((err) => setLoadError(err instanceof ApiError ? err.message : "Could not load the status."));
  }, [reload]);

  function refresh() {
    setStatus(null);
    setLoadError(null);
    setReload((n) => n + 1);
  }

  return (
    <>
      <PageHeader title="WhatsApp" description="Connection to Meta's WhatsApp Cloud API."
        actions={<button className="btn-secondary" onClick={refresh}>Refresh</button>} />
      <div className="grid max-w-3xl gap-6">
        <StatusCard status={status} error={loadError} />
        <TestSend disabled={!status?.phoneNumber} />
        <PricingCard />
        <ApiKeysCard />
      </div>
    </>
  );
}

function StatusCard({ status, error }: { status: WhatsAppStatus | null; error: string | null }) {
  if (error) return <div className="card p-6"><ErrorText error={error} /></div>;
  if (!status) return <div className="card p-6 text-sm text-gray-500">Checking connection…</div>;

  const phone = status.phoneNumber;
  return (
    <div className="card p-6">
      <div className="flex items-center justify-between gap-4">
        <h2 className="font-semibold">Connection</h2>
        {status.configured
          ? <Badge tone="green">Connected</Badge>
          : phone ? <Badge tone="amber">Incomplete</Badge> : <Badge tone="red">Not connected</Badge>}
      </div>
      {phone && (
        <dl className="mt-4 grid grid-cols-[auto_1fr] gap-x-6 gap-y-2 text-sm">
          <dt className="text-gray-500">Number</dt><dd className="font-medium">{phone.displayPhoneNumber ?? "—"}</dd>
          <dt className="text-gray-500">Display name</dt><dd>{phone.verifiedName ?? "—"}</dd>
          <dt className="text-gray-500">Quality rating</dt><dd>{phone.qualityRating ?? "—"}</dd>
          <dt className="text-gray-500">Phone number ID</dt><dd className="font-mono text-xs leading-5">{phone.id}</dd>
        </dl>
      )}
      {status.error && <div className="mt-4"><ErrorText error={`Meta: ${status.error}`} /></div>}
      {status.missing.length > 0 && (
        <p className="mt-4 rounded-md bg-amber-50 px-3 py-2 text-sm text-amber-800">
          Missing server settings: {status.missing.join(", ")}. They are set on the API server, not here.
        </p>
      )}
    </div>
  );
}

function TestSend({ disabled }: { disabled: boolean }) {
  const [to, setTo] = useState("");
  const [template, setTemplate] = useState("hello_world");
  const [language, setLanguage] = useState("en_US");
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setResult(null);
    try {
      const res = await api<{ messageId: string }>("/whatsapp/test-message", {
        method: "POST",
        body: { to: toInternational(to), template: template.trim(), language: language.trim() },
      });
      setResult(res.messageId);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="card space-y-4 p-6">
      <div>
        <h2 className="font-semibold">Send a test message</h2>
        <p className="mt-1 text-sm text-gray-500">
          Sends an approved template that has no variables. The template must exist in WhatsApp Manager.
        </p>
      </div>
      <div className="grid gap-4 sm:grid-cols-3">
        <div>
          <label className="label" htmlFor="to">Phone number</label>
          <input id="to" required inputMode="tel" placeholder="0791234567" className="input"
            value={to} onChange={(e) => setTo(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="template">Template name</label>
          <input id="template" required pattern="[a-z0-9_]+" className="input"
            value={template} onChange={(e) => setTemplate(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="language">Language</label>
          <input id="language" required pattern="[a-z]{2,3}(_[A-Z]{2})?" placeholder="ar, en_US" className="input"
            value={language} onChange={(e) => setLanguage(e.target.value)} />
        </div>
      </div>
      <p className="text-xs text-gray-500">Local Jordanian numbers (07…) are converted to +962 automatically.</p>
      <ErrorText error={error} />
      {result && (
        <p role="status" className="rounded-md bg-green-50 px-3 py-2 text-sm text-green-800">
          Sent. Message ID <span className="break-all font-mono text-xs">{result}</span>
        </p>
      )}
      <button type="submit" className="btn-primary" disabled={busy || disabled}>{busy ? "Sending…" : "Send test"}</button>
    </form>
  );
}

/** 0791234567 → 962791234567; numbers already in international form are kept (without + and spaces). */
function toInternational(raw: string) {
  const digits = raw.replace(/[^\d]/g, "");
  if (digits.startsWith("00")) return digits.slice(2);
  if (digits.startsWith("07") && digits.length === 10) return `962${digits.slice(1)}`;
  return digits;
}

interface Pricing { currency: string; marketing: number; utility: number; authentication: number; service: number }

function PricingCard() {
  const [p, setP] = useState<Pricing | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  useEffect(() => {
    api<Pricing>("/settings/pricing").then(setP).catch((e) => setError(e.message));
  }, []);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setSaved(false);
    try {
      setP(await api<Pricing>("/settings/pricing", { method: "PUT", body: p }));
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not save.");
    }
  }

  if (!p) return <div className="card p-6 text-sm text-gray-500">{error ?? "Loading prices…"}</div>;
  const field = (k: keyof Omit<Pricing, "currency">, label: string) => (
    <div key={k}>
      <label className="label" htmlFor={`price-${k}`}>{label}</label>
      <input id={`price-${k}`} type="number" min={0} step="0.0001" className="input" value={p[k]}
        onChange={(e) => setP({ ...p, [k]: Number(e.target.value) })} />
    </div>
  );
  return (
    <form onSubmit={onSubmit} className="card space-y-4 p-6">
      <div>
        <h2 className="font-semibold">Message prices</h2>
        <p className="mt-1 text-sm text-gray-500">
          Price per billable message for Jordanian numbers, from Meta&apos;s WhatsApp rate card. Used for the dashboard&apos;s cost estimate.
        </p>
      </div>
      <div className="grid gap-4 sm:grid-cols-5">
        <div>
          <label className="label" htmlFor="price-currency">Currency</label>
          <input id="price-currency" className="input" maxLength={8} value={p.currency} onChange={(e) => setP({ ...p, currency: e.target.value })} />
        </div>
        {field("utility", "Utility")}
        {field("marketing", "Marketing")}
        {field("authentication", "Authentication")}
        {field("service", "Service")}
      </div>
      <ErrorText error={error} />
      {saved && <p role="status" className="text-sm text-green-700">Saved.</p>}
      <button type="submit" className="btn-primary">Save prices</button>
    </form>
  );
}

interface ApiKey { id: number; name: string; prefix: string; createdAt: string; lastUsedAt: string | null; revokedAt: string | null }

function ApiKeysCard() {
  const [keys, setKeys] = useState<ApiKey[]>([]);
  const [name, setName] = useState("");
  const [secret, setSecret] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    api<ApiKey[]>("/api-keys").then(setKeys).catch((e) => setError(e.message));
  }, [reload]);

  async function create(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      const r = await api<{ secret: string }>("/api-keys", { method: "POST", body: { name } });
      setSecret(r.secret);
      setName("");
      setReload((n) => n + 1);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not create the key.");
    }
  }

  async function revoke(id: number) {
    try {
      await api(`/api-keys/${id}`, { method: "DELETE" });
      setReload((n) => n + 1);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not revoke the key.");
    }
  }

  return (
    <div className="card space-y-4 p-6">
      <div>
        <h2 className="font-semibold">API keys</h2>
        <p className="mt-1 text-sm text-gray-500">
          Let the hospital&apos;s other systems (appointments, lab) send templates automatically through <code>/api/v1</code>.
        </p>
      </div>
      {secret && (
        <div className="rounded-md bg-amber-50 p-3 text-sm text-amber-900">
          <p className="font-medium">Copy this key now. It won&apos;t be shown again.</p>
          <code className="mt-1 block break-all rounded bg-white px-2 py-1 font-mono text-xs">{secret}</code>
          <button className="mt-2 text-xs text-amber-900 underline" onClick={() => setSecret(null)}>I&apos;ve stored it safely</button>
        </div>
      )}
      <form onSubmit={create} className="flex gap-2">
        <input className="input max-w-xs" required maxLength={200} placeholder="Which system? e.g. Appointments"
          value={name} onChange={(e) => setName(e.target.value)} aria-label="Key name" />
        <button className="btn-primary">Create key</button>
      </form>
      <ErrorText error={error} />
      {keys.length > 0 && (
        <table className="w-full text-sm">
          <thead className="text-left text-gray-500">
            <tr><th className="pb-2 font-medium">Name</th><th className="pb-2 font-medium">Key</th><th className="pb-2 font-medium">Last used</th><th></th></tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {keys.map((k) => (
              <tr key={k.id} className={k.revokedAt ? "text-gray-400" : ""}>
                <td className="py-2">{k.name}</td>
                <td className="py-2 font-mono text-xs">{k.prefix}…</td>
                <td className="py-2">{formatDate(k.lastUsedAt)}</td>
                <td className="py-2 text-right">
                  {k.revokedAt ? `revoked ${formatDate(k.revokedAt)}` : <button className="text-red-700 hover:underline" onClick={() => revoke(k.id)}>Revoke</button>}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      <details className="text-sm">
        <summary className="cursor-pointer text-brand-700">How to call the API</summary>
        <pre className="mt-2 overflow-x-auto rounded-md bg-gray-900 p-3 text-xs text-gray-100">{`POST https://whatsappapi.alamalhospitaljo.com/api/v1/messages
X-Api-Key: wak_...
Content-Type: application/json

{
  "phone": "0791234567",
  "name": "Patient name",
  "template": "lab_result",
  "language": "ar",
  "body": ["Patient name", "CBC"],
  "document": { "fileName": "result.pdf", "contentBase64": "JVBERi0..." },
  "scheduledAt": null
}

→ 202 { "id": 123, "status": "queued", ... }
GET /api/v1/messages/123   → current status
GET /api/v1/templates      → approved templates and their variables`}</pre>
      </details>
    </div>
  );
}
