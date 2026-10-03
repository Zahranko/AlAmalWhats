"use client";

import { useMe } from "@/components/app-shell";
import { PageHeader } from "@/components/ui";

const roadmap = [
  { phase: 2, title: "Foundation", detail: "Login, Admin/Employee roles, users, audit log", done: true },
  { phase: 3, title: "WhatsApp core", detail: "Cloud API client, template sync, media upload, settings" },
  { phase: 4, title: "Queue + webhook", detail: "Rate-limited sending, delivery statuses, opt-out on \"stop\"" },
  { phase: 5, title: "Single send", detail: "Customers, multi-template + multi-attachment send, history" },
  { phase: 6, title: "Bulk send", detail: "Campaigns, import, per-person variables and files, schedule" },
  { phase: 7, title: "Dashboard + triggers", detail: "Stats, cost, API for your main system, backups" },
];

export default function DashboardPage() {
  const me = useMe();
  return (
    <>
      <PageHeader title={`Welcome, ${me.name}`} description="Messaging stats will appear here in phase 7." />
      <div className="card divide-y divide-gray-100">
        {roadmap.map((r) => (
          <div key={r.phase} className="flex items-center gap-4 px-5 py-4">
            <span className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-full text-sm font-semibold ${
              r.done ? "bg-brand-600 text-white" : "bg-gray-100 text-gray-500"}`}>
              {r.phase}
            </span>
            <div>
              <p className="font-medium">{r.title}{r.done && <span className="ml-2 text-sm text-brand-600">done</span>}</p>
              <p className="text-sm text-gray-500">{r.detail}</p>
            </div>
          </div>
        ))}
      </div>
    </>
  );
}
