"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { api, ApiError, formatDate, formatPhone, qs, type Message, type Paged } from "@/lib/api";
import { useMe } from "@/components/app-shell";
import { ErrorText, PageHeader } from "@/components/ui";
import { Pagination, StatusBadge } from "@/components/messaging";

const PAGE_SIZE = 50;
const statuses = ["queued", "sending", "sent", "delivered", "read", "failed", "cancelled", "received"];
const sources: [string, string][] = [["manual", "Single send"], ["reply", "Reply"], ["campaign", "Campaign"], ["api", "API"], ["system", "Automatic"]];

export default function HistoryPage() {
  return (
    <Suspense fallback={<p className="text-sm text-gray-500">Loading…</p>}>
      <History />
    </Suspense>
  );
}

function History() {
  const me = useMe();
  const params = useSearchParams();
  const [search, setSearch] = useState("");
  const [filters, setFilters] = useState({ search: "", status: params.get("status") ?? "", direction: "", source: "", from: "", to: "" });
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<Message> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);

  // Dates are picked in local time; the day after "to" is the exclusive end.
  const query = {
    ...filters,
    from: filters.from ? new Date(`${filters.from}T00:00`).toISOString() : "",
    to: filters.to ? new Date(new Date(`${filters.to}T00:00`).getTime() + 86_400_000).toISOString() : "",
  };

  useEffect(() => {
    api<Paged<Message>>(`/messages${qs({ ...query, page, pageSize: PAGE_SIZE })}`).then(setData).catch((e) => setError(e.message));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters, page, reload]);

  const set = (k: keyof typeof filters, v: string) => { setFilters((f) => ({ ...f, [k]: v })); setPage(1); };

  async function act(m: Message, action: "cancel" | "resend") {
    setError(null);
    try {
      await api(`/messages/${m.id}/${action}`, { method: "POST" });
      setReload((n) => n + 1);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
    }
  }

  return (
    <>
      <PageHeader title="Message history" description="Every message sent and received."
        actions={me.role === "admin" && <a className="btn-secondary" href={`/api/messages/export${qs(query)}`}>Export CSV</a>} />

      <div className="mb-4 flex flex-wrap gap-3">
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); set("search", search.trim()); }}>
          <input className="input w-64" placeholder="Name, phone, file no., template" dir="auto" value={search}
            onChange={(e) => setSearch(e.target.value)} aria-label="Search messages" />
          <button className="btn-secondary">Search</button>
        </form>
        <select className="input w-40" value={filters.status} onChange={(e) => set("status", e.target.value)} aria-label="Status">
          <option value="">All statuses</option>
          {statuses.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
        <select className="input w-36" value={filters.direction} onChange={(e) => set("direction", e.target.value)} aria-label="Direction">
          <option value="">In and out</option>
          <option value="out">Outgoing</option>
          <option value="in">Incoming</option>
        </select>
        <select className="input w-40" value={filters.source} onChange={(e) => set("source", e.target.value)} aria-label="Source">
          <option value="">All sources</option>
          {sources.map(([v, l]) => <option key={v} value={v}>{l}</option>)}
        </select>
        <input type="date" className="input w-40" value={filters.from} onChange={(e) => set("from", e.target.value)} aria-label="From date" />
        <input type="date" className="input w-40" value={filters.to} onChange={(e) => set("to", e.target.value)} aria-label="To date" />
      </div>

      <ErrorText error={error} />
      <div className="card overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="border-b border-gray-200 text-left text-gray-500">
            <tr>
              <th className="px-4 py-3 font-medium">Time</th>
              <th className="px-4 py-3 font-medium">Customer</th>
              <th className="px-4 py-3 font-medium">Message</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium">By</th>
              <th className="px-4 py-3 font-medium"></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {data?.items.map((m) => (
              <tr key={m.id} className="align-top hover:bg-gray-50">
                <td className="px-4 py-3 whitespace-nowrap">
                  {m.direction === "in" ? "⬅" : "➡"} {formatDate(m.createdAt)}
                  {m.scheduledAt && m.status === "queued" && <p className="text-xs text-amber-700">scheduled {formatDate(m.scheduledAt)}</p>}
                </td>
                <td className="px-4 py-3">
                  {m.customerId
                    ? <Link href={`/customers/${m.customerId}`} className="text-brand-700 hover:underline" dir="auto">{m.customerName}</Link>
                    : <span dir="auto">{m.customerName ?? "—"}</span>}
                  <p className="text-xs text-gray-500" dir="ltr">{formatPhone(m.phone)}</p>
                </td>
                <td className="max-w-md px-4 py-3">
                  {m.templateName && <p className="text-xs font-medium text-brand-700">{m.templateName}{m.campaignName && ` · ${m.campaignName}`}</p>}
                  <p className="line-clamp-2 whitespace-pre-wrap" dir="auto">{m.body ?? <span className="italic text-gray-500">{m.type}</span>}</p>
                  {m.hasMedia && <a href={`/api/messages/${m.id}/media`} className="text-xs text-sky-700 hover:underline" target="_blank" rel="noopener">📎 {m.mediaFileName ?? "attachment"}</a>}
                  {m.error && <p className="text-xs text-red-700">{m.error}</p>}
                </td>
                <td className="px-4 py-3"><StatusBadge status={m.status} /></td>
                <td className="px-4 py-3 text-gray-600">{m.sentBy ?? (m.source === "system" ? "Automatic" : "—")}</td>
                <td className="px-4 py-3 text-right whitespace-nowrap">
                  {m.status === "queued" && <button className="text-red-700 hover:underline" onClick={() => act(m, "cancel")}>Cancel</button>}
                  {m.status === "failed" && m.direction === "out" && <button className="text-brand-700 hover:underline" onClick={() => act(m, "resend")}>Resend</button>}
                </td>
              </tr>
            ))}
            {data?.items.length === 0 && <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-500">No messages found.</td></tr>}
          </tbody>
        </table>
      </div>
      {data && <Pagination page={page} total={data.total} pageSize={PAGE_SIZE} onPage={setPage} />}
    </>
  );
}
