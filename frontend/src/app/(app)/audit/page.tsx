"use client";

import { useEffect, useState } from "react";
import { api, formatDate, type AuditEntry, type Paged } from "@/lib/api";
import { ErrorText, PageHeader } from "@/components/ui";

const PAGE_SIZE = 50;

export default function AuditPage() {
  const [actions, setActions] = useState<string[]>([]);
  const [action, setAction] = useState("");
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<AuditEntry> | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<string[]>("/audit/actions").then(setActions).catch(() => {});
  }, []);

  useEffect(() => {
    const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) });
    if (action) params.set("action", action);
    if (query) params.set("search", query);
    api<Paged<AuditEntry>>(`/audit?${params}`).then(setData).catch((e) => setError(e.message));
  }, [page, action, query]);

  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  return (
    <>
      <PageHeader title="Audit log" description="Logins, sends, template changes and user changes." />
      <div className="mb-4 flex flex-wrap gap-3">
        <select className="input w-56" value={action} aria-label="Filter by action"
          onChange={(e) => { setAction(e.target.value); setPage(1); }}>
          <option value="">All actions</option>
          {actions.map((a) => <option key={a} value={a}>{a}</option>)}
        </select>
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); setQuery(search.trim()); setPage(1); }}>
          <input className="input w-64" placeholder="Search user or target" value={search}
            onChange={(e) => setSearch(e.target.value)} />
          <button className="btn-secondary">Search</button>
        </form>
      </div>
      <ErrorText error={error} />

      <div className="card overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-gray-200 bg-gray-50 text-xs uppercase tracking-wide text-gray-500">
            <tr>
              <th className="px-4 py-3">Time</th>
              <th className="px-4 py-3">User</th>
              <th className="px-4 py-3">Action</th>
              <th className="px-4 py-3">Target</th>
              <th className="px-4 py-3">Details</th>
              <th className="px-4 py-3">IP</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {data?.items.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-6 text-center text-gray-500">No entries.</td></tr>
            )}
            {data?.items.map((e) => (
              <tr key={e.id} className="align-top">
                <td className="whitespace-nowrap px-4 py-2 text-gray-600">{formatDate(e.createdAt)}</td>
                <td className="px-4 py-2">{e.userEmail ?? <span className="text-gray-400">system</span>}</td>
                <td className="px-4 py-2 font-mono text-xs">{e.action}</td>
                <td className="px-4 py-2">{e.target}</td>
                <td className="max-w-xs break-all px-4 py-2 font-mono text-xs text-gray-600">{e.details}</td>
                <td className="px-4 py-2 text-gray-500">{e.ip}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      <div className="mt-4 flex items-center justify-between text-sm text-gray-600">
        <span>{data ? `${data.total} entries` : "Loading…"}</span>
        <div className="flex items-center gap-2">
          <button className="btn-secondary" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
          <span>Page {page} of {pages}</span>
          <button className="btn-secondary" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
        </div>
      </div>
    </>
  );
}
