"use client";

import { useState, type FormEvent } from "react";
import { api, ApiError, type Customer } from "@/lib/api";
import { useMe } from "@/components/app-shell";
import { ErrorText, Modal } from "@/components/ui";

/** Add / edit dialog; admins can also delete. */
export function CustomerForm({ customer, onClose, onSaved, onDeleted }: {
  customer: Customer | null;
  onClose: () => void;
  onSaved: (c: Customer) => void;
  onDeleted?: () => void;
}) {
  const me = useMe();
  const [name, setName] = useState(customer?.name ?? "");
  const [phone, setPhone] = useState(customer ? `+${customer.phone}` : "");
  const [fileNumber, setFileNumber] = useState(customer?.fileNumber ?? "");
  const [notes, setNotes] = useState(customer?.notes ?? "");
  const [optedOut, setOptedOut] = useState(customer?.optedOut ?? false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const body = { name, phone, fileNumber, notes, optedOut };
      const saved = customer
        ? await api<Customer>(`/customers/${customer.id}`, { method: "PUT", body })
        : await api<Customer>("/customers", { method: "POST", body });
      onSaved(saved);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
      setBusy(false);
    }
  }

  async function remove() {
    if (!customer) return;
    setBusy(true);
    try {
      await api(`/customers/${customer.id}`, { method: "DELETE" });
      onDeleted?.();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
      setBusy(false);
    }
  }

  return (
    <Modal title={customer ? "Edit customer" : "Add customer"} onClose={onClose}>
      <form onSubmit={onSubmit} className="space-y-4">
        <div>
          <label className="label" htmlFor="c-name">Name</label>
          <input id="c-name" className="input" required maxLength={200} dir="auto" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="c-phone">WhatsApp number</label>
          <input id="c-phone" className="input" required inputMode="tel" placeholder="0791234567" dir="ltr"
            value={phone} onChange={(e) => setPhone(e.target.value)} />
          <p className="mt-1 text-xs text-gray-500">Local numbers (07…) get +962 automatically.</p>
        </div>
        <div>
          <label className="label" htmlFor="c-file">File number (optional)</label>
          <input id="c-file" className="input" maxLength={50} value={fileNumber} onChange={(e) => setFileNumber(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="c-notes">Notes (optional)</label>
          <textarea id="c-notes" className="input" rows={2} maxLength={2000} dir="auto" value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        {customer && (
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-1" checked={optedOut} onChange={(e) => setOptedOut(e.target.checked)} />
            <span>Opted out: never send this person WhatsApp messages. Set automatically when they reply STOP.</span>
          </label>
        )}
        <ErrorText error={error} />
        <div className="flex items-center justify-between gap-2">
          <div>
            {customer && me.role === "admin" && onDeleted && (
              confirmDelete
                ? <button type="button" className="btn-danger" disabled={busy} onClick={remove}>Confirm delete</button>
                : <button type="button" className="text-sm text-red-700 hover:underline" onClick={() => setConfirmDelete(true)}>Delete…</button>
            )}
          </div>
          <div className="flex gap-2">
            <button type="button" className="btn-secondary" onClick={onClose}>Cancel</button>
            <button type="submit" className="btn-primary" disabled={busy}>{busy ? "Saving…" : "Save"}</button>
          </div>
        </div>
        {confirmDelete && <p className="text-xs text-gray-500">Message history is kept but no longer linked to this customer. Queued messages are cancelled.</p>}
      </form>
    </Modal>
  );
}
