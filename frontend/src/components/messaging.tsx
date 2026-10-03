"use client";

import { useRef, useState } from "react";
import { ApiError, formatSize, upload, type Campaign, type MediaFile, type MessageStatus, type Template } from "@/lib/api";
import { Badge } from "@/components/ui";

const statusTone: Record<MessageStatus, "green" | "gray" | "red" | "amber" | "blue"> = {
  queued: "gray",
  sending: "amber",
  sent: "blue",
  delivered: "green",
  read: "green",
  failed: "red",
  cancelled: "gray",
  received: "blue",
};

const statusLabel: Record<MessageStatus, string> = {
  queued: "Queued",
  sending: "Sending",
  sent: "Sent ✓",
  delivered: "Delivered ✓✓",
  read: "Read ✓✓",
  failed: "Failed",
  cancelled: "Cancelled",
  received: "Received",
};

export function StatusBadge({ status }: { status: MessageStatus }) {
  return <Badge tone={statusTone[status] ?? "gray"}>{statusLabel[status] ?? status}</Badge>;
}

export function CampaignStatus({ campaign }: { campaign: Campaign }) {
  const tone = { scheduled: "amber", sending: "blue", completed: "green", cancelled: "gray" } as const;
  return <Badge tone={tone[campaign.status]}>{campaign.status}</Badge>;
}

export function Pagination({ page, total, pageSize, onPage }: { page: number; total: number; pageSize: number; onPage: (p: number) => void }) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  return (
    <div className="mt-4 flex items-center justify-between text-sm text-gray-600">
      <span>{total.toLocaleString()} total</span>
      <div className="flex items-center gap-2">
        <button className="btn-secondary" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button>
        <span>Page {page} of {pages}</span>
        <button className="btn-secondary" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next</button>
      </div>
    </div>
  );
}

export interface TemplateValues {
  header: string[];
  body: string[];
  buttons: string[];
}

export const emptyValues = (t: Template | null): TemplateValues => ({
  header: t ? t.headerParams.map(() => "") : [],
  body: t ? t.bodyParams.map(() => "") : [],
  buttons: t ? t.buttonParams.map(() => "") : [],
});

export const needsMedia = (t: Template | null) => !!t && ["IMAGE", "VIDEO", "DOCUMENT"].includes(t.headerFormat ?? "");

export function templateLabel(t: Template) {
  return `${t.name} (${t.language})`;
}

/** Inputs for every variable a template declares. */
export function TemplateFields({ template, value, onChange, idPrefix }: {
  template: Template;
  value: TemplateValues;
  onChange: (v: TemplateValues) => void;
  idPrefix: string;
}) {
  const set = (part: keyof TemplateValues, i: number, v: string) => {
    const next = { ...value, [part]: [...value[part]] };
    next[part][i] = v;
    onChange(next);
  };
  const field = (part: keyof TemplateValues, i: number, label: string) => (
    <div key={`${part}-${i}`}>
      <label className="label" htmlFor={`${idPrefix}-${part}-${i}`}>{label}</label>
      <input id={`${idPrefix}-${part}-${i}`} className="input" required dir="auto"
        value={value[part][i] ?? ""} onChange={(e) => set(part, i, e.target.value)} />
    </div>
  );

  if (!template.headerParams.length && !template.bodyParams.length && !template.buttonParams.length) {
    return <p className="text-sm text-gray-500">This template has no variables.</p>;
  }
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {template.headerParams.map((p, i) => field("header", i, `Header {{${p}}}`))}
      {template.bodyParams.map((p, i) => field("body", i, `{{${p}}}`))}
      {template.buttonParams.map((b, i) => field("buttons", i, `Button "${b.text}" ${b.subType === "url" ? "link value" : "code"}`))}
    </div>
  );
}

function fill(text: string | null, names: string[], values: string[]) {
  let out = text ?? "";
  names.forEach((n, i) => { out = out.replaceAll(`{{${n}}}`, values[i] || `{{${n}}}`); });
  return out;
}

/** WhatsApp-like bubble showing the template with the values filled in. */
export function TemplatePreview({ template, values, fileName }: { template: Template; values?: TemplateValues; fileName?: string }) {
  const v = values ?? emptyValues(template);
  return (
    <div className="rounded-lg bg-[#e5ddd5] p-3">
      <div className="max-w-sm rounded-lg bg-white p-3 text-sm shadow-sm">
        {needsMedia(template) && (
          <div className="mb-2 flex items-center gap-2 rounded bg-gray-100 px-3 py-4 text-xs text-gray-600">
            <span aria-hidden>{template.headerFormat === "IMAGE" ? "🖼" : template.headerFormat === "VIDEO" ? "🎬" : "📄"}</span>
            {fileName ?? `${template.headerFormat?.toLowerCase()} attachment`}
          </div>
        )}
        {template.headerFormat === "TEXT" && <p className="mb-1 font-semibold" dir="auto">{fill(template.headerText, template.headerParams, v.header)}</p>}
        <p className="whitespace-pre-wrap" dir="auto">{fill(template.bodyText, template.bodyParams, v.body)}</p>
        {template.footerText && <p className="mt-1 text-xs text-gray-500" dir="auto">{template.footerText}</p>}
        {template.buttons.length > 0 && (
          <div className="mt-2 divide-y divide-gray-100 border-t border-gray-100">
            {template.buttons.map((b, i) => <p key={i} className="py-1.5 text-center text-sm text-sky-600">{b.text}</p>)}
          </div>
        )}
      </div>
    </div>
  );
}

/** Uploads chosen files to /api/media right away and reports the stored files. */
export function UploadButton({ multiple, onUploaded, label = "Attach file", disabled }: {
  multiple?: boolean;
  onUploaded: (files: MediaFile[]) => void;
  label?: string;
  disabled?: boolean;
}) {
  const input = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onChange(list: FileList | null) {
    if (!list?.length) return;
    setBusy(true);
    setError(null);
    try {
      const form = new FormData();
      Array.from(list).forEach((f) => form.append("files", f));
      onUploaded(await upload<MediaFile[]>("/media", form));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Upload failed.");
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  }

  return (
    <div>
      <input ref={input} type="file" className="hidden" multiple={multiple} onChange={(e) => onChange(e.target.files)}
        accept=".pdf,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.jpg,.jpeg,.png,.mp4,.3gp,.mp3,.m4a,.aac,.amr,.ogg" />
      <button type="button" className="btn-secondary" disabled={busy || disabled} onClick={() => input.current?.click()}>
        {busy ? "Uploading…" : label}
      </button>
      {error && <p role="alert" className="mt-1 text-sm text-red-700">{error}</p>}
    </div>
  );
}

export function FileChip({ file, onRemove }: { file: MediaFile; onRemove?: () => void }) {
  return (
    <span className="inline-flex items-center gap-2 rounded-full bg-gray-100 px-3 py-1 text-xs text-gray-700">
      📎 {file.fileName} <span className="text-gray-400">{formatSize(file.size)}</span>
      {onRemove && <button type="button" onClick={onRemove} className="text-gray-500 hover:text-red-600" aria-label={`Remove ${file.fileName}`}>✕</button>}
    </span>
  );
}
