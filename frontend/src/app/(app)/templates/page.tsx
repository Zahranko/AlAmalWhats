"use client";

import { useEffect, useState } from "react";
import { api, ApiError, formatDate, type Template } from "@/lib/api";
import { useMe } from "@/components/app-shell";
import { Badge, ErrorText, PageHeader } from "@/components/ui";
import { TemplatePreview, needsMedia } from "@/components/messaging";

const statusTone = (s: string) => (s === "APPROVED" ? "green" : s === "REJECTED" || s === "DISABLED" ? "red" : "amber") as "green" | "red" | "amber";

export default function TemplatesPage() {
  const me = useMe();
  const [templates, setTemplates] = useState<Template[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    api<Template[]>("/templates").then(setTemplates).catch((e) => setError(e.message));
  }, [reload]);

  async function sync() {
    setSyncing(true);
    setError(null);
    setNotice(null);
    try {
      const r = await api<{ added: number; updated: number; removed: number }>("/templates/sync", { method: "POST" });
      setNotice(`Synced from WhatsApp Manager: ${r.added} added, ${r.updated} updated, ${r.removed} removed.`);
      setReload((n) => n + 1);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Sync failed.");
    } finally {
      setSyncing(false);
    }
  }

  return (
    <>
      <PageHeader title="Templates"
        description="Approved message templates from WhatsApp Manager. Create and edit them there; they sync here every hour."
        actions={me.role === "admin" && <button className="btn-primary" onClick={sync} disabled={syncing}>{syncing ? "Syncing…" : "Sync now"}</button>} />
      <ErrorText error={error} />
      {notice && <p role="status" className="mb-4 rounded-md bg-green-50 px-3 py-2 text-sm text-green-800">{notice}</p>}
      {templates === null ? (
        <p className="text-sm text-gray-500">Loading…</p>
      ) : templates.length === 0 ? (
        <div className="card p-8 text-center text-sm text-gray-500">
          No templates yet. Create them in WhatsApp Manager → Message templates, then sync.
        </div>
      ) : (
        <div className="grid gap-4 lg:grid-cols-2">
          {templates.map((t) => (
            <div key={t.id} className="card p-5">
              <div className="mb-3 flex flex-wrap items-center gap-2">
                <h2 className="font-semibold">{t.name}</h2>
                <Badge tone="gray">{t.language}</Badge>
                <Badge tone="blue">{t.category.toLowerCase()}</Badge>
                <Badge tone={statusTone(t.status)}>{t.status.toLowerCase()}</Badge>
              </div>
              <TemplatePreview template={t} />
              <p className="mt-3 text-xs text-gray-500">
                {t.bodyParams.length + t.headerParams.length + t.buttonParams.length} variable(s)
                {needsMedia(t) && ` · needs a ${t.headerFormat?.toLowerCase()} attachment`}
                {" · synced "}{formatDate(t.syncedAt)}
              </p>
            </div>
          ))}
        </div>
      )}
    </>
  );
}
