"use client";

import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError, formatDate, formatPhone, type Customer, type MediaFile, type Message } from "@/lib/api";
import { Badge, ErrorText } from "@/components/ui";
import { FileChip, StatusBadge, UploadButton } from "@/components/messaging";
import { CustomerForm } from "@/components/customer-form";

export default function CustomerPage() {
  const { id } = useParams<{ id: string }>();
  const router = useRouter();
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [messages, setMessages] = useState<Message[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState(false);
  const [tick, setTick] = useState(0);
  const bottom = useRef<HTMLDivElement>(null);
  const lastCount = useRef(0);

  // Poll for new messages and status changes while the page is open.
  useEffect(() => {
    const t = setInterval(() => setTick((n) => n + 1), 10_000);
    return () => clearInterval(t);
  }, []);

  useEffect(() => {
    api<Customer>(`/customers/${id}`).then(setCustomer).catch((e) => setError(e.message));
    api<Message[]>(`/customers/${id}/messages?take=200`).then(setMessages).catch((e) => setError(e.message));
  }, [id, tick]);

  useEffect(() => {
    if (messages && messages.length !== lastCount.current) {
      lastCount.current = messages.length;
      bottom.current?.scrollIntoView({ block: "end" });
    }
  }, [messages]);

  if (!customer) return error ? <ErrorText error={error} /> : <p className="text-sm text-gray-500">Loading…</p>;

  return (
    <div className="flex h-[calc(100vh-4rem)] flex-col">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-4">
        <div>
          <Link href="/customers" className="text-sm text-gray-500 hover:underline">← Customers</Link>
          <h1 className="mt-1 text-2xl font-semibold" dir="auto">{customer.name}</h1>
          <p className="text-sm text-gray-500">
            <span dir="ltr">{formatPhone(customer.phone)}</span>
            {customer.fileNumber && ` · File ${customer.fileNumber}`}
            {customer.optedOut && <span className="ml-2"><Badge tone="red">opted out {formatDate(customer.optedOutAt)}</Badge></span>}
          </p>
          {customer.notes && <p className="mt-1 max-w-xl text-sm text-gray-600" dir="auto">{customer.notes}</p>}
        </div>
        <div className="flex gap-2">
          <button className="btn-secondary" onClick={() => setEditing(true)}>Edit</button>
          <Link className="btn-primary" href={`/send?customer=${customer.id}`}>Send template</Link>
        </div>
      </div>

      <div className="card flex min-h-0 flex-1 flex-col overflow-hidden">
        <div className="flex-1 space-y-2 overflow-y-auto bg-[#efeae2] p-4">
          {messages?.length === 0 && <p className="py-8 text-center text-sm text-gray-500">No messages yet.</p>}
          {messages?.map((m) => <Bubble key={m.id} m={m} />)}
          <div ref={bottom} />
        </div>
        <ReplyBox customer={customer} onSent={() => setTick((n) => n + 1)} />
      </div>

      {editing && (
        <CustomerForm customer={customer} onClose={() => setEditing(false)}
          onSaved={(c) => { setCustomer(c); setEditing(false); }}
          onDeleted={() => router.replace("/customers")} />
      )}
    </div>
  );
}

function Bubble({ m }: { m: Message }) {
  const out = m.direction === "out";
  return (
    <div className={`flex ${out ? "justify-end" : "justify-start"}`}>
      <div className={`max-w-[75%] rounded-lg px-3 py-2 text-sm shadow-sm ${out ? "bg-[#d9fdd3]" : "bg-white"}`}>
        {m.templateName && <p className="mb-1 text-xs font-medium text-brand-700">Template: {m.templateName}</p>}
        {m.hasMedia && (
          <a href={`/api/messages/${m.id}/media`} className="mb-1 flex items-center gap-1 text-xs text-sky-700 hover:underline" target="_blank" rel="noopener">
            📎 {m.mediaFileName ?? `${m.type} attachment`}
          </a>
        )}
        {m.body && <p className="whitespace-pre-wrap" dir="auto">{m.body}</p>}
        {!m.body && !m.hasMedia && <p className="italic text-gray-500">{m.type} message</p>}
        <div className="mt-1 flex items-center justify-end gap-2 text-[11px] text-gray-500">
          {out && m.sentBy && <span>{m.sentBy}</span>}
          <span>{formatDate(m.createdAt)}</span>
          {out && <StatusBadge status={m.status} />}
        </div>
        {m.status === "failed" && m.error && <p className="mt-1 text-xs text-red-700">{m.error}</p>}
      </div>
    </div>
  );
}

function ReplyBox({ customer, onSent }: { customer: Customer; onSent: () => void }) {
  const [text, setText] = useState("");
  const [file, setFile] = useState<MediaFile | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (customer.optedOut) {
    return <p className="border-t border-gray-200 p-4 text-sm text-gray-500">This customer opted out. They can reply START to subscribe again.</p>;
  }
  if (!customer.canReply) {
    return (
      <p className="border-t border-gray-200 p-4 text-sm text-gray-500">
        Free replies are only possible within 24 hours of the customer&apos;s last message.{" "}
        <Link href={`/send?customer=${customer.id}`} className="text-brand-700 hover:underline">Send a template</Link> instead.
      </p>
    );
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await api(`/customers/${customer.id}/reply`, { method: "POST", body: { text, mediaFileId: file?.id } });
      setText("");
      setFile(null);
      onSent();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not send.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={onSubmit} className="space-y-2 border-t border-gray-200 p-3">
      <ErrorText error={error} />
      {file && <FileChip file={file} onRemove={() => setFile(null)} />}
      <div className="flex items-end gap-2">
        <textarea className="input min-h-[42px] flex-1" rows={2} maxLength={4096} dir="auto" placeholder="Write a reply…"
          value={text} onChange={(e) => setText(e.target.value)}
          onKeyDown={(e) => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); e.currentTarget.form?.requestSubmit(); } }} />
        <UploadButton label="📎" onUploaded={(f) => setFile(f[0])} disabled={busy} />
        <button type="submit" className="btn-primary" disabled={busy || (!text.trim() && !file)}>{busy ? "Sending…" : "Send"}</button>
      </div>
    </form>
  );
}
