"use client";

import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError, type WhatsAppStatus } from "@/lib/api";
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
