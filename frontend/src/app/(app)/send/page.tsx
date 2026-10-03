"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState, type FormEvent } from "react";
import { api, ApiError, formatPhone, qs, type Customer, type MediaFile, type Message, type Paged, type Template } from "@/lib/api";
import { ErrorText, PageHeader } from "@/components/ui";
import {
  FileChip, StatusBadge, TemplateFields, TemplatePreview, UploadButton,
  emptyValues, needsMedia, templateLabel, type TemplateValues,
} from "@/components/messaging";

interface Item { key: number; templateId: number | null; values: TemplateValues; files: MediaFile[] }

let nextKey = 1;
const newItem = (): Item => ({ key: nextKey++, templateId: null, values: emptyValues(null), files: [] });

export default function SendPage() {
  return (
    <Suspense fallback={<p className="text-sm text-gray-500">Loading…</p>}>
      <SendForm />
    </Suspense>
  );
}

function SendForm() {
  const params = useSearchParams();
  const [templates, setTemplates] = useState<Template[]>([]);
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [phone, setPhone] = useState("");
  const [name, setName] = useState("");
  const [items, setItems] = useState<Item[]>([newItem()]);
  const [schedule, setSchedule] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [sent, setSent] = useState<Message[] | null>(null);

  useEffect(() => {
    api<Template[]>("/templates?approved=true").then(setTemplates).catch((e) => setError(e.message));
  }, []);

  const customerParam = params.get("customer");
  useEffect(() => {
    if (customerParam) api<Customer>(`/customers/${customerParam}`).then(setCustomer).catch(() => {});
  }, [customerParam]);

  const update = (key: number, change: Partial<Item>) => setItems((list) => list.map((i) => (i.key === key ? { ...i, ...change } : i)));

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const body = {
        customerId: customer?.id,
        phone: customer ? undefined : phone,
        name: customer ? undefined : name,
        scheduledAt: schedule ? new Date(schedule).toISOString() : undefined,
        items: items.map((i) => ({
          templateId: i.templateId, header: i.values.header, body: i.values.body, buttons: i.values.buttons,
          mediaFileIds: i.files.map((f) => f.id),
        })),
      };
      setSent(await api<Message[]>("/messages/send", { method: "POST", body }));
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not send.");
    } finally {
      setBusy(false);
    }
  }

  if (sent) {
    return (
      <>
        <PageHeader title="Queued for sending" />
        <div className="card max-w-2xl divide-y divide-gray-100">
          {sent.map((m) => (
            <div key={m.id} className="flex items-center justify-between gap-4 px-5 py-3 text-sm">
              <span>{m.templateName}{m.mediaFileName && ` · ${m.mediaFileName}`}</span>
              <StatusBadge status={m.status} />
            </div>
          ))}
        </div>
        <div className="mt-4 flex gap-2">
          {sent[0]?.customerId && <Link className="btn-primary" href={`/customers/${sent[0].customerId}`}>Open conversation</Link>}
          <button className="btn-secondary" onClick={() => { setSent(null); setItems([newItem()]); setSchedule(""); }}>Send another</button>
        </div>
      </>
    );
  }

  return (
    <>
      <PageHeader title="Single send" description="Send one or more approved templates to one person." />
      <form onSubmit={onSubmit} className="max-w-4xl space-y-6">
        <section className="card space-y-4 p-6">
          <h2 className="font-semibold">Recipient</h2>
          {customer ? (
            <div className="flex items-center justify-between rounded-md bg-gray-50 px-4 py-3 text-sm">
              <span><span className="font-medium" dir="auto">{customer.name}</span> · <span dir="ltr">{formatPhone(customer.phone)}</span>
                {customer.optedOut && <span className="ml-2 text-red-700">opted out</span>}</span>
              <button type="button" className="text-brand-700 hover:underline" onClick={() => setCustomer(null)}>Change</button>
            </div>
          ) : (
            <>
              <CustomerSearch onPick={setCustomer} />
              <p className="text-xs text-gray-500">…or a new number:</p>
              <div className="grid gap-4 sm:grid-cols-2">
                <div>
                  <label className="label" htmlFor="phone">WhatsApp number</label>
                  <input id="phone" className="input" required={!customer} inputMode="tel" placeholder="0791234567" dir="ltr"
                    value={phone} onChange={(e) => setPhone(e.target.value)} />
                </div>
                <div>
                  <label className="label" htmlFor="name">Name</label>
                  <input id="name" className="input" dir="auto" value={name} onChange={(e) => setName(e.target.value)} />
                </div>
              </div>
            </>
          )}
        </section>

        {items.map((item, idx) => {
          const t = templates.find((x) => x.id === item.templateId) ?? null;
          return (
            <section key={item.key} className="card space-y-4 p-6">
              <div className="flex items-center justify-between">
                <h2 className="font-semibold">Message {idx + 1}</h2>
                {items.length > 1 && (
                  <button type="button" className="text-sm text-red-700 hover:underline" onClick={() => setItems((l) => l.filter((i) => i.key !== item.key))}>Remove</button>
                )}
              </div>
              <div>
                <label className="label" htmlFor={`t-${item.key}`}>Template</label>
                <select id={`t-${item.key}`} className="input" required value={item.templateId ?? ""}
                  onChange={(e) => {
                    const nt = templates.find((x) => x.id === Number(e.target.value)) ?? null;
                    update(item.key, { templateId: nt?.id ?? null, values: emptyValues(nt), files: [] });
                  }}>
                  <option value="">Choose a template…</option>
                  {templates.map((x) => <option key={x.id} value={x.id}>{templateLabel(x)}</option>)}
                </select>
                {templates.length === 0 && <p className="mt-1 text-xs text-gray-500">No approved templates. Create them in WhatsApp Manager and sync on the Templates page.</p>}
              </div>
              {t && (
                <div className="grid gap-6 lg:grid-cols-2">
                  <div className="space-y-4">
                    <TemplateFields template={t} value={item.values} idPrefix={`i${item.key}`} onChange={(values) => update(item.key, { values })} />
                    {needsMedia(t) && (
                      <div>
                        <p className="label">{t.headerFormat === "DOCUMENT" ? "Documents" : t.headerFormat === "IMAGE" ? "Images" : "Videos"}</p>
                        <div className="mb-2 flex flex-wrap gap-2">
                          {item.files.map((f) => <FileChip key={f.id} file={f} onRemove={() => update(item.key, { files: item.files.filter((x) => x.id !== f.id) })} />)}
                        </div>
                        <UploadButton multiple label="Add files" onUploaded={(f) => update(item.key, { files: [...item.files, ...f] })} />
                        <p className="mt-1 text-xs text-gray-500">Each file is sent as its own message with this template.</p>
                      </div>
                    )}
                  </div>
                  <TemplatePreview template={t} values={item.values} fileName={item.files[0]?.fileName} />
                </div>
              )}
            </section>
          );
        })}

        <button type="button" className="btn-secondary" onClick={() => setItems((l) => [...l, newItem()])}>+ Add another template</button>

        <section className="card space-y-2 p-6">
          <label className="label" htmlFor="schedule">Send at (optional)</label>
          <input id="schedule" type="datetime-local" className="input max-w-xs" value={schedule} onChange={(e) => setSchedule(e.target.value)} />
          <p className="text-xs text-gray-500">Leave empty to send now.</p>
        </section>

        <ErrorText error={error} />
        <button type="submit" className="btn-primary" disabled={busy || items.some((i) => !i.templateId)}>
          {busy ? "Sending…" : schedule ? "Schedule" : "Send now"}
        </button>
      </form>
    </>
  );
}

