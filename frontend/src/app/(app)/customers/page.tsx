"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api, formatDate, formatPhone, qs, type Customer, type Paged } from "@/lib/api";
import { useMe } from "@/components/app-shell";
import { Badge, ErrorText, PageHeader } from "@/components/ui";
import { Pagination } from "@/components/messaging";
import { CustomerForm } from "@/components/customer-form";

const PAGE_SIZE = 50;
type Filter = "all" | "awaiting" | "optedOut";

export default function CustomersPage() {
  const me = useMe();
  const [search, setSearch] = useState("");
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState<Filter>("all");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<Customer> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Customer | "new" | null>(null);
  const [reload, setReload] = useState(0);

  useEffect(() => {
    const params = qs({
      page, pageSize: PAGE_SIZE, search: query,
      awaitingReply: filter === "awaiting" || undefined,
      optedOut: filter === "optedOut" || undefined,
    });
    api<Paged<Customer>>(`/customers${params}`).then(setData).catch((e) => setError(e.message));
  }, [page, query, filter, reload]);

  return (
    <>
      <PageHeader title="Customers" description="Patients and contacts. People who message the hospital are added automatically."
        actions={
          <div className="flex gap-2">
            {me.role === "admin" && <a className="btn-secondary" href="/api/customers/export">Export CSV</a>}
            <button className="btn-primary" onClick={() => setEditing("new")}>Add customer</button>
          </div>
        } />

      <div className="mb-4 flex flex-wrap gap-3">
        <form className="flex gap-2" onSubmit={(e) => { e.preventDefault(); setQuery(search.trim()); setPage(1); }}>
          <input className="input w-72" placeholder="Name, phone or file number" value={search} dir="auto"
            onChange={(e) => setSearch(e.target.value)} aria-label="Search customers" />
          <button className="btn-secondary">Search</button>
        </form>
        <div className="flex rounded-md border border-gray-300 bg-white text-sm" role="group" aria-label="Filter">
          {([["all", "All"], ["awaiting", "Awaiting reply"], ["optedOut", "Opted out"]] as [Filter, string][]).map(([f, label]) => (
            <button key={f} onClick={() => { setFilter(f); setPage(1); }} aria-pressed={filter === f}
              className={`px-3 py-2 first:rounded-l-md last:rounded-r-md ${filter === f ? "bg-brand-600 text-white" : "hover:bg-gray-50"}`}>
              {label}
            </button>
          ))}
        </div>
      </div>

      <ErrorText error={error} />
      <div className="card overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="border-b border-gray-200 text-left text-gray-500">
            <tr>
              <th className="px-4 py-3 font-medium">Name</th>
              <th className="px-4 py-3 font-medium">Phone</th>
              <th className="px-4 py-3 font-medium">File no.</th>
              <th className="px-4 py-3 font-medium">Last message in</th>
              <th className="px-4 py-3 font-medium">Last message out</th>
              <th className="px-4 py-3 font-medium"></th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {data?.items.map((c) => (
              <tr key={c.id} className="hover:bg-gray-50">
                <td className="px-4 py-3">
                  <Link href={`/customers/${c.id}`} className="font-medium text-brand-700 hover:underline" dir="auto">{c.name}</Link>
                  {c.optedOut && <span className="ml-2"><Badge tone="red">opted out</Badge></span>}
                  {c.lastInboundAt && (!c.lastMessageAt || c.lastInboundAt > c.lastMessageAt) && (
                    <span className="ml-2"><Badge tone="amber">awaiting reply</Badge></span>
                  )}
                </td>
                <td className="px-4 py-3 whitespace-nowrap" dir="ltr">{formatPhone(c.phone)}</td>
                <td className="px-4 py-3">{c.fileNumber ?? "—"}</td>
                <td className="px-4 py-3 whitespace-nowrap">{formatDate(c.lastInboundAt)}</td>
                <td className="px-4 py-3 whitespace-nowrap">{formatDate(c.lastMessageAt)}</td>
                <td className="px-4 py-3 text-right">
                  <button className="text-brand-700 hover:underline" onClick={() => setEditing(c)}>Edit</button>
                </td>
              </tr>
            ))}
            {data?.items.length === 0 && (
              <tr><td colSpan={6} className="px-4 py-8 text-center text-gray-500">No customers found.</td></tr>
            )}
          </tbody>
        </table>
      </div>
      {data && <Pagination page={page} total={data.total} pageSize={PAGE_SIZE} onPage={setPage} />}

      {editing && (
        <CustomerForm customer={editing === "new" ? null : editing} onClose={() => setEditing(null)}
          onSaved={() => { setEditing(null); setReload((n) => n + 1); }} />
      )}
    </>
  );
}
