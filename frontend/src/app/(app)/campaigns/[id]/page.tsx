"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useEffect, useState } from "react";
import { api, ApiError, formatDate, formatPhone, qs, type Campaign, type Message, type Paged } from "@/lib/api";
import { ErrorText, PageHeader } from "@/components/ui";
import { CampaignStatus, Pagination, StatusBadge } from "@/components/messaging";

const PAGE_SIZE = 50;

export default function CampaignPage() {
  const { id } = useParams<{ id: string }>();
  const [campaign, setCampaign] = useState<Campaign | null>(null);
  const [messages, setMessages] = useState<Paged<Message> | null>(null);
  const [status, setStatus] = useState("");
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [tick, setTick] = useState(0);
  const [confirm, setConfirm] = useState(false);

  // Refresh while messages are still going out.
  const active = campaign?.status === "scheduled" || campaign?.status === "sending";
  useEffect(() => {
    if (!active) return;
    const t = setInterval(() => setTick((n) => n + 1), 5_000);
    return () => clearInterval(t);
  }, [active]);

  useEffect(() => {
    api<Campaign>(`/campaigns/${id}`).then(setCampaign).catch((e) => setError(e.message));
    api<Paged<Message>>(`/messages${qs({ campaignId: id, status, page, pageSize: PAGE_SIZE })}`).then(setMessages).catch((e) => setError(e.message));
  }, [id, status, page, tick]);

  async function cancel() {
    try {
      await api(`/campaigns/${id}/cancel`, { method: "POST" });
      setConfirm(false);
      setTick((n) => n + 1);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not cancel.");
    }
  }

  if (!campaign) return error ? <ErrorText error={error} /> : <p className="text-sm text-gray-500">Loading…</p>;
  const c = campaign.counts;
  const tiles: [string, number, string][] = [
    ["Waiting", c.queued + c.sending, ""],
    ["Sent", c.sent, ""],
    ["Delivered", c.delivered, ""],
    ["Read", c.read, ""],
    ["Failed", c.failed, c.failed ? "text-red-700" : ""],
    ["Cancelled", c.cancelled, ""],
  ];

  return (
    <>
      <Link href="/campaigns" className="text-sm text-gray-500 hover:underline">← Bulk send</Link>
      <PageHeader title={campaign.name}
        description={`${campaign.templateName} (${campaign.templateLanguage}) · ${campaign.totalRecipients} recipients · created ${formatDate(campaign.createdAt)} by ${campaign.createdBy ?? "—"}${campaign.scheduledAt ? ` · scheduled for ${formatDate(campaign.scheduledAt)}` : ""}`}
        actions={
          <div className="flex items-center gap-2">
            <CampaignStatus campaign={campaign} />
            {active && (confirm
              ? <button className="btn-danger" onClick={cancel}>Confirm: cancel unsent messages</button>
              : <button className="btn-secondary" onClick={() => setConfirm(true)}>Cancel campaign</button>)}
          </div>
        } />
      <ErrorText error={error} />

      <div className="mb-6 grid grid-cols-3 gap-3 lg:grid-cols-6">
        {tiles.map(([label, n, cls]) => (
          <div key={label} className="card px-4 py-3">
            <p className="text-xs text-gray-500">{label}</p>
            <p className={`text-2xl font-semibold tabular-nums ${cls}`}>{n.toLocaleString()}</p>
          </div>
        ))}
      </div>

      <div className="mb-3 flex items-center gap-3">
        <h2 className="font-semibold">Recipients</h2>
        <select className="input w-44" value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }} aria-label="Filter by status">
          <option value="">All statuses</option>
          {["queued", "sent", "delivered", "read", "failed", "cancelled"].map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
      <div className="card overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="border-b border-gray-200 text-left text-gray-500">
            <tr>
              <th className="px-4 py-3 font-medium">Customer</th>
              <th className="px-4 py-3 font-medium">Message</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium">Updated</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {messages?.items.map((m) => (
              <tr key={m.id} className="align-top">
                <td className="px-4 py-3">
                  {m.customerId ? <Link href={`/customers/${m.customerId}`} className="text-brand-700 hover:underline" dir="auto">{m.customerName}</Link> : m.customerName}
                  <p className="text-xs text-gray-500" dir="ltr">{formatPhone(m.phone)}</p>
                </td>
                <td className="max-w-md px-4 py-3">
                  <p className="line-clamp-2" dir="auto">{m.body}</p>
                  {m.mediaFileName && <p className="text-xs text-gray-500">📎 {m.mediaFileName}</p>}
                  {m.error && <p className="text-xs text-red-700">{m.error}</p>}
                </td>
                <td className="px-4 py-3"><StatusBadge status={m.status} /></td>
                <td className="px-4 py-3 whitespace-nowrap">{formatDate(m.readAt ?? m.deliveredAt ?? m.sentAt ?? m.failedAt ?? m.createdAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {messages && <Pagination page={page} total={messages.total} pageSize={PAGE_SIZE} onPage={setPage} />}
    </>
  );
}
