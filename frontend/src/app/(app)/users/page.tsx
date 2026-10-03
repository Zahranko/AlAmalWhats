"use client";

import { useCallback, useEffect, useState, type FormEvent } from "react";
import { api, ApiError, formatDate, type Role, type UserRow } from "@/lib/api";
import { useMe } from "@/components/app-shell";
import { Badge, ErrorText, Modal, PageHeader } from "@/components/ui";

type Dialog =
  | { kind: "create" }
  | { kind: "edit"; user: UserRow }
  | { kind: "reset"; user: UserRow }
  | { kind: "delete"; user: UserRow }
  | null;

export default function UsersPage() {
  const me = useMe();
  const [users, setUsers] = useState<UserRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [dialog, setDialog] = useState<Dialog>(null);
  const close = useCallback(() => setDialog(null), []);

  const load = useCallback(() => {
    api<UserRow[]>("/users").then(setUsers).catch((e) => setError(e.message));
  }, []);
  useEffect(load, [load]);

  async function unlock(u: UserRow) {
    try {
      await api(`/users/${u.id}/unlock`, { method: "POST" });
      load();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  function done() {
    setDialog(null);
    load();
  }

  return (
    <>
      <PageHeader
        title="Users"
        description="Admins manage users and settings. Employees can do all messaging work."
        actions={<button className="btn-primary" onClick={() => setDialog({ kind: "create" })}>Add user</button>}
      />
      <ErrorText error={error} />

      <div className="card overflow-x-auto">
        <table className="w-full text-left text-sm">
          <thead className="border-b border-gray-200 bg-gray-50 text-xs uppercase tracking-wide text-gray-500">
            <tr>
              <th className="px-4 py-3">Name</th>
              <th className="px-4 py-3">Role</th>
              <th className="px-4 py-3">Status</th>
              <th className="px-4 py-3">Last login</th>
              <th className="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-100">
            {users === null && (
              <tr><td colSpan={5} className="px-4 py-6 text-center text-gray-500">Loading…</td></tr>
            )}
            {users?.map((u) => (
              <tr key={u.id}>
                <td className="px-4 py-3">
                  <p className="font-medium">{u.name}{u.id === me.id && <span className="ml-2 text-xs text-gray-400">(you)</span>}</p>
                  <p className="text-gray-500">{u.email}</p>
                </td>
                <td className="px-4 py-3">
                  <Badge tone={u.role === "admin" ? "blue" : "gray"}>{u.role}</Badge>
                </td>
                <td className="space-x-1 px-4 py-3">
                  {u.isActive ? <Badge tone="green">active</Badge> : <Badge tone="red">disabled</Badge>}
                  {u.isLocked && <Badge tone="amber">locked</Badge>}
                  {u.mustChangePassword && <Badge tone="gray">must change password</Badge>}
                </td>
                <td className="px-4 py-3 text-gray-600">{formatDate(u.lastLoginAt)}</td>
                <td className="space-x-3 whitespace-nowrap px-4 py-3 text-right">
                  {u.isLocked && <button className="text-brand-700 hover:underline" onClick={() => unlock(u)}>Unlock</button>}
                  <button className="text-brand-700 hover:underline" onClick={() => setDialog({ kind: "edit", user: u })}>Edit</button>
                  <button className="text-brand-700 hover:underline" onClick={() => setDialog({ kind: "reset", user: u })}>Reset password</button>
                  {u.id !== me.id && (
                    <button className="text-red-600 hover:underline" onClick={() => setDialog({ kind: "delete", user: u })}>Delete</button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {dialog?.kind === "create" && <UserForm onClose={close} onDone={done} />}
      {dialog?.kind === "edit" && <UserForm user={dialog.user} isSelf={dialog.user.id === me.id} onClose={close} onDone={done} />}
      {dialog?.kind === "reset" && <ResetPassword user={dialog.user} onClose={close} onDone={done} />}
      {dialog?.kind === "delete" && <DeleteUser user={dialog.user} onClose={close} onDone={done} />}
    </>
  );
}

function useSubmit(action: () => Promise<unknown>, onDone: () => void) {
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  async function submit(e?: FormEvent) {
    e?.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await action();
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Something went wrong.");
      setBusy(false);
    }
  }
  return { error, busy, submit };
}

function UserForm({ user, isSelf, onClose, onDone }: { user?: UserRow; isSelf?: boolean; onClose: () => void; onDone: () => void }) {
  const [name, setName] = useState(user?.name ?? "");
  const [email, setEmail] = useState(user?.email ?? "");
  const [role, setRole] = useState<Role>(user?.role ?? "employee");
  const [isActive, setIsActive] = useState(user?.isActive ?? true);
  const [password, setPassword] = useState("");

  const { error, busy, submit } = useSubmit(
    () => user
      ? api(`/users/${user.id}`, { method: "PUT", body: { name, email, role, isActive } })
      : api("/users", { method: "POST", body: { name, email, role, password } }),
    onDone,
  );

  return (
    <Modal title={user ? "Edit user" : "Add user"} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <div>
          <label className="label" htmlFor="u-name">Name</label>
          <input id="u-name" required maxLength={200} className="input" value={name} onChange={(e) => setName(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="u-email">Email</label>
          <input id="u-email" type="email" required className="input" value={email} onChange={(e) => setEmail(e.target.value)} />
        </div>
        <div>
          <label className="label" htmlFor="u-role">Role</label>
          <select id="u-role" className="input" value={role} disabled={isSelf} onChange={(e) => setRole(e.target.value as Role)}>
            <option value="employee">Employee: messaging only</option>
            <option value="admin">Admin: also users and settings</option>
          </select>
        </div>
        {user ? (
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" checked={isActive} disabled={isSelf} onChange={(e) => setIsActive(e.target.checked)} />
            Active (unchecking signs the user out immediately)
          </label>
        ) : (
          <div>
            <label className="label" htmlFor="u-password">Temporary password</label>
            <input id="u-password" type="password" autoComplete="new-password" required minLength={8} className="input"
              value={password} onChange={(e) => setPassword(e.target.value)} />
            <p className="mt-1 text-xs text-gray-500">The user must change it at first login.</p>
          </div>
        )}
        {isSelf && <p className="text-xs text-gray-500">You can&apos;t change your own role or disable yourself.</p>}
        <ErrorText error={error} />
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={busy}>{busy ? "Saving…" : "Save"}</button>
        </div>
      </form>
    </Modal>
  );
}

function ResetPassword({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const [password, setPassword] = useState("");
  const { error, busy, submit } = useSubmit(
    () => api(`/users/${user.id}/reset-password`, { method: "POST", body: { password } }),
    onDone,
  );
  return (
    <Modal title={`Reset password for ${user.name}`} onClose={onClose}>
      <form onSubmit={submit} className="space-y-4">
        <p className="text-sm text-gray-600">
          This signs {user.name} out everywhere and unlocks the account. They must choose a new password at next login.
        </p>
        <div>
          <label className="label" htmlFor="r-password">Temporary password</label>
          <input id="r-password" type="password" autoComplete="new-password" required minLength={8} className="input"
            value={password} onChange={(e) => setPassword(e.target.value)} />
        </div>
        <ErrorText error={error} />
        <div className="flex justify-end gap-2">
          <button type="button" className="btn-secondary" onClick={onClose}>Cancel</button>
          <button type="submit" className="btn-primary" disabled={busy}>{busy ? "Saving…" : "Reset password"}</button>
        </div>
      </form>
    </Modal>
  );
}

function DeleteUser({ user, onClose, onDone }: { user: UserRow; onClose: () => void; onDone: () => void }) {
  const { error, busy, submit } = useSubmit(() => api(`/users/${user.id}`, { method: "DELETE" }), onDone);
  return (
    <Modal title="Delete user" onClose={onClose}>
      <p className="text-sm text-gray-600">
        Delete <strong>{user.name}</strong> ({user.email})? Their audit history is kept. To keep the account but block
        access, disable it instead.
      </p>
      <div className="mt-4"><ErrorText error={error} /></div>
      <div className="mt-4 flex justify-end gap-2">
        <button className="btn-secondary" onClick={onClose}>Cancel</button>
        <button className="btn-danger" disabled={busy} onClick={() => submit()}>{busy ? "Deleting…" : "Delete"}</button>
      </div>
    </Modal>
  );
}
