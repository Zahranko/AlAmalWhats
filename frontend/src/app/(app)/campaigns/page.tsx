"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { api, formatDate, type Campaign, type Paged } from "@/lib/api";
import { ErrorText, PageHeader } from "@/components/ui";
import { CampaignStatus, Pagination } from "@/components/messaging";

const PAGE_SIZE = 25;

export default function CampaignsPage() {
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Paged<Campaign> | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api<Paged<Campaign>>(`/campaigns?page=${page}&pageSize=${PAGE_SIZE}`).then(setData).catch((e) => setError(e.message));
  }, [page]);

  return (
    <>
      <PageHeader title="Bulk send" description="Campaigns: one template sent to a list of people."
        actions={<Link className="btn-primary" href="/campaigns/new">New bulk send</Link>} />
      <ErrorText error={error} />
      <div className="card overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="border-b border-gray-200 text-left text-gray-500">
            <tr>
              <th className="px-4 py-3 font-medium">Campaign</th>
              <th className="px-4 py-3 font-medium">Template</th>
              <th className="px-4 py-3 font-medium">Status</th>
              <th className="px-4 py-3 font-medium">Progress</th>
              <th className="px-4 py-3 font-medium">Created</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {data?.items.map((c) => {
              const done = c.counts.sent + c.counts.delivered + c.counts.read;
              return (
                <tr key={c.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3"><Link href={`/campaigns/${c.id}`} className="font-medium text-brand-700 hover:underline" dir="auto">{c.name}</Link></td>
                  <td className="px-4 py-3">{c.templateName}</td>
                  <td className="px-4 py-3"><CampaignStatus campaign={c} /></td>
                  <td className="px-4 py-3 whitespace-nowrap">
                    {done} / {c.totalRecipients} sent{c.counts.failed > 0 && <span className="text-red-700"> · {c.counts.failed} failed</span>}
                  </td>
                  <td className="px-4 py-3 whitespace-nowrap">{formatDate(c.createdAt)}<p className="text-xs text-gray-500">{c.createdBy}</p></td>
                </tr>
              );
            })}
            {data?.items.length === 0 && <tr><td colSpan={5} className="px-4 py-8 text-center text-gray-500">No campaigns yet.</td></tr>}
          </tbody>
        </table>
      </div>
      {data && <Pagination page={page} total={data.total} pageSize={PAGE_SIZE} onPage={setPage} />}
    </>
  );
}
