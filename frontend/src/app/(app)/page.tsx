"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api } from "@/lib/api";
import { useMe } from "@/components/app-shell";
import { ErrorText, PageHeader } from "@/components/ui";

interface Daily { day: string; sent: number; delivered: number; read: number; failed: number; received: number }
interface Dashboard {
  days: number; sent: number; delivered: number; read: number; failed: number; queued: number; received: number;
  customers: number; optedOut: number; awaitingReply: number; daily: Daily[];
  costs: { category: string; billable: number; price: number; cost: number }[]; totalCost: number; currency: string;
  topTemplates: { name: string; count: number }[];
}

const pct = (n: number, of: number) => (of ? `${Math.round((n / of) * 100)}%` : "—");
const dayLabel = (iso: string) => new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short" });

export default function DashboardPage() {
  const me = useMe();
  const [days, setDays] = useState(30);
  const [d, setD] = useState<Dashboard | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<Dashboard>(`/dashboard?days=${days}`).then(setD).catch((e) => setError(e.message));
  }, [days]);

  return (
    <>
      <PageHeader title={`Welcome, ${me.name}`} description="WhatsApp activity at a glance."
        actions={
          <div className="flex rounded-md border border-gray-300 bg-white text-sm" role="group" aria-label="Period">
            {[7, 30, 90].map((n) => (
              <button key={n} onClick={() => setDays(n)} aria-pressed={days === n}
                className={`px-3 py-2 first:rounded-l-md last:rounded-r-md ${days === n ? "bg-brand-600 text-white" : "hover:bg-gray-50"}`}>
                {n} days
              </button>
            ))}
          </div>
        } />
      <ErrorText error={error} />
      {d && (
        <div className="space-y-6">
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
            <Tile label="Sent" value={d.sent.toLocaleString()} note={d.queued ? `${d.queued} waiting in queue` : undefined} />
            <Tile label="Delivered" value={pct(d.delivered, d.sent)} note={`${d.delivered.toLocaleString()} messages`} />
            <Tile label="Read" value={pct(d.read, d.sent)} note={`${d.read.toLocaleString()} messages`} />
            <Tile label="Failed" value={d.failed.toLocaleString()} tone={d.failed ? "critical" : undefined}
              note={d.failed ? <Link href="/history?status=failed" className="text-brand-700 hover:underline">Review in history</Link> : "None"} />
            <Tile label="Received" value={d.received.toLocaleString()} note="Messages from customers" />
            <Tile label="Awaiting reply" value={d.awaitingReply.toLocaleString()} tone={d.awaitingReply ? "warning" : undefined}
              note={<Link href="/customers" className="text-brand-700 hover:underline">Open customers</Link>} />
            <Tile label="Customers" value={d.customers.toLocaleString()} note={`${d.optedOut} opted out`} />
            <Tile label="Estimated cost" value={`${d.totalCost.toFixed(2)} ${d.currency}`}
              note={d.costs.length ? `${d.costs.reduce((s, c) => s + c.billable, 0)} billable messages` : "No billable messages"} />
          </div>

          <DailyChart daily={d.daily} />

          <div className="grid gap-6 lg:grid-cols-2">
            <div className="card p-5">
              <h2 className="mb-3 font-semibold">Most used templates</h2>
              {d.topTemplates.length === 0 ? <p className="text-sm text-gray-500">No template messages in this period.</p> : (
                <ul className="space-y-2 text-sm">
                  {d.topTemplates.map((t) => (
                    <li key={t.name} className="flex justify-between"><span>{t.name}</span><span className="tabular-nums text-gray-600">{t.count.toLocaleString()}</span></li>
                  ))}
                </ul>
              )}
            </div>
            <div className="card p-5">
              <h2 className="mb-3 font-semibold">Cost by category</h2>
              {d.costs.length === 0 ? <p className="text-sm text-gray-500">No billable messages reported by Meta in this period.</p> : (
                <table className="w-full text-sm">
                  <thead className="text-left text-gray-500"><tr><th className="pb-2 font-medium">Category</th><th className="pb-2 text-right font-medium">Messages</th><th className="pb-2 text-right font-medium">Price</th><th className="pb-2 text-right font-medium">Cost</th></tr></thead>
                  <tbody>
                    {d.costs.map((c) => (
                      <tr key={c.category}><td className="py-1">{c.category}</td><td className="text-right tabular-nums">{c.billable}</td>
                        <td className="text-right tabular-nums">{c.price}</td><td className="text-right tabular-nums">{c.cost.toFixed(2)}</td></tr>
                    ))}
                  </tbody>
                </table>
              )}
              <p className="mt-3 text-xs text-gray-500">
                An estimate from Meta&apos;s billable flags and the prices entered in{" "}
                {me.role === "admin" ? <Link href="/settings" className="text-brand-700 hover:underline">WhatsApp settings</Link> : "WhatsApp settings"}.
                Your Meta invoice is the final amount.
              </p>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function Tile({ label, value, note, tone }: { label: string; value: string; note?: React.ReactNode; tone?: "critical" | "warning" }) {
  return (
    <div className="card px-4 py-3">
      <p className="flex items-center gap-1 text-xs text-gray-500">
        {tone === "critical" && <span aria-hidden className="text-red-600">●</span>}
        {tone === "warning" && <span aria-hidden className="text-amber-500">●</span>}
        {label}
      </p>
      <p className="mt-1 text-2xl font-semibold tabular-nums text-gray-900">{value}</p>
      {note && <p className="mt-1 text-xs text-gray-500">{note}</p>}
    </div>
  );
}

/** One series (messages sent per day) as bars; hover a day for the full breakdown. */
function DailyChart({ daily }: { daily: Daily[] }) {
  const [hover, setHover] = useState<number | null>(null);
  const [table, setTable] = useState(false);
  const max = Math.max(1, ...daily.map((x) => x.sent));
  const step = niceStep(max);
  const top = Math.ceil(max / step) * step;
  const ticks = Array.from({ length: Math.round(top / step) + 1 }, (_, i) => i * step);
  const labelEvery = Math.ceil(daily.length / 8);

  return (
    <div className="card p-5">
      <div className="mb-4 flex items-center justify-between">
        <h2 className="font-semibold">Messages sent per day</h2>
        <button className="text-sm text-brand-700 hover:underline" onClick={() => setTable((t) => !t)}>{table ? "Show chart" : "Show table"}</button>
      </div>
      {table ? (
        <div className="max-h-80 overflow-y-auto">
          <table className="w-full text-sm">
            <thead className="sticky top-0 bg-white text-left text-gray-500">
              <tr>{["Day", "Sent", "Delivered", "Read", "Failed", "Received"].map((h) => <th key={h} className="py-2 font-medium">{h}</th>)}</tr>
            </thead>
            <tbody className="divide-y divide-gray-100 tabular-nums">
              {[...daily].reverse().map((x) => (
                <tr key={x.day}><td className="py-1">{dayLabel(x.day)}</td><td>{x.sent}</td><td>{x.delivered}</td><td>{x.read}</td><td>{x.failed}</td><td>{x.received}</td></tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <div className="flex gap-2 pt-2">
          <div className="relative h-56 w-8 shrink-0 text-right text-[11px] text-gray-500 tabular-nums" aria-hidden>
            {ticks.map((t) => <span key={t} className="absolute right-0 -translate-y-1/2" style={{ bottom: `${(t / top) * 100}%` }}>{t}</span>)}
          </div>
          <div className="min-w-0 flex-1">
            <div className="relative h-56">
              {ticks.map((t) => <div key={t} className="absolute inset-x-0 border-t border-gray-100" style={{ bottom: `${(t / top) * 100}%` }} />)}
              <div className="absolute inset-0 flex items-end gap-[2px]" role="img"
                aria-label={`Messages sent per day, ${daily.length} days, peak ${max}. Use "Show table" for the numbers.`}>
                {daily.map((x, i) => (
                  <div key={x.day} className="relative flex h-full flex-1 items-end" onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}>
                    <div className={`w-full rounded-t ${hover === i ? "bg-brand-700" : "bg-brand-600"}`}
                      style={{ height: x.sent ? `max(2px, ${(x.sent / top) * 100}%)` : 0 }} />
                    {hover === i && (
                      <div className={`pointer-events-none absolute bottom-full z-10 mb-2 w-40 rounded-md border border-gray-200 bg-white p-2 text-xs shadow-lg ${i > daily.length / 2 ? "right-0" : "left-0"}`}>
                        <p className="mb-1 font-medium text-gray-900">{dayLabel(x.day)}</p>
                        {([["Sent", x.sent], ["Delivered", x.delivered], ["Read", x.read], ["Failed", x.failed], ["Received", x.received]] as [string, number][]).map(([k, v]) => (
                          <p key={k} className="flex justify-between text-gray-600"><span>{k}</span><span className="tabular-nums text-gray-900">{v}</span></p>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            </div>
            <div className="mt-1 flex gap-[2px] text-[11px] text-gray-500" aria-hidden>
              {daily.map((x, i) => <span key={x.day} className="flex-1 truncate text-center">{i % labelEvery === 0 ? dayLabel(x.day) : ""}</span>)}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function niceStep(max: number) {
  const raw = max / 4;
  const pow = 10 ** Math.floor(Math.log10(raw));
  return Math.max(1, [1, 2, 5, 10].map((m) => m * pow).find((s) => s >= raw) ?? pow * 10);
}