function CustomerSearch({ onPick }: { onPick: (c: Customer) => void }) {
  const [q, setQ] = useState("");
  const [results, setResults] = useState<Customer[]>([]);

  useEffect(() => {
    if (q.trim().length < 2) return;
    const t = setTimeout(() => {
      api<Paged<Customer>>(`/customers${qs({ search: q.trim(), pageSize: 8 })}`).then((r) => setResults(r.items)).catch(() => {});
    }, 250);
    return () => clearTimeout(t);
  }, [q]);

  return (
    <div className="relative">
      <label className="label" htmlFor="csearch">Existing customer</label>
      <input id="csearch" className="input" placeholder="Search name, phone or file number" dir="auto" value={q}
        onChange={(e) => { setQ(e.target.value); if (e.target.value.trim().length < 2) setResults([]); }} autoComplete="off" />
      {results.length > 0 && q.trim().length >= 2 && (
        <ul className="absolute z-10 mt-1 w-full rounded-md border border-gray-200 bg-white shadow-lg">
          {results.map((c) => (
            <li key={c.id}>
              <button type="button" className="flex w-full justify-between px-3 py-2 text-left text-sm hover:bg-gray-50"
                onClick={() => { onPick(c); setQ(""); setResults([]); }}>
                <span dir="auto">{c.name}</span><span className="text-gray-500" dir="ltr">{formatPhone(c.phone)}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
